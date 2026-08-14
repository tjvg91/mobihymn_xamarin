using System;
using System.Threading;
using System.Threading.Tasks;

namespace MobiHymn4.Services;

public interface IUserSettingsSyncService
{
    event EventHandler SyncStateChanged;

    bool IsSyncing { get; }
    string LastSyncStatus { get; }
    DateTimeOffset? LastSyncedAt { get; }

    /// <summary>Upload current local settings/bookmarks/history to Firestore.</summary>
    Task PushAsync(CancellationToken cancellationToken = default);

    /// <summary>Download cloud data, merge with local, save, then upload merged result.</summary>
    Task PullAndMergeAsync(CancellationToken cancellationToken = default);

    /// <summary>Full sync: pull+merge then ensure cloud matches local.</summary>
    Task SyncNowAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetch and apply current cloud settings when the app opens or returns to foreground.</summary>
    Task SyncOnAppOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>Debounced push after local SaveSettings (no-op if signed out).</summary>
    void SchedulePush();
}
