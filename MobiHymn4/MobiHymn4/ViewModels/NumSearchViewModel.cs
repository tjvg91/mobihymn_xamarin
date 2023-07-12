using System;
using System.Text.RegularExpressions;
using System.Windows.Input;
using MobiHymn4.Models;
using MobiHymn4.Utils;
using MvvmHelpers;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace MobiHymn4.ViewModels
{
    public class NumSearchViewModel : MvvmHelpers.BaseViewModel
    {
        private Globals globalInstance = Globals.Instance;
        int totalHundreds = 0;
        int totalTens = 0;
        int maxNum = 1;
        string maxNumString = "1";

        private int activeHundreds = -1;
        public int ActiveHundreds
        {
            get { return activeHundreds; }
            set
            {
                SetProperty(ref activeHundreds, value, nameof(ActiveHundreds));
                OnPropertyChanged();
            }
        }

        private int activeTens = -1;
        public int ActiveTens
        {
            get { return activeTens; }
            set
            {
                SetProperty(ref activeTens, value, nameof(ActiveTens));
                OnPropertyChanged();
            }
        }

        private Hymn activeOnes = new Hymn();
        public Hymn ActiveOnes
        {
            get { return activeOnes; }
            set
            {
                SetProperty(ref activeOnes, value, nameof(ActiveOnes));
                OnPropertyChanged();
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
                    maxNum = Convert.ToInt32(new Regex("fst").Replace(globalInstance.HymnList[globalInstance.HymnList.Count - 1].Number, ""));
                    ActiveHundreds = index;
                    var totalTens = (Math.Min(100 * (index + 1), maxNum) - (100 * index + 1)) / 10 + 1;

                    TensList.Clear();
                    for (var i = 0; i < totalTens; i++)
                    {
                        TensList.Add(new Models.GridOptions
                        {
                            Text = (100 * index + i * 10 + 1) + "-" + Math.Min(100 * index + 10 * (i + 1), maxNum),
                            Index = i
                        });
                    }
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

                    OnesList.Clear();

                    var startIndex = globalInstance.HymnList.FindIndex((h) => h.Number == (activeHundreds * 100 + activeTens * 10 + 1 + ""));
                    var lastIndex = activeHundreds * 100 + (activeTens + 1) * 10 > maxNum ?
                        globalInstance.HymnList.FindIndex((h) => h.Number == maxNumString) :
                        globalInstance.HymnList.FindIndex((h) => h.Number == (activeHundreds * 100 + (activeTens + 1) * 10 + ""));

                    for (var i = startIndex; i <= lastIndex; i++)
                    {
                        OnesList.Add(new Models.GridOptions
                        {
                            Text = globalInstance.HymnList[i].Number,
                            Index = globalInstance.HymnList[i]
                        });
                    }
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
            globalInstance.InitFinished += GlobalInstance_InitFinsihed;
            globalInstance.ActiveHymnChanged += GlobalInstance_ActiveHymnChanged;
        }

        private void GlobalInstance_ActiveHymnChanged(object sender, EventArgs e)
        {
            HymnNum = ((Hymn)sender).Number;
        }

        private void GlobalInstance_InitFinsihed(object sender, EventArgs e)
        {
            IsBusy = false;
            HymnNum = globalInstance.ActiveHymn != null ? globalInstance.ActiveHymn.Number : "1";

            maxNum = Convert.ToInt32(new Regex("fst").Replace(globalInstance.HymnList[globalInstance.HymnList.Count - 1].Number, ""));
            maxNumString = globalInstance.HymnList[globalInstance.HymnList.Count - 1].Number;

            totalHundreds = maxNum / 100 + 1;

            for (var i = 0; i < totalHundreds; i++)
            {
                HundredsList.Add(new GridOptions
                {
                    Text = (100 * i + 1) + "-" + Math.Min(100 * (i + 1), maxNum),
                    Index = i
                });
                //await Task.Delay(100);
            }
        }

        private void Globals_HymnInputTypeChanged(object sender, EventArgs e)
        {
            HymnInputType = (InputType)sender;
        }
    }
}

