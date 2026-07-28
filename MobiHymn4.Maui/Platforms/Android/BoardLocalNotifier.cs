#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using System;
using System.Collections.Generic;

namespace MobiHymn4.Platforms.Android;

/// <summary>
/// Shows a tray notification for board updates while the app is in the foreground
/// (FCM notification payloads are often suppressed when the app is active).
/// </summary>
public static class BoardLocalNotifier
{
    const int BaseNotifyId = 4200;
    static string lastDedupKey;
    static long lastDedupAtMs;

    public static void Show(string title, string body, IReadOnlyDictionary<string, string> data = null)
    {
        try
        {
            var context = global::Android.App.Application.Context;
            if (context == null)
                return;

            var listId = data != null && data.TryGetValue("listId", out var lid) ? lid : null;
            if (string.IsNullOrWhiteSpace(listId) && data != null && data.TryGetValue("date", out var date))
                listId = date;

            var dedupKey = $"{title}|{body}|{listId}";
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (string.Equals(dedupKey, lastDedupKey, StringComparison.Ordinal) && nowMs - lastDedupAtMs < 4000)
                return;
            lastDedupKey = dedupKey;
            lastDedupAtMs = nowMs;

            var channelId = $"{context.PackageName}.board";
            EnsureChannel(context, channelId);

            var intent = new Intent(context, typeof(MainActivity));
            intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop | ActivityFlags.NewTask);
            if (data != null)
            {
                foreach (var pair in data)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                        intent.PutExtra(pair.Key, pair.Value);
                }
            }

            var requestCode = StableId(listId);
            var pendingIntentFlags = PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable;
            var contentIntent = PendingIntent.GetActivity(
                context,
                requestCode,
                intent,
                pendingIntentFlags);

            var tag = string.IsNullOrWhiteSpace(listId) ? "board" : $"board-{listId}";
            var builder = new NotificationCompat.Builder(context, channelId)
                .SetContentTitle(string.IsNullOrWhiteSpace(title) ? "Hymn list updated" : title)
                .SetContentText(body ?? string.Empty)
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(body ?? string.Empty))
                .SetSmallIcon(Resource.Mipmap.ic_stat_logo)
                .SetAutoCancel(true)
                .SetOnlyAlertOnce(true)
                .SetPriority(NotificationCompat.PriorityHigh)
                .SetDefaults((int)NotificationDefaults.All)
                .SetContentIntent(contentIntent);

            var manager = NotificationManagerCompat.From(context);
            if (!manager.AreNotificationsEnabled())
            {
                System.Diagnostics.Debug.WriteLine("BoardLocalNotifier: notifications disabled for app");
                return;
            }

            // Same tag+id replaces an existing board notification instead of stacking.
            manager.Notify(tag, BaseNotifyId, builder.Build());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BoardLocalNotifier.Show failed: {ex.Message}");
        }
    }

    static int StableId(string listId)
    {
        if (string.IsNullOrWhiteSpace(listId))
            return BaseNotifyId;
        unchecked
        {
            return BaseNotifyId + listId.GetHashCode();
        }
    }

    static void EnsureChannel(Context context, string channelId)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
        if (manager == null)
            return;

        var existing = manager.GetNotificationChannel(channelId);
        if (existing != null)
            return;

        var channel = new NotificationChannel(
            channelId,
            "Worship board",
            NotificationImportance.High)
        {
            Description = "Updates when your group hymn list changes",
        };
        manager.CreateNotificationChannel(channel);
    }
}
#endif
