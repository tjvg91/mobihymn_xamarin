using System;
using System.Collections.Generic;
using System.Linq;

using FontAwesome;
using MobiHymn4.Models;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;

using Microsoft.Maui.Controls;

using System.Threading.Tasks;

namespace MobiHymn4.Views
{
    public partial class BookmarksGroupPage : ContentPage
    {
        private Globals globalInstance = Globals.Instance;
        private BookmarksViewModel model;
        private ToolbarItem tbSelect;
        private ToolbarItem tbSelectAll;
        private ToolbarItem tbDeleteSelected;
        private ToolbarItem tbCancelSelection;
        private bool isSelectionMode;
        private bool suppressSelectionChanged;

        public BookmarksGroupPage()
        {
            InitializeComponent();
            model = (BookmarksViewModel)BindingContext;
            model.OnBookmarksChanged += Model_OnBookmarksChanged;
            BuildSelectionToolbarItems();
            SetSelectionMode(false);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            model?.RefreshBookmarks();
            if (!isSelectionMode)
                SetSelectionMode(false);
        }

        void Model_OnBookmarksChanged(object sender, EventArgs e)
        {
            if (!isSelectionMode)
                SetSelectionMode(false);
            else
                UpdateSelectionActions();
        }

        bool HasSavedBookmarks() => model.GroupKeys?.Count > 0;

        private void BuildSelectionToolbarItems()
        {
            tbSelect = new ToolbarItem { IconImageSource = CreateToolbarIcon(FontAwesomeIcons.SquareCheck) };
            tbSelect.Clicked += tbSelect_Clicked;

            tbSelectAll = new ToolbarItem { IconImageSource = CreateToolbarIcon(FontAwesomeIcons.CheckDouble) };
            tbSelectAll.Clicked += tbSelectAll_Clicked;

            tbDeleteSelected = new ToolbarItem { Text = "Delete", IconImageSource = CreateToolbarIcon(FontAwesomeIcons.TrashCan) };
            tbDeleteSelected.Clicked += tbDeleteSelected_Clicked;

            tbCancelSelection = new ToolbarItem { Text = "Cancel", IconImageSource = CreateToolbarIcon(FontAwesomeIcons.ArrowLeft) };
            tbCancelSelection.Clicked += tbCancelSelection_Clicked;
        }

        private static FontImageSource CreateToolbarIcon(string glyph) =>
            new FontImageSource
            {
                FontFamily = "FAS",
                Glyph = glyph,
                Size = 18,
                Color = (Color)Application.Current.Resources["PrimaryText"]
            };

        private void SetSelectionMode(bool enabled)
        {
            isSelectionMode = enabled;
            suppressSelectionChanged = true;
            GroupListView.SelectedItems?.Clear();
            GroupListView.SelectedItem = null;
            GroupListView.SelectionMode = enabled ? SelectionMode.Multiple : SelectionMode.Single;
            suppressSelectionChanged = false;

            ToolbarItems.Clear();
            if (enabled)
            {
                ToolbarItems.Add(tbSelectAll);
                ToolbarItems.Add(tbDeleteSelected);
                ToolbarItems.Add(tbCancelSelection);
                Title = "Bookmarks";
                UpdateSelectionActions();
                return;
            }

            if (HasSavedBookmarks())
                ToolbarItems.Add(tbSelect);
            Title = "Bookmarks";
        }

        private List<GroupDisplay> GetSelectedGroups() =>
            GroupListView.SelectedItems?.OfType<GroupDisplay>().ToList() ?? new List<GroupDisplay>();

        private void UpdateSelectionActions()
        {
            var count = GetSelectedGroups().Count;
            var total = model.GroupKeys?.Count ?? 0;
            tbSelectAll.IsEnabled = total > 0 && count < total;
            tbDeleteSelected.IsEnabled = count > 0;
        }

        private void GroupSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (suppressSelectionChanged)
                return;

            if (isSelectionMode)
            {
                UpdateSelectionActions();
                return;
            }

            var selected = e.CurrentSelection?.FirstOrDefault() as GroupDisplay;
            if (selected == null)
                return;

            suppressSelectionChanged = true;
            GroupListView.SelectedItem = null;
            suppressSelectionChanged = false;

            if (model.BookmarkGroupSelected?.CanExecute(selected.Name) == true)
                model.BookmarkGroupSelected.Execute(selected.Name);
        }

        private void tbSelect_Clicked(object sender, EventArgs e) =>
            SetSelectionMode(true);

        private void tbSelectAll_Clicked(object sender, EventArgs e)
        {
            if (model.GroupKeys == null || model.GroupKeys.Count == 0)
                return;

            suppressSelectionChanged = true;
            GroupListView.SelectedItems?.Clear();
            foreach (var group in model.GroupKeys)
                GroupListView.SelectedItems?.Add(group);
            suppressSelectionChanged = false;

            UpdateSelectionActions();
        }

        private void tbCancelSelection_Clicked(object sender, EventArgs e) =>
            SetSelectionMode(false);

        private async void tbDeleteSelected_Clicked(object sender, EventArgs e)
        {
            var selected = GetSelectedGroups();
            if (selected.Count == 0)
                return;

            var answer = await DisplayAlert(
                "Delete groups?",
                $"Delete {selected.Count} selected group{(selected.Count == 1 ? "" : "s")} and all their bookmarks?",
                "Yes",
                "No");

            if (!answer)
                return;

            var deletedCount = selected.Count;
            foreach (var group in selected)
                globalInstance.RemoveBookmarkGroup(group.Name);

            SetSelectionMode(false);
            Globals.ShowToastPopup(
                "bookmark-groups-deleted",
                $"{deletedCount} bookmark group{(deletedCount == 1 ? "" : "s")} deleted.",
                DeviceInfo.Platform == DevicePlatform.Android ? 100 : 0.4);
        }

        async void DeleteGroup_Invoked(System.Object sender, System.EventArgs e)
        {
            try
            {
                var swipeItem = (SwipeItem)sender;
                var group = (GroupDisplay)swipeItem.CommandParameter;
                if (group == null)
                    return;

                var answer = await DisplayAlert(
                    "Delete group?",
                    $"Delete \"{group.Name}\" and its {group.CountString}?",
                    "Yes",
                    "No");

                if (!answer)
                    return;

                if (globalInstance.RemoveBookmarkGroup(group.Name))
                {
                    Globals.ShowToastPopup(
                        "bookmark-group-deleted",
                        "Bookmark group deleted.",
                        DeviceInfo.Platform == DevicePlatform.Android ? 100 : 0.4);
                }
            }
            catch (Exception ex)
            {
                Console.Write(ex.Message);
            }
        }
    }
}
