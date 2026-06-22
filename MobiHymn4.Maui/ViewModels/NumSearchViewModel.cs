using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Input;
using MobiHymn4.Models;
using MobiHymn4.Utils;
using MvvmHelpers;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

namespace MobiHymn4.ViewModels
{
    public class NumSearchViewModel : MvvmHelpers.BaseViewModel
    {
        static readonly Regex HymnNumberSuffixRegex = new("[fst]");

        private Globals globalInstance = Globals.Instance;
        int totalHundreds = 0;
        int maxNum = 1;
        string maxNumString = "1";

        private int activeHundreds = -1;
        public int ActiveHundreds
        {
            get => activeHundreds;
            set
            {
                if (!SetProperty(ref activeHundreds, value, nameof(ActiveHundreds)))
                    return;

                UpdateHundredsSelection();
            }
        }

        private int activeTens = -1;
        public int ActiveTens
        {
            get => activeTens;
            set
            {
                if (!SetProperty(ref activeTens, value, nameof(ActiveTens)))
                    return;

                UpdateTensSelection();
            }
        }

        private Hymn activeOnes = new Hymn();
        public Hymn ActiveOnes
        {
            get => activeOnes;
            set
            {
                if (!SetProperty(ref activeOnes, value, nameof(ActiveOnes)))
                    return;

                UpdateOnesSelection();
            }
        }

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

        private DisplayOrientation orientation = DisplayOrientation.Portrait;
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

        private ObservableRangeCollection<GridOptions> hundredsList = new ObservableRangeCollection<GridOptions>();
        public ObservableRangeCollection<GridOptions> HundredsList
        {
            get => hundredsList;
            set => hundredsList = value;
        }

        private ObservableRangeCollection<GridOptions> tensList = new ObservableRangeCollection<GridOptions>();
        public ObservableRangeCollection<GridOptions> TensList
        {
            get => tensList;
            set => tensList = value;
        }

        private ObservableRangeCollection<GridOptions> onesList = new ObservableRangeCollection<GridOptions>();
        public ObservableRangeCollection<GridOptions> OnesList
        {
            get => onesList;
            set => onesList = value;
        }

        private ICommand hundredSelected;
        public ICommand HundredSelected
        {
            get
            {
                return hundredSelected ?? (hundredSelected = new Command<int>((index) =>
                {
                    if (globalInstance.HymnList == null || globalInstance.HymnList.Count == 0)
                        return;

                    maxNum = Convert.ToInt32(HymnNumberSuffixRegex.Replace(globalInstance.HymnList[globalInstance.HymnList.Count - 1].Number, ""));
                    ActiveHundreds = index;
                    ActiveTens = -1;
                    OnesList.ReplaceRange(Array.Empty<GridOptions>());

                    var totalTens = (Math.Min(100 * (index + 1), maxNum) - (100 * index + 1)) / 10 + 1;
                    var tensItems = new List<GridOptions>(totalTens);
                    for (var i = 0; i < totalTens; i++)
                    {
                        tensItems.Add(new GridOptions
                        {
                            Text = (100 * index + i * 10 + 1) + "-" + Math.Min(100 * index + 10 * (i + 1), maxNum),
                            Index = i
                        });
                    }

                    TensList.ReplaceRange(tensItems);
                }));
            }
        }

        private ICommand tenSelected;
        public ICommand TenSelected
        {
            get
            {
                return tenSelected ?? (tenSelected = new Command<int>((index) =>
                {
                    ActiveTens = index;
                    PopulateOnesList(activeHundreds, index);
                }));
            }
        }

        private ICommand oneSelected;
        public ICommand OneSelected
        {
            get
            {
                return oneSelected ?? (oneSelected = new Command<Hymn>(async (val) =>
                {
                    ActiveOnes = val;
                    globalInstance.ActiveHymn = val;
                    await Shell.Current.GoToAsync($"//{Routes.READ}");
                }));
            }
        }

        public NumSearchViewModel()
        {
            IsBusy = true;
            HymnInputType = globalInstance.HymnInputType;
            HymnNum = globalInstance.ActiveHymn != null ? globalInstance.ActiveHymn.Number : "1";

            globalInstance.HymnInputTypeChanged += Globals_HymnInputTypeChanged;
            globalInstance.InitFinished += GlobalInstance_InitFinished;
            globalInstance.ActiveHymnChanged += GlobalInstance_ActiveHymnChanged;

            ApplyInitComplete();
        }

