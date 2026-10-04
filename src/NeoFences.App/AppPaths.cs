using System.IO;

namespace NeoFences.App;

/// <summary>Runtime data lives in %LOCALAPPDATA%\NeoFences (config.json, backups\, logs\, watchdog state).</summary>
public static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoFences");

    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "logs");

    /// <summary>The Game Library's shortcuts and index (M12): NeoFences' own files.</summary>
    public static string LibraryDirectory { get; } = Path.Combine(DataDirectory, "library");
}
