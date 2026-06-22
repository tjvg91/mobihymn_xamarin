using System;
using System.Threading;
using System.Threading.Tasks;
using MobiHymn4.Models;
using Microsoft.Maui.ApplicationModel;

#if ANDROID
using Android.Media;
#elif IOS
using AVFoundation;
using CoreMedia;
using Foundation;
#endif

namespace MobiHymn4.Services;

public class HymnAudioPlayer : IPlayService
{
#if ANDROID
    MediaPlayer mediaPlayer;
    System.Timers.Timer positionTimer;
    TaskCompletionSource<bool>? pendingLoad;
    CancellationTokenSource? loadTimeoutCts;
#elif IOS
    AVPlayer avPlayer;
    NSObject timeObserver;
    NSObject bufferEmptyObserver;
    NSObject bufferReadyObserver;
#endif

    public double Duration { get; private set; }
    public double Position { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool IsBuffering { get; private set; }

    public event EventHandler PlaybackEnded;
    public event EventHandler PositionChanged;
    public event EventHandler BufferingChanged;

    public Task<bool> LoadAsync(string url)
    {
        StopInternal(resetDuration: true);

#if ANDROID
        return LoadAndroidAsync(url);
#elif IOS
        return LoadIosAsync(url);
#else
        return Task.FromResult(false);
#endif
    }

    public void Play()
    {
#if ANDROID
        if (mediaPlayer == null)
            return;

        mediaPlayer.Start();
        IsPlaying = true;
        StartPositionTimer();
#elif IOS
        if (avPlayer == null)
            return;

        avPlayer.Play();
        IsPlaying = true;
#endif
    }

    public void Pause()
    {
#if ANDROID
        if (mediaPlayer?.IsPlaying == true)
            mediaPlayer.Pause();
#elif IOS
        avPlayer?.Pause();
#endif

        IsPlaying = false;
        StopPositionTimer();
        SetBuffering(false);
    }

    public void Stop()
    {
        StopInternal(resetDuration: false);
    }

    public void Seek(double seconds)
    {
        var clamped = Math.Max(0, seconds);
#if ANDROID
        if (mediaPlayer != null)
        {
            mediaPlayer.SeekTo((int)(clamped * 1000));
            Position = clamped;
            RaisePositionChanged();
        }
#elif IOS
        if (avPlayer != null)
        {
            var time = CMTime.FromSeconds(clamped, 1000);
            avPlayer.Seek(time);
            Position = clamped;
            RaisePositionChanged();
        }
#endif
    }

    void StopInternal(bool resetDuration)
    {
        IsPlaying = false;
        StopPositionTimer();
        SetBuffering(false);

#if ANDROID
        CancelPendingLoad();

        if (mediaPlayer != null)
        {
            if (mediaPlayer.IsPlaying)
                mediaPlayer.Stop();

            mediaPlayer.Release();
            mediaPlayer.Dispose();
            mediaPlayer = null;
        }
#elif IOS
        if (timeObserver != null)
        {
            avPlayer?.RemoveTimeObserver(timeObserver);
            timeObserver.Dispose();
            timeObserver = null;
        }

        if (bufferEmptyObserver != null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(bufferEmptyObserver);
            bufferEmptyObserver.Dispose();
            bufferEmptyObserver = null;
        }

        if (bufferReadyObserver != null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(bufferReadyObserver);
            bufferReadyObserver.Dispose();
            bufferReadyObserver = null;
        }

        avPlayer?.Pause();
        avPlayer?.Dispose();
        avPlayer = null;
#endif

        Position = 0;
        if (resetDuration)
            Duration = 0;

        RaisePositionChanged();
    }

    void StopPositionTimer()
    {
#if ANDROID
        if (positionTimer == null)
            return;

        positionTimer.Stop();
        positionTimer.Dispose();
        positionTimer = null;
#endif
    }

#if ANDROID
    Task<bool> LoadAndroidAsync(string url)
    {
        CancelPendingLoad();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingLoad = tcs;
        loadTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        loadTimeoutCts.Token.Register(() => CompletePendingLoad(false));

        try
        {
            mediaPlayer = new MediaPlayer();
            mediaPlayer.SetDataSource(url);
            mediaPlayer.Info += (_, e) =>
            {
                if (pendingLoad != tcs)
                    return;

                if (e.What == MediaInfo.BufferingStart)
                    SetBuffering(true);
                else if (e.What == MediaInfo.BufferingEnd)
                    SetBuffering(false);
            };
            mediaPlayer.Prepared += (_, _) =>
            {
                if (pendingLoad != tcs)
                    return;

                Duration = mediaPlayer.Duration / 1000.0;
                SetBuffering(false);
                CompletePendingLoad(true);
            };
            mediaPlayer.Error += (_, e) =>
            {
                if (pendingLoad != tcs)
                    return;

                System.Diagnostics.Debug.WriteLine($"Audio load error: {e.What} / {e.Extra}");
                CompletePendingLoad(false);
            };
            mediaPlayer.Completion += (_, _) =>
            {
                IsPlaying = false;
                SetBuffering(false);
                FinalizePlaybackAtCurrentPosition();
                StopPositionTimer();
                RaisePositionChanged();
                PlaybackEnded?.Invoke(this, EventArgs.Empty);
            };
            mediaPlayer.PrepareAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Audio load failed: {ex.Message}");
            CompletePendingLoad(false);
        }

        return tcs.Task;
    }

    void CompletePendingLoad(bool success)
    {
        loadTimeoutCts?.Cancel();
        loadTimeoutCts?.Dispose();
        loadTimeoutCts = null;

        if (pendingLoad == null)
            return;

        pendingLoad.TrySetResult(success);
        pendingLoad = null;
    }

    void CancelPendingLoad()
    {
        loadTimeoutCts?.Cancel();
        loadTimeoutCts?.Dispose();
        loadTimeoutCts = null;
        pendingLoad?.TrySetResult(false);
        pendingLoad = null;
    }

    void StartPositionTimer()
    {
        StopPositionTimer();
        positionTimer = new System.Timers.Timer(500);
        positionTimer.Elapsed += (_, _) =>
        {
            if (mediaPlayer == null)
                return;

            Position = mediaPlayer.CurrentPosition / 1000.0;
            MainThread.BeginInvokeOnMainThread(RaisePositionChanged);
        };
        positionTimer.Start();
    }
#elif IOS
    Task<bool> LoadIosAsync(string url)
    {
        try
        {
            avPlayer = AVPlayer.FromUrl(new NSUrl(url));
            if (avPlayer?.CurrentItem == null)
                return Task.FromResult(false);

            var durationSeconds = avPlayer.CurrentItem.Asset.Duration.Seconds;
            Duration = double.IsNaN(durationSeconds) || durationSeconds < 0 ? 0 : durationSeconds;

            timeObserver = avPlayer.AddPeriodicTimeObserver(
                CMTime.FromSeconds(0.5, 1000),
                null,
                time =>
                {
                    Position = time.Seconds;
                    MainThread.BeginInvokeOnMainThread(RaisePositionChanged);
                });

            NSNotificationCenter.DefaultCenter.AddObserver(
                AVPlayerItem.DidPlayToEndTimeNotification,
                _ =>
                {
                    IsPlaying = false;
                    SetBuffering(false);
                    FinalizePlaybackAtCurrentPosition();
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        RaisePositionChanged();
                        PlaybackEnded?.Invoke(this, EventArgs.Empty);
                    });
                },
                avPlayer.CurrentItem);

            var currentItem = avPlayer.CurrentItem;
            bufferEmptyObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                AVPlayerItem.PlaybackBufferEmptyNotification,
                _ =>
                {
                    if (IsPlaying)
                        SetBuffering(true);
                },
                currentItem);

            bufferReadyObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                AVPlayerItem.PlaybackLikelyToKeepUpNotification,
                _ => SetBuffering(false),
                currentItem);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Audio load failed: {ex.Message}");
            return Task.FromResult(false);
        }
    }
#endif

    void FinalizePlaybackAtCurrentPosition()
    {
#if ANDROID
        if (mediaPlayer != null)
        {
            var endPosition = mediaPlayer.CurrentPosition / 1000.0;
            if (endPosition > 0)
            {
                Position = endPosition;
                if (endPosition < Duration)
                    Duration = endPosition;
            }
            else
            {
                Position = Duration;
            }
        }
#elif IOS
        if (avPlayer?.CurrentItem != null)
        {
            var endPosition = avPlayer.CurrentTime.Seconds;
            if (!double.IsNaN(endPosition) && endPosition > 0)
            {
                Position = endPosition;
                if (endPosition < Duration)
                    Duration = endPosition;
            }
            else
            {
                Position = Duration;
            }
        }
#endif
    }

    void SetBuffering(bool buffering)
    {
        if (IsBuffering == buffering)
            return;

        IsBuffering = buffering;
        BufferingChanged?.Invoke(this, EventArgs.Empty);
    }

    void RaisePositionChanged() => PositionChanged?.Invoke(this, EventArgs.Empty);
}
