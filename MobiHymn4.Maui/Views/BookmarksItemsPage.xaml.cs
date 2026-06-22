using System;
using System.Linq;
using System.Collections.Generic;
using FontAwesome;
using MobiHymn4.Models;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using MobiHymn4.Views.Popups;
using CommunityToolkit.Maui.Views;
using System.Threading.Tasks;

namespace MobiHymn4.Views
{
    [QueryProperty(nameof(Name), "name")]
	public partial class BookmarksItemsPage : ContentPage
	{
        private Globals globalInstance = Globals.Instance;
        private BookmarksViewModel model;
        private ToolbarItem tbSelect;
        private ToolbarItem tbSelectAll;
        private ToolbarItem tbMoveSelected;
        private ToolbarItem tbDeleteSelected;
        private ToolbarItem tbCancelSelection;
        private bool isSelectionMode;
        private bool suppressSelectionChanged;

        private string name;
        public string Name
        {
            get => name;
            set
            {
                name = value;
                Title = value.Capitalize();
                var group = model.BookmarksList.FirstOrDefault(bk => bk.Key == value);
                model.BookmarksPerKey = (group ?? Enumerable.Empty<ShortHymn>())
                    .OrderBy(bk => bk.Line)
                    .ToObservableRangeCollection();
            }
        }

        public BookmarksItemsPage ()
		{
			InitializeComponent ();
            model = (BookmarksViewModel)BindingContext;
            model.IsBookmarkGroupsShown = false;
            model.OnBookmarksChanged += Model_OnBookmarksChanged;
            BuildSelectionToolbarItems();
            SetSelectionMode(false);
        }

        private async void Model_OnBookmarksChanged(object sender, EventArgs e)
        {
            if (isSelectionMode)
                UpdateSelectionActions();

            var initQuery = model.BookmarksList.Where(bk => bk.Key == Name);
            if (initQuery.Count() == 0)
            {
                await Task.Delay(500);
                await Shell.Current.GoToAsync("..");
            }
            else
                model.BookmarksPerKey = initQuery.First().OrderBy(bk => bk.Line).ToObservableRangeCollection();
        }

        private void BuildSelectionToolbarItems()
        {
            tbSelect = new ToolbarItem { Text = "Select" };
            tbSelect.Clicked += tbSelect_Clicked;

            tbSelectAll = new ToolbarItem { Text = "Select All", IconImageSource = CreateToolbarIcon(FontAwesomeIcons.Check) };
            tbSelectAll.Clicked += tbSelectAll_Clicked;

            tbMoveSelected = new ToolbarItem { Text = "Move", IconImageSource = CreateToolbarIcon(FontAwesomeIcons.LayerGroup) };
            tbMoveSelected.Clicked += tbMoveSelected_Clicked;

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
            MyListView.SelectedItems?.Clear();
            MyListView.SelectedItem = null;
            MyListView.SelectionMode = enabled ? SelectionMode.Multiple : SelectionMode.Single;
            suppressSelectionChanged = false;

            ToolbarItems.Clear();
            if (enabled)
            {
                ToolbarItems.Add(tbSelectAll);
                ToolbarItems.Add(tbMoveSelected);
                ToolbarItems.Add(tbDeleteSelected);
                ToolbarItems.Add(tbCancelSelection);
                UpdateSelectionActions();
                return;
            }

            ToolbarItems.Add(tbSelect);
            ToolbarItems.Add(tbHome);
            Title = Name?.Capitalize();
        }

        private List<ShortHymn> GetSelectedBookmarks() =>
            MyListView.SelectedItems?.OfType<ShortHymn>().ToList() ?? new List<ShortHymn>();

        private void UpdateSelectionActions()
        {
            var count = GetSelectedBookmarks().Count;
            var total = model.BookmarksPerKey?.Count ?? 0;
            tbSelectAll.IsEnabled = total > 0 && count < total;
            tbMoveSelected.IsEnabled = count > 0;
            tbDeleteSelected.IsEnabled = count > 0;
            Title = count == 0 ? "Select bookmarks" : $"{count} selected";
        }

        private void BookmarkSelection_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (suppressSelectionChanged)
                return;

            if (isSelectionMode)
            {
                UpdateSelectionActions();
                return;
            }

            var selected = e.CurrentSelection?.FirstOrDefault() as ShortHymn;
            if (selected == null)
                return;

            suppressSelectionChanged = true;
            MyListView.SelectedItem = null;
            suppressSelectionChanged = false;

            if (model.BookmarkSelected?.CanExecute(selected) == true)
                model.BookmarkSelected.Execute(selected);
        }

        private void tbSelect_Clicked(object sender, EventArgs e) =>
            SetSelectionMode(true);

        private void tbSelectAll_Clicked(object sender, EventArgs e)
        {
            if (model.BookmarksPerKey == null || model.BookmarksPerKey.Count == 0)
                return;

            suppressSelectionChanged = true;
            MyListView.SelectedItems?.Clear();
            foreach (var bookmark in model.BookmarksPerKey)
                MyListView.SelectedItems?.Add(bookmark);
            suppressSelectionChanged = false;

            UpdateSelectionActions();
        }

        private void tbCancelSelection_Clicked(object sender, EventArgs e) =>
            SetSelectionMode(false);

