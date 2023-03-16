using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Xam.Plugin.SimpleAppIntro;
using Xamarin.CommunityToolkit.Extensions;
using Xamarin.CommunityToolkit.UI.Views;
using Xamarin.Forms;

namespace MobiHymn2.Views.Popups
{
    public class IntroPopup : Popup
    {
        AnimatedSimpleAppIntro welcomePage;

        private Utils.Globals globalInstance = Utils.Globals.Instance;

        public event EventHandler DoneSkip;

        public IntroPopup()
        {
            string bgColor = globalInstance.DarkMode ? globalInstance.PrimaryText.ToHex() :
                            Color.White.ToHex();
            string ButtonTextColor = !globalInstance.DarkMode ? globalInstance.PrimaryText.ToHex() :
                            Color.White.ToHex();

            bool isMacIOS = new Regex("ios|macos", RegexOptions.IgnoreCase).IsMatch(Device.RuntimePlatform);
            string welcome = isMacIOS ? "icon.png" : "welcome.json";
            string readPlay = isMacIOS ? "read play.gif" : "read play.json";
            welcomePage = new AnimatedSimpleAppIntro(new List<object>()
            {
                new Slide(new SlideConfig("Welcome", "Browsing a hymn is now a few taps away.", "icon.png",
                    bgColor, ButtonTextColor, ButtonTextColor,
                    FontAttributes.Bold, FontAttributes.None, 24, 16)),

                new Slide(new SlideConfig("Read", "Read & play a hymn anytime, anywhere", "read play.json",
                    bgColor, ButtonTextColor, ButtonTextColor,
                    FontAttributes.Bold, FontAttributes.None, 24, 16)),

                new Slide(new SlideConfig("Save ", "Bookmark your favorite hymns now.",  "save music.json",
                    bgColor, ButtonTextColor, ButtonTextColor,
                    FontAttributes.Bold, FontAttributes.None, 24, 16)),

                new Slide(new SlideConfig("All Set!", "Let's go!", "done.json",
                    bgColor, ButtonTextColor, ButtonTextColor,
                    FontAttributes.Bold, FontAttributes.None, 24, 16)),
            });
            welcomePage.DoneText = "Finish";
            welcomePage.SkipText = "Skip";
            welcomePage.NextText = "Next";
            welcomePage.BackText = "Back";
            welcomePage.BackButtonBackgroundColor = bgColor;
            welcomePage.NextButtonBackgroundColor = bgColor;
            welcomePage.SkipButtonBackgroundColor = bgColor;
            welcomePage.DoneButtonBackgroundColor = bgColor;
            welcomePage.BackButtonTextColor = ButtonTextColor;
            welcomePage.NextButtonTextColor = ButtonTextColor;
            welcomePage.SkipButtonTextColor = ButtonTextColor;
            welcomePage.DoneButtonTextColor = ButtonTextColor;

            welcomePage.OnDoneButtonClicked = () => OnDoneSkip();

            welcomePage.ShowPositionIndicator = true;
            welcomePage.ShowSkipButton = true;
            welcomePage.ShowNextButton = true;
            welcomePage.ShowBackButton = true;
        }

        public void OnDoneSkip()
        {
            if(DoneSkip != null) DoneSkip(null, EventArgs.Empty);
        }

        public async void Show()
        {
            try
            {
                await Shell.Current.Navigation.PushModalAsync(welcomePage);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}


