namespace Sitecore.Support.ContentSearch
{
    using Sitecore.ContentSearch;
    using System.Collections.Concurrent;
    using Sitecore.ContentSearch.Pipelines.GetDependencies;
    using Sitecore.Data;
    using Sitecore.Data.Items;
    using Sitecore.SecurityModel;
    public class SitecoreItemCrawler : Sitecore.ContentSearch.SitecoreItemCrawler
    {
        protected ConcurrentDictionary<IIndexableUniqueId, object> Processed;
        public override void Update(IProviderUpdateContext context, IIndexableUniqueId indexableUniqueId,
            IndexEntryOperationContext operationContext, IndexingOptions indexingOptions = IndexingOptions.Default)
        {
            if (!this.IsExcludedFromIndex(indexableUniqueId, operationContext, true))
            {
                base.Update(context, indexableUniqueId, operationContext, indexingOptions);
            }
            else
            {
                Sitecore.Events.Event.RaiseEvent("indexing:excludedfromindex",
                    new object[] {this.index.Name, indexableUniqueId.Value});
                if (this.DocumentOptions.ProcessDependencies)
                    {
                    ItemUri itemUri = indexableUniqueId as SitecoreItemUniqueId;
                    using (new SecurityDisabler())
                    {
                        Item item;
                        using (new WriteCachesDisabler())
                        {
                            item = Sitecore.Data.Database.GetItem(itemUri);
                        }
                        if (item != null)
                        {
                            Sitecore.Events.Event.RaiseEvent("indexing:updatedependents",
                                new object[] {this.index.Name, indexableUniqueId});
                            this.UpdateDependents(context, item);
                        }
                    }
                }
            }
        }

        protected override void UpdateDependents(IProviderUpdateContext context, SitecoreIndexableItem indexable)
        {
            foreach (IIndexableUniqueId current in indexable.GetIndexingDependencies())
            {                
                if (!this.IsExcludedFromIndex(current, true))
                {
                    this.Update(context, current, IndexingOptions.Default);
                }
                else
                {
                    Sitecore.Events.Event.RaiseEvent("indexing:excludedfromindex",
                        new object[] {this.index.Name, current.Value});
                }
            }
        }
    }
}