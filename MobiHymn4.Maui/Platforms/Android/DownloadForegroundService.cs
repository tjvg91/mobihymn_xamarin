using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace MobiHymn4;

/// <summary>
/// Android foreground service that keeps a persistent progress notification alive
/// for the duration of a hymn download. Started / updated / stopped via Intent
/// actions so it can be driven from any thread.
/// </summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public class DownloadForegroundService : Service
{
    public const int NotificationId = 2001;
    public const string ChannelId = "mobihymn_download_v3";
    public const string ActionUpdate = "com.mobihymn4.download.UPDATE";
    public const string ActionStop = "com.mobihymn4.download.STOP";
    public const string ExtraShowDownloadPopup = "show_download_popup";

    public static bool IsRunning { get; private set; }

    private NotificationManagerCompat notifManager;
    private string currentTitle = "Downloading hymns…";
    private PowerManager.WakeLock wakeLock;

    public override void OnCreate()
    {
        base.OnCreate();
        notifManager = NotificationManagerCompat.From(this);
    }

    public override IBinder OnBind(Intent intent) => null;

    public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
    {
        try
        {
            switch (intent?.Action)
            {
                case ActionStop:
                    if (OperatingSystem.IsAndroidVersionAtLeast(24))
                        StopForeground(StopForegroundFlags.Remove);
                    else
                        StopForeground(true);
                    ReleaseWakeLock();
                    StopSelf();
                    IsRunning = false;
                    return StartCommandResult.NotSticky;

                case ActionUpdate:
                {
                    var msg = intent.GetStringExtra("message") ?? "";
                    var cur = intent.GetIntExtra("current", -1);
                    var tot = intent.GetIntExtra("total", -1);
                    notifManager?.Notify(NotificationId, BuildNotification(currentTitle, msg, cur, tot));
                    break;
                }

                default:
                {
                    currentTitle = intent?.GetStringExtra("title") ?? "Downloading hymns…";
                    var notification = BuildNotification(currentTitle, "Starting…", -1, -1);

                    // API 29+ requires the foreground service type to be declared.
                    if (OperatingSystem.IsAndroidVersionAtLeast(29))
                        StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
                    else
                        StartForeground(NotificationId, notification);

                    AcquireWakeLock();
                    IsRunning = true;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DownloadForegroundService.OnStartCommand: {ex.Message}");
        }

        return StartCommandResult.Sticky;
    }

    private Notification BuildNotification(string title, string text, int current, int total)
    {
        var iconId = Resource.Mipmap.ic_stat_logo;
        if (iconId == 0)
            iconId = Android.Resource.Drawable.StatSysDownload;

        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle(title)
            .SetContentText(string.IsNullOrEmpty(text) ? "Working…" : text)
            .SetSmallIcon(iconId)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetSilent(true)
            .SetPriority(NotificationCompat.PriorityLow)
            .SetCategory(NotificationCompat.CategoryProgress)
            .SetVisibility(NotificationCompat.VisibilityPublic)
            .SetShowWhen(true);

        if (current >= 0 && total > 0)
            builder.SetProgress(total, current, false);
        else
            builder.SetProgress(0, 0, true); // indeterminate

        builder.SetContentIntent(CreateOpenAppPendingIntent());

        return builder.Build();
    }

    PendingIntent CreateOpenAppPendingIntent()
    {
        var launchIntent = new Intent(this, typeof(MainActivity));
        launchIntent.PutExtra(ExtraShowDownloadPopup, true);
        launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

        var flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
            flags |= PendingIntentFlags.Immutable;

        return PendingIntent.GetActivity(this, NotificationId, launchIntent, flags)!;
    }

    public override void OnDestroy()
    {
        ReleaseWakeLock();
        IsRunning = false;
        base.OnDestroy();
    }

    void AcquireWakeLock()
    {
        try
        {
            if (wakeLock?.IsHeld == true)
                return;

            var powerManager = (PowerManager)GetSystemService(PowerService);
            wakeLock = powerManager?.NewWakeLock(WakeLockFlags.Partial, "MobiHymn:DownloadSync");
            wakeLock?.Acquire();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DownloadForegroundService.WakeLock acquire: {ex.Message}");
        }
    }

    void ReleaseWakeLock()
    {
        try
        {
            if (wakeLock?.IsHeld == true)
                wakeLock.Release();

            wakeLock?.Dispose();
            wakeLock = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DownloadForegroundService.WakeLock release: {ex.Message}");
        }
    }
}
