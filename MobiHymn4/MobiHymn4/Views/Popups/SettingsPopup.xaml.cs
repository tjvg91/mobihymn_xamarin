using System;
using Xamarin.CommunityToolkit.UI.Views;
using Xamarin.Essentials;
using Xamarin.Forms;

using MobiHymn4.Utils;

using System.Threading.Tasks;

namespace MobiHymn4.Views.Popups
{
    public partial class SettingsPopup : Popup
    {
        private Globals globalInstance = Globals.Instance;
        ViewModels.ReadViewModel model;
        public SettingsPopup()
        {
            InitializeComponent();

            var mainDispalyInfo = DeviceDisplay.MainDisplayInfo;
            var width = Math.Min(450,(mainDispalyInfo.Width / mainDispalyInfo.Density) - (mainDispalyInfo.Width * 0.1));
            var height = 230;
            Size = new Size(width, height);

            model = (ViewModels.ReadViewModel)this.BindingContext;

            globalInstance.ActiveReadThemeChanged += Globals_ActiveReadThemeChanged;
        }

        private void Globals_ActiveReadThemeChanged(object sender, EventArgs e)
        {
            model.ActiveColor = (Color)sender;
        }

        void rbTheme_CheckedChanged(System.Object sender, Xamarin.Forms.CheckedChangedEventArgs e)
        {
            var rbTheme = (RadioButton)sender;
            if(rbTheme.IsChecked)
                globalInstance.ActiveReadTheme = (Color)rbTheme.Value;
        }

        void rbAlignment_CheckedChanged(System.Object sender, Xamarin.Forms.CheckedChangedEventArgs e)
        {
            var rbAlignment = (RadioButton)sender;
            if(rbAlignment.IsChecked)
                globalInstance.ActiveAlignment = (TextAlignment)rbAlignment.Value;
        }

        void rbFont_CheckedChanged(System.Object sender, Xamarin.Forms.CheckedChangedEventArgs e)
        {
            var rbFont = (RadioButton)sender;
            if(rbFont.IsChecked)
                globalInstance.ActiveFont = (string)rbFont.Value;
        }

        void Button_Clicked(System.Object sender, System.EventArgs e)
        {
            //Dismiss(null);
            IsVisible = false;

            Task.Delay(250);
            /*var fontTypePopup = new FontTypePopup
            {
                HeightRequest = 40,
                IsLightDismissEnabled = false
            };
            fontTypePopup.Dismissed += FontTypePopup_Dismissed;

            Navigation.ShowPopup(fontTypePopup);*/
        }

        private void FontTypePopup_Dismissed(object sender, PopupDismissedEventArgs e)
        {
            IsVisible = true;
            /*Navigation.ShowPopup(new SettingsPopup
            {
                IsLightDismissEnabled = true,
                HeightRequest = 60
            });*/
        }
    }
}

