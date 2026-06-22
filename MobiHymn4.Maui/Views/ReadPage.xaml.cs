using System;
using System.Collections.Generic;
using System.Linq;

using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

using FontAwesome;

using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using MobiHymn4.Views.Popups;


using MobiHymn4.Models;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;

namespace MobiHymn4.Views
{
    public partial class ReadPage : ContentPage
    {
        ReadViewModel model;
        IPlayService audioPlayer;
        bool isAudioReady;
        bool isPlaying;
        bool suppressSliderSeek;
        int audioLoadGeneration;
        Exception initializationException;

        private Globals globalInstance = Globals.Instance;
        bool introChecked;
        bool initStartQueued;
        bool settingsOverlayBuilt;
        bool settingsOverlayBuildQueued;
        List<(Border Box, Label Label, TextAlignment Value)> alignmentOptions;
        List<(Border Box, Label Label, Color Value)> themeOptions;
        List<(Border Box, Label Label, string Value)> fontOptions;
        Border alignStartBox, alignCenterBox, alignEndBox;
        Label alignStartLabel, alignCenterLabel, alignEndLabel;
        Border themeWhiteBox, themeSepiaBox, themeBrownBox, themeGrayBox, themeDarkBox;
        Border themeGreenBox, themeOrangeBox, themeBlueBox, themePurpleBox, themePinkBox;
        Label themeWhiteLabel, themeSepiaLabel, themeBrownLabel, themeGrayLabel, themeDarkLabel;
        Label themeGreenLabel, themeOrangeLabel, themeBlueLabel, themePurpleLabel, themePinkLabel;
        Border fontRobotoBox, fontNotoBox, fontChelseaBox, fontUnifrakturBox, fontStyleScriptBox;
        Border fontCookieBox, fontFrostyBox, fontKissBox, fontMelonBox, fontTeacherBox;
        Label fontRobotoLabel, fontNotoLabel, fontChelseaLabel, fontUnifrakturLabel, fontStyleScriptLabel;
        Label fontCookieLabel, fontFrostyLabel, fontKissLabel, fontMelonLabel, fontTeacherLabel;

        public ReadPage()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                initializationException = ex.GetBaseException();
                System.Diagnostics.Debug.WriteLine($"ReadPage XAML initialization failed: {ex}");

