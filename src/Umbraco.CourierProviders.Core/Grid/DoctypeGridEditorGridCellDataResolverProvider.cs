namespace DisPlay.Umbraco.CourierProviders.Core.Grid
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using NestedContent;

    using global::Umbraco.Courier.Core;
    using global::Umbraco.Courier.Core.Logging;
    using global::Umbraco.Courier.Core.ProviderModel;
    using global::Umbraco.Courier.DataResolvers.PropertyDataResolvers;
    using global::Umbraco.Courier.ItemProviders;

    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    public class DoctypeGridEditorGridCellDataResolverProvider : GridCellDataResolverProvider
    {
        private enum Direction
        {
            Extracting,
            Packaging
        }

        public override bool ShouldRun(string view, dynamic cell)
        {
            try
            {
                return cell?.editor?.alias?.ToString() == "docType";
            }
            catch
            {
                // ignored
            }

            return false;
        }

        public override void PackagingCell(Item item, ContentProperty propertyData, dynamic cell)
        {
            ProcessCell(item, propertyData, cell, Direction.Packaging);
        }

        public override void ExtractingCell(Item item, ContentProperty propertyData, dynamic cell)
        {
            ProcessCell(item, propertyData, cell, Direction.Extracting);
        }

        private void ProcessCell(Item item, ContentProperty propertyData, dynamic cell, Direction direction)
        {
            string docTypeId = cell?.value?.docType?.ToString();
            string cellValue = cell?.value?.value?.ToString();
            if (cellValue == null || docTypeId == null)
                return;

            var data = JsonConvert.DeserializeObject(cellValue);
            if (!(data is JObject))
                return;

            var propValues = ((JObject)data).ToObject<Dictionary<string, object>>();
            var docType = ExecutionContext.DatabasePersistence.RetrieveItem<DocumentType>(
                new ItemIdentifier(docTypeId, ItemProviderIds.documentTypeItemProviderGuid));

            if (direction == Direction.Packaging)
            {
                item.Dependencies.Add(docTypeId, ItemProviderIds.documentTypeItemProviderGuid);

                //package doctype from guid to alias
                cell.value.docType = new JValue(docType.Alias);
            }
            else if (direction == Direction.Extracting)
            {
                //extract doctype from alias to guid
                cell.value.docType = new JValue(docType.UniqueId.ToString());
            }

            var propertyItemProvider = ItemProviderCollection.Instance.GetProvider(ItemProviderIds.propertyDataItemProviderGuid, ExecutionContext);

            foreach (var prop in docType.Properties)
            {
                object value;
                if (!propValues.TryGetValue(prop.Alias, out value))
                    continue;

                var datatype =
                    ExecutionContext.DatabasePersistence.RetrieveItem<DataType>(
                        new ItemIdentifier(
                            prop.DataTypeDefinitionId.ToString(),
                            ItemProviderIds.dataTypeItemProviderGuid));

                var fakeItem = new ContentPropertyData
                {
                    ItemId = item.ItemId,
                    Name = string.Format("{0} [{1}: Nested {2} ({3})]", item.Name, EditorAlias, datatype.PropertyEditorAlias, prop.Alias),
                    Data = new List<ContentProperty>
                            {
                                new ContentProperty
                                {
                                    Alias = prop.Alias,
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
                        CourierLogHelper.Error<NestedContentDataResolverProvider>(
                            string.Concat("Error packaging data value: ", fakeItem.Name), ex);
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
                        CourierLogHelper.Error<NestedContentDataResolverProvider>(
                            string.Concat("Error extracting data value: ", fakeItem.Name), ex);
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
                        string serializedValue = firstDataType.Value as string ??
                                                 JsonConvert.SerializeObject(firstDataType.Value);

                        object jsonValue;
                        try
                        {
                            jsonValue = JsonConvert.DeserializeObject(serializedValue);
                        }
                        catch
                        {
                            jsonValue = serializedValue;
                        }

                        propValues[prop.Alias] = jsonValue;

                        // (if packaging) add a dependency for the property's data-type
                        if (direction == Direction.Packaging)
                            item.Dependencies.Add(firstDataType.DataType.ToString(), ItemProviderIds.dataTypeItemProviderGuid);
                    }
                }
            }

            var serialized = JsonConvert.SerializeObject(propValues);
            cell.value.value = JsonConvert.DeserializeObject(serialized);
        }
    }
}
