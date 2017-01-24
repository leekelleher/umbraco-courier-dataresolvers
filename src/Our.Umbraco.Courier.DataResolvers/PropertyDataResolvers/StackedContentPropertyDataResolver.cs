using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Umbraco.Core;
using Umbraco.Courier.Core;
using Umbraco.Courier.Core.Logging;
using Umbraco.Courier.Core.ProviderModel;
using Umbraco.Courier.DataResolvers;
using Umbraco.Courier.ItemProviders;

namespace Our.Umbraco.Courier.DataResolvers.PropertyDataResolvers
{
    public class StackedContentPropertyDataResolver : InnerContentPropertyDataResolver
    {
        public override string EditorAlias
        {
            get
            {
                return "Our.Umbraco.StackedContent";
            }
        }
    }

    public abstract class InnerContentPropertyDataResolver : PropertyDataResolverProvider
    {
        private enum Direction
        {
            Extracting,
            Packaging
        }

        public override void ExtractingProperty(Item item, ContentProperty propertyData)
        {
            ProcessPropertyData(item, propertyData, Direction.Extracting);
        }

        public override void PackagingDataType(DataType item)
        {
            AddDataTypeDependencies(item);
        }

        public override void PackagingProperty(Item item, ContentProperty propertyData)
        {
            ProcessPropertyData(item, propertyData, Direction.Packaging);
        }

        private void AddDataTypeDependencies(DataType item)
        {
            if (item == null || item.Prevalues == null || item.Prevalues.Count == 0)
                return;

            var json = item.Prevalues.FirstOrDefault(x => x.Alias.InvariantEquals("contentTypes"));
            if (json == null)
                return;

            var contentTypes = JsonConvert.DeserializeObject<JArray>(json.Value);
            if (contentTypes == null)
                return;

            foreach (var contentType in contentTypes)
            {
                var alias = contentType["icContentTypeAlias"];
                if (alias == null)
                    continue;

                var documentType = ExecutionContext.DatabasePersistence.RetrieveItem<DocumentType>(new ItemIdentifier(alias.ToString(), ItemProviderIds.documentTypeItemProviderGuid));
                if (documentType == null)
                    continue;

                item.Dependencies.Add(documentType.UniqueId.ToString(), ItemProviderIds.documentTypeItemProviderGuid);
            }
        }

        private void ProcessPropertyData(Item item, ContentProperty propertyData, Direction direction)
        {
            if (direction == Direction.Packaging)
                item.Dependencies.Add(propertyData.DataType.ToString(), ItemProviderIds.dataTypeItemProviderGuid);

            var propertyItemProvider = ItemProviderCollection.Instance.GetProvider(ItemProviderIds.propertyDataItemProviderGuid, ExecutionContext);

            var icItems = JsonConvert.DeserializeObject<JArray>(propertyData.Value.ToString());

            ProcessItems(item, icItems, propertyItemProvider, direction);

            propertyData.Value = JsonConvert.SerializeObject(icItems);
        }

        private void ProcessItems(Item item, JArray icItems, ItemProvider propertyItemProvider, Direction direction)
        {
            foreach (var icItem in icItems)
            {
                var docTypeAlias = icItem["icContentTypeAlias"];
                if (docTypeAlias == null)
                    continue;

                var docType = ExecutionContext.DatabasePersistence.RetrieveItem<DocumentType>(new ItemIdentifier(docTypeAlias.ToString(), ItemProviderIds.documentTypeItemProviderGuid));
                if (docType == null)
                    continue;

                var propertyTypes = docType.Properties;

                // check for compositions
                foreach (var masterDocTypeAlias in docType.MasterDocumentTypes)
                {
                    var masterDocType = ExecutionContext.DatabasePersistence.RetrieveItem<DocumentType>(new ItemIdentifier(masterDocTypeAlias, ItemProviderIds.documentTypeItemProviderGuid));
                    if (masterDocType != null)
                        propertyTypes.AddRange(masterDocType.Properties);
                }

                foreach (var propertyType in propertyTypes)
                {
                    ProcessItemPropertyData(item, propertyType, icItem, propertyItemProvider, direction);
                }
            }
        }

        private void ProcessItemPropertyData(Item item, ContentTypeProperty propertyType, JToken icItem, ItemProvider propertyItemProvider, Direction direction)
        {
            var value = icItem[propertyType.Alias];
            if (value != null)
            {
                var datatype = ExecutionContext.DatabasePersistence.RetrieveItem<DataType>(new ItemIdentifier(propertyType.DataTypeDefinitionId.ToString(), ItemProviderIds.dataTypeItemProviderGuid));

                // create a 'fake' item for Courier to process
                var fakeItem = new ContentPropertyData
                {
                    ItemId = item.ItemId,
                    Name = string.Format("{0} [{1}: Inner {2} ({3})]", item.Name, this.EditorAlias, datatype.PropertyEditorAlias, propertyType.Alias),
                    Data = new List<ContentProperty>
                    {
                        new ContentProperty
                        {
                            Alias = propertyType.Alias,
                            DataType = datatype.UniqueID,
                            PropertyEditorAlias = datatype.PropertyEditorAlias,
                            Value = value.ToString()
                        }
                    }
                };

                if (direction == Direction.Packaging)
                {
                    try
                    {
                        // run the 'fake' item through Courier's data resolvers
                        ResolutionManager.Instance.PackagingItem(fakeItem, propertyItemProvider);
                    }
                    catch (Exception ex)
                    {
                        CourierLogHelper.Error<NestedContentPropertyDataResolver>(string.Concat("Error packaging data value: ", fakeItem.Name), ex);
                    }
                }
                else if (direction == Direction.Extracting)
                {
                    try
                    {
                        // run the 'fake' item through Courier's data resolvers
                        ResolutionManager.Instance.ExtractingItem(fakeItem, propertyItemProvider);
                    }
                    catch (Exception ex)
                    {
                        CourierLogHelper.Error<NestedContentPropertyDataResolver>(string.Concat("Error extracting data value: ", fakeItem.Name), ex);
                    }
                }

                // pass up the dependencies and resources
                item.Dependencies.AddRange(fakeItem.Dependencies);
                item.Resources.AddRange(fakeItem.Resources);

                if (fakeItem.Data != null && fakeItem.Data.Any())
                {
                    var firstDataType = fakeItem.Data.FirstOrDefault();
                    if (firstDataType != null)
                    {
                        // set the resolved property data value
                        string serializedValue = firstDataType.Value as string ?? JsonConvert.SerializeObject(firstDataType.Value);

                        icItem[propertyType.Alias] = new JValue(serializedValue);

                        // (if packaging) add a dependency for the property's data-type
                        if (direction == Direction.Packaging)
                            item.Dependencies.Add(firstDataType.DataType.ToString(), ItemProviderIds.dataTypeItemProviderGuid);
                    }
                }
            }
        }
    }
}