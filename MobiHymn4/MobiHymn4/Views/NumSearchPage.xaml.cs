using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using Xamarin.CommunityToolkit.Extensions;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace MobiHymn4.Views
{
    public partial class NumSearchPage : ContentPage
    {
        bool isNewInput = true;

        NumSearchViewModel model;

        private Globals globalInstance = Globals.Instance;

        Popups.DownloadPopup downloadPopup = new Popups.DownloadPopup
        {
            IsLightDismissEnabled = false
        };

        public NumSearchPage()
        {
            InitializeComponent();

            model = ((NumSearchViewModel)BindingContext);
            model.OnHymnInputChanged += Model_OnHymnInputChanged;

            var isNew = Preferences.Get(PreferencesVar.IS_NEW, true);
            downloadPopup.Todo = globalInstance.Init;
            if (isNew)
            {
                Popups.IntroPopup popup = new Popups.IntroPopup
                {
                    IsLightDismissEnabled = false
                };
                popup.Dismissed += Popup_Dismissed;
                Navigation.ShowPopup(popup);
            }
        }

        private void Popup_Dismissed(object sender, Xamarin.CommunityToolkit.UI.Views.PopupDismissedEventArgs e)
        {
            Globals.LogAppCenter("Finished Intro Popup", "Last Type", (string)e.Result);

            globalInstance.DownloadStarted += GlobalInstance_DownloadStarted;
            globalInstance.DownloadError += GlobalInstance_DownloadStarted;

            Preferences.Set(PreferencesVar.IS_NEW, false);

            globalInstance.Init();
        }

        private void GlobalInstance_DownloadStarted(object sender, EventArgs e)
        {
            Navigation.ShowPopup(downloadPopup);
        }

        private void Model_OnHymnInputChanged(object sender, EventArgs e)
        {
            btnHymnNum.Text = (string)sender;
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            model.Orientation = DeviceDisplay.MainDisplayInfo.Orientation;
        }

        async void Key_Clicked(Object sender, EventArgs e)
        {
            if (sender.Equals(FindByName("btnHymnNum"))) isNewInput = true;
            else
            {
                string key = (string)((Button)sender).BindingContext;

                if (new Regex("[0-9]").IsMatch(key))
                {
                    if (isNewInput)
                    {
                        model.HymnNum = key;
                        isNewInput = false;
                    }
                    else model.HymnNum += key;
                }
                else if (new Regex("[stf]").IsMatch(key))
                {
                    model.HymnNum += key;
                    isNewInput = false;
                }
                else
                {
                    if (key == "b")
                    {
                        model.HymnNum = model.HymnNum.Remove(model.HymnNum.Length - 1);
                        if (model.HymnNum.Length == 0)
                        {
                            model.HymnNum = "1";
                            isNewInput = true;
                        }
                    }
                    else if (key == "e" && globalInstance.HymnList.Any(h => h.Number == model.HymnNum))
                    {
                        globalInstance.ActiveHymn = globalInstance.HymnList[model.HymnNum];
                        isNewInput = true;
                        await Shell.Current.GoToAsync($"//{Routes.READ}");
                    }
                    else
                    {
                        if (DeviceInfo.Platform == DevicePlatform.Android)
                            Globals.ShowToastPopup(
                                "not-found",
                                "Hymn not found",
                                130,
                                new Rectangle(0.3, 0, 0.8, 0.9)
                            );
                        else
                            Globals.ShowToastPopup(
                                "not-found",
                                "Hymn not found",
                                0.7,
                                new Rectangle(0.3, -0.5, 0.8, 0.9)
                            );
                        isNewInput = true;
                    }
                }
            }

        }

        void Toggle_Clicked(Object sender, EventArgs e)
        {
            var tempVal = ((int)globalInstance.HymnInputType) + 1;
            globalInstance.HymnInputType = (Utils.InputType)(tempVal % 2);
        }

        async void tbSearch_Clicked(Object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync($"//{Routes.SEARCH}");
        }

        async void tbSettings_Clicked(System.Object sender, System.EventArgs e)
        {
            await Shell.Current.GoToAsync($"//{Routes.SETTINGS}");
        }

        void Hundreds_Clicked(Object sender, EventArgs e)
        {
            model.HundredSelected.Execute(((Button)sender).CommandParameter);
        }

        void Tens_Clicked(System.Object sender, System.EventArgs e)
        {
            model.TenSelected.Execute(((Button)sender).CommandParameter);
        }

        void Ones_Clicked(System.Object sender, System.EventArgs e)
        {
            model.OneSelected.Execute(((Button)sender).CommandParameter);
        }
    }
}

