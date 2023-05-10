using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;

using Acr.UserDialogs;
using HtmlAgilityPack;
using Microsoft.AppCenter.Crashes;
using MobiHymn2.Models;
using MobiHymn2.Utils;
using Xamarin.Forms;

namespace MobiHymn2.ViewModels
{
	public class SearchViewModel : MvvmHelpers.BaseViewModel
    {
        private Globals globalInstance = Globals.Instance;
        HtmlDocument htmlDocument;

        private ObservableCollection<ShortHymn> items;
        public ObservableCollection<ShortHymn> Items
        {
            get => items;
            set
            {
                items = value;
                SetProperty(ref itemCount, value.Count, "ItemCount");
            }
        }
        private int itemCount;
        public int ItemCount
        {
            get => itemCount;
            set
            {
                itemCount = value;
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

            Title = "Search";
        }

        private void BwSearcher_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            IsBusy = false;
            //Task.Delay(1000);
            //UserDialogs.Instance.HideLoading();
            if (!e.Cancelled)
            {
                Items = (ObservableCollection<ShortHymn>)e.Result;
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
                           where new Regex(text, RegexOptions.IgnoreCase).IsMatch(hymn.Lyrics)
                           select new
                           {
                               Number = hymn.Number,
                               Lines = new Regex("<br>").Split(parsedLyrics(hymn.Lyrics))
                                         .Where(line => new Regex(text, RegexOptions.IgnoreCase).IsMatch(line))
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
                Crashes.TrackError(ex);
            }
        }

        private string parsedLyrics(string lyrics)
        {
            htmlDocument.LoadHtml(lyrics);
            var root = htmlDocument.DocumentNode;
            var preText = root.Descendants("pre").SingleOrDefault();
            return preText.InnerHtml;
        }
    }
}
