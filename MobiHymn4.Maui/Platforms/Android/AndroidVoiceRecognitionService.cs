using Android.Content;
using Android.OS;
using Android.Speech;
using Java.Util;
using MobiHymn4.Models;

namespace MobiHymn4;

public class AndroidVoiceRecognitionService : IVoiceRecognitionService
{
    readonly Context context = Android.App.Application.Context;

    public async Task<string> ListenOnceAsync(CancellationToken cancellationToken = default)
    {
        var permission = await Permissions.RequestAsync<Permissions.Microphone>();
        if (permission != PermissionStatus.Granted)
            throw new InvalidOperationException("Microphone permission is required for voice search.");

        if (!SpeechRecognizer.IsRecognitionAvailable(context))
            throw new NotSupportedException("Speech recognition is not available on this device.");

        return await MainThread.InvokeOnMainThreadAsync(() => ListenOnMainThreadAsync(cancellationToken));
    }

    Task<string> ListenOnMainThreadAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var recognizer = SpeechRecognizer.CreateSpeechRecognizer(context);
        var listener = new SingleResultRecognitionListener(completion);
        recognizer.SetRecognitionListener(listener);

        var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(RecognizerIntent.ExtraLanguage, Java.Util.Locale.Default);
        intent.PutExtra(RecognizerIntent.ExtraMaxResults, 3);
        intent.PutExtra(RecognizerIntent.ExtraPartialResults, false);

        CancellationTokenRegistration cancellationRegistration = default;
        if (cancellationToken.CanBeCanceled)
        {
            cancellationRegistration = cancellationToken.Register(() =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try { recognizer.Cancel(); } catch { }
                    completion.TrySetCanceled(cancellationToken);
                });
            });
        }

        completion.Task.ContinueWith(_ =>
        {
            cancellationRegistration.Dispose();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try { recognizer.Destroy(); } catch { }
            });
        });

        recognizer.StartListening(intent);
        return completion.Task;
    }

    class SingleResultRecognitionListener : Java.Lang.Object, IRecognitionListener
    {
        readonly TaskCompletionSource<string> completion;

        public SingleResultRecognitionListener(TaskCompletionSource<string> completion)
        {
            this.completion = completion;
        }

        public void OnReadyForSpeech(Bundle @params) { }
        public void OnBeginningOfSpeech() { }
        public void OnRmsChanged(float rmsdB) { }
        public void OnBufferReceived(byte[] buffer) { }
        public void OnEndOfSpeech() { }
        public void OnPartialResults(Bundle partialResults) { }
        public void OnEvent(int eventType, Bundle @params) { }

        public void OnError(SpeechRecognizerError error)
        {
            var message = error == SpeechRecognizerError.NoMatch
                ? "I couldn't understand that. Please try again."
                : $"Voice recognition failed: {error}";
            completion.TrySetException(new InvalidOperationException(message));
        }

        public void OnResults(Bundle results)
        {
            var matches = results?.GetStringArrayList(SpeechRecognizer.ResultsRecognition);
            var text = matches?.FirstOrDefault() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text))
                completion.TrySetException(new InvalidOperationException("I couldn't understand that. Please try again."));
            else
                completion.TrySetResult(text);
        }
    }
}
