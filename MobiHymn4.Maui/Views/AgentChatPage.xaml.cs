using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using FontAwesome;
using MobiHymn4.Extensions;
using MobiHymn4.Models;
using MobiHymn4.Services;
using MobiHymn4.ViewModels;
using MobiHymn4.Views.Popups;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Views
{
    public partial class AgentChatPage : ContentPage
    {
        CancellationTokenSource voiceListenCts;
        AgentChatViewModel model;

        public AgentChatPage()
        {
            InitializeComponent();

            model = BindingContext as AgentChatViewModel;
            if (model != null)
                model.Messages.CollectionChanged += Messages_CollectionChanged;

            DraftEntry.HandlerChanged += DraftEntry_HandlerChanged;
        }

        void tbSettings_Clicked(object sender, EventArgs e)
        {
            Navigation.ShowPopup(new AgentChatSettingsPopup());
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            StopVoiceListening();
        }

        void ChatScroll_SizeChanged(object sender, EventArgs e)
        {
            // Keep short threads (e.g. welcome) pinned to the bottom of the viewport.
            if (ChatScroll.Height > 0)
                MessagesHost.MinimumHeightRequest = ChatScroll.Height;
        }

        void DraftEntry_HandlerChanged(object sender, EventArgs e)
        {
            // Replace the platform underline with our theme-aware BoxView line.
#if ANDROID
            if (DraftEntry.Handler?.PlatformView is Android.Widget.EditText editText)
            {
                editText.Background = null;
                editText.SetBackgroundColor(Android.Graphics.Color.Transparent);
            }
#endif
#if IOS || MACCATALYST
            if (DraftEntry.Handler?.PlatformView is UIKit.UITextField field)
                field.BorderStyle = UIKit.UITextBorderStyle.None;
#endif
        }

        async void btnVoice_Clicked(object sender, EventArgs e)
        {
            if (voiceListenCts != null)
            {
                StopVoiceListening();
                return;
            }

            if (model?.IsBusy == true)
                return;

            DraftEntry.Unfocus();
            voiceListenCts = new CancellationTokenSource();
            voiceListeningPanel.IsVisible = true;
            SetVoiceIconActive(true);
            _ = RunVoiceListenAsync(voiceListenCts.Token);
        }

        async Task RunVoiceListenAsync(CancellationToken token)
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

                        model.Draft = text.Trim();
                        if (model.SendCommand?.CanExecute(null) == true)
                            model.SendCommand.Execute(null);
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        await DisplayAlert("Voice", ex.GetBaseException().Message, "OK");
                        break;
                    }
                }
            }
            finally
            {
                StopVoiceListening();
            }
        }

        void StartVoicePulse() => _ = RunVoicePulseAsync();

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

            SetVoiceIconActive(false);

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

        void SetVoiceIconActive(bool isListening)
        {
            if (btnVoiceIcon == null)
                return;

            btnVoiceIcon.Glyph = isListening
                ? FontAwesomeIcons.MicrophoneSlash
                : FontAwesomeIcons.Microphone;
        }

        async void Messages_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is not (NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset))
                return;

            await ScrollChatToEndAsync();
        }

        async Task ScrollChatToEndAsync()
        {
            await Task.Delay(40);
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try { await ChatScroll.ScrollToAsync(0, ChatScroll.ContentSize.Height, animated: true); }
                catch { /* layout may not be ready yet */ }
            });
        }
    }
}
