using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Maui.Controls;
using MvvmHelpers;

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
                OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(errorString);

        private ObservableRangeCollection<string> groupOptions = new();
        public ObservableRangeCollection<string> GroupOptions
        {
            get => groupOptions;
            set
            {
                groupOptions = value;
                SetProperty(ref groupOptions, value, nameof(GroupOptions));
                OnPropertyChanged(nameof(HasGroupOptions));
            }
        }

        public bool HasGroupOptions => GroupOptions?.Count > 0;

        private string selectedGroup = "General";
        public string SelectedGroup
        {
            get => selectedGroup;
            set => SetProperty(ref selectedGroup, value, nameof(SelectedGroup));
        }

        public void SetGroupOptions(IReadOnlyList<string> names)
        {
            GroupOptions.Clear();
            foreach (var name in names)
                GroupOptions.Add(name);

            OnPropertyChanged(nameof(HasGroupOptions));
        }

        public InputPopupModel ()
		{
			
		}
	}
}
