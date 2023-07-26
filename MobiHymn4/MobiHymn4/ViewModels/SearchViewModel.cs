using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;

using HtmlAgilityPack;
using MobiHymn4.Models;
using MobiHymn4.Utils;
using Xamarin.Essentials;
using Xamarin.Forms;


namespace MobiHymn4.ViewModels
{
	public class SearchViewModel : MvvmHelpers.BaseViewModel
    {
        private Globals globalInstance = Globals.Instance;
        HtmlDocument htmlDocument;

        private bool isSearched;
        public bool IsSearched
        {
            get => isSearched;
            set
            {
                isSearched = value;
                SetProperty(ref isSearched, value, nameof(IsSearched));
                OnPropertyChanged();
            }
        }
        private ObservableCollection<ShortHymn> items;
        public ObservableCollection<ShortHymn> Items
        {
            get => items;
            set
            {
                items = value;
                SetProperty(ref items, value, nameof(Items));
                OnPropertyChanged();
            }
        }

        private int itemCount;
        public int ItemCount
        {
            get => itemCount;
            private set
            {
                itemCount = value;
                ItemCountString = $"{(value == 0 ? "No" : value.ToString())} result{(value > 1 ? "s" : "")} found";
                SetProperty(ref itemCount, value, nameof(ItemCount));
                OnPropertyChanged();
            }
        }

        private string itemCountString;
        public string ItemCountString
        {
            get => itemCountString;
            private set
            {
                itemCountString = value;
                SetProperty(ref itemCountString, value, nameof(ItemCountString));
                OnPropertyChanged();
            }
        }
        private BackgroundWorker bwSearcher;
        public event EventHandler OnSearchStarted;
        public event EventHandler OnSearchFinished;

        public SearchViewModel()
        {
            Items = new ObservableCollection<ShortHymn>();
            bwSearcher = new BackgroundWorker();
            bwSearcher.DoWork += BwSearcher_DoWork;
            bwSearcher.RunWorkerCompleted += BwSearcher_RunWorkerCompleted;

            htmlDocument = new HtmlDocument();
            IsSearched = false;

            Title = "Search";
        }

        private void BwSearcher_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            IsBusy = false;
            if (!e.Cancelled)
            {
                Items = (ObservableCollection<ShortHymn>)e.Result;
                ItemCount = Items.Count();
                IsSearched = true;
                Globals.LogAppCenter($"Searched", "Search Count", Items.Count().ToString());
                OnSearchFinished(Items, EventArgs.Empty);
            }
        }

        private ICommand _searchHymns;
        public ICommand SearchHymns
        {
            get
            {
                return _searchHymns ?? (_searchHymns = new Xamarin.Forms.Command<string>((text) =>
                {
                    IsBusy = true;
                    OnSearchFinished(null, EventArgs.Empty);
                    globalInstance.SearchList.Add(text);
                    if (globalInstance.SearchList.Count > 10)
                        globalInstance.SearchList = globalInstance.SearchList.Skip(1).ToObservableRangeCollection();
                    bwSearcher.RunWorkerAsync(text);
                }));
            }
        }

        private ICommand searchItemSelected;
        public ICommand SearchItemSelected
        {
            get
            {
                return searchItemSelected ?? (searchItemSelected = new Xamarin.Forms.Command<ShortHymn>(async (shortHymn) =>
                {
                    globalInstance.ActiveHymn = (from x in globalInstance.HymnList
                                                where x.Number == shortHymn.Number
                                                select x).First();

                    await Shell.Current.GoToAsync($"//{Routes.READ}");
                }));
            }
        }

        private void BwSearcher_DoWork(object sender, DoWorkEventArgs e)
        {
            string text = (string)e.Argument;

            Globals.LogAppCenter($"Searching", "Search Term", text);

            try
            {

                var res = (from hymn in globalInstance.HymnList
                           where new Regex(text.StripPunctuation(), RegexOptions.IgnoreCase).IsMatch(hymn.Lyrics.StripPunctuation())
                           select new
                           {
                               Number = hymn.Number,
                               Lines = new Regex("<br>").Split(ParsedLyrics(hymn.Lyrics))
                                         .Where(line => new Regex(text.StripPunctuation(), RegexOptions.IgnoreCase).IsMatch(line.StripPunctuation()))
                                         .Select(line => new ShortHymn
                                         {
                                             Number = hymn.Number,
                                             Line = line
                                         })
                           }).Select(arr => arr.Lines).SelectMany(x => x)
                               .OrderBy(searchRes => searchRes.Line.StripPunctuation())
                               .GroupBy(s => s.Line.StripPunctuation())
                               .Select(s => s.FirstOrDefault())
                               .ToList().ToObservableCollection();
                e.Result = res;
            }
            catch (Exception ex)
            {
                Globals.TrackError(ex);
            }
        }

        private string ParsedLyrics(string lyrics)
        {
            htmlDocument.LoadHtml(lyrics);
            var root = htmlDocument.DocumentNode;
            var preText = root.Descendants("pre").SingleOrDefault();
            return preText.InnerHtml;
        }
    }
}
