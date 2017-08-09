namespace Sitecore.Support.ContentSearch.Maintenance.Strategies
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.Serialization;
    using Sitecore.Abstractions;
    using Sitecore.Configuration;
    using Sitecore.ContentSearch;
    using Sitecore.ContentSearch.Diagnostics;
    using Sitecore.ContentSearch.Maintenance;
    using Sitecore.ContentSearch.Maintenance.Strategies;
    using Sitecore.Data;
    using Sitecore.Data.Events;
    using Sitecore.Data.Items;
    using Sitecore.Diagnostics;
    using Sitecore.Events;
    [DataContract]
    public class SynchronousStrategy : IIndexUpdateStrategy
    {
        #region Constants

        internal const string CouldntRetreiveUri = "[Index={0}] Couldn't retrieve Uri for the added item. The index will not be updated.";

        #endregion

        #region Fields and Properties

        ISearchIndex index;

        private readonly IFactory factory;


        public Database Database { get; protected set; }

        #endregion

        #region Public methods

        public SynchronousStrategy(string database)
        {
            Assert.IsNotNullOrEmpty(database, "database");
            this.factory = ContentSearchManager.Locator.GetInstance<IFactory>();
            this.Database = this.factory.GetDatabase(database);
            Assert.IsNotNull(this.Database, string.Format("Database '{0}' was not found", database));
        }

        public void Initialize(ISearchIndex index)
        {
            Assert.IsNotNull(index, "index");
            CrawlingLog.Log.Info(string.Format("[Index={0}] Initializing SynchronousStrategy with fix for issue #96740.", index.Name));

            this.index = index;

            var events = index.Locator.GetInstance<IEvent>();

            EventHub.ItemMoved += (sender, args) => this.HandleItemMoved(events.ExtractParameter<ItemUri>(args, 0), events.ExtractParameter<ID>(args, 1));
            EventHub.ItemCopied += (sender, args) => this.Run(events.ExtractParameter<ItemUri>(args, 0), true);

            EventHub.ItemUpdated += (sender, args) => this.Run(args, false);
            EventHub.ItemVersionAdded += (sender, args) => this.RunAddedVersion(args);
            EventHub.ItemCopied += (sende, args) => this.Run(events.ExtractParameter<ItemUri>(args, 0), true);
            EventHub.ItemVersionDeleted += (sender, args) => this.RunDeletedVersion(events.ExtractParameter<ItemUri>(args, 0));
            EventHub.ItemDeleted += (sender, args) => this.RunDeleted(events.ExtractParameter<ID>(args, 0), events.ExtractParameter<string>(args, 1));
        }

        public void RunAddedVersion(EventArgs args)
        {
            Assert.ArgumentNotNull(args, "args");

            ItemUri addedItemUri = this.GetItemUriOfAddedVersion(args);

            if (addedItemUri == null)
            {
                var indexName = this.index == null ? "Unknown" : this.index.Name;
                CrawlingLog.Log.Warn(string.Format(CouldntRetreiveUri, indexName));

                return;
            }

            if (this.NeedBreakOperation(addedItemUri.DatabaseName))
            {
                return;
            }

            IndexableInfo indexableInfo = new IndexableInfo((SitecoreItemUniqueId)addedItemUri, this.index.Summary.LastUpdated)
            {
                IsVersionAdded = true
            };

            IndexCustodian.UpdateItem(this.index, indexableInfo);
        }

        public void RunDeletedVersion(ItemUri itemUri)
        {
            Assert.ArgumentNotNull(itemUri, "itemUri");

            if (this.NeedBreakOperation(itemUri.DatabaseName))
            {
                return;
            }

            IndexCustodian.DeleteVersion(this.index, (SitecoreItemUniqueId)itemUri);
        }

        public void RunDeleted(ID id, string databaseName)
        {
            Assert.ArgumentNotNull(id, "id");
            Assert.ArgumentNotNull(databaseName, "databaseName");

            if (this.NeedBreakOperation(databaseName))
            {
                return;
            }

            IndexCustodian.DeleteItem(this.index, (SitecoreItemId)id);
        }

        public void Run(ItemUri itemUri, bool rebuildDescendants)
        {
            Assert.ArgumentNotNull(itemUri, "itemUri");

            this.Run(itemUri, rebuildDescendants, false, false);
        }

        public void Run(EventArgs args, bool rebuildDescendants)
        {
            Assert.ArgumentNotNull(args, "args");

            ItemUri itemUri = null;
            bool isSharedFieldChanged = false;
            bool isUnversionedFieldChanged = false;

            EventArgs eventArgs = ContentSearchManager.Locator.GetInstance<IEvent>().ExtractParameter<EventArgs>(args, 1);

            if (eventArgs is ItemSavedRemoteEventArgs)
            {
                ItemSavedRemoteEventArgs itemSavedRemoteEventArgs = eventArgs as ItemSavedRemoteEventArgs;

                List<FieldChange> realFieldChanges = this.GetRealFieldChanges(itemSavedRemoteEventArgs.Changes.FieldChanges);
                itemUri = itemSavedRemoteEventArgs.Item.Uri;
                isSharedFieldChanged = realFieldChanges.Any(fieldChange => fieldChange.IsShared);
                isUnversionedFieldChanged = realFieldChanges.Any(fieldChange => fieldChange.IsUnversioned);
            }

            if (eventArgs is ExecutedEventArgs<Data.Engines.DataCommands.SaveItemCommand>)
            {
                ExecutedEventArgs<Data.Engines.DataCommands.SaveItemCommand> executedEventArgs = eventArgs as ExecutedEventArgs<Data.Engines.DataCommands.SaveItemCommand>;

                List<FieldChange> realFieldChanges = this.GetRealFieldChanges(executedEventArgs.Command.Changes.FieldChanges);
                itemUri = executedEventArgs.Command.Item.Uri;
                isSharedFieldChanged = realFieldChanges.Any(fieldChange => fieldChange.IsShared);
                isUnversionedFieldChanged = realFieldChanges.Any(fieldChange => fieldChange.IsUnversioned);
            }

            this.Run(itemUri, rebuildDescendants, isSharedFieldChanged, isUnversionedFieldChanged);
        }

        public void Run(ItemUri itemUri, bool rebuildDescendants, bool isSharedFieldChanged, bool isUnversionedFieldChanged)
        {
            if (this.NeedBreakOperation(itemUri.DatabaseName))
            {
                return;
            }
            #region fix for issue #96740
            //if (!this.IsItemUnderCrawlerRoot(itemUri))
            //{
            //    return;
            //}
            #endregion

            if (rebuildDescendants)
            {
                var item = Database.GetItem(itemUri);
                IndexCustodian.Refresh(this.index, (SitecoreIndexableItem)item);
                return;
            }

            IndexableInfo indexableInfo = new IndexableInfo((SitecoreItemUniqueId)itemUri, index.Summary.LastUpdated)
            {
                IsUnversionedFieldChanged = isUnversionedFieldChanged,
                IsSharedFieldChanged = isSharedFieldChanged
            };

            IndexCustodian.UpdateItem(this.index, indexableInfo);
        }

        #endregion


        #region Private methods

        private List<FieldChange> GetRealFieldChanges(FieldChangeList fieldChangeList)
        {
            List<FieldChange> result = new List<FieldChange>();

            foreach (FieldChange fieldChange in fieldChangeList)
            {
                if (fieldChange.OriginalValue != fieldChange.Value)
                {
                    result.Add(fieldChange);
                }
            }

            return result;
        }

        private bool IsItemUnderCrawlerRoot(ItemUri itemUri)
        {
            bool isItemUnderRoot = true;

            foreach (var crawler in this.index.Crawlers)
            {
                var sitecoreItemCrawler = crawler as SitecoreItemCrawler;

                if (sitecoreItemCrawler != null)
                {
                    var root = Database.GetItem(sitecoreItemCrawler.Root);
                    if (root == null)
                    {
                        isItemUnderRoot = false;
                        continue;
                    }

                    string crawlerRoot = root.Paths.LongID;
                    var item = Database.GetItem(itemUri);

                    if (item != null)
                    {
                        isItemUnderRoot = item.Paths.LongID.StartsWith(crawlerRoot);

                        if (isItemUnderRoot)
                        {
                            break;
                        }
                    }
                }
            }

            return isItemUnderRoot;
        }

        private bool NeedBreakOperation(string databaseName)
        {
            bool needBreakOperation = !this.Database.Name.Equals(databaseName);

            if (IndexCustodian.IsIndexingPaused(this.index))
            {
                CrawlingLog.Log.Warn(string.Format("[Index={0}] Synchronous Indexing Strategy is disabled while indexing is paused.", this.index.Name));
                needBreakOperation = true;
            }

            if (BulkUpdateContext.IsActive)
            {
                CrawlingLog.Log.Debug("Synchronous Indexing Strategy is disabled during BulkUpdateContext");
                needBreakOperation = true;
            }

            return needBreakOperation;
        }

        private ItemUri GetItemUriOfAddedVersion(EventArgs args)
        {
            ItemUri itemUri = null;
            var eventArgs = ContentSearchManager.Locator.GetInstance<IEvent>().ExtractParameter<EventArgs>(args, 0);

            if (eventArgs is VersionAddedEventArgs)
            {
                var itemSavedRemoteEventArgs = eventArgs as VersionAddedEventArgs;

                itemUri = itemSavedRemoteEventArgs.Item.Uri;
            }

            if (eventArgs is ExecutedEventArgs<Data.Engines.DataCommands.AddVersionCommand>)
            {
                var executedEventArgs = eventArgs as ExecutedEventArgs<Data.Engines.DataCommands.AddVersionCommand>;

                itemUri = executedEventArgs.Command.Item.Uri;
            }

            if (eventArgs is AddedVersionRemoteEventArgs)
            {
                AddedVersionRemoteEventArgs itemSavedRemoteEventArgs = eventArgs as AddedVersionRemoteEventArgs;

                itemUri = itemSavedRemoteEventArgs.Item.Uri;
            }

            if (itemUri != null)
            {
                itemUri = new ItemUri(itemUri.ItemID, itemUri.Language, Data.Version.Latest, itemUri.DatabaseName);
                var item = Database.GetItem(itemUri);

                return item != null ? item.Uri : null;
            }

            return null;
        }

        private void HandleItemMoved(ItemUri itemUri, ID oldParentId)
        {
            if (this.NeedBreakOperation(itemUri.DatabaseName))
            {
                return;
            }
            #region fix for issue #96740
            //if (this.IsItemUnderCrawlerRoot(itemUri))
            //{
            //    this.Run(itemUri, true, false, false);
            //    return;
            //}

            //var oldParentUri = new ItemUri(oldParentId, this.factory.GetDatabase(itemUri.DatabaseName));
            //if (this.IsItemUnderCrawlerRoot(oldParentUri))
            //{
            #endregion
            this.RunDeleted(itemUri.ItemID, itemUri.DatabaseName);
            #region fix for issue #96740
            //}

            this.Run(itemUri, true, false, false);
            #endregion
        }

        #endregion
    }
}