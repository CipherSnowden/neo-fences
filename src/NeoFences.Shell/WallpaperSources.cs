using System.Diagnostics;
using Microsoft.Win32;
using NeoFences.Core.Appearance;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace NeoFences.Shell;

/// <summary>One monitor's wallpaper image (M14): where it came from, for Settings ("From Wallpaper Engine: Kara").</summary>
public sealed record WallpaperImage(int Left, int Top, int Right, int Bottom, string? ImagePath, string Origin);

/// <summary>
/// Where the wallpaper accent comes from (M14, spec §4), read only, first that works per monitor: Wallpaper Engine's
/// selected wallpaper's preview, Windows' wallpaper for that monitor, then Windows' accent colour. Never captures the
/// screen. Every failure falls through to the next source (hard rule 7).
/// </summary>
public static class WallpaperSources
{
    /// <summary>Wallpaper Engine's <c>config.json</c> (to watch for wallpaper changes), or null when WE is not installed.</summary>
    public static string? WallpaperEngineConfig()
    {
        if (GameScanners.SteamRootForWallpaper() is not { } steam) return null;
        return GameScanners.SteamLibrariesForWallpaper(steam)
            .Select(library => Path.Combine(library, "steamapps", "common", "wallpaper_engine", "config.json"))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Every monitor with its wallpaper image (null when only Windows' accent is left for it).</summary>
    public static IReadOnlyList<WallpaperImage> Read(Action<string, Exception> logFailure)
    {
        var monitors = WindowsWallpapers(logFailure);
        var engine = WallpaperEnginePreviews(logFailure);
        return monitors.Select((monitor, index) =>
        {
            // ponytail: WE's "MonitorN" is taken as Windows' Nth wallpaper monitor; its first entry when N is missing.
            if (engine.Count > 0 && (engine.TryGetValue(index, out var preview) || engine.TryGetValue(engine.Keys.Min(), out preview)))
                return monitor with { ImagePath = preview.Path, Origin = "Wallpaper Engine: " + preview.Title };
            return monitor;
        }).ToList();
    }

    /// <summary>Windows' accent colour (Settings → Personalization → Colors), or null.</summary>
    public static Argb? WindowsAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is not int abgr) return null;
            return new Argb(0xFF, (byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
        }
        catch (Exception failure) when (failure is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<int, (string Path, string Title)> WallpaperEnginePreviews(Action<string, Exception> logFailure)
    {
        var previews = new Dictionary<int, (string, string)>();
        try
        {
            if (!Process.GetProcessesByName("wallpaper64").Concat(Process.GetProcessesByName("wallpaper32")).Any()) return previews;
            if (WallpaperEngineConfig() is not { } config) return previews;
            foreach (var (index, file) in WallpaperEngineFiles.SelectedWallpapers(File.ReadAllText(config)))
            {
                var folder = Path.GetDirectoryName(file);
                var project = folder is null ? null : Path.Combine(folder, "project.json");
                if (project is null || !File.Exists(project)) continue;
                var json = File.ReadAllText(project);
                if (WallpaperEngineFiles.PreviewName(json) is not { } name || !File.Exists(Path.Combine(folder!, name))) continue;
                previews[index] = (Path.Combine(folder!, name), WallpaperEngineFiles.TitleOf(json) ?? Path.GetFileName(folder!));
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException) // an odd Steam path or file: Windows' wallpaper is used (final review I2)
        {
            logFailure("Wallpaper Engine", failure); // its files are being written, or not readable: Windows' wallpaper is used
        }
        return previews;
    }

    /// <summary>Windows' monitors with their wallpaper files (<c>IDesktopWallpaper</c>), in Windows' order.</summary>
    private static unsafe List<WallpaperImage> WindowsWallpapers(Action<string, Exception> logFailure)
    {
        var monitors = new List<WallpaperImage>();
        try
        {
            var wallpaper = (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(typeof(DesktopWallpaper).GUID)!)!;
            try
            {
                wallpaper.GetMonitorDevicePathCount(out var count);
                for (uint index = 0; index < count; index++)
                {
                    wallpaper.GetMonitorDevicePathAt(index, out var monitorId);
                    try
                    {
                        if (monitorId.Value == null) continue;
                        RECT bounds;
                        try { wallpaper.GetMonitorRECT(monitorId, &bounds); }
                        catch (System.Runtime.InteropServices.COMException) { continue; } // a detached monitor still listed
                        if (bounds.right <= bounds.left || bounds.bottom <= bounds.top) continue; // listed but not attached: no area (M16)
                        string? path = null;
                        try
                        {
                            PWSTR file;
                            wallpaper.GetWallpaper(monitorId, &file);
                            path = file.ToString();
                            PInvoke.CoTaskMemFree(file.Value);
                        }
                        catch (System.Runtime.InteropServices.COMException failure) { logFailure("Windows wallpaper", failure); }
                        monitors.Add(new WallpaperImage(bounds.left, bounds.top, bounds.right, bounds.bottom,
                            path is { Length: > 0 } && File.Exists(path) ? path : null, "Windows wallpaper"));
                    }
                    finally
                    {
                        PInvoke.CoTaskMemFree(monitorId.Value);
                    }
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.ReleaseComObject(wallpaper);
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException) // hard rule 7: Windows' accent is left
        {
            logFailure("Windows wallpaper", failure);
        }
        return monitors;
    }
}
