using System;
using System.Linq;
using System.Windows.Input;

using MobiHymn4.Models;
using MobiHymn4.Utils;

using MvvmHelpers;
using Xamarin.Forms;

namespace MobiHymn4.ViewModels
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
                HistoryGroupList = ModifyHistory(value);
				SetProperty(ref historyList, value, nameof(HistoryList));
				OnPropertyChanged();
			}
		}

        private ObservableRangeCollection<IGrouping<string, ShortHymn>> historyGroupList;
        public ObservableRangeCollection<IGrouping<string, ShortHymn>> HistoryGroupList
        {
            get => historyGroupList;
            private set
            {
                historyGroupList = value;
                SetProperty(ref historyGroupList, value, nameof(HistoryGroupList));
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

        private ObservableRangeCollection<IGrouping<string, ShortHymn>> ModifyHistory(ObservableRangeCollection<ShortHymn> shortHymns)
        {
            return shortHymns.OrderByDescending(shortHymn => shortHymn.TimeStamp)
                .GroupBy((shortHymn) => shortHymn.HistoryGroup)
                .ToObservableRangeCollection();
        }
    }
}

