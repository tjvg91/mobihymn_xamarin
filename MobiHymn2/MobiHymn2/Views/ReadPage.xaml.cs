using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Xamarin.CommunityToolkit.Extensions;
using Xamarin.Forms;

using MobiHymn2.Utils;
using MobiHymn2.ViewModels;
using MobiHymn2.Views.Popups;

using Acr.UserDialogs;

using Xamanimation;

using Plugin.AudioRecorder;
using MobiHymn2.Models;
using static System.Net.WebRequestMethods;
using Xamarin.Essentials;

namespace MobiHymn2.Views
{
    public partial class ReadPage : ContentPage
    {
        ReadViewModel model;
        ToolbarItem tbBookmark;

        private Globals globalInstance = Globals.Instance;

        AudioPlayer audioPlayer;

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

            audioPlayer = new AudioPlayer();
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

        void tbBookmarks_Clicked(System.Object sender, System.EventArgs e)
        {
            if (model.BookmarkFont == "FAR")
            {
                globalInstance.AddBookmark();
                Globals.ShowToastPopup(this, "bookmark-saved", "Bookmard added.");
                model.BookmarkFont = "FAS";
            }
            else
            {
                UserDialogs.Instance.Confirm(new Acr.UserDialogs.ConfirmConfig
                {
                    Message = $"Are you sure you want to delete Hymn #{globalInstance.ActiveHymn.Number} as bookmark?",
                    OkText = "Yes",
                    CancelText = "No",
                    Title = "Delete Bookmark?",
                    OnAction = (confirmed) =>
                    {
                        if (confirmed)
                        {
                            globalInstance.RemoveBookmark();
                            Globals.ShowToastPopup(this,
                                "bookmark-deleted",
                                "Bookmard deleted.",
                                DeviceInfo.Platform == DevicePlatform.Android ? 100: 0.75,
                                DeviceInfo.Platform == DevicePlatform.Android ? new Rectangle(0.5, -0.2, 2, 2) : new Rectangle(0.8, 0.8, 1, 1));
                            model.BookmarkFont = "FAR";
                        }
                    }
                });
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

