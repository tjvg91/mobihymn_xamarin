using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using MobiHymn4.Models;
using MobiHymn4.Utils;

namespace MobiHymn4;

/// <summary>
/// Android implementation of IDownloadNotificationService.
/// Wires into Globals events at construction time and drives
/// DownloadForegroundService via Intents.
/// </summary>
public class DownloadNotificationService : IDownloadNotificationService
{
    private readonly Context ctx = Android.App.Application.Context;

    public DownloadNotificationService()
    {
        CreateChannel();

        var g = Globals.Instance;
        g.DownloadStarted += (_, e) => { _ = StartAsync(); };
        g.DownloadProgressed += (sender, e) => { _ = UpdateProgressAsync((string)sender, -1, -1); };
        g.InitFinished += (_, e) => TryRun(Stop);
        g.DownloadError += (_, e) => TryRun(Stop);
    }

    public void Start(string title = "Downloading hymns…")
        => _ = StartAsync(title);

    public void UpdateProgress(string message, int current = -1, int total = -1)
        => _ = UpdateProgressAsync(message, current, total);

    public void Stop()
    {
        if (!DownloadForegroundService.IsRunning)
            return;

        var intent = new Intent(ctx, typeof(DownloadForegroundService));
        intent.SetAction(DownloadForegroundService.ActionStop);
        ctx.StartService(intent);
    }

    async Task StartAsync(string title = "Downloading hymns…")
    {
        try
        {
            RunOnMainThread(() =>
            {
                var intent = new Intent(ctx, typeof(DownloadForegroundService));
                intent.PutExtra("title", title);
                StartOrUpdateService(intent, foreground: true);
            });

            await EnsureNotificationPermissionAsync(requestIfNeeded: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DownloadNotif.Start: {ex.Message}");
        }
    }

    async Task UpdateProgressAsync(string message, int current, int total)
    {
        try
        {
            RunOnMainThread(() =>
            {
                if (!DownloadForegroundService.IsRunning)
                {
                    var startIntent = new Intent(ctx, typeof(DownloadForegroundService));
                    startIntent.PutExtra("title", "Downloading hymns…");
                    StartOrUpdateService(startIntent, foreground: true);
                }

                var intent = new Intent(ctx, typeof(DownloadForegroundService));
                intent.SetAction(DownloadForegroundService.ActionUpdate);
                intent.PutExtra("message", message);
                intent.PutExtra("current", current);
                intent.PutExtra("total", total);
                ctx.StartService(intent);
            });

            await EnsureNotificationPermissionAsync(requestIfNeeded: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DownloadNotif.Update: {ex.Message}");
        }
    }

    static async Task<bool> EnsureNotificationPermissionAsync(bool requestIfNeeded)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return true;

        var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
        if (status == PermissionStatus.Granted)
            return true;

        if (!requestIfNeeded)
            return false;

        status = await Permissions.RequestAsync<Permissions.PostNotifications>();
        return status == PermissionStatus.Granted;
    }

    static void RunOnMainThread(Action action)
    {
        if (MainThread.IsMainThread)
            action();
        else
            MainThread.BeginInvokeOnMainThread(action);
    }

    static void TryRun(Action action)
    {
        try { action(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"DownloadNotif: {ex.Message}"); }
    }

    void StartOrUpdateService(Intent intent, bool foreground)
    {
        if (foreground && OperatingSystem.IsAndroidVersionAtLeast(26))
            ctx.StartForegroundService(intent);
        else
            ctx.StartService(intent);
    }

    void CreateChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var channel = new NotificationChannel(
            DownloadForegroundService.ChannelId,
            "Hymn Download",
            NotificationImportance.Low)
        {
            Description = "Shows hymn download progress"
        };
        channel.SetSound(null, null);
        channel.EnableVibration(false);
        channel.EnableLights(false);
        channel.SetShowBadge(true);

        var mgr = (NotificationManager)ctx.GetSystemService(Context.NotificationService)!;
        mgr.CreateNotificationChannel(channel);
    }
}
