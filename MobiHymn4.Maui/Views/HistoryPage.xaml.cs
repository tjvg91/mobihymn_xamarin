using System;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;

using Microsoft.Maui.Controls;

namespace MobiHymn4.Views
{
    public partial class HistoryPage : ContentPage
    {
        private Globals globalInstance = Globals.Instance;

        public HistoryPage()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            (BindingContext as HistoryViewModel)?.RefreshHistoryGroups();
        }

        async void MyListView_ChildAdded(Object sender, ElementEventArgs e)
        {
            var item = e.Element as StackLayout;
            if(item != null)
            {
                item.Opacity = 0;
                await item.FadeTo(1, globalInstance.Duration);
            }
        }
    }
}