        public void ApplyInitComplete()
        {
            if (globalInstance.HymnList == null || globalInstance.HymnList.Count == 0)
                return;

            IsBusy = false;
            HymnNum = globalInstance.ActiveHymn != null ? globalInstance.ActiveHymn.Number : "1";

            maxNum = Convert.ToInt32(HymnNumberSuffixRegex.Replace(globalInstance.HymnList[globalInstance.HymnList.Count - 1].Number, ""));
            maxNumString = globalInstance.HymnList[globalInstance.HymnList.Count - 1].Number;

            if (HundredsList.Count > 0)
                return;

            totalHundreds = maxNum / 100 + 1;
            var hundredsItems = new List<GridOptions>(totalHundreds);
            for (var i = 0; i < totalHundreds; i++)
            {
                hundredsItems.Add(new GridOptions
                {
                    Text = (100 * i + 1) + "-" + Math.Min(100 * (i + 1), maxNum),
                    Index = i
                });
            }

            HundredsList.ReplaceRange(hundredsItems);
        }

        void UpdateHundredsSelection()
        {
            foreach (var item in HundredsList)
                item.IsActive = item.Index is int i && i == activeHundreds;
        }

        void UpdateTensSelection()
        {
            foreach (var item in TensList)
                item.IsActive = item.Index is int i && i == activeTens;
        }

        void UpdateOnesSelection()
        {
            var activeNumber = activeOnes?.Number;
            foreach (var item in OnesList)
                item.IsActive = item.Index is Hymn hymn && hymn.Number == activeNumber;
        }

        void PopulateOnesList(int hundredIndex, int tenIndex)
        {
            var list = globalInstance.HymnList;
            if (list == null || list.Count == 0)
            {
                OnesList.ReplaceRange(Array.Empty<GridOptions>());
                return;
            }

            var rangeStart = hundredIndex * 100 + tenIndex * 10 + 1;
            var decadeEnd = hundredIndex * 100 + (tenIndex + 1) * 10;
            var rangeEnd = decadeEnd > maxNum ? maxNum : decadeEnd - 1;

            var startIdx = FindFirstHymnAtOrAbove(list, rangeStart);
            if (startIdx < 0)
            {
                OnesList.ReplaceRange(Array.Empty<GridOptions>());
                return;
            }

            var items = new List<GridOptions>();
            for (var i = startIdx; i < list.Count; i++)
            {
                var hymn = list[i];
                var numeric = ParseHymnNumericBase(hymn?.Number);
                if (numeric > rangeEnd)
                    break;
                if (numeric >= rangeStart)
                {
                    items.Add(new GridOptions
                    {
                        Text = hymn.Number,
                        Index = hymn
                    });
                }
            }

            OnesList.ReplaceRange(items);
        }

        static int FindFirstHymnAtOrAbove(HymnList list, int rangeStart)
        {
            var lo = 0;
            var hi = list.Count - 1;
            var result = -1;

            while (lo <= hi)
            {
                var mid = lo + (hi - lo) / 2;
                var numeric = ParseHymnNumericBase(list[mid]?.Number);
                if (numeric >= rangeStart)
                {
                    result = mid;
                    hi = mid - 1;
                }
                else
                    lo = mid + 1;
            }

            return result;
        }

        static int ParseHymnNumericBase(string number)
        {
            if (string.IsNullOrWhiteSpace(number))
                return -1;

            var digitsOnly = HymnNumberSuffixRegex.Replace(number, "");
            return int.TryParse(digitsOnly, out var value) ? value : -1;
        }

        private void GlobalInstance_ActiveHymnChanged(object sender, EventArgs e)
        {
            HymnNum = ((Hymn)sender).Number;
        }

        private void GlobalInstance_InitFinished(object sender, EventArgs e)
        {
            ApplyInitComplete();
        }

        private void Globals_HymnInputTypeChanged(object sender, EventArgs e)
        {
            HymnInputType = (InputType)sender;
        }
    }
}

