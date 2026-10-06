using System.IO;

namespace NeoFences.App;

/// <summary>Runtime data lives in %LOCALAPPDATA%\NeoFences (config.json, items.json, backups\, logs\, watchdog state).</summary>
public static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoFences");

    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "logs");

    /// <summary>The Game Library's shortcuts and index (M12): NeoFences' own files.</summary>
    public static string LibraryDirectory { get; } = Path.Combine(DataDirectory, "library");

    /// <summary>Pictures chosen as item icons (M18), copied in so they survive the original being moved: NeoFences' own files.</summary>
    public static string IconsDirectory { get; } = Path.Combine(DataDirectory, "icons");

    /// <summary>Covers and website icons (M34, ADR-055): chosen ones, found ones and <c>index.json</c>; NeoFences' own files.</summary>
    public static string CoversDirectory { get; } = Path.Combine(DataDirectory, "covers");

    public static string SiteIconsDirectory { get; } = Path.Combine(CoversDirectory, "sites");

    /// <summary>The icon and name cache (M37, ADR-058): NeoFences' own PNGs and their index; safe to delete (it fills again).</summary>
    public static string IconCacheDirectory { get; } = Path.Combine(DataDirectory, "cache", "icons");
}
