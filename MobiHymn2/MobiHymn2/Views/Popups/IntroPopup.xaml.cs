using System;
using System.Collections.Generic;
using MobiHymn2.Utils;
using Xamarin.CommunityToolkit.UI.Views;
using Xamarin.Essentials;
using Xamarin.Forms;

using PanCardView.EventArgs;
using MobiHymn2.ViewModels;

namespace MobiHymn2.Views.Popups
{
    public delegate void PositionChangedEventHandler(object sender, PositionChangedEventArgs e);

    public partial class IntroPopup : Popup
	{
        private Globals globalInstance = Globals.Instance;
        private AboutViewModel model;

        public IntroPopup ()
		{
			InitializeComponent ();

            var mainDispalyInfo = DeviceDisplay.MainDisplayInfo;
            var width = (mainDispalyInfo.Width / mainDispalyInfo.Density);
            var height = (mainDispalyInfo.Height / mainDispalyInfo.Density);
            this.Size = new Size(width, height);
            HorizontalOptions = LayoutOptions.Center;
            VerticalOptions = LayoutOptions.Center;
            Color = globalInstance.PrimaryText;

            model = (AboutViewModel)BindingContext;
            carouselIntro.ItemAppearing += CarouselIntro_ItemAppearing;
            model.IsLastIndexVisited += Model_IsLastIndexVisited;
        }

        private void Model_IsLastIndexVisited(object sender, EventArgs e)
        {
            btnSkipDone.Text = "Done";
        }

        private void CarouselIntro_ItemAppearing(PanCardView.CardsView view, ItemAppearingEventArgs args)
        {
            model.CurrentSlideIndex = args.Index;
        }

        void Button_Clicked(System.Object sender, System.EventArgs e)
        {
            Dismiss(btnSkipDone.Text);
        }
    }
}

