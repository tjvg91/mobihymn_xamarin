﻿using System;
using System.Linq;
using System.Windows.Input;

using MobiHymn2.Models;
using MobiHymn2.Utils;

using MvvmHelpers;
using Xamarin.Forms;

namespace MobiHymn2.ViewModels
{
	public class HistoryViewModel : BaseViewModel
	{
        private Globals globalInstance = Globals.Instance;

        private ObservableRangeCollection<ShortHymn> historyList;
		public ObservableRangeCollection<ShortHymn> HistoryList
		{
			get => historyList;
			set
			{
				historyList = value;
				SetProperty(ref historyList, value, nameof(HistoryList));
				OnPropertyChanged();
			}
		}

		public HistoryViewModel()
		{
            HistoryList = globalInstance.HistoryList;
			Title = "History";

            globalInstance.HistoryChanged += Globals_HistoryChanged;
        }

        private void Globals_HistoryChanged(object sender, EventArgs e)
        {
            HistoryList = (ObservableRangeCollection<ShortHymn>)sender;
        }

        private ICommand historyItemSelected;
        public ICommand HistoryItemSelected
        {
            get
            {
                return historyItemSelected ?? (historyItemSelected = new Xamarin.Forms.Command<ShortHymn>(async (shortHymn) =>
                {
                    globalInstance.ActiveHymn = (from x in globalInstance.HymnList
                                                where x.Number == shortHymn.Number
                                                select x).First();

                    await Shell.Current.GoToAsync($"//{Routes.READ}");
                }));
            }
        }
    }
}

