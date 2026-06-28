namespace MobiHymn4.Models
{
    /// <summary>
    /// Tracks an in-progress hymn download so it can resume after force-close.
    /// </summary>
    public class DownloadCheckpoint
    {
        /// <summary>Next base hymn index to download (e.g. 42 → download 42, 42s, 42t, 42f).</summary>
        public int NextBaseIndex { get; set; } = 1;

        public bool ForceSync { get; set; }

        /// <summary>When true, only hymns not already on disk are downloaded.</summary>
        public bool MissingOnly { get; set; }

        /// <summary>When set, an incremental Firebase sync was in progress.</summary>
        public int? NextSyncDetailIndex { get; set; }

        public int SavedHymnCount { get; set; }
    }
}
