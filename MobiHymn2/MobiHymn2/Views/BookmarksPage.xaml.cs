using System;
using System.Collections.Generic;
using System.Linq;

using MobiHymn2.Models;
using MobiHymn2.Utils;

using Acr.UserDialogs;

using Xamarin.Forms;
using Microsoft.AppCenter.Crashes;

namespace MobiHymn2.Views
{
    public partial class BookmarksPage : ContentPage
    {
        private Globals globalInstance = Globals.Instance;
        public BookmarksPage()
        {
            InitializeComponent();
        }

        void SwipeItem_Invoked(System.Object sender, System.EventArgs e)
        {
            try
            {
                var swipeItem = (SwipeItem)sender;
                var shortHymn = (ShortHymn)swipeItem.CommandParameter;

                UserDialogs.Instance.Confirm(new Acr.UserDialogs.ConfirmConfig
                {
                    Message = $"Are you sure you want to delete Hymn #{shortHymn.Number} as bookmark?",
                    OkText = "Yes",
                    CancelText = "No",
                    Title = "Delete Bookmark?",
                    OnAction = (confirmed) =>
                    {
                        if (confirmed)
                        {
                            globalInstance.RemoveBookmark(shortHymn);
                            Globals.ShowToastPopup(this, "bookmark-deleted", $"Hymn #{shortHymn.Number} bookmark deleted.");
                        }
                    }
                });
                
            }
            catch (Exception ex)
            {
                Console.Write(ex.Message);
                Crashes.TrackError(ex);
            }
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

