using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FontAwesome;
using MobiHymn4.Models;
using MobiHymn4.Utils;
using MobiHymn4.ViewModels;
using CommunityToolkit.Maui.Views;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MobiHymn4.Views
{
    public partial class NumSearchPage : ContentPage
    {
        bool isNewInput = true;

        NumSearchViewModel model;
        CancellationTokenSource voicePulseCts;
        CancellationTokenSource voiceListenCts;

        private Globals globalInstance = Globals.Instance;

        public NumSearchPage()
        {
            InitializeComponent();

            model = ((NumSearchViewModel)BindingContext);
            model.OnHymnInputChanged += Model_OnHymnInputChanged;

            globalInstance.DownloadStarted += GlobalInstance_DownloadStarted;
            globalInstance.DownloadError += GlobalInstance_DownloadStarted;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            model.HymnInputType = globalInstance.HymnInputType;
            model.ApplyInitComplete();
            UpdateToolbarIcons();
            UpdateInputModeBar();
            UpdateBackIcon();
            UpdateVoiceListeningAnimation();
            ShowDownloadPopupIfNeeded();
            ScheduleDownloadPopupRetries();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopVoiceListening();
        }

        static Color GetNavBarIconColor() =>
            (Color)Application.Current.Resources["PrimaryText"];

        void UpdateBackIcon()
        {
            if (btnBack == null)
                return;

            btnBack.Source = new FontImageSource
            {
                FontFamily = "FAS",
                Glyph = FontAwesomeIcons.ArrowLeft,
                Size = 20,
                Color = GetNavBarIconColor(),
            };
        }

        async void btnBack_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync($"//{Routes.READ}");
        }

        void UpdateToolbarIcons()
        {
            var iconColor = GetNavBarIconColor();

            if (tbSearch != null)
            {
                tbSearch.IconImageSource = new FontImageSource
                {
                    FontFamily = "FAS",
                    Glyph = FontAwesomeIcons.Search,
                    Size = 17,
                    Color = iconColor,
                };
            }

            if (tbSettings != null)
            {
                tbSettings.IconImageSource = new FontImageSource
                {
                    FontFamily = "FAS",
                    Glyph = FontAwesomeIcons.Cogs,
                    Size = 17,
                    Color = iconColor,
                };
            }
        }

        void UpdateInputModeBar()
        {
            var activeType = model?.HymnInputType ?? globalInstance.HymnInputType;
            var primary = (Color)Application.Current.Resources["Primary"];
            var primaryText = (Color)Application.Current.Resources["PrimaryText"];
            var inactiveBg = Application.Current?.RequestedTheme == AppTheme.Dark
                ? (Color)Application.Current.Resources["Gray"]
                : Colors.White;
            var inactiveText = Application.Current?.RequestedTheme == AppTheme.Dark
                ? Colors.White
                : (Color)Application.Current.Resources["PrimaryText"];

            SetModeButtonStyle(btnModeGrid, activeType == Utils.InputType.Grid, primary, primaryText, inactiveBg, inactiveText);
            SetModeButtonStyle(btnModeNumpad, activeType == Utils.InputType.Numpad, primary, primaryText, inactiveBg, inactiveText);
            SetModeButtonStyle(btnModeVoice, activeType == Utils.InputType.Voice, primary, primaryText, inactiveBg, inactiveText);
        }

        static void SetModeButtonStyle(Button button, bool isActive, Color activeBg, Color activeText, Color inactiveBg, Color inactiveText)
        {
            if (button == null)
                return;

            button.BackgroundColor = isActive ? activeBg : inactiveBg;
            var iconColor = isActive ? activeText : inactiveText;
            if (button.ImageSource is FontImageSource fontImage)
                fontImage.Color = iconColor;
        }

        void SetInputMode(Utils.InputType inputType)
        {
            globalInstance.HymnInputType = inputType;
            model.HymnInputType = inputType;
            UpdateInputModeBar();
            UpdateVoiceListeningAnimation();
        }

        void ModeGrid_Clicked(object sender, EventArgs e) => SetInputMode(Utils.InputType.Grid);

        void ModeNumpad_Clicked(object sender, EventArgs e) => SetInputMode(Utils.InputType.Numpad);

        void ModeVoice_Clicked(object sender, EventArgs e) => SetInputMode(Utils.InputType.Voice);

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
        }

        private void GlobalInstance_DownloadStarted(object sender, EventArgs e)
        {
            DownloadPopupPresenter.ShowFromEvent();
        }

        private void Model_OnHymnInputChanged(object sender, EventArgs e)
        {
            btnHymnNum.Text = (string)sender;
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            model.Orientation = DeviceDisplay.MainDisplayInfo.Orientation;
        }

        async void Key_Clicked(Object sender, EventArgs e)
        {
            try
            {
                if (sender.Equals(FindByName("btnHymnNum"))) isNewInput = true;
                else
                {
                    string key = ((Button)sender).BindingContext as string;
                    if (string.IsNullOrEmpty(key))
                        return;

                    if (new Regex("[0-9]").IsMatch(key))
                    {
                        if (isNewInput)
                        {
                            model.HymnNum = key;
                            isNewInput = false;
                        }
                        else model.HymnNum += key;
                    }
                    else if (new Regex("[stf]").IsMatch(key))
                    {
                        model.HymnNum += key;
                        isNewInput = false;
                    }
                    else
                    {
                        if (key == "b")
                        {
                            if (string.IsNullOrEmpty(model.HymnNum))
                            {
                                model.HymnNum = "1";
                                isNewInput = true;
                                return;
                            }

                            model.HymnNum = model.HymnNum.Remove(model.HymnNum.Length - 1);
                            if (model.HymnNum.Length == 0)
                            {
                                model.HymnNum = "1";
                                isNewInput = true;
                            }
                        }
                        else if (key == "e")
                        {
                            var hymn = globalInstance.HymnList?.FirstOrDefault(h => h?.Number == model.HymnNum);
                            if (hymn == null)
                            {
                                ShowHymnNotFound();
                                isNewInput = true;
                                return;
                            }

                            globalInstance.ActiveHymn = hymn;
                            isNewInput = true;
                            await Shell.Current.GoToAsync($"//{Routes.READ}");
                        }
                        else
                        {
                            ShowHymnNotFound();
                            isNewInput = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Numpad Error", FormatExceptionForAlert(ex), "OK");
            }
        }

        private static string FormatExceptionForAlert(Exception ex)
        {
            var baseException = ex.GetBaseException();
            return $"{baseException.GetType().Name}: {baseException.Message}";
        }

        private static void ShowHymnNotFound() =>
            ShowVoiceToast("Hymn not found");

        private static void ShowVoiceToast(string message)
        {
            if (DeviceInfo.Platform == DevicePlatform.Android)
                Globals.ShowToastPopup(
                    "not-found",
                    message,
                    130,
                    new Rect(0.3, 0, 0.8, 0.9)
                );
            else
                Globals.ShowToastPopup(
                    "not-found",
                    message,
                    0.7,
                    new Rect(0.3, -0.5, 0.8, 0.9)
                );
        }

        void UpdateVoiceListeningAnimation()
        {
            if (model?.HymnInputType == Utils.InputType.Voice && model.IsNotBusy)
                StartVoiceListening();
            else
                StopVoiceListening();
        }

        async void StartVoiceListening()
        {
            if (voiceListenCts != null)
                return;

            if (model?.HymnInputType != Utils.InputType.Voice || !model.IsNotBusy)
                return;

            voiceListenCts = new CancellationTokenSource();
            _ = RunVoiceListenLoopAsync(voiceListenCts.Token);
        }

        async Task RunVoiceListenLoopAsync(CancellationToken token)
        {
            StartVoicePulse();
            try
            {
                while (!token.IsCancellationRequested &&
                       model?.HymnInputType == Utils.InputType.Voice &&
                       model.IsNotBusy)
                {
                    try
                    {
                        var voiceService = ServiceHelper.Get<IVoiceRecognitionService>();
                        var recognizedText = await voiceService.ListenOnceAsync(token);
                        if (string.IsNullOrWhiteSpace(recognizedText))
                            continue;

                        if (!VoiceHymnParser.TryParseHymnNumber(recognizedText, out var hymnNumber))
                        {
                            ShowVoiceToast($"Couldn't find hymn number in \"{recognizedText}\"");
                            continue;
                        }

                        model.HymnNum = hymnNumber;
                        var hymn = globalInstance.HymnList?.FirstOrDefault(h => h?.Number.Equals(hymnNumber, StringComparison.OrdinalIgnoreCase) == true);
                        if (hymn == null)
                        {
                            ShowHymnNotFound();
                            continue;
                        }

                        globalInstance.ActiveHymn = hymn;
                        await Shell.Current.GoToAsync($"//{Routes.READ}");
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        ShowVoiceToast(ex.GetBaseException().Message);
                    }
                }
            }
            finally
            {
                StopVoiceListening();
            }
        }

        void StopVoiceListening()
        {
            var cts = voiceListenCts;
            voiceListenCts = null;

            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
            }

            StopVoicePulse();
        }

        void StartVoicePulse()
        {
            if (voicePulseCts != null || micPulse == null)
                return;

            voicePulseCts = new CancellationTokenSource();
            _ = RunVoicePulseAsync(voicePulseCts.Token);
        }

        void StopVoicePulse()
        {
            var cts = voicePulseCts;
            voicePulseCts = null;

            if (cts == null)
                return;

            cts.Cancel();
            cts.Dispose();

            if (micPulse == null)
                return;

            micPulse.AbortAnimation("ScaleTo");
            micPulse.Scale = 1;
            micPulse.Opacity = 0.25;
        }

        async Task RunVoicePulseAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && micPulse != null)
            {
                try
                {
                    micPulse.Opacity = 0.25;
                    await micPulse.ScaleTo(1.22, 650, Easing.CubicOut);
                    if (token.IsCancellationRequested)
                        break;

                    micPulse.Opacity = 0.08;
                    await micPulse.ScaleTo(1, 650, Easing.CubicIn);
                }
                catch
                {
                    break;
                }
            }
        }

        async void tbSearch_Clicked(Object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync($"//{Routes.SEARCH}");
        }

        async void tbSettings_Clicked(System.Object sender, System.EventArgs e)
        {
            await Shell.Current.GoToAsync($"//{Routes.SETTINGS}");
        }

        void Hundreds_Clicked(Object sender, EventArgs e)
        {
            var index = ParseGridIndex(((Button)sender).CommandParameter);
            if (index >= 0)
                model.HundredSelected.Execute(index);
        }

        void Tens_Clicked(System.Object sender, System.EventArgs e)
        {
            var index = ParseGridIndex(((Button)sender).CommandParameter);
            if (index >= 0)
                model.TenSelected.Execute(index);
        }

        void Ones_Clicked(System.Object sender, System.EventArgs e)
        {
            if (((Button)sender).CommandParameter is Hymn hymn)
                model.OneSelected.Execute(hymn);
        }

        static int ParseGridIndex(object parameter) =>
            parameter switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var n) => n,
                _ => -1
            };
    }
}

