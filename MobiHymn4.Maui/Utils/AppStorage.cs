using System.Diagnostics;

namespace MobiHymn4.Utils;

internal static class AppStorage
{
    public const string FolderRoot = "mobihymn";

    public static string Root => Microsoft.Maui.Storage.FileSystem.AppDataDirectory;

    public static string GetPath(params string[] segments) =>
        Path.Combine(new[] { Root }.Concat(segments).ToArray());

    public static void EnsureDirectory(string directoryPath, bool replaceExisting = false)
    {
        if (replaceExisting && Directory.Exists(directoryPath))
            Directory.Delete(directoryPath, true);

        Directory.CreateDirectory(directoryPath);
    }

    /// <summary>
    /// Copies user data from legacy Xamarin/PCLStorage locations when missing in the current app data folder.
    /// </summary>
    public static void MigrateLegacyStorageIfNeeded()
    {
        var primaryFolder = GetPath(FolderRoot);
        var primarySettings = GetPath(FolderRoot, "settings.json");
        if (File.Exists(primarySettings))
            return;

        foreach (var legacyRoot in GetLegacyRoots())
        {
            var legacyFolder = Path.Combine(legacyRoot, FolderRoot);
            var legacySettings = Path.Combine(legacyFolder, "settings.json");
            if (!File.Exists(legacySettings))
                continue;

            try
            {
                Directory.CreateDirectory(primaryFolder);
                CopyDirectory(legacyFolder, primaryFolder);
                Debug.WriteLine($"Migrated user data from {legacyFolder}");
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Legacy storage migration failed: {ex.Message}");
            }
        }
    }

    static IEnumerable<string> GetLegacyRoots()
    {
#if IOS
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(documents) && !PathsEqual(documents, Root))
            yield return documents;
#endif
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData) && !PathsEqual(localAppData, Root))
            yield return localAppData;
    }

    static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(destination, Path.GetFileName(file));
            if (!File.Exists(destFile))
                File.Copy(file, destFile);
        }

        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
