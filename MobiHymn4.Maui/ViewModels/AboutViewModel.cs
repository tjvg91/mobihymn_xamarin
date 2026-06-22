using System;
using System.Collections.Generic;
using System.Windows.Input;
using MobiHymn4.Models;
using MobiHymn4.Services;
using MvvmHelpers;

using Microsoft.Maui.Controls;

namespace MobiHymn4.ViewModels
{
    public class IntroSlide
    {
        public string Title { get; set; }
        public string Subitle { get; set; }
        public string Image { get; set; }
        public double Size { get; set; }
    }

    public class AboutViewModel : MvvmHelpers.BaseViewModel
    {
        public event EventHandler IsLastIndexVisited;

        private ObservableRangeCollection<Timeline> revisions;
        public ObservableRangeCollection<Timeline> Revisions
        {
            get => revisions;
            set => revisions = value;
        }

        private bool isLoadingRevisions;
        public bool IsLoadingRevisions
        {
            get => isLoadingRevisions;
            set
            {
                if (SetProperty(ref isLoadingRevisions, value, nameof(IsLoadingRevisions)))
                    OnPropertyChanged(nameof(IsNotLoadingRevisions));
            }
        }

        public bool IsNotLoadingRevisions => !IsLoadingRevisions;

        private List<IntroSlide> introSlides;
        public List<IntroSlide> IntroSlides
        {
            get => introSlides;
            set => introSlides = value;
        }

        private int currentSlideIndex;
        public int CurrentSlideIndex
        {
            get => currentSlideIndex;
            set
            {
                if (!SetProperty(ref currentSlideIndex, value, nameof(CurrentSlideIndex)))
                    return;

                IsFirstIndex = CurrentSlideIndex == 0;
                IsLastIndex = CurrentSlideIndex == IntroSlides.Count - 1;
                OnPropertyChanged(nameof(CurrentSlideImage));
                OnPropertyChanged(nameof(CurrentSlideSize));
            }
        }

        public string CurrentSlideImage =>
            IntroSlides != null && currentSlideIndex >= 0 && currentSlideIndex < IntroSlides.Count
                ? IntroSlides[currentSlideIndex].Image
                : string.Empty;

        public double CurrentSlideSize =>
            IntroSlides != null && currentSlideIndex >= 0 && currentSlideIndex < IntroSlides.Count
                ? IntroSlides[currentSlideIndex].Size
                : 0;

        private bool isFirstIndex;
        public bool IsFirstIndex
        {
            get => isFirstIndex;
            set
            {
                isFirstIndex = value;
                SetProperty(ref isFirstIndex, value, nameof(IsFirstIndex));
            }
        }

        private bool isLastIndex;
        public bool IsLastIndex
        {
            get => isLastIndex;
            set
            {
                isLastIndex = value;
                SetProperty(ref isLastIndex, value, nameof(IsLastIndex));

                if (value)
                    IsLastIndexVisited(true, EventArgs.Empty);
            }
        }

        private string appVersion;
        public string AppVersion
        {
            get => appVersion;
            set => SetProperty(ref appVersion, value);
        }

        public AboutViewModel()
        {
            Title = "About";

            try
            {
                var build = ServiceHelper.Get<IAppVersionBuild>();
                AppVersion = $"Version {build.GetVersionNumber()} · Build {build.GetBuildNumber()}";
            }
            catch
            {
                AppVersion = "Version 0.9.0";
            }

            Revisions = new ObservableRangeCollection<Timeline>();
            IsLoadingRevisions = true;
            IntroSlides = new List<IntroSlide>();
            IntroSlides.Add(new IntroSlide
            {
                Title = "Welcome",
                Subitle = "Browsing a hymn is now a few taps away.",
                Image = "welcome-2",
                Size = DeviceInfo.Platform == DevicePlatform.Android ? 150 : 1.5 ,
            });
            IntroSlides.Add(new IntroSlide
            {
                Title = "Read",
                Subitle = "Read & play a hymn anytime, anywhere",
                Image = "read-play",
                Size = DeviceInfo.Platform == DevicePlatform.Android ? 155 : 1
            });
            IntroSlides.Add(new IntroSlide
            {
                Title = "Save",
                Subitle = "Bookmark your favorite hymns now.",
                Image = "save-music",
                Size = DeviceInfo.Platform == DevicePlatform.Android ? 150 : 1
            });

            var isNew = Preferences.Get("isNew", true);
            if(isNew)
            {
                IntroSlides.Add(new IntroSlide
                {
                    Title = "All Set!",
                    Subitle = "Let's go!",
                    Image = "done",
                    Size = DeviceInfo.Platform == DevicePlatform.Android ? 100 : 1
                });
            }
            
            CurrentSlideIndex = 0;
            OnPropertyChanged(nameof(CurrentSlideImage));
            OnPropertyChanged(nameof(CurrentSlideSize));
        }

        public void LoadRevisions()
        {
            if (Revisions.Count > 0)
            {
                IsLoadingRevisions = false;
                return;
            }

            Revisions.Add(new Timeline
            {
                Header = "0.9.0",
                Details =
                {
                    "MP3 player for some hymns",
                    "Downloading and syncing hymns in the background",
                    "Bookmark groups",
                    "Clipboard icon for Android"
                },
                Height = 110
            });
            Revisions.Add(new Timeline
            {
                Header = "0.8.2",
                Details =
                {
                    "Disable selection of lyrics for Android temporarily",
                    "New UI",
                    "Disable MIDI playing temporarily",
                    "Feature to sync updates from cloud",
                    "New themes and fonts"
                },
                Height = 120
            });
            Revisions.Add(new Timeline
            {
                Header = "0.8.0",
                Details = {"Slider intro", "Splash Screen", "Can play MIDI"},
                Height = 90
            });
            Revisions.Add(new Timeline
            {
                Header = "0.7.6",
                Details = { "Bug fixes", "New fonts" },
                Height = 70
            });
            Revisions.Add(new Timeline
            {
                Header = "0.7.4",
                Details = { "New app icon", "Initial MIDI player" },
                Height = 70
            });
            Revisions.Add(new Timeline
            {
                Header = "0.7.2",
                Details = { "Can select lyrics", "Can opt for app-provided font size", "Splash screen disabled" },
                Height = 90
            });
            Revisions.Add(new Timeline
            {
                Header = "0.7.0",
                Details = { "Slider intro", "Splash Screen", "Can play MIDI" },
                Height = 90
            });

            IsLoadingRevisions = false;
        }
    }
}
