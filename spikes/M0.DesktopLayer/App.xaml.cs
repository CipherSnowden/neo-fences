using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace NeoFences.Spikes.M0;

public partial class App : Application
{
    private DesktopZOrder? _zOrder;
    private MouseHookThread? _mouseHook;
    private bool _iconsHidden;
    private bool _isWatchdog;

    protected override void OnStartup(StartupEventArgs startupArgs)
    {
        base.OnStartup(startupArgs);

        if (startupArgs.Args is ["--watchdog", var processIdText])
        {
            _isWatchdog = true;
            Watchdog.Run(mainProcessId: int.Parse(processIdText, CultureInfo.InvariantCulture));
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, unhandled) => Lab.Log($"UNHANDLED: {unhandled.Exception}");
        // Sign-out/shutdown kills the watchdog too, and Explorer persists FWF_NOICONS: restore while we still can.
        SessionEnding += (_, sessionEnding) =>
        {
            Lab.Log($"session ending ({sessionEnding.ReasonSessionEnding}) -> restoring icons");
            if (_iconsHidden) DesktopIcons.TrySetHidden(false);
            Watchdog.MarkCleanShutdown();
        };

        var labWindow = new LabWindow();
        MainWindow = labWindow;
        labWindow.Show();
        Lab.Log($"M0 lab started. OS {Environment.OSVersion.Version}");

        Watchdog.Spawn();

        var fenceWindow = new SpikeFenceWindow();
        fenceWindow.Show();

        _zOrder = new DesktopZOrder();
        _zOrder.Track(fenceWindow);
        _zOrder.ForegroundChanged += GameDetector.LogForeground;

        labWindow.ExplorerRestarted += () => ReapplyAfterExplorerRestart(zOrder: _zOrder, iconsWereHidden: _iconsHidden);

        labWindow.AddButton(label: "Backdrop: none", onClick: () => fenceWindow.SetBackdrop(BackdropMode.None));
        labWindow.AddButton(label: "Backdrop: DWM acrylic", onClick: () => fenceWindow.SetBackdrop(BackdropMode.DwmAcrylic));
        labWindow.AddButton(label: "Backdrop: DWM acrylic + keep active", onClick: () => fenceWindow.SetBackdrop(BackdropMode.DwmAcrylicKeepActive));
        labWindow.AddButton(label: "Backdrop: accent acrylic", onClick: () => fenceWindow.SetBackdrop(BackdropMode.AccentAcrylic));
        labWindow.AddButton(label: "Backdrop: accent acrylic light", onClick: () => fenceWindow.SetBackdrop(BackdropMode.AccentAcrylicLight));
        labWindow.AddButton(label: "Backdrop: accent blur", onClick: () => fenceWindow.SetBackdrop(BackdropMode.AccentBlur));

        var layeredFenceWindow = new SpikeFenceWindow(layered: true);
        layeredFenceWindow.Show();
        _zOrder.Track(layeredFenceWindow);
        labWindow.AddButton(label: "Layered: tint only", onClick: () => layeredFenceWindow.SetBackdrop(BackdropMode.None));
        labWindow.AddButton(label: "Layered: accent blur", onClick: () => layeredFenceWindow.SetBackdrop(BackdropMode.AccentBlur));
        labWindow.AddButton(label: "Layered: accent acrylic", onClick: () => layeredFenceWindow.SetBackdrop(BackdropMode.AccentAcrylic));
        labWindow.AddButton(label: "Layered: accent acrylic light", onClick: () => layeredFenceWindow.SetBackdrop(BackdropMode.AccentAcrylicLight));

        labWindow.AddButton(label: "Z: bottom only", onClick: () => _zOrder.SetStrategy(ZOrderStrategy.BottomOnly));
        labWindow.AddButton(label: "Z: raise on Win+D", onClick: () => _zOrder.SetStrategy(ZOrderStrategy.RaiseOnShowDesktop));
        labWindow.AddButton(label: "Z: owned by Progman", onClick: () => _zOrder.SetStrategy(ZOrderStrategy.OwnedByProgman));

        labWindow.AddButton(label: "Hide desktop icons", onClick: () => { if (DesktopIcons.TrySetHidden(true)) _iconsHidden = true; });
        labWindow.AddButton(label: "Show desktop icons", onClick: () => { if (DesktopIcons.TrySetHidden(false)) _iconsHidden = false; });
        labWindow.AddButton(label: "Query icons", onClick: () => Lab.Log($"icons hidden = {DesktopIcons.TryIsHidden()?.ToString() ?? "unknown"}"));
        labWindow.AddButton(label: "CRASH (FailFast)", onClick: () => Environment.FailFast("M0 spike: simulated crash"));

        labWindow.AddButton(label: "Mouse hook: start", onClick: StartMouseHook);
        labWindow.AddButton(label: "Mouse hook: stop", onClick: StopMouseHook);
        labWindow.AddButton(label: "Right-click: observe", onClick: () => SetSuppression(RightClickSuppression.None));
        labWindow.AddButton(label: "Right-click: S1 swallow up", onClick: () => SetSuppression(RightClickSuppression.SwallowUpAfterDrag));
        labWindow.AddButton(label: "Right-click: S2 swallow+replay", onClick: () => SetSuppression(RightClickSuppression.SwallowDownAndReplay));
    }

    private void ReapplyAfterExplorerRestart(DesktopZOrder zOrder, bool iconsWereHidden)
    {
        // The new Explorer's desktop view may not exist yet when TaskbarCreated arrives: retry for ~5 s.
        var attempts = 0;
        var retryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        retryTimer.Tick += (_, _) =>
        {
            attempts++;
            zOrder.Reapply();
            var iconsOk = !iconsWereHidden || DesktopIcons.TrySetHidden(true);
            if (iconsOk || attempts >= 10)
            {
                retryTimer.Stop();
                Lab.Log($"re-applied after Explorer restart in {attempts} attempt(s), icons ok={iconsOk}");
            }
        };
        retryTimer.Start();
    }

    private void StartMouseHook()
    {
        if (_mouseHook is not null) return;
        _mouseHook = new MouseHookThread(onGesture: (gesture, x, y) =>
            Dispatcher.BeginInvoke(() => Lab.Log($"GESTURE {gesture} at ({x},{y})")));
    }

    private void StopMouseHook()
    {
        _mouseHook?.Dispose();
        _mouseHook = null;
    }

    private void SetSuppression(RightClickSuppression suppression)
    {
        if (_mouseHook is null) StartMouseHook();
        _mouseHook!.Suppression = suppression;
        Lab.Log($"right-click suppression = {suppression}");
    }

    protected override void OnExit(ExitEventArgs exitArgs)
    {
        // The watchdog must not write a clean-shutdown marker of its own: a later main process reusing
        // its PID would then look "clean" after a crash and its icons would stay hidden.
        if (_isWatchdog)
        {
            base.OnExit(exitArgs);
            return;
        }
        StopMouseHook();
        _zOrder?.Dispose();
        if (_iconsHidden) DesktopIcons.TrySetHidden(false);
        Watchdog.MarkCleanShutdown();
        Lab.Log("clean exit");
        base.OnExit(exitArgs);
    }
}
