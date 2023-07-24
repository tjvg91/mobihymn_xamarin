using System;

using Xamarin.Forms;

namespace MobiHymn4.ViewModels
{
	public class InputPopupModel : BaseViewModel
	{
        private string title;
        public string Title
        {
            get => title;
            set
            {
                title = value;
                SetProperty(ref title, value, nameof(Title));
                OnPropertyChanged();
            }
        }

        private string actionString = "OK";
        public string ActionString
        {
            get => actionString;
            set
            {
                actionString = value;
                SetProperty(ref actionString, value, nameof(ActionString));
                OnPropertyChanged();
            }
        }

        private string errorString = "";
        public string ErrorString
        {
            get => errorString;
            set
            {
                errorString = value;
                SetProperty(ref errorString, value, nameof(ErrorString));
                OnPropertyChanged();
            }
        }

        public InputPopupModel ()
		{
			
		}
	}
}


