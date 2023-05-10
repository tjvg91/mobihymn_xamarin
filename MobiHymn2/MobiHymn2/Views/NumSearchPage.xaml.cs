using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MobiHymn2.Utils;
using MobiHymn2.ViewModels;
using MobiHymn2.Views.Popups;
using Xamarin.CommunityToolkit.Extensions;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace MobiHymn2.Views
{
    public partial class NumSearchPage : ContentPage
    {
        int totalHundreds = 0;
        int totalTens = 0;
        int totalOnes = 0;
        int maxNum = 1;
        string maxNumString = "1";

        int activeHundreds = -1;
        int activeTens = -1;

        bool isNewInput = true;

        const string isAppNew = "isNew";

        ScrollView swHundreds = new ScrollView();
        ScrollView swTens = new ScrollView();
        ScrollView swOnes = new ScrollView();

        NumSearchViewModel model;

        private Globals globalInstance = Globals.Instance;

        Popups.DownloadPopup downloadPopup = new Popups.DownloadPopup
        {
            IsLightDismissEnabled = false
        };

        public NumSearchPage()
        {
            InitializeComponent();

            model = ((NumSearchViewModel)this.BindingContext);
            model.OnHymnInputChanged += Model_OnHymnInputChanged;

            var isNew = Preferences.Get(isAppNew, true);
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
            else globalInstance.InitFinished += Globals_InitFinsihed;
        }

        private void Popup_Dismissed(object sender, Xamarin.CommunityToolkit.UI.Views.PopupDismissedEventArgs e)
        {
            Globals.LogAppCenter("Finished Intro Popup", "Last Type", (string)e.Result);

            globalInstance.DownloadStarted += GlobalInstance_DownloadStarted;
            globalInstance.DownloadError += GlobalInstance_DownloadStarted;
            globalInstance.InitFinished += Globals_InitFinsihed;

            Preferences.Set(isAppNew, false);

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

        private void Globals_InitFinsihed(object sender, EventArgs e)
        {
            maxNum = Convert.ToInt32(new Regex("fst").Replace(globalInstance.HymnList[globalInstance.HymnList.Count - 1].Number, ""));
            maxNumString = globalInstance.HymnList[globalInstance.HymnList.Count - 1].Number;

            totalHundreds = maxNum / 100 + 1;
            grdNums.Children.Add(swHundreds, 0, 0);
            grdNums.Children.Add(swTens, 1, 0);
            grdNums.Children.Add(swOnes, 2, 0);

            var layout = new StackLayout();

            layout.ChildAdded += async (s, e) =>
            {
                Button btn = e.Element as Button;
                btn.Opacity = 0;
                await btn.FadeTo(1, globalInstance.Duration);
            };

            for (var i = 0; i < totalHundreds; i++)
            {
                var btn = new Button
                {
                    Text = (100 * i + 1) + "-" + Math.Min(100 * (i + 1), maxNum),
                    BindingContext = i
                };
                btn.Clicked += (s, e1) => generateTens((int)((Button)s).BindingContext);
                layout.Children.Add(btn);
                //await Task.Delay(100);
            }

            swHundreds.Content = layout;
        }

        private void generateTens(int index)
        {
            activeHundreds = index;
            swTens.Content = null;
            swOnes.Content = null;

            totalTens = (Math.Min(100 * (index + 1), maxNum) - (100 * index + 1)) / 10 + 1;

            var layout = new StackLayout();
            layout.ChildAdded += async (s, e) =>
            {
                Button btn = e.Element as Button;
                btn.Opacity = 0;
                await btn.FadeTo(1, globalInstance.Duration);
            };

            for (var i = 0; i < totalTens; i++)
            {
                var btn = new Button
                {
                    Text = (100 * index + i * 10 + 1) + "-" + Math.Min(100 * index + 10 * (i + 1), maxNum),
                    BindingContext = i
                };
                btn.Clicked += (s, e) => generateOnes((int)((Button)s).BindingContext);
                layout.Children.Add(btn);
                //await Task.Delay(100);
            }
            swTens.Content = layout;
        }

        private async void generateOnes(int index)
        {
            activeTens = index;
            swOnes.Content = null;

            var startIndex = globalInstance.HymnList.FindIndex((h) => h.Number == (activeHundreds * 100 + activeTens * 10 + 1 + ""));
            var lastIndex = activeHundreds * 100 + (activeTens + 1) * 10 > maxNum ?
                globalInstance.HymnList.FindIndex((h) => h.Number == maxNumString) :
                globalInstance.HymnList.FindIndex((h) => h.Number == (activeHundreds * 100 + (activeTens + 1) * 10 + ""));

            var layout = new StackLayout();
            layout.ChildAdded += async (s, e) =>
            {
                Button btn = e.Element as Button;
                btn.Opacity = 0;
                await btn.FadeTo(1, globalInstance.Duration);
            };

            for (var i = startIndex; i <= lastIndex; i++)
            {
                var btn = new Button
                {
                    Text = globalInstance.HymnList[i].Number,
                    BindingContext = globalInstance.HymnList[i],
                    TextTransform = TextTransform.Lowercase
                };
                btn.Clicked += async (s, e) => {
                    globalInstance.ActiveHymn = (Models.Hymn)((Button)s).BindingContext;
                    await Shell.Current.GoToAsync($"//{Routes.READ}");
                };
                layout.Children.Add(btn);
                //await Task.Delay(100);
            }
            swOnes.Content = layout;
        }

        async void Key_Clicked(System.Object sender, System.EventArgs e)
        {
            if(sender.Equals(FindByName("btnHymnNum"))) isNewInput = true;
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
                    else Globals.ShowToastPopup(this, "not-found", "Hymn not found");
                }
            }
            
        }

        void Toggle_Clicked(System.Object sender, System.EventArgs e)
        {
            var tempVal = ((int)globalInstance.HymnInputType) + 1;
            globalInstance.HymnInputType = (Utils.InputType)(tempVal % 2);
        }

        async void tbSearch_Clicked(System.Object sender, System.EventArgs e)
        {
            await Shell.Current.GoToAsync($"//{Routes.SEARCH}");
        }
    }
}

