namespace MobiHymn4.Utils;

internal static class AppStorage
{
    public static string Root => Microsoft.Maui.Storage.FileSystem.AppDataDirectory;

    public static string GetPath(params string[] segments) =>
        Path.Combine(new[] { Root }.Concat(segments).ToArray());

    public static void EnsureDirectory(string directoryPath, bool replaceExisting = false)
    {
        if (replaceExisting && Directory.Exists(directoryPath))
            Directory.Delete(directoryPath, true);

        Directory.CreateDirectory(directoryPath);
    }
}
