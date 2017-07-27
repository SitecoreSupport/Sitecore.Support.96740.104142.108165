using System.Collections.Concurrent;

namespace Sitecore.Support.ContentSearch
{
    using Sitecore.ContentSearch;
    public class SitecoreItemCrawler : Sitecore.ContentSearch.SitecoreItemCrawler
    {
        protected ConcurrentDictionary<IIndexableUniqueId, object> Processed;
        public override void Update(IProviderUpdateContext context, IIndexableUniqueId indexableUniqueId,
            IndexEntryOperationContext operationContext, IndexingOptions indexingOptions = IndexingOptions.Default)
        {
            var contextEx = context as ITrackingIndexingContext;
            if (contextEx == null)
            {
                base.Update(context, indexableUniqueId, operationContext, indexingOptions);
                return;
            }
            if (this.IsExcludedFromIndex(indexableUniqueId, operationContext, true))
                return;
            base.Update(context, indexableUniqueId, operationContext, indexingOptions);
        }
    }
}