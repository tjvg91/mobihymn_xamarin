using System;
using System.ComponentModel;

using MobiHymn2.Utils;
using Xamarin.Essentials;

namespace MobiHymn2.ViewModels
{
	public class NumSearchViewModel: MvvmHelpers.BaseViewModel
    {
        private Globals globalInstance = Globals.Instance;

        private InputType hymnInputType;
        public InputType HymnInputType
		{
            get { return hymnInputType; }
            set
            {
                SetProperty(ref hymnInputType, value, nameof(HymnInputType));
            }
        }

        public event EventHandler OnHymnInputChanged;

        private DisplayOrientation orientation = DeviceDisplay.MainDisplayInfo.Orientation;
        public DisplayOrientation Orientation
        {
            get => orientation;
            set
            {
                orientation = value;
                SetProperty(ref orientation, value, nameof(Orientation));
                OnPropertyChanged();
            }
        }

        private string hymnNum;
        public string HymnNum
        {
            get { return hymnNum; }
            set
            {
                SetProperty(ref hymnNum, value, nameof(HymnNum));
                OnPropertyChanged();
                //OnHymnInputChanged(value, EventArgs.Empty);
            }
        }

        public NumSearchViewModel()
		{
            IsBusy = true;
            HymnInputType = globalInstance.HymnInputType;
            HymnNum = globalInstance.ActiveHymn != null ? globalInstance.ActiveHymn.Number : "1";

            globalInstance.HymnInputTypeChanged += Globals_HymnInputTypeChanged;
            globalInstance.InitFinished += GlobalInstance_InitFinsihed;
            globalInstance.ActiveHymnChanged += GlobalInstance_ActiveHymnChanged;
        }

        private void GlobalInstance_ActiveHymnChanged(object sender, EventArgs e)
        {
            HymnNum = ((Models.Hymn)sender).Number;
        }

        private void GlobalInstance_InitFinsihed(object sender, EventArgs e)
        {
            IsBusy = false;
            HymnNum = globalInstance.ActiveHymn != null ? globalInstance.ActiveHymn.Number : "1";
        }

        private void Globals_HymnInputTypeChanged(object sender, EventArgs e)
        {
            HymnInputType = (InputType)sender;
        }
    }
}

