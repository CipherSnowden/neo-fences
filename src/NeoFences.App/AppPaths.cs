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
}
