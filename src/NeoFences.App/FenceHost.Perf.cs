using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using NeoFences.Core.Lifecycle;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Timing marks for the measurements (M37, spec §1, ADR-058): "fences shown" once every window is placed and drawn,
/// "icons settled" the first time no icon request waits after that; with <c>NEOFENCES_PERF=1</c> also frame statistics
/// every 2 s (it keeps WPF drawing every frame, so it is for measuring only). Read back by <see cref="PerfLog"/>.
/// </summary>
public sealed partial class FenceHost
{
    private static readonly DateTime ProcessStarted = Process.GetCurrentProcess().StartTime;
    private bool _fencesShownLogged, _iconsSettledLogged;

    private static long SinceStartMs => (long)(DateTime.Now - ProcessStarted).TotalMilliseconds;

    /// <summary>After <see cref="Start"/>: once everything waiting to load and draw has run (the windows' Loaded included).</summary>
    private void MarkFencesShown()
    {
        _iconLoader.Settled += OnIconsSettled;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            _fencesShownLogged = true;
            Log.Information("{Mark:l}", PerfLog.FencesShownText(SinceStartMs, _windows.Count));
            if (_iconLoader.Pending == 0) OnIconsSettled(_iconLoader.Requested, _iconLoader.FromCache); // all from the cache before the windows were drawn
            StartAfterFencesShown();
        });
        StartFrameStats();
    }

    /// <summary>
    /// M37 (spec §5, fences first): work the fences do not need to show waits until they are on screen — the old library
    /// fence's folder, the target checks and watching (Missing marks fill in a moment later), the wallpaper colour.
    /// </summary>
    private void StartAfterFencesShown()
    {
        EnsureLibraryLister();
        StartWatching(); // M18: states fill in as the checks finish (spec §4)
        if (Appearance.WallpaperAccent) UpdateAccents(); // M14: the accent is read once at start, then on wallpaper changes
    }

    /// <summary>A step of the start, for the measurement (not read back: it shows where the time goes).</summary>
    private static void StepMark(string step) => Log.Information("timing: {Step:l} after {Ms} ms", step, SinceStartMs);

    private void OnIconsSettled(int requests, int fromCache)
    {
        if (!_fencesShownLogged || _iconsSettledLogged) return;
        _iconsSettledLogged = true;
        Log.Information("{Mark:l}", PerfLog.IconsSettledText(SinceStartMs, requests, fromCache));
    }

    /// <summary>NEOFENCES_PERF=1 only: the longest gap between frames and how many took over 33 ms, every 2 s.</summary>
    private void StartFrameStats()
    {
        if (Environment.GetEnvironmentVariable("NEOFENCES_PERF") != "1") return;
        var last = TimeSpan.Zero;
        var (frames, slow, worst) = (0, 0, 0.0);
        CompositionTarget.Rendering += (_, args) =>
        {
            var at = ((RenderingEventArgs)args).RenderingTime;
            if (at == last) return; // the same frame reported twice
            if (last != TimeSpan.Zero)
            {
                var gap = (at - last).TotalMilliseconds;
                frames++;
                worst = Math.Max(worst, gap);
                if (gap > 33) slow++;
            }
            last = at;
        };
        var report = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        report.Tick += (_, _) =>
        {
            if (frames > 0) Log.Information("{Mark:l}", PerfLog.FramesText(frames, worst, slow));
            (frames, slow, worst) = (0, 0, 0.0);
        };
        report.Start();
        Log.Information("perf mode: frame statistics every 2 s (NEOFENCES_PERF=1)");
    }
}
