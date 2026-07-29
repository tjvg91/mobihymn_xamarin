using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using MobiHymn4.Services;
using MobiHymn4.Models;
using MobiHymn4.ViewModels;
using MobiHymn4.Utils;
using MobiHymn4.Services;
using MobiHymn4.Views.Popups;
using FontAwesome;

using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;
#if ANDROID
using AndroidX.AppCompat.Widget;
using AEditText = Android.Widget.EditText;
#endif


namespace MobiHymn4.Views
{
    public partial class SearchPage : ContentPage
    {
        SearchViewModel model;
        Globals globalInstance = Globals.Instance;
        readonly IAddToBoardService addToBoardService = ServiceHelper.Get<IAddToBoardService>();
        CancellationTokenSource voiceListenCts;

        public SearchPage ()
        {
            InitializeComponent();

            model = ((SearchViewModel)this.BindingContext);

            layoutSearching.HeightRequest = (DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density) - 200;
        }

        async void root_Appearing(System.Object sender, System.EventArgs e)
        {
            await Task.Delay(750);
            if (voiceListenCts == null)
                searchBar.Focus();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopVoiceListening();
        }

        async void tbHome_Clicked(System.Object sender, System.EventArgs e)
        {
            await Shell.Current.GoToAsync($"//{Routes.READ}");
        }

        async void AddToBoard_Invoked(object sender, EventArgs e)
        {
            if (sender is not SwipeItem swipe || swipe.BindingContext is not ShortHymn hymn)
                return;

            await addToBoardService.TryAddHymnAsync(hymn.Number, this);
        }

        void AddBookmark_Invoked(object sender, EventArgs e)
        {
            if (sender is not SwipeItem swipe || swipe.BindingContext is not ShortHymn hymn)
                return;

            BookmarkSaveHelper.ShowSavePopup(this, hymn);
        }

        void tbSettings_Clicked(object sender, EventArgs e)
        {
            Navigation.ShowPopup(new AgentChatSettingsPopup());
        }

        async void MyListView_ChildAdded(System.Object sender, Microsoft.Maui.Controls.ElementEventArgs e)
        {
            if (e.Element is VisualElement item)
            {
                item.Opacity = 0;
                await item.FadeTo(1, globalInstance.Duration);
            }
        }


        void searchBar_HandlerChanged(object sender, EventArgs e) => ApplySearchBarTransparentBackground();

        void searchBar_SearchButtonPressed(object sender, EventArgs e)
        {
            searchBar.Unfocus();
            _ = HideSearchKeyboardAsync();
        }

        async Task HideSearchKeyboardAsync()
        {
            try
            {
#if ANDROID
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                Android.OS.IBinder token = activity?.CurrentFocus?.WindowToken;
                if (token == null && searchBar?.Handler?.PlatformView is Android.Views.View nativeView)
                    token = nativeView.WindowToken;

                if (activity != null && token != null)
                {
                    var imm = activity.GetSystemService(Android.Content.Context.InputMethodService)
                        as Android.Views.InputMethods.InputMethodManager;
                    imm?.HideSoftInputFromWindow(token, Android.Views.InputMethods.HideSoftInputFlags.None);
                }
#else
                await Task.CompletedTask;
#endif
            }
            catch
            {
            }
        }

        void ApplySearchBarTransparentBackground()
        {
#if ANDROID
            if (searchBar?.Handler?.PlatformView is not SearchView searchView)
                return;

            searchView.SetBackgroundColor(Android.Graphics.Color.Transparent);
            SetAndroidChildBackground(searchView, "android:id/search_plate");
            SetAndroidChildBackground(searchView, "android:id/search_edit_frame");

            var textId = searchView.Context.Resources.GetIdentifier("android:id/search_src_text", null, null);
            if (searchView.FindViewById(textId) is AEditText editText)
                editText.SetBackgroundColor(Android.Graphics.Color.Transparent);
#endif
        }

#if ANDROID
        static void SetAndroidChildBackground(Android.Views.View parent, string androidId)
        {
            var id = parent.Context?.Resources?.GetIdentifier(androidId, null, null) ?? 0;
            if (id == 0)
                return;

            parent.FindViewById(id)?.SetBackgroundColor(Android.Graphics.Color.Transparent);
        }
#endif

        async void btnVoiceSearch_Clicked(object sender, EventArgs e)
        {
            if (voiceListenCts != null)
            {
                StopVoiceListening();
                return;
            }

            searchBar.Unfocus();
            voiceListenCts = new CancellationTokenSource();
            voiceListeningPanel.IsVisible = true;
            SetVoiceSearchIconActive(true);
            _ = RunVoiceSearchListenLoopAsync(voiceListenCts.Token);
        }

        async Task RunVoiceSearchListenLoopAsync(CancellationToken token)
        {
            StartVoicePulse();
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var voiceService = ServiceHelper.Get<IVoiceRecognitionService>();
                        var text = await voiceService.ListenOnceAsync(token);
                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        searchBar.Text = text;
                        if (model.SearchHymns.CanExecute(text))
                            model.SearchHymns.Execute(text);
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        await DisplayAlert("Voice Search", ex.GetBaseException().Message, "OK");
                    }
                }
            }
            finally
            {
                StopVoiceListening();
            }
        }

        void StartVoicePulse()
        {
            _ = RunVoicePulseAsync();
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

            if (voiceListeningPanel != null)
                voiceListeningPanel.IsVisible = false;

            SetVoiceSearchIconActive(false);

            if (micPulse == null)
                return;

            micPulse.AbortAnimation("ScaleTo");
            micPulse.Scale = 1;
            micPulse.Opacity = 0.25;
        }

        async Task RunVoicePulseAsync()
        {
            while (voiceListenCts != null && micPulse != null)
            {
                try
                {
                    micPulse.Opacity = 0.25;
                    await micPulse.ScaleTo(1.2, 650, Easing.CubicOut);
                    if (voiceListenCts == null)
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

        void SetVoiceSearchIconActive(bool isListening)
        {
            if (btnVoiceSearchIcon == null)
                return;

            btnVoiceSearchIcon.Glyph = isListening
                ? FontAwesomeIcons.MicrophoneSlash
                : FontAwesomeIcons.Microphone;
        }
    }
}

