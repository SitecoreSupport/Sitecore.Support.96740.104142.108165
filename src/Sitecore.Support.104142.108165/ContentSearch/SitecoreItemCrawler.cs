namespace Sitecore.Support.ContentSearch
{
    using Sitecore.ContentSearch;
    using System.Collections.Concurrent;
    using Sitecore.ContentSearch.Pipelines.GetDependencies;
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
        }

        protected override void UpdateDependents(IProviderUpdateContext context, SitecoreIndexableItem indexable)
        {
            foreach (IIndexableUniqueId current in indexable.GetIndexingDependencies())
            {                
                if (!this.IsExcludedFromIndex(current, true))
                {
                    this.Update(context, current, IndexingOptions.Default);
                }
            }
        }
    }
}