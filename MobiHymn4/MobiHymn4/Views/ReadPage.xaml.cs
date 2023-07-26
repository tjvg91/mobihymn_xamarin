using System;
using System.Linq;

using Xamarin.CommunityToolkit.Extensions;
using Xamarin.Forms;

using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using MobiHymn4.Views.Popups;

using Plugin.AudioRecorder;
using Xamarin.Essentials;
using MobiHymn4.Models;
using System.Threading.Tasks;

namespace MobiHymn4.Views
{
    public partial class ReadPage : ContentPage
    {
        ReadViewModel model;
        ToolbarItem tbBookmark;
        IMidiHelper player;
        bool hasMidi;

        private Globals globalInstance = Globals.Instance;

        public ReadPage()
        {
            InitializeComponent();

            //BindingContext = new ReadViewModel(DependencyService.Get<IPlayService>());
            model = (ReadViewModel)this.BindingContext;
            model.OnBookmarked += Model_OnBookmarked;
            model.OnHymnChanged += Model_OnHymnChanged;

            tbBookmark = (ToolbarItem)FindByName("tbBookmarks");
            tbBookmark.IconImageSource = new FontImageSource
            {
                FontFamily = model.BookmarkFont,
                Glyph = FontAwesome.FontAwesomeIcons.Heart,
                Size = 17,
            };

            InitMidi();
            lblCurTime.Text = sldlrPlayer.Value.ToMinSec();
        }

        private async void InitMidi(string fileName = "")
        {
            try
            {
                player = DependencyService.Get<IMidiHelper>();
                hasMidi = await player.Load(string.IsNullOrEmpty(fileName) ? globalInstance.ActiveHymn.MidiFileName : fileName);
            }
            catch (Exception ex)
            {

            }
        }

        private async void AddBookmark(string groupName)
        {
            globalInstance.AddBookmark(groupName);

            await System.Threading.Tasks.Task.Delay(500);
            Globals.ShowToastPopup("bookmark-saved", "Bookmard added.",
                    DeviceInfo.Platform == DevicePlatform.Android ? 120 : 0.5,
                    DeviceInfo.Platform == DevicePlatform.Android ? new Rectangle(0.5, -0.1, 2, 2) : new Rectangle(0.8, -0.8, 1, 0.9));
            model.BookmarkFont = "FAS";

            tbBookmark.IconImageSource = new FontImageSource
            {
                FontFamily = model.BookmarkFont,
                Glyph = FontAwesome.FontAwesomeIcons.Heart,
                Size = 17,
            };
        }

        private void InitPopup()
        {
            model.IsBookmarkGroupsShown = false;

            var inpPopup = new InputPopup
            {
                Title = "Create Group",
                ActionString = "Create",
                Validation = (newKey) =>
                {
                    return model.GroupKeys.Where(key => key.Name.ToLower().Equals(newKey.ToLower())).Count() > 0 ?
                            "Group already exists." : "";
                }
            };
            inpPopup.OK += InpPopup_OK;
            Navigation.ShowPopup(inpPopup);
        }

        private void Model_OnHymnChanged(object sender, EventArgs e)
        {
            var activeHymn = (Hymn)sender;
            InitMidi(activeHymn.MidiFileName);
        }

        private async void Model_OnBookmarked(object sender, EventArgs e)
        {
            await Task.Delay(250);
            tbBookmark.IconImageSource = new FontImageSource
            {
                FontFamily = (string)sender,
                Glyph = FontAwesome.FontAwesomeIcons.Heart,
                Size = 17,
            };
        }

        async void btnHome_Clicked(System.Object sender, System.EventArgs e)
        {
            await Shell.Current.GoToAsync($"//{Routes.HOME}");
        }

        async void tbSearch_Clicked(System.Object sender, System.EventArgs e)
        {
            await Shell.Current.GoToAsync($"//{Routes.SEARCH}");
        }

