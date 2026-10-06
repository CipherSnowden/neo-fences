using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NeoFences.Core.Appearance;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Appearance (M14, spec 2026-10-04-appearance-design): each fence's look from the config, the theme and the wallpaper
/// accent; the accent read from the wallpaper's own image when the wallpaper changes (no timer, no screen capture).
/// </summary>
public sealed partial class FenceHost
{
    /// <summary>Each monitor's wallpaper and the accent it gives (null: none found); empty while the switch is off.</summary>
    private IReadOnlyList<(WallpaperImage Monitor, Argb? Accent)> _accents = [];
    private Argb? _windowsAccent;
    private FileSystemWatcher? _wallpaperEngineWatcher;
    private DispatcherTimer? _accentDebounce;
    private readonly HashSet<string> _loggedAccentFailures = new(StringComparer.Ordinal); // spec §6: logged once
    private int _accentGeneration; // a slower older read never overwrites a newer one

    private AppearanceSettings Appearance => _config.Settings.Appearance;

    /// <summary>The look of the tab a window shows (spec §3; a box takes its front tab's look).</summary>
    private void ApplyStyle(FenceWindow window)
    {
        if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } shown) return;
        window.ApplyStyle(FenceLook.Resolve(Appearance, shown, _lightTheme, AccentAt(window)));
    }

    private void RestyleAll()
    {
        foreach (var window in _windows.Values) ApplyStyle(window);
    }

    /// <summary>The accent of the monitor under the fence's centre; the first monitor's, then Windows' accent, when unknown.</summary>
    private Argb? AccentAt(FenceWindow window)
    {
        if (!Appearance.WallpaperAccent) return null;
        var fallback = (_accents.Count > 0 ? _accents[0].Accent : null) ?? _windowsAccent;
        if (window.Handle == 0) return fallback; // a fence being created (drawn): restyled on its first move (final review M1)
        var rect = FenceWindowChrome.GetPixelRect(window.Handle);
        var (centerX, centerY) = (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var under = _accents.FirstOrDefault(entry => centerX >= entry.Monitor.Left && centerX < entry.Monitor.Right
                                                     && centerY >= entry.Monitor.Top && centerY < entry.Monitor.Bottom);
        return under.Accent ?? fallback;
    }

    /// <summary>Settings → Appearance changed (applies live, spec §5).</summary>
    private void SetAppearance(AppearanceSettings appearance)
    {
        var accentTurnedOn = appearance.WallpaperAccent && !Appearance.WallpaperAccent;
        var accentTurnedOff = !appearance.WallpaperAccent && Appearance.WallpaperAccent;
        _config = _config with { Settings = _config.Settings with { Appearance = appearance } };
        if (accentTurnedOn || accentTurnedOff) UpdateAccents(); // refreshes Settings itself (the accent's source line)
        RestyleAll();
        RefreshAllFenceSettings(); // M36: "Like all fences (…)" names the new values
        ScheduleSave();
        // No RefreshSettings here: Settings already shows what the user changed, and a full refresh re-reads every snapshot
        // file on each slider step (final review I3).
    }

    /// <summary>
    /// Reads every monitor's wallpaper on the shell worker (STA, off the UI thread) and restyles when done. Off: forgets the
    /// accents and stops watching Wallpaper Engine.
    /// </summary>
    private void UpdateAccents()
    {
        var generation = ++_accentGeneration;
        if (!Appearance.WallpaperAccent || !Current.ExtrasWanted) // M33: no wallpaper watching in safe mode
        {
            _accents = [];
            WatchWallpaperEngine(watch: false);
            RestyleAll();
            RefreshSettings();
            return;
        }
        WatchWallpaperEngine(watch: true);
        var dispatcher = Dispatcher.CurrentDispatcher;
        _shellWorker.Run(() =>
        {
            var failures = new List<(string Source, Exception Failure)>();
            var monitors = WallpaperSources.Read((source, failure) => failures.Add((source, failure)));
            var accents = monitors.Select(monitor => (monitor, monitor.ImagePath is { } path ? AccentOf(path, failures) : null)).ToList();
            var windowsAccent = WallpaperSources.WindowsAccent();
            dispatcher.BeginInvoke(() =>
            {
                foreach (var (source, failure) in failures)
                {
                    if (_loggedAccentFailures.Add(source)) Log.Warning(failure, "wallpaper accent: {Source} could not be read; the next source is used", source);
                }
                if (generation != _accentGeneration || !Appearance.WallpaperAccent) return;
                _accents = accents;
                _windowsAccent = windowsAccent;
                Log.Information("wallpaper accent: {Accents}", string.Join("; ", accents.Select(entry => $"{entry.monitor.Origin} {entry.Item2?.ToHex() ?? "none"}")));
                RestyleAll();
                RefreshSettings();
            });
        });
    }

    /// <summary>The image decoded at about 64 px wide and its strongest colour (spec §4); null when it cannot be read.</summary>
    private static Argb? AccentOf(string path, List<(string, Exception)> failures)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.DecodePixelWidth = 64;
            image.CacheOption = BitmapCacheOption.OnLoad; // the file is closed at once (WE may replace it)
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // Spotlight reuses file names: never an old decode (final review M5)
            image.EndInit();
            var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var bytes = new byte[pixels.PixelWidth * pixels.PixelHeight * 4];
            pixels.CopyPixels(bytes, pixels.PixelWidth * 4, 0);
            return AccentColor.FromPixels(bytes);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException) // also FileFormatException for a half-written image (final review I2)
        {
            failures.Add(("wallpaper image " + Path.GetFileName(path), failure));
            return null;
        }
    }

    /// <summary>
    /// Wallpaper Engine writes its <c>config.json</c> when the wallpaper changes: read again 2 s after the last write.
    /// ponytail: WE closed without a wallpaper change keeps the accent until the next change; watch the WE process if needed.
    /// </summary>
    private void WatchWallpaperEngine(bool watch)
    {
        if (!watch || !Current.ExtrasWanted)
        {
            _wallpaperEngineWatcher?.Dispose();
            _wallpaperEngineWatcher = null;
            _accentDebounce?.Stop();
            return;
        }
        if (_wallpaperEngineWatcher is not null) return;
        try
        {
            if (WallpaperSources.WallpaperEngineConfig() is not { } config) return;
            var dispatcher = Dispatcher.CurrentDispatcher;
            _accentDebounce ??= new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) =>
            {
                _accentDebounce!.Stop();
                UpdateAccents();
            }, dispatcher);
            _accentDebounce.Stop();
            var watcher = new FileSystemWatcher(Path.GetDirectoryName(config)!, Path.GetFileName(config))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            FileSystemEventHandler changed = (_, _) => dispatcher.BeginInvoke(() =>
            {
                if (_accentDebounce is null || _wallpaperEngineWatcher is null) return;
                _accentDebounce.Stop();
                _accentDebounce.Start();
            });
            watcher.Changed += changed;
            watcher.Created += changed;
            watcher.Renamed += (_, _) => changed(null!, null!);
            watcher.EnableRaisingEvents = true;
            _wallpaperEngineWatcher = watcher;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warning(failure, "Wallpaper Engine's config cannot be watched; the accent follows Windows' wallpaper changes only");
        }
    }

    /// <summary>What Settings → Appearance shows: the settings, the tone now and where the accent comes from.</summary>
    private AppearanceView AppearanceView()
    {
        var first = _accents.Count > 0 ? _accents[0] : default;
        var accent = first.Accent ?? (Appearance.WallpaperAccent ? _windowsAccent : null);
        var origin = !Appearance.WallpaperAccent ? ""
            : first.Accent is not null ? "From " + first.Monitor.Origin
            : _windowsAccent is not null ? "From Windows' accent colour (no wallpaper colour found)"
            : "Reading the wallpaper…";
        return new AppearanceView(Appearance, _lightTheme, accent?.ToHex(), origin);
    }

    /// <summary>Fence menu → Colour → Custom colour… (M14, M35): the colour picker; Cancel changes nothing.</summary>
    private void PickCustomColour(FenceWindow window)
    {
        var fenceId = window.FenceId; // the dialog is modal to this fence only: the tab or the fence may change meanwhile (final review M4)
        var fence = _config.Fences.FirstOrDefault(candidate => candidate.Id == fenceId);
        if (fence is null) return;
        var current = Argb.FromHex(fence.CustomColor) ?? (fence.TabColor is { } swatch ? FenceLook.Swatches[swatch] : null);
        string? picked;
        try
        {
            picked = ColourWindow.Pick(window, current); // M35: NeoFences' own picker (Windows' 1995 dialog is gone)
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Warning(failure, "colour picker failed"); // hard rule 7: no custom colour this time
            return;
        }
        if (picked is null || _config.Fences.All(candidate => candidate.Id != fenceId))
        {
            if (_windows.ContainsValue(window)) RefreshTabs(window); // Cancel: the click ticked "Custom…"; the fence's own choice again (M16)
            return;
        }
        _config = FenceEdits.SetCustomColor(_config, fenceId, picked);
        if (_windows.ContainsValue(window)) RefreshTabs(window);
        ScheduleSave();
    }
}
