﻿using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using MobiHymn4.Models;
using MobiHymn4.ViewModels;
using MobiHymn4.Utils;

using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using Lottie.Forms;
using Xamarin.Essentials;

namespace MobiHymn4.Views
{
    public partial class SearchPage : ContentPage
    {
        SearchViewModel model;
        Globals globalInstance = Globals.Instance;

        public SearchPage ()
        {
            InitializeComponent();

            model = ((SearchViewModel)this.BindingContext);
            model.OnSearchFinished += Model_OnSearchFinished;

            MyListView.ItemsSource = model.Items;

            layoutSearching.HeightRequest = (DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density) - 200;
        }

        private void Model_OnSearchFinished(object sender, EventArgs e)
        {
            var list = (ObservableCollection<ShortHymn>)sender;
            MyListView.ItemsSource = list;
        }

        async void root_Appearing(System.Object sender, System.EventArgs e)
        {
            await Task.Delay(750);
            searchBar.Focus();
        }

        async void tbHome_Clicked(System.Object sender, System.EventArgs e)
        {
            await Shell.Current.GoToAsync($"//{Routes.READ}");
        }

        async void MyListView_ChildAdded(System.Object sender, Xamarin.Forms.ElementEventArgs e)
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

