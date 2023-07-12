using System;

using Xamarin.CommunityToolkit.Extensions;
using Xamarin.Forms;

using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using MobiHymn4.Views.Popups;

using Plugin.AudioRecorder;
using Xamarin.Essentials;

namespace MobiHymn4.Views
{
    public partial class ReadPage : ContentPage
    {
        ReadViewModel model;
        ToolbarItem tbBookmark;

        private Globals globalInstance = Globals.Instance;

        public ReadPage()
        {
            InitializeComponent();

            tbBookmark = (ToolbarItem)FindByName("tbBookmarks");
            tbBookmark.IconImageSource = new FontImageSource
            {
                FontFamily = "FAR",
                Glyph = FontAwesome.FontAwesomeIcons.Heart,
                Size = 17,
            };

            //BindingContext = new ReadViewModel(DependencyService.Get<IPlayService>());
            model = (ReadViewModel)this.BindingContext;
            model.OnBookmarked += Model_OnBookmarked;

            lblCurTime.Text = sldlrPlayer.Value.ToMinSec();
        }

        private void Model_OnBookmarked(object sender, EventArgs e)
        {
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
                globalInstance.AddBookmark();
                Globals.ShowToastPopup("bookmark-saved", "Bookmard added.",
                        DeviceInfo.Platform == DevicePlatform.Android ? 120 : 0.5,
                        DeviceInfo.Platform == DevicePlatform.Android ? new Rectangle(0.5, -0.1, 2, 2) : new Rectangle(0.8, -0.8, 1, 0.9));
                model.BookmarkFont = "FAS";
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
            /*var file = System.Environment.CurrentDirectory + "/midi/h592.mid";
            //var file = "http://www.soundswell.co.uk/music/midi/classical/jsb_2part_f.mid";
            try
            {
                audioPlayer.Play(file);
            }
            catch (Exception ex)
            {
                Console.Write(ex.StackTrace);
            }*
            UserDialogs.Instance.Toast("Player coming soon.", new TimeSpan(3));*/
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
            model.IsEditable = !model.IsEditable;
        }
    }
}