                Title = "Read Error";
                Content = new VerticalStackLayout
                {
                    Padding = new Thickness(20),
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label
                        {
                            Text = "Unable to open hymn reader.",
                            FontSize = 18,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = $"{initializationException.GetType().Name}: {initializationException.Message}",
                            FontSize = 12,
                            Margin = new Thickness(0, 12, 0, 0),
                            HorizontalTextAlignment = TextAlignment.Center
                        }
                    }
                };
                return;
            }

            try
            {
                model = (ReadViewModel)this.BindingContext;
                model.PropertyChanged += Model_PropertyChanged;
                model.OnHymnChanged += Model_OnHymnChanged;
                model.ConnectivityChanged += (_, _) =>
                {
                    if (HasInternetConnection())
                        InitAudio();
                };

                UpdateBookmarkIcon();
                UpdatePlayIcon();

                globalInstance.DownloadStarted += GlobalInstance_DownloadStarted;
                globalInstance.DownloadError += GlobalInstance_DownloadStarted;

                audioPlayer = ServiceHelper.Get<IPlayService>();
                audioPlayer.PlaybackEnded += AudioPlayer_PlaybackEnded;
                audioPlayer.PositionChanged += AudioPlayer_PositionChanged;
                audioPlayer.BufferingChanged += AudioPlayer_BufferingChanged;

                ResetPlayerUi();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReadPage initialization failed: {ex}");
            }
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (initializationException != null)
            {
                var ex = initializationException;
                initializationException = null;
                await DisplayAlert("Read Page Error", $"{ex.GetType().Name}: {ex.Message}", "OK");
                return;
            }

            UpdateBookmarkIcon();
            UpdatePlayIcon();
            UpdateSelectionToolbar();
            model?.UpdateInternetNotice();
            globalInstance.RefreshIncompleteDownloadState();
            model?.RefreshLoadingState();
            ShowIntroIfNeeded();
            ShowDownloadPopupIfNeeded();
            QueueInitStarted();
            ScheduleDownloadPopupRetries();
            model?.RefreshFromActiveHymn();

            QueueSettingsOverlayBuild();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            audioPlayer?.Pause();
            isPlaying = false;
            model?.SetAudioBuffering(false);
            UpdatePlayIcon();
        }

        void QueueInitStarted()
        {
            if (initStartQueued ||
                Preferences.Get(PreferencesVar.IS_NEW, true) ||
                (globalInstance.InitComplete && !globalInstance.HasIncompleteDownloadOnDisk
#if ANDROID
                 && !DownloadForegroundService.IsRunning
#endif
                ) ||
                globalInstance.InitInProgress)
                return;

            initStartQueued = true;
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), EnsureInitStarted);
        }

        void EnsureInitStarted()
        {
            if (Preferences.Get(PreferencesVar.IS_NEW, true))
                return;

            initStartQueued = false;

            var hasDownloadRecovery = globalInstance.HasIncompleteDownloadOnDisk ||
#if ANDROID
                            DownloadForegroundService.IsRunning;
#else
                            false;
#endif

            if (hasDownloadRecovery &&
                !DownloadPopupPresenter.IsPopupOpen &&
                !DownloadPopupPresenter.TryShowOnPage(this))
            {
                DownloadPopupPresenter.ShowWithRetry(this);
            }

            globalInstance.Init();
        }

        void ShowIntroIfNeeded()
        {
            if (introChecked)
                return;

            introChecked = true;

            if (!Preferences.Get(PreferencesVar.IS_NEW, true))
                return;

            var popup = new IntroPopup
            {
                CanBeDismissedByTappingOutsideOfPopup = false
            };
            popup.Closed += IntroPopup_Dismissed;
            Navigation.ShowPopup(popup);
        }

        void IntroPopup_Dismissed(object sender, PopupClosedEventArgs e)
        {
            Preferences.Set(PreferencesVar.IS_NEW, false);
            globalInstance.Init();
        }

        void ShowDownloadPopupIfNeeded()
        {
            if (!DownloadPopupPresenter.IsDownloadRecoveryPending())
                return;

            if (!DownloadPopupPresenter.TryShowOnPage(this))
                DownloadPopupPresenter.ShowWithRetry(this);
        }

        void ScheduleDownloadPopupRetries()
        {
            if (!DownloadPopupPresenter.IsDownloadRecoveryPending())
                return;

            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500),
                () => DownloadPopupPresenter.ShowWithRetry(this));
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(1500),
                () => DownloadPopupPresenter.ShowWithRetry(this));
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(3000),
                () => DownloadPopupPresenter.ShowWithRetry(this));
        }

        void GlobalInstance_DownloadStarted(object sender, EventArgs e)
        {
            DownloadPopupPresenter.ShowFromEvent();
        }

        private void Model_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ReadViewModel.BookmarkFont))
                UpdateBookmarkIcon();
            else if (e.PropertyName is nameof(ReadViewModel.IsSelectable) or nameof(ReadViewModel.ShowLyricsContent))
                UpdateSelectionToolbar();
        }

        void UpdateSelectionToolbar()
        {
            if (model == null || tbSelection == null)
                return;

            var showOnAndroid = DeviceInfo.Platform == DevicePlatform.Android && model.ShowLyricsContent;
            if (!showOnAndroid)
            {
                ToolbarItems.Remove(tbSelection);
                return;
            }

            EnsureToolbarItemAfterSearch(tbSelection);
            tbSelection.IconImageSource = new FontImageSource
            {
                FontFamily = "FAS",
                Color = (Color)Application.Current.Resources["PrimaryText"],
                Size = 17,
                Glyph = model.IsSelectable
                    ? FontAwesome.FontAwesomeIcons.TextSlash
                    : FontAwesome.FontAwesomeIcons.ICursor,
            };
        }

        void EnsureToolbarItemAfterSearch(ToolbarItem item)
        {
            if (ToolbarItems.Contains(item))
                return;

            var searchIndex = ToolbarItems.IndexOf(tbSearch);
            ToolbarItems.Insert(searchIndex >= 0 ? searchIndex + 1 : 0, item);
        }

        void tbSelection_Clicked(object sender, EventArgs e)
        {
            model.IsSelectable = !model.IsSelectable;
        }

        private void UpdateBookmarkIcon()
        {
            if (model == null || tbBookmarks == null)
                return;

            tbBookmarks.IconImageSource = CreateBookmarkIcon(model.BookmarkFont);
        }

        private static FontImageSource CreateBookmarkIcon(string fontFamily)
        {
            return new FontImageSource
            {
                FontFamily = string.IsNullOrEmpty(fontFamily) ? "FAR" : fontFamily,
                Glyph = FontAwesome.FontAwesomeIcons.Heart,
                Size = 17,
                Color = (Color)Application.Current.Resources["PrimaryText"],
            };
        }

        private void UpdatePlayIcon()
        {
            if (btnPlay == null)
                return;

            btnPlay.Source = new FontImageSource
            {
                FontFamily = "FAS",
                Glyph = isPlaying ? FontAwesomeIcons.Pause : FontAwesomeIcons.Play,
                Size = 18,
                Color = (Color)Application.Current.Resources["Primary"],
            };
        }

        private void InitAudio(string hymnNumber = "") => _ = InitAudioAsync(hymnNumber);

        private async Task InitAudioAsync(string hymnNumber = "")
        {
            var loadId = Interlocked.Increment(ref audioLoadGeneration);
            model?.SetAudioLoading(true);
            try
            {
                var number = string.IsNullOrEmpty(hymnNumber)
                    ? globalInstance.ActiveHymn?.Number
                    : hymnNumber;

                audioPlayer?.Stop();
                isAudioReady = false;
                isPlaying = false;
                ResetPlayerUi();
                UpdatePlayIcon();
                model?.SetAudioNotFound(false);

                if (string.IsNullOrEmpty(number))
                    return;

                model?.UpdateInternetNotice();
                if (!HasInternetConnection())
                    return;

                if (loadId != audioLoadGeneration)
                    return;

                var audioUrl = Globals.GetHymnAudioUrl(number);
                isAudioReady = await audioPlayer.LoadAsync(audioUrl);

                if (loadId != audioLoadGeneration)
                    return;

                if (!isAudioReady)
                    model?.SetAudioNotFound(true);
                else
                    UpdatePlayerDuration();
            }
            catch (Exception)
            {
                if (loadId == audioLoadGeneration)
                {
                    isAudioReady = false;
                    model?.SetAudioNotFound(true);
                }
            }
            finally
            {
                if (loadId == audioLoadGeneration)
                    model?.SetAudioLoading(false);
            }
        }

        void AudioPlayer_BufferingChanged(object sender, EventArgs e) =>
            MainThread.BeginInvokeOnMainThread(() =>
                model?.SetAudioBuffering(audioPlayer?.IsBuffering ?? false));

        void ResetPlayerUi()
        {
            if (sldlrPlayer == null || lblCurTime == null || lblTotalTime == null)
                return;

            sldlrPlayer.Maximum = 1;
            sldlrPlayer.Value = 0;
            lblCurTime.Text = 0d.ToMinSec();
            lblTotalTime.Text = 0d.ToMinSec();
        }

        void UpdatePlayerDuration()
        {
            if (audioPlayer == null || sldlrPlayer == null || lblTotalTime == null)
                return;

            var duration = Math.Max(audioPlayer.Duration, 1);
            sldlrPlayer.Maximum = duration;
            lblTotalTime.Text = duration.ToMinSec();
        }

        void AudioPlayer_PositionChanged(object sender, EventArgs e)
        {
            if (audioPlayer == null)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (sldlrPlayer == null || lblCurTime == null)
                    return;

                suppressSliderSeek = true;
                sldlrPlayer.Value = audioPlayer.Position;
                lblCurTime.Text = Math.Floor(audioPlayer.Position).ToMinSec();
                suppressSliderSeek = false;
            });
        }

        void AudioPlayer_PlaybackEnded(object sender, EventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                isPlaying = false;
                UpdatePlayIcon();

                if (audioPlayer == null || sldlrPlayer == null || lblCurTime == null || lblTotalTime == null)
                    return;

                suppressSliderSeek = true;
                var endPosition = audioPlayer.Position;
                sldlrPlayer.Maximum = Math.Max(endPosition, 1);
                sldlrPlayer.Value = endPosition;
                lblCurTime.Text = endPosition.ToMinSec();
                lblTotalTime.Text = audioPlayer.Duration.ToMinSec();
                suppressSliderSeek = false;
            });
        }

        bool HasInternetConnection() =>
            Connectivity.NetworkAccess == NetworkAccess.Internet;

        private async void AddBookmark(string groupName)
        {
            globalInstance.AddBookmark(groupName);

            await System.Threading.Tasks.Task.Delay(500);
            Globals.ShowToastPopup("bookmark-saved", "Bookmard added.",
                    DeviceInfo.Platform == DevicePlatform.Android ? 120 : 0.5);
            model.BookmarkFont = "FAS";
        }

        private void ShowBookmarkSavePopup()
        {
            model.IsBookmarkGroupsShown = false;

            var inpPopup = new InputPopup
            {
                Title = "Save to",
                ActionString = "Save",
                Validation = (newKey) =>
                    model.GroupKeys.Any(key => key.Name.Equals(newKey, StringComparison.OrdinalIgnoreCase))
                        ? "Group already exists." : ""
            };
            inpPopup.SetGroups(model.GroupKeys.Select(group => group.Name));
            inpPopup.OK += InpPopup_OK;
            Navigation.ShowPopup(inpPopup);
        }

        private void Model_OnHymnChanged(object sender, EventArgs e)
        {
            var activeHymn = (Hymn)sender;
            InitAudio(activeHymn.Number);
        }

        async void btnHome_Clicked(System.Object sender, System.EventArgs e)
        {
            await Shell.Current.GoToAsync($"//{Routes.HOME}");
        }

        async void tbSearch_Clicked(System.Object sender, System.EventArgs e)
        {
            await Shell.Current.GoToAsync($"//{Routes.SEARCH}");
        }

        void tbSettings_Clicked(object sender, EventArgs e)
        {
            if (settingsOverlay?.IsVisible == true)
                return;

            EnsureSettingsOverlayBuilt();
            UpdateSelectedStates();
            settingsOverlay.IsVisible = true;
        }

        void SettingsBackdrop_Clicked(object sender, EventArgs e)
        {
            settingsOverlay.IsVisible = false;
        }

        void QueueSettingsOverlayBuild()
        {
            if (settingsOverlayBuilt || settingsOverlayBuildQueued)
                return;

            settingsOverlayBuildQueued = true;
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(500), EnsureSettingsOverlayBuilt);
        }

        void EnsureSettingsOverlayBuilt()
        {
            if (settingsOverlayBuilt || settingsOverlay == null)
                return;

            settingsOverlayBuilt = true;
            settingsOverlayBuildQueued = false;

            var backdrop = new Button
            {
                Padding = 0,
                BorderWidth = 0,
                CornerRadius = 0,
                Text = string.Empty,
                BackgroundColor = Color.FromArgb("#8C000000"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };
            backdrop.Clicked += SettingsBackdrop_Clicked;
            settingsOverlay.Children.Add(backdrop);

            var card = new Border
            {
                Padding = 14,
                Stroke = (Color)Application.Current.Resources["GrayLight"],
                StrokeThickness = 1,
                BackgroundColor = Colors.White,
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                WidthRequest = 310,
                MaximumWidthRequest = 450,
                Margin = 16,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Content = BuildSettingsContent()
            };
            settingsOverlay.Children.Add(card);

            InitializeSettingsOverlay();
        }

        View BuildSettingsContent()
        {
            var content = new VerticalStackLayout { Spacing = 8 };
            content.Children.Add(new Label
            {
                Text = "Reader Settings",
                FontAttributes = FontAttributes.Bold,
                FontSize = 15,
                TextColor = globalInstance.PrimaryText,
                Margin = new Thickness(0, 0, 0, 2)
            });

            content.Children.Add(BuildOptionSection("Alignment",
                BuildOptionRow(
                    CreateOptionBox(FontAwesomeIcons.AlignLeft, "FAS", "Start", Alignment_Tapped, out alignStartBox, out alignStartLabel),
                    CreateOptionBox(FontAwesomeIcons.AlignCenter, "FAS", "Center", Alignment_Tapped, out alignCenterBox, out alignCenterLabel),
                    CreateOptionBox(FontAwesomeIcons.AlignRight, "FAS", "End", Alignment_Tapped, out alignEndBox, out alignEndLabel))));

            content.Children.Add(BuildOptionSection("Theme",
                new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        BuildOptionRow(
                            CreateThemeBox(globalInstance.White, "White", globalInstance.PrimaryText, out themeWhiteBox, out themeWhiteLabel),
                            CreateThemeBox(globalInstance.Sepia, "Sepia", globalInstance.PrimaryText, out themeSepiaBox, out themeSepiaLabel),
                            CreateThemeBox(globalInstance.Brown, "Brown", Colors.White, out themeBrownBox, out themeBrownLabel),
                            CreateThemeBox(globalInstance.Gray, "Gray", Colors.White, out themeGrayBox, out themeGrayLabel),
                            CreateThemeBox(globalInstance.PrimaryText, "PrimaryText", Colors.White, out themeDarkBox, out themeDarkLabel)),
                        BuildOptionRow(
                            CreateThemeBox(globalInstance.Green, "Green", globalInstance.Primary, out themeGreenBox, out themeGreenLabel),
                            CreateThemeBox(globalInstance.Orange, "Orange", Colors.White, out themeOrangeBox, out themeOrangeLabel),
                            CreateThemeBox(globalInstance.Blue, "Blue", globalInstance.Primary, out themeBlueBox, out themeBlueLabel),
                            CreateThemeBox(globalInstance.Purple, "Purple", globalInstance.Primary, out themePurpleBox, out themePurpleLabel),
                            CreateThemeBox(globalInstance.Pink, "Pink", globalInstance.Primary, out themePinkBox, out themePinkLabel))
                    }
                }));

            content.Children.Add(BuildOptionSection("Font Type",
                new VerticalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        BuildOptionRow(
                            CreateOptionBox("Aa", "Roboto", "Roboto", Font_Tapped, out fontRobotoBox, out fontRobotoLabel),
                            CreateOptionBox("Aa", "NotoSerif", "NotoSerif", Font_Tapped, out fontNotoBox, out fontNotoLabel),
                            CreateOptionBox("Aa", "ChelseaMarket", "ChelseaMarket", Font_Tapped, out fontChelseaBox, out fontChelseaLabel),
                            CreateOptionBox("Aa", "UnifrakturMaguntia", "UnifrakturMaguntia", Font_Tapped, out fontUnifrakturBox, out fontUnifrakturLabel),
                            CreateOptionBox("Aa", "StyleScript", "StyleScript", Font_Tapped, out fontStyleScriptBox, out fontStyleScriptLabel)),
                        BuildOptionRow(
                            CreateOptionBox("Aa", "Cookie", "Cookie", Font_Tapped, out fontCookieBox, out fontCookieLabel),
                            CreateOptionBox("Aa", "Frosty", "Frosty", Font_Tapped, out fontFrostyBox, out fontFrostyLabel),
                            CreateOptionBox("Aa", "KGKissMeSlowly", "KGKissMeSlowly", Font_Tapped, out fontKissBox, out fontKissLabel),
                            CreateOptionBox("Aa", "KGMelonheadz", "KGMelonheadz", Font_Tapped, out fontMelonBox, out fontMelonLabel),
                            CreateOptionBox("Aa", "KGWhattheTeacherWants", "KGWhattheTeacherWants", Font_Tapped, out fontTeacherBox, out fontTeacherLabel))
                    }
                }));

            return content;
        }

        View BuildOptionSection(string title, View body)
        {
            return new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label
                    {
                        Text = title,
                        FontSize = 14,
                        FontFamily = DeviceInfo.Platform == DevicePlatform.Android ? "Roboto" : "SFPro",
                        TextColor = globalInstance.PrimaryText
                    },
                    body
                }
            };
        }

        HorizontalStackLayout BuildOptionRow(params View[] children)
        {
            var row = new HorizontalStackLayout
            {
                Spacing = 4,
                HorizontalOptions = LayoutOptions.Center
            };

            foreach (var child in children)
                row.Children.Add(child);

            return row;
        }

        Border CreateOptionBox(string text, string fontFamily, string parameter, EventHandler<TappedEventArgs> tapped, out Border box, out Label label)
        {
            label = new Label
            {
                Text = text,
                FontFamily = fontFamily,
                FontSize = 15,
                TextColor = globalInstance.PrimaryText,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            };

            box = CreateBaseOptionBox();
            box.Content = label;
            box.GestureRecognizers.Add(new TapGestureRecognizer
            {
                CommandParameter = parameter
            });
            ((TapGestureRecognizer)box.GestureRecognizers[0]).Tapped += tapped;
            return box;
        }

        Border CreateThemeBox(Color background, string parameter, Color checkColor, out Border box, out Label label)
        {
            box = CreateOptionBox(FontAwesomeIcons.Check, "FAS", parameter, Theme_Tapped, out var createdBox, out label);
            box = createdBox;
            box.BackgroundColor = background;
            label.TextColor = checkColor;
            return box;
        }

        Border CreateBaseOptionBox()
        {
            return new Border
            {
                HeightRequest = 36,
                WidthRequest = 36,
                Margin = 2,
                StrokeThickness = 1,
                Stroke = globalInstance.Gray,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                BackgroundColor = Colors.Transparent
            };
        }

        void InitializeSettingsOverlay()
        {
            alignmentOptions = new()
            {
                (alignStartBox, alignStartLabel, TextAlignment.Start),
                (alignCenterBox, alignCenterLabel, TextAlignment.Center),
                (alignEndBox, alignEndLabel, TextAlignment.End)
            };

            themeOptions = new()
            {
                (themeWhiteBox, themeWhiteLabel, globalInstance.White),
                (themeSepiaBox, themeSepiaLabel, globalInstance.Sepia),
                (themeBrownBox, themeBrownLabel, globalInstance.Brown),
                (themeGrayBox, themeGrayLabel, globalInstance.Gray),
                (themeDarkBox, themeDarkLabel, globalInstance.PrimaryText),
                (themeGreenBox, themeGreenLabel, globalInstance.Green),
                (themeOrangeBox, themeOrangeLabel, globalInstance.Orange),
                (themeBlueBox, themeBlueLabel, globalInstance.Blue),
                (themePurpleBox, themePurpleLabel, globalInstance.Purple),
                (themePinkBox, themePinkLabel, globalInstance.Pink)
            };

            fontOptions = new()
            {
                (fontRobotoBox, fontRobotoLabel, DeviceInfo.Platform == DevicePlatform.Android ? "Roboto" : "SFPro"),
                (fontNotoBox, fontNotoLabel, "NotoSerif"),
                (fontChelseaBox, fontChelseaLabel, "ChelseaMarket"),
                (fontUnifrakturBox, fontUnifrakturLabel, "UnifrakturMaguntia"),
                (fontStyleScriptBox, fontStyleScriptLabel, "StyleScript"),
                (fontCookieBox, fontCookieLabel, "Cookie"),
                (fontFrostyBox, fontFrostyLabel, "Frosty"),
                (fontKissBox, fontKissLabel, "KGKissMeSlowly"),
                (fontMelonBox, fontMelonLabel, "KGMelonheadz"),
                (fontTeacherBox, fontTeacherLabel, "KGWhattheTeacherWants")
            };

            globalInstance.ActiveReadThemeChanged += Globals_ActiveReadThemeChanged;
            UpdateSelectedStates();
        }

        void Alignment_Tapped(object sender, TappedEventArgs e)
        {
            globalInstance.ActiveAlignment = (e.Parameter as string) switch
            {
                "Center" => TextAlignment.Center,
                "End" => TextAlignment.End,
                _ => TextAlignment.Start
            };
            UpdateAlignmentSelection();
        }

        void Theme_Tapped(object sender, TappedEventArgs e)
        {
            globalInstance.ActiveReadTheme = (e.Parameter as string) switch
            {
                "Sepia" => globalInstance.Sepia,
                "Brown" => globalInstance.Brown,
                "Gray" => globalInstance.Gray,
                "PrimaryText" => globalInstance.PrimaryText,
                "Green" => globalInstance.Green,
                "Orange" => globalInstance.Orange,
                "Blue" => globalInstance.Blue,
                "Purple" => globalInstance.Purple,
                "Pink" => globalInstance.Pink,
                _ => globalInstance.White
            };
            UpdateThemeSelection();
        }

        void Font_Tapped(object sender, TappedEventArgs e)
        {
            if (e.Parameter is not string fontName)
                return;

            globalInstance.ActiveFont = fontName;
            UpdateFontSelection();
        }

        void Globals_ActiveReadThemeChanged(object sender, EventArgs e)
        {
            UpdateThemeSelection();
        }

        void UpdateSelectedStates()
        {
            UpdateAlignmentSelection();
            UpdateThemeSelection();
            UpdateFontSelection();
        }

        void UpdateAlignmentSelection()
        {
            foreach (var option in alignmentOptions)
                SetOptionSelected(option.Box, option.Label, option.Value == globalInstance.ActiveAlignment);
        }

        void UpdateThemeSelection()
        {
            foreach (var option in themeOptions)
            {
                var isSelected = option.Value.Equals(globalInstance.ActiveReadTheme);
                option.Label.Opacity = isSelected ? 1 : 0;
                option.Box.Stroke = isSelected ? globalInstance.PrimaryText : globalInstance.Gray;
            }
        }

        void UpdateFontSelection()
        {
            foreach (var option in fontOptions)
                SetOptionSelected(option.Box, option.Label, option.Value == globalInstance.ActiveFont);
        }

        void SetOptionSelected(Border box, Label label, bool isSelected)
        {
            box.BackgroundColor = isSelected ? globalInstance.Primary : Colors.Transparent;
            box.Stroke = isSelected ? globalInstance.Primary : globalInstance.Gray;
            label.TextColor = globalInstance.PrimaryText;
        }

        void PinchGestureRecognizer_PinchUpdated(System.Object sender, Microsoft.Maui.Controls.PinchGestureUpdatedEventArgs e)
        {
            if(e.Status == GestureStatus.Running)
            {
                globalInstance.ActiveFontSize = (e.Scale < 1) ?
                    Math.Max(globalInstance.ActiveFontSize * e.Scale, 15) :
                    Math.Min(globalInstance.ActiveFontSize * e.Scale, 40);
            }
        }

        async void tbBookmarks_Clicked(System.Object sender, System.EventArgs e)
        {
            if (model.BookmarkFont == "FAR")
            {
                ShowBookmarkSavePopup();
            }
            else
            {
                var answer = await DisplayAlert("Delete?", $"Are you sure you want to delete Hymn #{globalInstance.ActiveHymn.Number} as bookmark?", "Yes", "No");
                if (answer)
                {
                    globalInstance.RemoveBookmark();
                    Globals.ShowToastPopup(
                        "bookmark-deleted",
                        "Bookmard deleted.",
                        DeviceInfo.Platform == DevicePlatform.Android ? 100 : 0.4);
                    model.BookmarkFont = "FAR";
                }
            }
        }

        async void btnPlay_Clicked(System.Object sender, System.EventArgs e)
        {
            if (model?.ShowAudioPlayLoader == true)
                return;

            if (!HasInternetConnection())
            {
                await DisplayAlert(
                    "Internet Required",
                    "Hymn audio requires an internet connection to play.",
                    "OK");
                return;
            }

            if (!isAudioReady)
            {
                var number = globalInstance.ActiveHymn?.Number;
                if (string.IsNullOrEmpty(number))
                    return;

                await InitAudioAsync(number);
                if (!isAudioReady)
                    return;
            }

            if (isPlaying)
            {
                audioPlayer.Pause();
                isPlaying = false;
            }
            else
            {
                audioPlayer.Play();
                isPlaying = true;
            }

            UpdatePlayIcon();
        }

        void TapGestureRecognizer_Tapped(System.Object sender, System.EventArgs e)
        {
            model.IsReadView = !model.IsReadView;
        }

        void Slider_ValueChanged(System.Object sender, Microsoft.Maui.Controls.ValueChangedEventArgs e)
        {
            if (lblCurTime == null || audioPlayer == null)
                return;

            lblCurTime.Text = e.NewValue.ToMinSec();

            if (!suppressSliderSeek)
                audioPlayer.Seek(e.NewValue);
        }

        void ToggleEditor(object sender, EventArgs e) =>
            model.IsSelectable = !model.IsSelectable;

        void btnGrpCancel_Clicked(System.Object sender, System.EventArgs e)
        {
            model.IsBookmarkGroupsShown = false;
        }

        void TapGestureRecognizer_Tapped_1(System.Object sender, System.EventArgs e)
        {
            model.IsBookmarkGroupsShown = false;
            var groupName = ((TappedEventArgs)e).Parameter as string;
            AddBookmark(groupName);
        }

        private void InpPopup_OK(object sender, EventArgs e)
        {
            AddBookmark((string)sender);
        }

        void btnAddNewGroup_Clicked(System.Object sender, System.EventArgs e)
        {
            ShowBookmarkSavePopup();
        }
    }
}