        private async void tbDeleteSelected_Clicked(object sender, EventArgs e)
        {
            var selected = GetSelectedBookmarks();
            if (selected.Count == 0)
                return;

            var answer = await DisplayAlert(
                "Delete bookmarks?",
                $"Delete {selected.Count} selected bookmark{(selected.Count == 1 ? "" : "s")}?",
                "Yes",
                "No");

            if (!answer)
                return;

            if (globalInstance.RemoveBookmarks(selected))
            {
                SetSelectionMode(false);
                Globals.ShowToastPopup(
                    "bookmarks-deleted",
                    "Bookmarks deleted.",
                    DeviceInfo.Platform == DevicePlatform.Android ? 100 : 0.4);
            }
        }

        private void tbMoveSelected_Clicked(object sender, EventArgs e)
        {
            if (GetSelectedBookmarks().Count == 0)
                return;

            model.GroupKeysExceptCurrent = (from key in model.GroupKeys
                                            where key.Name != Name
                                            select key).ToObservableRangeCollection();

            ShowMoveToPopup();
        }

        async void SwipeItem_Invoked(System.Object sender, System.EventArgs e)
        {
            try
            {
                var swipeItem = (SwipeItem)sender;
                var shortHymn = (ShortHymn)swipeItem.CommandParameter;

                var answer = await DisplayAlert("Delete?", $"Are you sure you want to delete Hymn #{shortHymn.Number} as bookmark?", "Yes", "No");
                if (answer)
                {
                    globalInstance.RemoveBookmark(shortHymn);
                    Globals.ShowToastPopup(
                        "bookmark-deleted",
                        "Bookmark deleted.",
                        DeviceInfo.Platform == DevicePlatform.Android ? 100 : 0.4);
                }
            }
            catch (Exception ex)
            {
                Console.Write(ex.Message);
            }
        }

        async void MyListView_ChildAdded(System.Object sender, Microsoft.Maui.Controls.ElementEventArgs e)
        {
            StackLayout item = e.Element as StackLayout;
            if (item != null)
            {
                item.Opacity = 0;
                await item.FadeTo(1, globalInstance.Duration);
            }
        }

        async void SwipeItem_Invoked_1(System.Object sender, System.EventArgs e)
        {
            var swipeItem = (SwipeItem)sender;
            var shortHymn = (ShortHymn)swipeItem.CommandParameter;

            model.GroupKeysExceptCurrent = (from key in model.GroupKeys
                                              where key.Name != shortHymn.BookmarkGroup
                                            select key).ToObservableRangeCollection();

            await Task.Delay(500);
            ShowMoveToPopup();
        }

        void TapGestureRecognizer_Tapped(System.Object sender, System.EventArgs e)
        {
            var groupName = (string)((TappedEventArgs)e).Parameter;
            SetNewGroup(groupName);
        }

        void btnAddNewGroup_Clicked(System.Object sender, System.EventArgs e)
        {
            model.IsBookmarkGroupsShown = false;
            ShowMoveToPopup();
        }

        private void InpPopup_OK(object sender, EventArgs e)
        {
            var groupName = (string)sender;
            SetNewGroup(groupName);
        }

        void btnGrpCancel_Clicked(System.Object sender, System.EventArgs e)
        {
            model.IsBookmarkGroupsShown = false;
        }

        void SwipeView_SwipeStarted(System.Object sender, Microsoft.Maui.Controls.SwipeStartedEventArgs e)
        {
            model.ActiveBookmark = ((SwipeView)sender).BindingContext as ShortHymn;
        }

        async void SetNewGroup(string groupName)
        {
            var selected = isSelectionMode ? GetSelectedBookmarks() : new List<ShortHymn>();
            if (selected.Count > 0)
            {
                foreach (var bookmark in selected)
                {
                    var savedBookmark = globalInstance.BookmarkList.FirstOrDefault(bk => bk.Number == bookmark.Number);
                    if (savedBookmark != null)
                        savedBookmark.BookmarkGroup = groupName;
                }

                SetSelectionMode(false);
            }
            else
            {
                globalInstance.BookmarkList.Where(bk => bk.Number == model.ActiveBookmark.Number)
                    .First().BookmarkGroup = groupName;
            }

            globalInstance.ForceBookmarkChangedEvent();
            await Task.Delay(500);
            Globals.ShowToastPopup("bookmark-saved", "Bookmark moved.",
                    DeviceInfo.Platform == DevicePlatform.Android ? 120 : 0.5);
        }

        async void tbHome_Clicked(System.Object sender, System.EventArgs e)
        {
            if (Shell.Current?.Navigation?.NavigationStack?.Count > 1)
                await Shell.Current.Navigation.PopToRootAsync(false);

            await Shell.Current.GoToAsync($"//{Routes.READ}");
        }

        void ShowMoveToPopup()
        {
            model.IsBookmarkGroupsShown = false;

            var inpPopup = new InputPopup
            {
                Title = "Move To",
                ActionString = "Move",
                Validation = (newKey) =>
                    model.GroupKeys.Any(key => key.Name.Equals(newKey, StringComparison.OrdinalIgnoreCase))
                        ? "Group already exists." : ""
            };
            inpPopup.SetGroups(model.GroupKeysExceptCurrent.Select(group => group.Name));
            inpPopup.OK += InpPopup_OK;
            Navigation.ShowPopup(inpPopup);
        }
    }
}

