using System;
using System.Collections.Generic;
using System.Linq;
using MobiHymn4.Models;
using MobiHymn4.Utils;
using MvvmHelpers;
using Xamarin.Forms;

namespace MobiHymn4.ViewModels
{
	public class SettingsViewModel : MvvmHelpers.BaseViewModel
    {
        private Globals globalInstance = Globals.Instance;

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

        private int syncCount;
        public int SyncCount
        {
            get => syncCount;
            set
            {
                syncCount = value;
                SetProperty(ref syncCount, value, nameof(SyncCount));
                OnPropertyChanged();
            }
        }

        public SettingsViewModel()
        {
            IsDarkMode = globalInstance.DarkMode;
            KeepAwake = globalInstance.KeepAwake;
            IsOrientationLocked = globalInstance.IsOrientationLocked;
            IsBusy = globalInstance.IsFetchingSyncDetails;

            InitResync();

            globalInstance.DarkModeChanged += GlobalInstance_DarkModeChanged;
            globalInstance.KeepAwakeChanged += GlobalInstance_KeepAwakeChanged;
            globalInstance.OrientationLockedChanged += GlobalInstance_OrientationLockedChanged;
            globalInstance.IsFetchingSyncDetailsChanged += GlobalInstance_IsFetchingSyncDetailsChanged;
            globalInstance.ResyncDetails.CollectionChanged += ResyncDetails_CollectionChanged;
        }

        private void ResyncDetails_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            InitResync();
        }

        private void GlobalInstance_IsFetchingSyncDetailsChanged(object sender, EventArgs e)
        {
            InitResync();
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

        private void InitResync()
        {
            ResyncList = globalInstance.ResyncDetails.ToObservableRangeCollection();

            var createDetails = ResyncList.Where(detail => detail.Mode == CRUD.Create)
                        .Select(detail => detail.Number == "*" ? "All" : $"{detail.Number.ToTitle()} ({Enum.GetName(detail.Type.GetType(), detail.Type)})")
                        .ToObservableRangeCollection();
            var updateDetails = ResyncList.Where(detail => detail.Mode == CRUD.Update)
                        .Select(detail => detail.Number == "*" ? "All" : $"{detail.Number.ToTitle()} ({Enum.GetName(detail.Type.GetType(), detail.Type)})")
                        .ToObservableRangeCollection();
            var deleteDetails = ResyncList.Where(detail => detail.Mode == CRUD.Delete)
                        .Select(detail => detail.Number == "*" ? "All" : $"{detail.Number.ToTitle()} ({Enum.GetName(detail.Type.GetType(), detail.Type)})")
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
    }
}

