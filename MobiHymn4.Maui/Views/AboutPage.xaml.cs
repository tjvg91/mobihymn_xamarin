using System;
using System.ComponentModel;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;
using MobiHymn4.ViewModels;

namespace MobiHymn4.Views
{
    public partial class AboutPage : ContentPage
    {
        Popups.IntroPopup popup;
        AboutViewModel model;
        bool revisionsLoadQueued;

        public AboutPage()
        {
            InitializeComponent();
            model = BindingContext as AboutViewModel;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            if (revisionsLoadQueued)
                return;

            revisionsLoadQueued = true;
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(650), () => model?.LoadRevisions());
        }

        void Button_Clicked(System.Object sender, System.EventArgs e)
        {
            popup = new Popups.IntroPopup();
            Navigation.ShowPopup(popup);
        }
    }
}
