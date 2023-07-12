using System;
using System.ComponentModel;
using Xamarin.CommunityToolkit.Extensions;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;

namespace MobiHymn4.Views
{
    public partial class AboutPage : ContentPage
    {
        Popups.IntroPopup popup;
        public AboutPage()
        {
            InitializeComponent();
        }

        void Button_Clicked(System.Object sender, System.EventArgs e)
        {
            popup = new Popups.IntroPopup();
            Navigation.ShowPopup(popup);
        }
    }
}
