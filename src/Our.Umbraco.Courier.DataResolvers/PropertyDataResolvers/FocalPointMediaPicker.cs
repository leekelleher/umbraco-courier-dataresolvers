using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Umbraco.Courier.Core;
using Umbraco.Courier.DataResolvers.PropertyDataResolvers;
using Umbraco.Courier.ItemProviders;

namespace Our.Umbraco.Courier.DataResolvers.PropertyDataResolvers
{
    public class FocalPointMediaPicker : MultiMediaPicker
    {
        private enum Direction
        {
            Extracting,
            Packaging
        }

        public override string EditorAlias
        {
            get
            {
                return "Pegasus.MediaPicker";
            }
        }

        public override void ExtractingProperty(Item item, ContentProperty propertyData)
        {
            ProcessPropertyData(item, propertyData, Direction.Extracting);
        }

        public override void PackagingProperty(Item item, ContentProperty propertyData)
        {
            ProcessPropertyData(item, propertyData, Direction.Packaging);
        }

        private void ProcessPropertyData(Item item, ContentProperty propertyData, Direction direction)
        {
            if (propertyData == null || propertyData.Value == null)
                return;

            if (direction == Direction.Packaging)
                item.Dependencies.Add(propertyData.DataType.ToString(), ItemProviderIds.dataTypeItemProviderGuid);

            var items = JsonConvert.DeserializeObject<JArray>(propertyData.Value.ToString());
            if (items == null)
                return;

            foreach (var media in items)
            {
                if (media["id"] == null)
                    continue;

                var id = media["id"].ToString();

                if (direction == Direction.Packaging)
                {
                    int mediaId;
                    if (!int.TryParse(id, out mediaId))
                        continue;

                    var guid = ExecutionContext.DatabasePersistence.GetUniqueId(mediaId, UmbracoNodeObjectTypeIds.Media);
                    if (Guid.Empty.Equals(guid))
                        continue;

                    var mediaGuid = guid.ToString();

                    item.Dependencies.Add(new Dependency(mediaGuid, ItemProviderIds.mediaItemProviderGuid));

                    media["id"] = mediaGuid;
                }
                else if (direction == Direction.Extracting)
                {
                    Guid mediaGuid;
                    if (Guid.TryParse(id, out mediaGuid))
                        media["id"] = ExecutionContext.DatabasePersistence.GetNodeId(mediaGuid, UmbracoNodeObjectTypeIds.Media);
                }
            }

            propertyData.Value = items.ToString();
        }
    }
}