        void tbSettings_Clicked(System.Object sender, System.EventArgs e)
        {
            Navigation.ShowPopup(new SettingsPopup
            {
                IsLightDismissEnabled = true
            });
        }

        void PinchGestureRecognizer_PinchUpdated(System.Object sender, Xamarin.Forms.PinchGestureUpdatedEventArgs e)
        {
            if(e.Status == GestureStatus.Running)
            {
                globalInstance.ActiveFontSize = (e.Scale < 1) ?
                    Math.Max(globalInstance.ActiveFontSize * e.Scale, 15) :
                    Math.Min(globalInstance.ActiveFontSize * e.Scale, 40);
            }
        }

        async void tbBookmarks_Clicked(System.Object sender, System.EventArgs e)
        {
            if (model.BookmarkFont == "FAR")
            {
                if (model.GroupKeys.Count > 0) model.IsBookmarkGroupsShown = true;
                else InitPopup();
            }
            else
            {
                var answer = await DisplayAlert("Delete?", $"Are you sure you want to delete Hymn #{globalInstance.ActiveHymn.Number} as bookmark?", "Yes", "No");
                if (answer)
                {
                    globalInstance.RemoveBookmark();
                    Globals.ShowToastPopup(
                        "bookmark-deleted",
                        "Bookmard deleted.",
                        DeviceInfo.Platform == DevicePlatform.Android ? 100 : 0.4,
                        DeviceInfo.Platform == DevicePlatform.Android ? new Rectangle(0.5, -0.2, 2, 2) : new Rectangle(0.8, -0.8, 1, 0.9));
                    model.BookmarkFont = "FAR";
                }
            }
        }

        void btnMdiSettings_Clicked(System.Object sender, System.EventArgs e)
        {
            Navigation.ShowPopup(new MidiPopup
            {
                IsLightDismissEnabled = true
            });
        }

        void btnPlay_Clicked(System.Object sender, System.EventArgs e)
        {
            if(hasMidi)
                player.Play();
            else
            {
                if (DeviceInfo.Platform == DevicePlatform.Android)
                    Globals.ShowToastPopup(
                        "not-found",
                        "MIDI not found",
                        130,
                        new Rectangle(0.3, 0, 0.8, 0.9)
                    );
                else
                    Globals.ShowToastPopup(
                        "not-found",
                        "MIDI not found",
                        0.7,
                        new Rectangle(0.3, -0.5, 0.8, 0.9)
                    );
            }
        }

        void TapGestureRecognizer_Tapped(System.Object sender, System.EventArgs e)
        {
            model.IsReadView = !model.IsReadView;
        }

        void Slider_ValueChanged(System.Object sender, Xamarin.Forms.ValueChangedEventArgs e)
        {
            lblCurTime.Text = e.NewValue.ToMinSec();
        }

        void ToggleEditor(System.Object sender, System.EventArgs e)
        {
            model.IsSelectable = !model.IsSelectable;
        }

        void btnGrpCancel_Clicked(System.Object sender, System.EventArgs e)
        {
            model.IsBookmarkGroupsShown = false;
        }

        void TapGestureRecognizer_Tapped_1(System.Object sender, System.EventArgs e)
        {
            model.IsBookmarkGroupsShown = false;
            var groupName = ((TappedEventArgs)e).Parameter as string;
            AddBookmark(groupName);
        }

        private void InpPopup_OK(object sender, EventArgs e)
        {
            AddBookmark((string)sender);
        }

        void btnAddNewGroup_Clicked(System.Object sender, System.EventArgs e)
        {
            InitPopup();
        }

        void btnSelectable_Clicked(System.Object sender, System.EventArgs e)
        {
            model.IsSelectable = !model.IsSelectable;
        }

        void btnNotSelectable_Clicked(System.Object sender, System.EventArgs e)
        {
            model.IsSelectable = !model.IsSelectable;
        }
    }
}

