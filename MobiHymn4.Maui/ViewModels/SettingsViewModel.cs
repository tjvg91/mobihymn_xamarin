using System;
using System.Collections.Generic;
using System.Linq;
using MobiHymn4.Models;
using MobiHymn4.Services;
using MobiHymn4.Utils;
using MvvmHelpers;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace MobiHymn4.ViewModels
{
	public class SettingsViewModel : MvvmHelpers.BaseViewModel
    {
        private Globals globalInstance = Globals.Instance;
        private bool resyncInitialized;

        private bool isDarkMode;
        public bool IsDarkMode
        {
            get => isDarkMode;
            set
            {
                isDarkMode = value;
                SetProperty(ref isDarkMode, value, "IsDarkMode");
                OnPropertyChanged();
            }
        }

        private bool keepAwake;
        public bool KeepAwake
        {
            get => keepAwake;
            set
            {
                keepAwake = value;
                SetProperty(ref keepAwake, value, "KeepAwake");
                OnPropertyChanged();
            }
        }

        private bool isOrientationLocked;
        public bool IsOrientationLocked
        {
            get => isOrientationLocked;
            set
            {
                isOrientationLocked = value;
                SetProperty(ref isOrientationLocked, value, "IsOrientationLocked");
                OnPropertyChanged();
            }
        }

        private ObservableRangeCollection<ResyncDetail> resyncList;
        public ObservableRangeCollection<ResyncDetail> ResyncList
        {
            get => resyncList;
            set
            {
                resyncList = value;
                SetProperty(ref resyncList, value, nameof(ResyncList));
                OnPropertyChanged();
            }
        }

        private Timeline resyncCreateList;
        public Timeline ResyncCreateList
        {
            get => resyncCreateList;
            set
            {
                resyncCreateList = value;
                SetProperty(ref resyncCreateList, value, nameof(ResyncCreateList));
                OnPropertyChanged();
            }
        }

        private Timeline resyncUpdateList;
        public Timeline ResyncUpdateList
        {
            get => resyncUpdateList;
            set
            {
                resyncUpdateList = value;
                SetProperty(ref resyncUpdateList, value, nameof(ResyncUpdateList));
                OnPropertyChanged();
            }
        }

        private Timeline resyncDeleteList;
        public Timeline ResyncDeleteList
        {
            get => resyncDeleteList;
            set
            {
                resyncDeleteList = value;
                SetProperty(ref resyncDeleteList, value, nameof(ResyncDeleteList));
                OnPropertyChanged();
            }
        }

        private bool showCreate;
        public bool ShowCreate
        {
            get => showCreate;
            set
            {
                showCreate = value;
                SetProperty(ref showCreate, value, nameof(ShowCreate));
                OnPropertyChanged();
            }
        }

        private bool showUpdate;
        public bool ShowUpdate
        {
            get => showUpdate;
            set
            {
                showUpdate = value;
                SetProperty(ref showUpdate, value, nameof(ShowUpdate));
                OnPropertyChanged();
            }
        }

        private bool showDelete;
        public bool ShowDelete
        {
            get => showDelete;
            set
            {
                showDelete = value;
                SetProperty(ref showDelete, value, nameof(ShowDelete));
                OnPropertyChanged();
            }
        }

        private bool showSyncs;
        public bool ShowSyncs
        {
            get => showSyncs;
            set
            {
                showSyncs = value;
                SetProperty(ref showSyncs, value, nameof(ShowSyncs));
                OnPropertyChanged();
            }
        }

        private bool hasCatalogChanges;
        public bool HasCatalogChanges
        {
            get => hasCatalogChanges;
            set
            {
                if (SetProperty(ref hasCatalogChanges, value))
                    OnPropertyChanged(nameof(ShowViewChangesButton));
            }
        }

        private bool isOpeningChangesView;
        public bool IsOpeningChangesView
        {
            get => isOpeningChangesView;
            set
            {
                if (SetProperty(ref isOpeningChangesView, value))
                    OnPropertyChanged(nameof(ShowViewChangesButton));
            }
        }

        public bool ShowViewChangesButton => HasCatalogChanges && !IsOpeningChangesView;

        private bool showResyncBadge;
        public bool ShowResyncBadge
        {
            get => showResyncBadge;
            private set => SetProperty(ref showResyncBadge, value);
        }

        private bool resyncBadgeAcknowledged;
        private string badgeCatalogHash;

        private int syncCount;
        public int SyncCount
        {
            get => syncCount;
            set
            {
                syncCount = value;
                SetProperty(ref syncCount, value, nameof(SyncCount));
            }
        }

        private string syncSummary = string.Empty;
        public string SyncSummary
        {
            get => syncSummary;
            set
            {
                syncSummary = value;
                SetProperty(ref syncSummary, value, nameof(SyncSummary));
                OnPropertyChanged();
            }
        }

        private bool isCloudSyncing;
        public bool IsCloudSyncing
        {
            get => isCloudSyncing;
            set => SetProperty(ref isCloudSyncing, value);
        }

        private string cloudSyncStatus = string.Empty;
        public string CloudSyncStatus
        {
            get => cloudSyncStatus;
            set => SetProperty(ref cloudSyncStatus, value);
        }

        private bool canCloudSync;
        public bool CanCloudSync
        {
            get => canCloudSync;
            set => SetProperty(ref canCloudSync, value);
        }

        private ObservableRangeCollection<SyncChangeItem> syncChangeItems = new();
        public ObservableRangeCollection<SyncChangeItem> SyncChangeItems
        {
            get => syncChangeItems;
            set
            {
                syncChangeItems = value;
                SetProperty(ref syncChangeItems, value, nameof(SyncChangeItems));
                OnPropertyChanged();
            }
        }

        public SettingsViewModel()
        {
            IsDarkMode = globalInstance.DarkMode;
            KeepAwake = globalInstance.KeepAwake;
            IsOrientationLocked = globalInstance.IsOrientationLocked;
            IsBusy = globalInstance.IsFetchingSyncDetails;

            badgeCatalogHash = globalInstance.PendingCatalogDiff?.CatalogHash;
            UpdateCatalogSummary();
            RefreshCloudSyncState();

            globalInstance.DarkModeChanged += GlobalInstance_DarkModeChanged;
            globalInstance.KeepAwakeChanged += GlobalInstance_KeepAwakeChanged;
            globalInstance.OrientationLockedChanged += GlobalInstance_OrientationLockedChanged;
            globalInstance.IsFetchingSyncDetailsChanged += GlobalInstance_IsFetchingSyncDetailsChanged;
            globalInstance.CatalogDiffChanged += GlobalInstance_CatalogDiffChanged;

            try
            {
                var cloudSync = ServiceHelper.Get<IUserSettingsSyncService>();
                cloudSync.SyncStateChanged += (_, _) =>
                    MainThread.BeginInvokeOnMainThread(RefreshCloudSyncState);
                ServiceHelper.Get<IAuthService>().AuthStateChanged += (_, _) =>
                    MainThread.BeginInvokeOnMainThread(RefreshCloudSyncState);
            }
            catch
            {
            }
        }

        public void RefreshCloudSyncState()
        {
            try
            {
                var auth = ServiceHelper.Get<IAuthService>();
                var sync = ServiceHelper.Get<IUserSettingsSyncService>();
                CanCloudSync = auth.IsSignedIn;
                IsCloudSyncing = sync.IsSyncing;
                if (!auth.IsSignedIn)
                {
                    CloudSyncStatus = "Sign in to back up settings, bookmarks, and history.";
                    return;
                }

                if (Preferences.Get(PreferencesVar.CLOUD_SETTINGS_PENDING, false) && !sync.IsSyncing)
                {
                    CloudSyncStatus = string.IsNullOrWhiteSpace(sync.LastSyncStatus)
                        ? "Saved offline — will sync when online."
                        : sync.LastSyncStatus;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(sync.LastSyncStatus))
                    CloudSyncStatus = sync.LastSyncStatus;
                else if (sync.LastSyncedAt.HasValue)
                    CloudSyncStatus = $"Last synced {sync.LastSyncedAt.Value.ToLocalTime():g}";
                else
                {
                    var stored = Preferences.Get(PreferencesVar.CLOUD_SETTINGS_UPDATED_AT, string.Empty);
                    CloudSyncStatus = DateTime.TryParse(stored, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var at)
                        ? $"Last synced {at.ToLocalTime():g}"
                        : "Not synced yet.";
                }
            }
            catch
            {
                CanCloudSync = false;
                CloudSyncStatus = "Cloud sync unavailable.";
            }
        }

        private void GlobalInstance_CatalogDiffChanged(object sender, EventArgs e)
        {
            var hash = globalInstance.PendingCatalogDiff?.CatalogHash;
            if (!string.Equals(hash, badgeCatalogHash, StringComparison.Ordinal))
            {
                badgeCatalogHash = hash;
                resyncBadgeAcknowledged = false;
            }

            UpdateCatalogSummary();
        }

        public void AcknowledgeResyncBadge()
        {
            resyncBadgeAcknowledged = true;
            ShowResyncBadge = false;
        }

        private void GlobalInstance_MissingHymnCountChanged(object sender, EventArgs e)
        {
            SyncCount = globalInstance.MissingHymnCount;
            UpdateMissingSummary();
        }

        private void ResyncDetails_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            resyncInitialized = false;
        }

        private void GlobalInstance_IsFetchingSyncDetailsChanged(object sender, EventArgs e)
        {
            IsBusy = (bool)sender;
            resyncInitialized = false;
            UpdateCatalogSummary();
        }

        void UpdateCatalogSummary()
        {
            ShowSyncs = true;
            if (globalInstance.IsFetchingSyncDetails)
            {
                SyncSummary = "Checking the server for hymn changes…";
                HasCatalogChanges = false;
                return;
            }

            if (!string.IsNullOrWhiteSpace(globalInstance.CatalogDiffCheckError))
            {
                SyncSummary = globalInstance.CatalogDiffCheckError;
                HasCatalogChanges = false;
                return;
            }

            var diff = globalInstance.PendingCatalogDiff;
            HasCatalogChanges = diff?.ChangeCount > 0;
            ShowResyncBadge = HasCatalogChanges && !resyncBadgeAcknowledged;
            SyncCount = diff?.ChangeCount ?? 0;
            if (!HasCatalogChanges)
            {
                SyncSummary = "Your hymn catalog is up to date.";
                return;
            }

            var parts = new List<string>();
            if (diff.AddedCount > 0)
                parts.Add($"{diff.AddedCount} added");
            if (diff.ModifiedCount > 0)
                parts.Add($"{diff.ModifiedCount} modified");
            if (diff.RemovedCount > 0)
                parts.Add($"{diff.RemovedCount} removed");
            SyncSummary = $"{diff.ChangeCount} hymn change{(diff.ChangeCount == 1 ? string.Empty : "s")} available: {string.Join(", ", parts)}.";
        }

        void UpdateMissingSummary()
        {
            if (globalInstance.IsFetchingSyncDetails)
            {
                SyncSummary = "Checking for missing hymns…";
                ShowSyncs = true;
                return;
            }

            var count = globalInstance.MissingHymnCount;
            SyncCount = count;
            SyncSummary = Globals.FormatMissingHymnSummary(globalInstance.MissingHymnNumbers);
            ShowSyncs = true;
        }

        private void GlobalInstance_OrientationLockedChanged(object sender, EventArgs e)
        {
            IsOrientationLocked = (bool)sender;
        }

        private void GlobalInstance_KeepAwakeChanged(object sender, EventArgs e)
        {
            KeepAwake = (bool)sender;
        }

        private void GlobalInstance_DarkModeChanged(object sender, EventArgs e)
        {
            IsDarkMode = (bool)sender;
        }

        public void EnsureResyncInitialized()
        {
            if (resyncInitialized)
                return;

            resyncInitialized = true;
            InitResync();
        }

        private void InitResync()
        {
            ResyncList = globalInstance.ResyncDetails.ToObservableRangeCollection();

            var createDetails = BuildDetailStrings(ResyncList, CRUD.Create);
            var updateDetails = BuildDetailStrings(ResyncList, CRUD.Update);
            var deleteDetails = BuildDetailStrings(ResyncList, CRUD.Delete);

            SyncChangeItems = ResyncList
                .Select(FormatSyncChangeItem)
                .ToObservableRangeCollection();

            var fontSize = 20;

            ResyncCreateList = new Timeline
            {
                Header = "Add:",
                Details = createDetails,
                Height = fontSize * (createDetails.Count + 1)
            };

            ResyncUpdateList = new Timeline
            {
                Header = "Edit:",
                Details = updateDetails,
                Height = fontSize * (updateDetails.Count + 1)
            };
            ResyncDeleteList = new Timeline
            {
                Header = "Remove:",
                Details = deleteDetails,
                Height = fontSize * (deleteDetails.Count + 1)
            };

            ShowCreate = createDetails.Count > 0;
            ShowUpdate = updateDetails.Count > 0;
            ShowDelete = deleteDetails.Count > 0;

            SyncCount = ResyncList.Count;
            ShowSyncs = SyncCount > 0;
        }

        static SyncChangeItem FormatSyncChangeItem(ResyncDetail detail) =>
            new()
            {
                ModeLabel = detail.Mode switch
                {
                    CRUD.Create => "Add",
                    CRUD.Update => "Edit",
                    CRUD.Delete => "Remove",
                    _ => detail.Mode.ToString()
                },
                Description = FormatResyncDescription(detail)
            };

        static ObservableRangeCollection<string> BuildDetailStrings(IEnumerable<ResyncDetail> details, CRUD mode) =>
            details
                .Where(detail => detail.Mode == mode)
                .Select(FormatResyncDescription)
                .ToObservableRangeCollection();

        static string FormatResyncDescription(ResyncDetail detail) =>
            detail.Number == "*"
                ? $"All {Enum.GetName(detail.Type.GetType(), detail.Type)} files"
                : $"{detail.Number.ToTitle()} ({Enum.GetName(detail.Type.GetType(), detail.Type)})";
    }
}

