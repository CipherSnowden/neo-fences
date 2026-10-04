using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NeoFences.Core.Config;
using NeoFences.Core.Input;
using NeoFences.Core.Items;
using NeoFences.Core.Layouts;
using NeoFences.Core.Lifecycle;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Owns the fences on the desktop: loads the config and the items, places one <see cref="FenceWindow"/> per box on the
/// current monitors, shows each fence's virtual items (ADR-040) and keeps their targets checked (FenceHost.Watching),
/// saves changes (debounced 500 ms, ADR-006, ADR-041), and keeps everything attached through display changes, Explorer
/// restarts and sign-out (ADR-011, ADR-013). Nothing here touches a user's file; every Win32/COM call goes through
/// NeoFences.Shell.
/// </summary>
public sealed partial class FenceHost
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReattachInterval = TimeSpan.FromMilliseconds(500);
    private const double SnapGapDips = 8;        // spec §6: 8 px spacing from other fences and screen edges
    private const double SnapThresholdDips = 12; // how close an edge must come before it snaps
    private const int ReattachAttempts = 10;
    private const int PeekHotkeyId = 1;
    private const int PeekEscapeHotkeyId = 2;
    private static readonly TimeSpan DrawFrame = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan TrayRetryInterval = TimeSpan.FromSeconds(2);
    private const int TrayRetryAttempts = 15;

    private readonly ConfigStore _store = new(AppPaths.DataDirectory);
    private readonly ItemStore _itemStore = new(AppPaths.DataDirectory); // ADR-041: the items in a file of their own
    private readonly Watchdog _watchdog = new(AppPaths.DataDirectory, message => Log.Information("watchdog: {Message}", message));
    private readonly SystemMessageWindow _messages = new();
    private readonly Dictionary<string, FenceWindow> _windows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _saveTimer;
    private readonly IconLoader _iconLoader = new(Dispatcher.CurrentDispatcher);
    private readonly ShellWorker _shellWorker = new(); // recycling snapshots: off the UI thread, on STA (M3a review)
    private SpecialIconNotifications? _specialIcons;
    private bool _specialIconsDeferred;
    private readonly SnapshotStore _snapshots = new(Path.Combine(AppPaths.DataDirectory, "snapshots")); // M10
    private readonly HashSet<string> _loggedSnapshotProblems = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _specialIconsTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly Dictionary<string, IDisposable> _dropRegistrations = new(StringComparer.Ordinal);
    private LibraryLister? _libraryLister; // the Game Library fence's folder (M12), while that fence exists
    private NeoFencesConfig _config = NeoFencesConfig.CreateDefault();
    private ItemsDocument _items = new();
    private IReadOnlyList<MonitorPlacement> _monitors = [];
    private bool _lightTheme = SystemTheme.AppsUseLightTheme();
    private bool _sessionEnding;
    // M5 desktop gestures
    private DesktopMouseHook? _mouseHook;
    private bool _quickHidden;              // double-click on the desktop: fences (and icons) hidden until the next one
    private bool _iconsHiddenByUser;        // RunState.IconsHiddenByUser: set when quick-hide begins, cleared when it ends
    private DrawFenceOverlay? _drawOverlay; // right-drag on the desktop: the fence being drawn
    private DispatcherTimer? _drawTimer;
    private (int X, int Y) _drawStart;
    private GlobalHotkey? _peekHotkey;
    private GlobalHotkey? _peekEscapeHotkey; // Esc ends Peek; registered only while peeking
    private bool _peeking;
    private bool _recordingHotkey;          // Settings' hotkey box has the keyboard: the Peek hotkey is released
    private string? _peekHotkeyProblem;     // why the Peek hotkey is not registered (Settings shows it), null when it is
    private bool _cornersLogged;
    private FenceWindow? _movingWindow; // the box being moved by its title (not resized): it may merge where it is dropped (M9)
    // M6a
    private bool _paused;                    // tray: Pause NeoFences (not saved)
    private bool _gameMode;                  // a full-screen app is in front: idle (spec §4.7, ADR-021)
    private ForegroundWatcher? _foregroundWatcher;
    private TrayIcon? _trayIcon;
    private const int TrayNewFence = 1, TrayQuickHide = 2, TrayPeek = 3, TrayPause = 4, TrayExit = 5, TraySettings = 6;
    // Snapshots (M10): "Restore snapshot" lists the newest few by id TrayRestoreFirst + index.
    private const int TrayTakeSnapshot = 7, TrayRestoreMenu = 8, TrayRestoreBefore = 9, TrayRestoreFirst = 100, TrayRestoreCount = 10;
    private const int TrayNewLibrary = 11; // M12
    private const int TraySnapshotsSettings = 12; // M13c: "More in Settings…" opens the Snapshots card
    private const int TrayAddFromDesktop = 14; // M19 §3 (13 is TrayRestartToUpdate)
    private SettingsWindow? _settingsWindow; // M6b: one at a time

    public event Action? ExitRequested;

    public FenceHost()
    {
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();
        _messages.ExplorerRestarted += OnExplorerRestarted;
        _messages.DisplayChanged += OnDisplayChanged;
        _messages.ThemeChanged += OnThemeChanged;
        _messages.WallpaperChanged += () => { if (Appearance.WallpaperAccent) UpdateAccents(); }; // M14
        _messages.HotkeyPressed += OnHotkey;
        _messages.TrayMenuRequested += ShowTrayMenu;
        _messages.SessionUnlocked += OnSessionUnlocked;
        _messages.SpecialIconsChanged += ScheduleSpecialIconRefresh;
        _messages.DeviceRemovalRequested += handle =>
        {
            if (_libraryLister?.ReleaseForRemoval(handle) == true) Log.Information("the library folder's drive is being removed: released it");
            if (ReleaseLibraryForRemoval(handle)) Log.Information("a drive the game library watches is being removed: released it");
            if (ReleaseTargetWatcherForRemoval(handle)) Log.Information("a drive holding item targets is being removed: released it");
        };
        _specialIconsTimer.Tick += (_, _) => RefreshSpecialIcons();
    }

    public void Start()
    {
        var loaded = _store.Load();
        Log.Information("config loaded from {Source} (read-only: {IsReadOnly}, corrupt copy: {CorruptCopyPath})",
            loaded.Source, loaded.IsReadOnly, loaded.CorruptCopyPath);
        _config = loaded.Config;
        var loadedItems = _itemStore.Load();
        Log.Information("items loaded from {Source} (read-only: {IsReadOnly}, corrupt copy: {CorruptCopyPath}): {Count} item(s)",
            loadedItems.Source, loadedItems.IsReadOnly, loadedItems.CorruptCopyPath, loadedItems.Document.Fences.Values.Sum(items => items.Count));
        _items = loadedItems.Document;
        if (loadedItems.Source == ConfigLoadSource.Primary && !loadedItems.IsReadOnly) CleanUnusedPictures(ItemEdits.ImagesInUse(_items), _snapshots.Directory);
        _watchdog.LaunchDetached(Environment.ProcessId);
        ApplyStartup(); // after a power loss NeoFences must come back by itself (ADR-019)

        RefreshMonitors();
        foreach (var box in FenceTabs.Boxes(_config)) OpenWindow(box); // one window per box (M9)
        EnsureLibraryLister();
        RefreshWindows();
        StartSpecialIconNotifications();
        ApplyLayout();
        if (_config.Settings.HideDesktopIcons) SetIconsHidden(true);
        else if (_watchdog.IsIconsHiddenMarked)
        {
            // Icons may still be hidden from a run whose "show" never happened (both processes killed): show them.
            Log.Warning("icons-hidden marker found while hiding is off; showing desktop icons");
            SetIconsHidden(false);
        }
        StartGestures();
        if (!_messages.SessionNotificationsActive) Log.Warning("unlock notices unavailable: the mouse hook is re-installed only after an Explorer restart");
        try
        {
            _trayIcon = new TrayIcon(_messages.Handle, TrayTooltip(), log: message => Log.Warning("{Message}", message));
            ShowTrayIcon();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "tray icon unavailable; fences and gestures keep working"); // hard rule 7: only the tray is lost
        }
        StartGameMode();
        StartWatching(); // M18: states fill in as the checks finish (spec §4)
        StartUpdates(); // M17: the first check a minute after start
        if (Appearance.WallpaperAccent) UpdateAccents(); // M14: the accent is read once at start, then on wallpaper changes
        ScheduleSave();
    }

    /// <summary>
    /// The clock for refresh throttling: wall time at start plus a monotonic stopwatch, so a clock change (time sync,
    /// daylight saving, the user) cannot stall or rush a refresh (M8a).
    /// </summary>
    private static readonly DateTimeOffset ClockStart = DateTimeOffset.Now;
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static DateTimeOffset Now => ClockStart + Clock.Elapsed;

    /// <summary>All of NeoFences' run-time modes together (Core rules: which fences, icons and hooks they imply).</summary>
    private RunState Current => new(HideIcons: _config.Settings.HideDesktopIcons, QuickHidden: _quickHidden, Paused: _paused, GameMode: _gameMode,
        IconsHiddenByUser: _iconsHiddenByUser);

    /// <summary>
    /// Windows asked to end the session (WPF's SessionEnding, inside WM_QUERYENDSESSION). WPF then shuts the app
    /// down, which may be the last thing that ever runs, so the icons come back now and the watchdog is told it is
    /// a session end, not a user exit: if the user cancels the shutdown, the watchdog restarts NeoFences (ADR-013).
    /// </summary>
    public void OnSessionEnding()
    {
        _sessionEnding = true;
        ApplyDeferredShellWork(); // renames seen during a game must be saved too (M6a review M2)
        SaveNow();
        if (Current.IconsHidden || _watchdog.IsIconsHiddenMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkSessionEnding(Environment.ProcessId), what: "session-ending marker");
    }

    /// <summary>Orderly exit: save, bring icons back, tell the watchdog all is well.</summary>
    public void Shutdown()
    {
        WatchWallpaperEngine(watch: false); // M14
        _libraryStopped = true; // also on session end: a library scan finishing now writes and re-arms nothing (M13a review)
        ApplyDeferredShellWork(); // renames seen during a game must be saved too (M6a review M2)
        SaveNow();
        _trayIcon?.Dispose(); // also on session end: a cancelled shutdown restarts us, and the old icon would linger (M8a)
        _trayIcon = null;
        if (_sessionEnding) return; // OnSessionEnding already restored and marked; no clean marker, so a cancel restarts us
        // The marker also catches a show that failed earlier (quick-hide ending while Explorer was busy; M5 review M6).
        if (Current.IconsHidden || _watchdog.IsIconsHiddenMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkCleanShutdown(Environment.ProcessId), what: "clean-shutdown marker");
        _foregroundWatcher?.Dispose();
        _mouseHook?.Dispose();
        _peekHotkey?.Dispose();
        _peekEscapeHotkey?.Dispose();
        StopWatching();
        _specialIcons?.Dispose();
        _libraryLister?.Dispose();
        StopLibraryWatchers();
        _libraryTimer?.Stop();
        _shellWorker.Dispose();
        _iconLoader.Dispose();
        _messages.Dispose();
        _updateTimer?.Stop();
        ApplyUpdateOnExit(); // M17: last, after the icons are back and the watchdog was told (never at session end: returned above)
    }

    /// <summary>Best effort from the crash handler; the watchdog restores too.</summary>
    public void EmergencyRestoreIcons()
    {
        if (Current.IconsHidden || _watchdog.IsIconsHiddenMarked) DesktopIcons.TrySetHidden(false);
    }

    /// <summary>One window per box (M9): it shows the box's active tab; roll-up and lock are the box's.</summary>
    private void OpenWindow(Fence box)
    {
        var shown = FenceTabs.ActiveOf(_config, box.Id);
        var window = new FenceWindow(shown with { RolledUp = box.RolledUp, Locked = box.Locked },
            lightTheme: _lightTheme, iconLoader: _iconLoader, rollupExpand: _config.Settings.RollupExpand)
        {
            // Spec §6: no animations when Windows' "Animation effects" are off; spec §4.7: none while gaming.
            AnimationsAllowed = () => !_gameMode && SystemParameters.ClientAreaAnimation,
            BoxId = box.Id,
        };
        window.TabSelected += fenceId => SwitchTab(window, fenceId);
        window.TabDropped += (fenceId, screenX, screenY) => OnTabDropped(window, fenceId, screenX, screenY);
        window.TabDragMoved += (screenX, screenY) =>
        {
            foreach (var other in _windows.Values) other.SetMergeHighlight(other != window && other.TitleRowContains(screenX, screenY));
        };
        window.TabColorRequested += color =>
        {
            _config = FenceTabs.SetColor(_config, window.FenceId, color);
            RefreshTabs(window);
            ScheduleSave();
        };
        window.CustomColorRequested += () => PickCustomColour(window); // M14
        window.DetachTabRequested += () => DetachTab(window, window.FenceId, dropPoint: null);
        window.TabCycleRequested += step => CycleTab(window, step);
        window.SetTabs(FenceTabs.TabsOf(_config, box.Id), shown.Id);
        ApplyStyle(window); // M14
        window.SnapRect = (rect, edges) => SnapFence(window, rect, edges);
        window.MovedByUser += OnFenceMoved;
        window.RenameRequested += title => RenameFence(window, title);
        window.IconSizeRequested += iconSize => SetFenceIconSize(window, iconSize);
        window.LockToggled += locked => SetFenceLocked(window, locked);
        window.DeleteRequested += () => DeleteFence(window);
        window.NewFenceRequested += CreateFence;
        window.ExitRequested += () => ExitRequested?.Invoke();
        window.OpenRequested += key => OpenKey(window, key);
        window.OpenManyRequested += keys =>
        {
            foreach (var key in keys) OpenKey(window, key);
        };
        window.ItemMenuRequested += (keys, screenX, screenY, fromKeyboard) => ShowItemMenu(window, keys, screenX, screenY, fromKeyboard);
        window.RemoveRequested += keys => RemoveItems(window, keys);
        window.PropertiesRequested += (key, focusName) => ShowProperties(window, key, focusName);
        window.AddItemRequested += () => AddItem(window);
        window.AddFromDesktopRequested += ShowDesktopFill;
        window.RefreshRequested += () => RefreshFence(window);
        window.DrivesChanged += OnDrivesChanged;
        window.NewLibraryRequested += CreateLibraryFence;
        window.StartupToggled += SetStartWithWindows;
        window.SettingsRequested += OpenSettings;
        window.LabelModeRequested += labels => SetFenceLabels(window, labels);
        window.SetShortcutArrows(_config.Settings.ShowShortcutArrows);
        window.SetStartupChecked(_config.Settings.StartWithWindows);
        window.SortRequested += sort => SortFence(window, sort);
        window.DragRequested += keys => DragItems(window, keys);
        window.RollUpToggled += () => ToggleRollUp(window);
        new WindowInteropHelper(window).EnsureHandle(); // HWND exists (styles, blur) before the first Show
        if (window.CornersUnavailable && !_cornersLogged)
        {
            _cornersLogged = true;
            Log.Information("Windows refused rounded corners (Windows 10?): fences keep square blur corners"); // M8a review
        }
        RegisterDrops(window); // needs the HWND
        if (!DesktopHost.AttachToDesktop(window.Handle)) Log.Warning("fence {FenceId}: not attached to the desktop yet (no Progman)", box.Id);
        _windows[box.Id] = window;
        // The library tab gets a window of its own (detached, or its host deleted): it lists its folder now (final review I1).
        if (shown.IsLibrary) _libraryLister?.Refresh();
    }

    /// <summary>Also after an Explorer restart: Explorer brokers the shell's change notices and forgets them (M8c review I6).</summary>
    private void StartSpecialIconNotifications()
    {
        _specialIcons?.Dispose();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _specialIcons = new SpecialIconNotifications(_messages.Handle,
            settingsChanged: () => dispatcher.BeginInvoke(ScheduleSpecialIconRefresh),
            log: (what, failure) => Log.Warning(failure, "{What} unavailable: special icons change after a restart", what));
    }

    private void ScheduleSpecialIconRefresh()
    {
        _specialIconsTimer.Stop(); // a burst (several icons, a Recycle Bin emptying) is one refresh
        _specialIconsTimer.Start();
    }

    /// <summary>The Recycle Bin turned full or empty: a Recycle Bin item shows the new icon (M8c).</summary>
    private void RefreshSpecialIcons()
    {
        _specialIconsTimer.Stop();
        if (Current.ShellWorkDeferred)
        {
            _specialIconsDeferred = true; // games get every bit of the machine: after the game (M8c review)
            return;
        }
        foreach (var window in _windows.Values) window.ReloadSpecialIcons();
        Log.Information("special icons refreshed");
    }

    /// <summary>Every window shows its fence's items as they are now (the library lists itself, LibraryLister).</summary>
    private void RefreshWindows()
    {
        foreach (var window in _windows.Values) RefreshWindow(window);
    }

    private void RefreshWindow(FenceWindow window)
    {
        if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { IsLibrary: false } shown) return;
        window.SetItems([.. _items.Of(shown.Id).Select(item => new ShownItem(item.Id, item.Target, item.OwnName, item.Icon, item.Note, StateOf(item.Target)))]);
    }

    /// <summary>Opens a path NeoFences knows (a library game, the logs or data folder).</summary>
    private static void OpenItem(string target, nint ownerHandle)
    {
        // Off the UI thread: ShellExecute can block on a network timeout or a UAC prompt, freezing every fence (M2b review I6);
        // on an STA thread, as shell handlers and Windows' error dialog expect (M3a review).
        // Each open on its own thread: one waiting on an offline share (and its error dialog) holds up nothing else (M8c review I4).
        ShellWorker.RunAlone(() =>
        {
            if (!ShellItems.TryOpen(target, ownerHandle)) Log.Warning("could not open {Target}", target);
        }, name: "NeoFences open");
    }

    /// <summary>Makes the fence accept drops (M3b; M18: drops create or move items, never a file operation).</summary>
    private void RegisterDrops(FenceWindow window)
    {
        try
        {
            _dropRegistrations[window.BoxId] = ShellDragDrop.RegisterFence(window.Handle, new FenceDropHandlers(
                HitTest: window.HitTest,
                AcceptsDrops: () => !window.IsLibrary, // the library shows NeoFences' own shortcuts only (M12)
                ItemsDropped: (keys, insertAt, duplicate) => OnItemsDropped(window, keys, insertAt, duplicate),
                TargetsDropped: (targets, insertAt) => OnTargetsDropped(window, targets, insertAt),
                ShowFeedback: window.ShowDropFeedback,
                LogFailure: failure => Log.Warning(failure, "drop on fence {FenceId} failed", window.FenceId)));
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "fence {FenceId} cannot accept drops", window.FenceId); // degrade: everything else still works
        }
    }

    private void ApplyLayout()
    {
        if (_monitors.Count == 0)
        {
            Log.Warning("no monitors reported; keeping the current layout");
            return;
        }
        try
        {
            var (resolved, layout) = LayoutEngine.Resolve(_config, _monitors.Select(monitor => monitor.ToDisplayMonitor()).ToList());
            _config = resolved;
            foreach (var (fenceId, rect) in layout.Fences)
            {
                if (!_windows.TryGetValue(fenceId, out var window)) continue;
                var monitor = _monitors.First(candidate => candidate.DeviceId == rect.Monitor);
                window.Place(FencePlacement.ToPixels(rect, monitor));
                if (!window.IsVisible && Current.FencesVisible)
                {
                    window.ShowNow();
                    if (_peeking)
                    {
                        // A fence made during Peek (its menus no longer end it, M5 review I1) joins the others on top.
                        window.Peeking = true;
                        FenceWindowChrome.SetTopmost(window.Handle, topmost: true);
                    }
                    else FenceWindowChrome.SendToBack(window.Handle); // Show puts it above every app; fences live just above the desktop
                }
            }
        }
        catch (ArgumentException unusableDisplay)
        {
            // Garbage from a monitor query mid-change (M1 review): skip this resolve, the next display event retries.
            Log.Warning(unusableDisplay, "display query unusable; keeping the current layout");
        }
    }

    private void RefreshMonitors()
    {
        _monitors = Monitors.Enumerate();
        Log.Information("monitors: {Monitors}", string.Join("; ", _monitors.Select(monitor =>
            $"{monitor.DeviceId} {monitor.PixelWidth}x{monitor.PixelHeight}@{monitor.ScalePercent}% " +
            $"work {monitor.WorkLeftPx},{monitor.WorkTopPx} {monitor.WorkWidthPx}x{monitor.WorkHeightPx}{(monitor.IsPrimary ? " primary" : "")}")));
    }

    private void OnFenceMoved(FenceWindow window, PixelRect pixels)
    {
        var wasMove = ReferenceEquals(_movingWindow, window);
        _movingWindow = null;
        foreach (var other in _windows.Values) other.SetMergeHighlight(false);
        // Dropped by its title onto another fence's title row: the whole box joins that box as tabs (M9).
        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
        if (wasMove && _windows.Values.FirstOrDefault(other => other != window && other.TitleRowContains(cursorX, cursorY)) is { } target)
        {
            Log.Information("fence {FenceId} merged into the box of {TargetId}", window.BoxId, target.BoxId);
            _config = FenceTabs.Merge(_config, movingFenceId: window.BoxId, targetFenceId: target.BoxId);
            SyncBoxes();
            return;
        }
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint) return;
        var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
        _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: window.BoxId, rect: FencePlacement.FromPixels(pixels, monitor));
        if (Appearance.WallpaperAccent) ApplyStyle(window); // M14: moved to another monitor, another wallpaper
        ScheduleSave();
    }

    private PixelRect SnapFence(FenceWindow window, PixelRect rect, SnapEdges edges)
    {
        // While a box moves, the title row it would merge into lights up; a resize never merges (M9).
        _movingWindow = edges == SnapEdges.Move ? window : null;
        if (_movingWindow is not null)
        {
            var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
            foreach (var other in _windows.Values) other.SetMergeHighlight(other != window && other.TitleRowContains(cursorX, cursorY));
        }
        if (_monitors.Count == 0) return rect;
        var monitor = FencePlacement.ContainingMonitor(rect, _monitors);
        var others = _windows.Values.Where(other => other != window && other.IsVisible).Select(other => FenceWindowChrome.GetPixelRect(other.Handle)).ToList();
        return Snapping.Snap(rect, edges,
            workArea: new PixelRect(monitor.WorkLeftPx, monitor.WorkTopPx, monitor.WorkWidthPx, monitor.WorkHeightPx),
            others: others,
            gapPx: (int)Math.Round(SnapGapDips * monitor.Scale),
            thresholdPx: (int)Math.Round(SnapThresholdDips * monitor.Scale),
            // A resize snap never makes the fence smaller than its minimum (M2c review carry-over).
            minWidthPx: (int)Math.Round(LayoutEngine.MinWidth * monitor.Scale),
            minHeightPx: (int)Math.Round(LayoutEngine.MinHeight * monitor.Scale));
    }

    private void RenameFence(FenceWindow window, string title)
    {
        _config = FenceEdits.Rename(_config, window.FenceId, title);
        window.SetTitle(_config.Fences.First(fence => fence.Id == window.FenceId).Title);
        RefreshTabs(window);
        ScheduleSave();
    }

    private void SetFenceIconSize(FenceWindow window, int iconSize)
    {
        _config = FenceEdits.SetIconSize(_config, window.FenceId, iconSize);
        window.SetIconSize(iconSize);
        ScheduleSave();
    }

    private void SetFenceLocked(FenceWindow window, bool locked)
    {
        _config = FenceEdits.SetLocked(_config, window.BoxId, locked); // the box's (M9)
        window.SetLocked(locked);
        ScheduleSave();
    }

    /// <summary>
    /// Removes the shown tab's fence and its items (asked first when it has any). Their targets are never touched (hard
    /// rule 1). The config is saved before the items: a crash in between leaves only an unused list (ADR-041).
    /// </summary>
    private void DeleteFence(FenceWindow window)
    {
        var fence = _config.Fences.First(candidate => candidate.Id == window.FenceId);
        var count = _items.Of(fence.Id).Count;
        if (count > 0 && MessageBox.Show(window,
                $"Delete \"{fence.Title}\" and its {count} item{(count == 1 ? "" : "s")}?\n\nYour files, folders and apps are not touched.",
                "NeoFences", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        Log.Information("fence {FenceId} deleted with {Count} item(s)", fence.Id, count);
        _config = FenceEdits.DeleteFence(_config, fence.Id); // the shown tab; the rest of its box stays (M9)
        _items = ItemEdits.RemoveFence(_items, fence.Id);
        SaveNow();
        SyncBoxes(); // closes the window when its box is gone
        UpdateWatching();
        ForgetGoneTargets();
    }

    private void OnThemeChanged()
    {
        var light = SystemTheme.AppsUseLightTheme();
        if (light == _lightTheme) return;
        _lightTheme = light;
        Log.Information("Windows app mode changed; light: {Light}", light);
        foreach (var window in _windows.Values) window.ApplyTheme(light);
        RestyleAll(); // M14: the tone's strength and ink
        RefreshSettings();
    }

    private void OnDisplayChanged()
    {
        RefreshMonitors();
        ApplyLayout();
        if (Appearance.WallpaperAccent) UpdateAccents(); // M14: monitors and their wallpapers may have changed
        ScheduleSave();
    }

    /// <summary>
    /// Brings the windows in line with the boxes after any change to them (M9): a window per box, showing the box's
    /// active tab, with its tab strip, lock and roll-up. A box that changed hands (its host left) keeps its window.
    /// </summary>
    private void SyncBoxes()
    {
        var boxes = FenceTabs.Boxes(_config);
        var boxIds = boxes.Select(box => box.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (boxId, window) in _windows.ToList())
        {
            if (boxIds.Contains(boxId)) continue;
            _windows.Remove(boxId);
            if (_config.Fences.Any(fence => fence.Id == window.FenceId) && FenceTabs.HostOf(_config, window.FenceId) is { } heir
                && boxIds.Contains(heir.Id) && !_windows.ContainsKey(heir.Id))
            {
                window.BoxId = heir.Id;
                _windows[heir.Id] = window;
                if (_dropRegistrations.Remove(boxId, out var moved)) _dropRegistrations[heir.Id] = moved;
                continue;
            }
            if (_dropRegistrations.Remove(boxId, out var registration)) registration.Dispose();
            window.Close();
        }
        foreach (var box in boxes.Where(box => !_windows.ContainsKey(box.Id))) OpenWindow(box);
        EnsureLibraryLister();
        foreach (var box in boxes)
        {
            var window = _windows[box.Id];
            var active = FenceTabs.ActiveOf(_config, box.Id);
            if (window.FenceId != active.Id)
            {
                window.ShowTab(active);
                if (active.IsLibrary) _libraryLister?.Refresh();
                else CheckFence(active.Id); // a fence becoming visible is checked again (spec §4)
            }
            window.SetTabs(FenceTabs.TabsOf(_config, box.Id), active.Id);
            window.SetLocked(box.Locked);
            window.SetRolledUp(box.RolledUp);
        }
        RefreshWindows();
        ApplyLayout();
        RestyleAll(); // M14: after placing, so each fence takes its own monitor's accent
        ScheduleSave();
    }

    private void RefreshTabs(FenceWindow window)
    {
        window.SetTabs(FenceTabs.TabsOf(_config, window.BoxId), window.FenceId);
        ApplyStyle(window); // the shown tab's colour and font (M14)
    }

    /// <summary>A header clicked (or hovered during a drop): that tab shows, now and after a restart.</summary>
    private void SwitchTab(FenceWindow window, string fenceId)
    {
        if (window.FenceId == fenceId || _config.Fences.FirstOrDefault(fence => fence.Id == fenceId) is not { } tab) return;
        _config = FenceTabs.SetActive(_config, fenceId);
        window.ShowTab(tab);
        RefreshTabs(window);
        if (tab.IsLibrary) _libraryLister?.Refresh();
        else CheckFence(fenceId); // a fence becoming visible is checked again (spec §4)
        RefreshWindow(window);
        ScheduleSave();
    }

    private void CycleTab(FenceWindow window, int step)
    {
        var tabs = FenceTabs.TabsOf(_config, window.BoxId);
        var index = tabs.ToList().FindIndex(tab => tab.Id == window.FenceId);
        SwitchTab(window, tabs[((index + step) % tabs.Count + tabs.Count) % tabs.Count].Id);
    }

    /// <summary>A tab header released after a drag: on its own strip = reorder; on another title row = merge; elsewhere = detach.</summary>
    private void OnTabDropped(FenceWindow window, string fenceId, int screenX, int screenY)
    {
        var target = _windows.Values.FirstOrDefault(candidate => candidate.TitleRowContains(screenX, screenY));
        var own = FenceWindowChrome.GetPixelRect(window.Handle);
        if (target is null && screenX >= own.X && screenX < own.X + own.Width && screenY >= own.Y && screenY < own.Y + own.Height)
        {
            return; // released over its own box: nothing happens (not a detach on top of itself; final review)
        }
        if (target == window)
        {
            var slot = window.TabIndexAt(screenX);
            var current = FenceTabs.TabsOf(_config, window.BoxId).ToList().FindIndex(tab => tab.Id == fenceId);
            _config = FenceTabs.Reorder(_config, fenceId, slot > current ? slot - 1 : slot);
        }
        else if (target is not null)
        {
            Log.Information("tab {FenceId} moved into the box of {TargetId}", fenceId, target.BoxId);
            // One tab moves, also when it is its box's host (final review C1).
            _config = FenceTabs.Merge(_config, movingFenceId: fenceId, targetFenceId: target.BoxId, insertAt: target.TabIndexAt(screenX), wholeBox: false);
        }
        else
        {
            DetachTab(window, fenceId, dropPoint: (screenX, screenY));
            return;
        }
        SyncBoxes();
    }

    /// <summary>A tab leaves its box: at the drop point (dragged out) or offset 40 DIP down-right of the box (menu), with the box's size.</summary>
    private void DetachTab(FenceWindow window, string fenceId, (int X, int Y)? dropPoint)
    {
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint
            || !_config.Layouts.TryGetValue(fingerprint, out var layout) || !layout.Fences.TryGetValue(window.BoxId, out var boxRect))
        {
            Log.Warning("tab {FenceId} not detached: its box has no place in the current layout", fenceId);
            return;
        }
        var boxMonitor = _monitors.FirstOrDefault(monitor => monitor.DeviceId == boxRect.Monitor) ?? _monitors[0];
        var box = FencePlacement.ToPixels(boxRect, boxMonitor);
        var offset = (int)Math.Round(40 * boxMonitor.Scale);
        var placed = dropPoint is { } point
            ? box with { X = point.X - offset, Y = point.Y - offset / 3 } // the pointer near its title
            : box with { X = box.X + offset, Y = box.Y + offset };
        var monitor = FencePlacement.ContainingMonitor(placed, _monitors);
        Log.Information("tab {FenceId} detached from the box of {BoxId}", fenceId, window.BoxId);
        _config = FenceTabs.Detach(_config, fenceId, fingerprint, FencePlacement.FromPixels(placed, monitor));
        SyncBoxes();
    }

    private void ApplyStartup() =>
        StartupRegistration.Apply(_config.Settings.StartWithWindows, Environment.ProcessPath ?? "", log: message => Log.Information("{Message}", message));

    private void SetStartWithWindows(bool startWithWindows)
    {
        _config = _config with { Settings = _config.Settings with { StartWithWindows = startWithWindows } };
        ApplyStartup();
        foreach (var window in _windows.Values) window.SetStartupChecked(startWithWindows);
        SaveNow();
        RefreshSettings();
    }

    private void CreateFence()
    {
        (_config, _) = FenceEdits.CreateFence(_config, "New fence");
        SyncBoxes();
    }

    /// <summary>Settings → "Hide desktop icons while NeoFences runs" (M18): Windows' own setting, restored on every exit (hard rule 2).</summary>
    private void SetHideDesktopIcons(bool hide)
    {
        SetQuickHidden(false); // an explicit icons choice ends quick-hide first, so the two never disagree
        _config = _config with { Settings = _config.Settings with { HideDesktopIcons = hide } };
        SetIconsHidden(Current.IconsHidden);
        RefreshSettings();
        SaveNow(); // not debounced: the saved setting must match the icons-hidden marker if we are killed next
    }

    /// <returns>True when Windows confirmed the new state.</returns>
    private bool SetIconsHidden(bool hidden)
    {
        // The watchdog must know whenever icons may be hidden: mark before hiding, unmark only after a confirmed show.
        if (hidden) TryMarker(() => _watchdog.SetIconsHiddenMarker(true), what: "icons-hidden marker");
        var applied = DesktopIcons.TrySetHidden(hidden);
        if (applied && !hidden) TryMarker(() => _watchdog.SetIconsHiddenMarker(false), what: "icons-hidden marker");
        if (applied) Log.Information("desktop icons hidden: {Hidden}", hidden);
        else Log.Warning("could not set desktop icons hidden: {Hidden}", hidden);
        return applied;
    }

    /// <summary>Icons as they should be: hidden while the setting (or quick-hide) wants it, shown (and unmarked) otherwise.</summary>
    private bool EnsureIconState() =>
        Current.IconsHidden ? SetIconsHidden(true)
        : !_watchdog.IsIconsHiddenMarked || SetIconsHidden(false);

    private static void TryMarker(Action writeMarker, string what)
    {
        try
        {
            writeMarker();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "could not update the {What}", what);
        }
    }

    private void OnExplorerRestarted()
    {
        Log.Information("Explorer restarted; re-attaching fences");
        SetPeek(false); // re-attaching sends fences to the bottom; Peek (and its global Esc) must end with it (M5 review M2)
        ShowTrayIcon(); // Explorer forgot every tray icon
        ReinstallMouseHook(); // a hook Windows dropped silently comes back here at the latest (M5 review)
        StartSpecialIconNotifications();
        ScheduleSpecialIconRefresh(); // the Recycle Bin may have changed meanwhile
        var attempts = 0;
        var retryTimer = new DispatcherTimer { Interval = ReattachInterval };
        retryTimer.Tick += (_, _) =>
        {
            attempts++;
            var unattachedCount = _windows.Values.Count(window => !DesktopHost.AttachToDesktop(window.Handle));
            foreach (var window in _windows.Values) FenceWindowChrome.SendToBack(window.Handle);
            var iconsOk = EnsureIconState();
            if ((unattachedCount == 0 && iconsOk) || attempts >= ReattachAttempts)
            {
                retryTimer.Stop();
                Log.Information("re-attach after Explorer restart: {Attempts} attempt(s), unattached {UnattachedCount}, icons ok {IconsOk}",
                    attempts, unattachedCount, iconsOk);
            }
        };
        retryTimer.Start();
    }

    /// <summary>The WH_MOUSE_LL desktop gestures and the Peek hotkey (M5). Either failing only turns that feature off.</summary>
    private void StartGestures()
    {
        UpdateMouseHook();
        UpdatePeekHotkey();
    }

    /// <summary>The Peek hotkey is registered exactly while wanted: released to Windows and the game while paused or gaming.</summary>
    private void UpdatePeekHotkey()
    {
        // Released while Settings records a new one, so pressing the current combination is recorded, not Peek (M6b review).
        if ((Current.PeekHotkeyWanted && !_recordingHotkey) == _peekHotkey is not null) return;
        if (_peekHotkey is not null)
        {
            _peekHotkey.Dispose();
            _peekHotkey = null;
            return;
        }
        _peekHotkey = TryRegisterPeekHotkey(_config.Settings.PeekHotkey, out var problem);
        _peekHotkeyProblem = _peekHotkey is null ? problem : null;
        if (_peekHotkey is null) Log.Warning("Peek is off: {Problem}", problem);
    }

    private GlobalHotkey? TryRegisterPeekHotkey(string text, out string problem)
    {
        if (!Hotkey.TryParse(text, out var hotkey) || !Enum.TryParse<System.Windows.Input.Key>(hotkey.Key, ignoreCase: true, out var key))
        {
            problem = $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.";
            return null;
        }
        var virtualKey = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            problem = $"{hotkey.DisplayTextWith(LayoutKeyCap)} has no key Windows can watch for. Pick another key."; // would say "Saved" and never fire (M6b review M4)
            return null;
        }
        var registration = new GlobalHotkey(_messages.Handle, PeekHotkeyId);
        if (registration.TryRegister(hotkey, (uint)virtualKey))
        {
            problem = "";
            Log.Information("Peek hotkey {Hotkey} registered", hotkey);
            return registration;
        }
        registration.Dispose();
        problem = $"{hotkey.DisplayTextWith(LayoutKeyCap)} is already taken by Windows or another app. Pick another combination.";
        return null;
    }

    /// <summary>Settings: a new Peek hotkey. It is kept only if Windows accepts it; otherwise the old one stays.</summary>
    private (bool Saved, string Message) SetPeekHotkey(string text)
    {
        if (!Hotkey.TryParse(text, out var parsed)) return (false, $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.");
        // A global hotkey swallows its keys in every app (M6b review I1): only safe combinations are recorded.
        if (!parsed.IsSafeToRecord) return (false, $"{parsed.DisplayTextWith(LayoutKeyCap)} would stop working everywhere else (typing, Tab, closing windows). Use Alt or Win with a key, or an F-key.");
        var normalized = parsed.ToString();
        // The same combination again is a retry when it is not registered (Settings showed it as not active, M6b review).
        if (normalized == _config.Settings.PeekHotkey && _peekHotkeyProblem is null) return (true, $"Peek: {parsed.DisplayTextWith(LayoutKeyCap)}");
        _peekHotkey?.Dispose();
        _peekHotkey = null;
        var registration = TryRegisterPeekHotkey(normalized, out var problem);
        if (registration is null)
        {
            UpdatePeekHotkey(); // the old one again
            return (false, problem);
        }
        registration.Dispose(); // proven free; UpdatePeekHotkey registers it whenever it is wanted
        _peekHotkeyProblem = null;
        _config = _config with { Settings = _config.Settings with { PeekHotkey = normalized } };
        UpdatePeekHotkey();
        SaveNow();
        return (true, $"Saved. Peek: {parsed.DisplayTextWith(LayoutKeyCap)}");
    }

    /// <summary>Tray or fence menu → Settings… (M6b). One window; a second request brings it to the front.</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        var window = new SettingsWindow();
        window.StartWithWindowsChanged += SetStartWithWindows;
        window.HideDesktopIconsChanged += SetHideDesktopIcons;
        window.PeekHotkeyChosen += text =>
        {
            var (saved, message) = SetPeekHotkey(text);
            RefreshSettings();
            window.ShowHotkeyResult(saved, message);
        };
        window.RollupExpandChanged += SetRollupExpand;
        window.TakeSnapshotRequested += () => TakeSnapshot();
        window.RestoreSnapshotRequested += RestoreSnapshot;
        window.RenameSnapshotRequested += (path, name) =>
        {
            if (!_snapshots.Rename(path, name))
            {
                Log.Warning(_snapshots.LastFailure, "snapshot {Path} could not be renamed", path);
                SnapshotFailure("Snapshot not renamed", "The snapshot file could not be changed (see the log)."); // final review M1
            }
            RefreshSettings();
        };
        window.DeleteSnapshotRequested += DeleteSnapshot;
        WireLibrarySettings(window);
        window.OpenSnapshotsRequested += () =>
        {
            try
            {
                Directory.CreateDirectory(_snapshots.Directory);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                Log.Warning(failure, "snapshots folder {Folder} could not be created", _snapshots.Directory); // hard rule 7 (final review I3)
                return;
            }
            OpenItem(_snapshots.Directory, ownerHandle: 0);
        };
        window.HotkeyRecording += recording =>
        {
            _recordingHotkey = recording;
            UpdatePeekHotkey();
            // Leaving the box re-registers: Settings must show if that failed (final review I5).
            if (!recording) RefreshSettings();
        };
        window.DefaultLabelsChanged += labels =>
        {
            _config = _config with { Settings = _config.Settings with { DefaultLabels = labels } };
            SaveNow();
        };
        window.LabelsAppliedToAll += labels =>
        {
            _config = FenceEdits.SetLabelsEverywhere(_config, labels);
            foreach (var fenceWindow in _windows.Values) fenceWindow.SetLabelMode(labels);
            Log.Information("labels for every fence: {Labels}", labels);
            SaveNow();
            RefreshSettings();
        };
        window.ShortcutArrowsChanged += show =>
        {
            _config = _config with { Settings = _config.Settings with { ShowShortcutArrows = show } };
            foreach (var fenceWindow in _windows.Values) fenceWindow.SetShortcutArrows(show);
            SaveNow();
        };
        window.GameModeChanged += SetGameModeEnabled;
        window.AppearanceChanged += SetAppearance; // M14
        window.AutoUpdateChanged += SetAutoUpdate; // M17
        window.CheckForUpdatesRequested += () => CheckForUpdate(manual: true);
        window.RestartToUpdateRequested += RestartToUpdate;
        window.KeyboardLayoutChanged += RefreshSettings; // M16: key caps follow the new layout
        window.OpenLogsRequested += () => OpenItem(AppPaths.LogsDirectory, ownerHandle: 0);
        window.OpenDataRequested += () => OpenItem(AppPaths.DataDirectory, ownerHandle: 0);
        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _recordingHotkey = false;
            UpdatePeekHotkey();
        };
        _settingsWindow = window;
        RefreshSettings();
        window.Show();
        window.Activate();
    }

    private void RefreshSettings() => _settingsWindow?.Show(new SettingsView(
        StartWithWindows: _config.Settings.StartWithWindows,
        HideDesktopIcons: _config.Settings.HideDesktopIcons,
        PeekHotkey: PeekHotkeyDisplay,
        PeekHotkeyActive: _peekHotkeyProblem is null,
        RollupExpand: _config.Settings.RollupExpand,
        GameModeEnabled: _config.Settings.GameMode,
        GameModeActive: _gameMode,
        Version: typeof(FenceHost).Assembly.GetName().Version?.ToString(3) ?? "",
        DataFolder: AppPaths.DataDirectory,
        DefaultLabels: _config.Settings.DefaultLabels,
        ShowShortcutArrows: _config.Settings.ShowShortcutArrows,
        Snapshots: ListSnapshots(),
        Library: LibrarySettingsView(),
        Appearance: AppearanceView(),
        Updates: UpdatesView()));

    /// <summary>The Peek hotkey as a person reads it ("Ctrl+Shift+=", not "Ctrl+Shift+OemPlus").</summary>
    private string PeekHotkeyDisplay =>
        Hotkey.TryParse(_config.Settings.PeekHotkey, out var hotkey) ? hotkey.DisplayTextWith(LayoutKeyCap) : _config.Settings.PeekHotkey;

    /// <summary>The character the user's keyboard layout prints on an OEM key ("+" for OemPlus on a German keyboard, where the US name is "="), or null (M13c).</summary>
    private static string? LayoutKeyCap(string key) =>
        key.StartsWith("Oem", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<System.Windows.Input.Key>(key, ignoreCase: true, out var wpfKey)
            ? KeyboardLayout.CharacterOf(System.Windows.Input.KeyInterop.VirtualKeyFromKey(wpfKey))
            : null;

    /// <summary>Saves the arrangement and its items now, named by date and time (M10); renamed in Settings if wanted.</summary>
    private void TakeSnapshot()
    {
        var now = DateTimeOffset.Now;
        var snapshot = Snapshots.Take(_config, _items, name: $"Snapshot {now:d MMM HH:mm}", now: now);
        if (_snapshots.Save(snapshot) is { } path)
        {
            Log.Information("snapshot saved: {Path}", path);
            _trayIcon?.ShowBalloon("Snapshot saved", snapshot.Name);
            _settingsWindow?.ShowSnapshotNotice($"Saved \"{snapshot.Name}\".", failed: false);
        }
        else
        {
            Log.Warning(_snapshots.LastFailure, "snapshot could not be saved");
            SnapshotFailure("Snapshot not saved", "NeoFences could not write the snapshot file (see the log).");
        }
        RefreshSettings();
    }

    /// <summary>
    /// Puts a snapshot's arrangement and items back (M10, spec §3; M18): only after "Before restore" was written, so the
    /// restore itself can be undone. One change, saved at once.
    /// </summary>
    private void RestoreSnapshot(string path)
    {
        if (_snapshots.Load(path) is not { } snapshot)
        {
            Log.Warning(_snapshots.LastFailure, "snapshot {Path} could not be read", path);
            SnapshotFailure("Snapshot not restored", "The snapshot file could not be read.");
            return;
        }
        var now = DateTimeOffset.Now;
        if (_snapshots.Save(Snapshots.Take(_config, _items, name: $"Before restore ({now:d MMM HH:mm})", now: now), SnapshotStore.BeforeRestoreFileName) is null)
        {
            Log.Warning(_snapshots.LastFailure, "snapshot not restored: 'Before restore' could not be saved");
            SnapshotFailure("Snapshot not restored", "NeoFences could not save 'Before restore' first (see the log).");
            return;
        }
        Log.Information("restoring snapshot {Name} from {Path}", snapshot.Name, path);
        (_config, _items) = Snapshots.Restore(_config, snapshot);
        SaveNow();
        SyncBoxes();
        // Windows that kept their fence still show its old title, icon size and labels (final review I1).
        foreach (var window in _windows.Values)
        {
            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } shown) continue;
            window.Refresh(shown);
            window.SetTitle(shown.Title);
        }
        CheckAllTargets(); // the restored items' targets may have changed since
        _settingsWindow?.ShowSnapshotNotice($"Restored \"{snapshot.Name}\".", failed: false); // replaces an earlier failure line (final review M1)
        RefreshSettings();
    }

    /// <summary>The snapshot list; a damaged file or an unreadable folder is logged once, not on every tray open (final review I2).</summary>
    private IReadOnlyList<SnapshotEntry> ListSnapshots()
    {
        var snapshots = _snapshots.List();
        foreach (var (path, failure) in _snapshots.Problems.Where(problem => _loggedSnapshotProblems.Add(problem.Path)))
            Log.Warning(failure, "snapshot {Path} skipped: it cannot be read", path);
        return snapshots;
    }

    /// <summary>
    /// A snapshot failure, said where the user looks (M13c): Windows' warning notice, and the Settings card when it is open
    /// (a notice alone is easy to miss, and Do Not Disturb hides it).
    /// </summary>
    private void SnapshotFailure(string title, string reason)
    {
        _trayIcon?.ShowBalloon(title, reason, warning: true);
        _settingsWindow?.ShowSnapshotNotice($"{title}: {reason}", failed: true);
    }

    /// <summary>A snapshot file (NeoFences' own) goes to the Recycle Bin, never deleted for good.</summary>
    private void DeleteSnapshot(string path)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        // Windows' delete confirmation (when turned on) belongs to Settings, not to no window behind it (M13c).
        var owner = _settingsWindow is { } settings ? new WindowInteropHelper(settings).Handle : 0;
        _shellWorker.Run(() =>
        {
            var (started, refused, missing) = ShellFileOps.TryRecycle(FenceWindowChrome.IsLiveWindow(owner) ? owner : 0, [path]);
            if (!started || refused.Count > 0)
            {
                Log.Warning("snapshot {Path} could not be moved to the Recycle Bin", path);
                dispatcher.BeginInvoke(() => SnapshotFailure("Snapshot not deleted", "It was not moved to the Recycle Bin (see the log).")); // final review M1
            }
            if (missing.Count > 0) Log.Information("snapshot {Path} was already gone", path);
            dispatcher.BeginInvoke(RefreshSettings);
        });
    }

    /// <summary>Fence menu → Labels (M8b).</summary>
    private void SetFenceLabels(FenceWindow window, LabelMode labels)
    {
        _config = FenceEdits.SetLabels(_config, window.FenceId, labels);
        window.SetLabelMode(labels);
        ScheduleSave();
    }

    private void SetRollupExpand(RollupExpand mode)
    {
        _config = _config with { Settings = _config.Settings with { RollupExpand = mode } };
        foreach (var window in _windows.Values) window.SetRollupExpand(mode);
        Log.Information("roll-up expands on: {Mode}", mode);
        SaveNow();
    }

    private void SetGameModeEnabled(bool enabled)
    {
        _config = _config with { Settings = _config.Settings with { GameMode = enabled } };
        Log.Information("game mode enabled: {Enabled}", enabled);
        SaveNow();
        CheckGameMode(); // turning it off in a game ends idle at once
        RefreshSettings();
    }

    /// <summary>The hook exists exactly while it is wanted: not while paused or gaming (hard rule 3, spec §4.7).</summary>
    private void UpdateMouseHook()
    {
        if (Current.MouseHookWanted == _mouseHook is not null) return;
        if (_mouseHook is not null)
        {
            EndDrawOverlay();
            _mouseHook.Dispose();
            _mouseHook = null;
            return;
        }
        var dispatcher = Dispatcher.CurrentDispatcher;
        _mouseHook = new DesktopMouseHook(
            onGesture: (gesture, screenX, screenY) => dispatcher.BeginInvoke(() => OnDesktopGesture(gesture, screenX, screenY)),
            onPeekClickOutside: () => dispatcher.BeginInvoke(() => SetPeek(false)),
            log: message => Log.Information("{Message}", message));
    }

    /// <summary>Windows drops a low-level hook silently (LowLevelHooksTimeout): a fresh one after Explorer restarts and on unlock.</summary>
    private void ReinstallMouseHook()
    {
        if (_mouseHook is null) return; // not wanted now; it comes back when it is
        EndDrawOverlay();
        _mouseHook.Dispose();
        _mouseHook = null;
        UpdateMouseHook();
    }

    private void OnSessionUnlocked()
    {
        Log.Information("session unlocked; re-installing the mouse hook");
        ReinstallMouseHook();
        CheckGameMode();
    }

    /// <summary>Game mode (spec §4.7, ADR-021): checked on every foreground change, and again shortly after (the signal lags).</summary>
    private void StartGameMode()
    {
        // Backstop for a game that goes full screen after the last re-check (M6a review I1).
        var poll = new DispatcherTimer { Interval = GameModePolicy.PollInterval };
        poll.Tick += (_, _) => CheckGameMode();
        poll.Start();
        _foregroundWatcher = new ForegroundWatcher(onForegroundChanged: OnForegroundChanged,
            logFailure: failure => Log.Error(failure, "game mode check failed"));
        if (!_foregroundWatcher.IsWatching) Log.Warning("game mode: foreground changes cannot be watched; game mode is off");
        CheckGameMode();
    }

    private void OnForegroundChanged()
    {
        CheckGameMode();
        foreach (var delay in GameModePolicy.RecheckDelays)
        {
            // ponytail: one short-lived timer per foreground change and delay; a coalescing timer if switching storms ever show up in a profile.
            var recheck = new DispatcherTimer { Interval = delay };
            recheck.Tick += (_, _) =>
            {
                recheck.Stop();
                CheckGameMode();
            };
            recheck.Start();
        }
    }

    private void CheckGameMode()
    {
        var gameMode = GameModePolicy.IsGameActive(enabled: _config.Settings.GameMode, foreground: GameDetection.TakeSnapshot());
        if (gameMode == _gameMode) return;
        _gameMode = gameMode;
        Log.Information("game mode: {GameMode}", gameMode);
        if (gameMode)
        {
            SetPeek(false);
            StopUpdateDownload(); // M17: no network while a game is in front
        }
        UpdateMouseHook();
        _libraryLister?.SetPaused(gameMode);
        if (!gameMode) ApplyDeferredShellWork();
        UpdatePeekHotkey();
        _trayIcon?.SetTooltip(TrayTooltip());
        RefreshSettings();
    }

    /// <summary>The game was left: the library scan, special icons, target checks and renames that waited (spec §4.7).</summary>
    private void ApplyDeferredShellWork()
    {
        if (_libraryDeferred)
        {
            _libraryDeferred = false;
            ScanLibrary(); // a game installed while playing shows up now (M12)
        }
        if (_specialIconsDeferred)
        {
            _specialIconsDeferred = false;
            ScheduleSpecialIconRefresh();
        }
        ApplyDeferredWatching();
    }

    /// <summary>Pause (tray): the desktop goes back to Windows — fences hidden, icons shown, the hook gone — until resumed.</summary>
    private void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        if (paused)
        {
            SetPeek(false);
            EndDrawOverlay();
        }
        _paused = paused;
        if (paused)
        {
            _quickHidden = false; // resuming shows everything
            _iconsHiddenByUser = false;
        }
        foreach (var window in _windows.Values)
        {
            if (!Current.FencesVisible) window.HideNow();
            else if (!window.IsVisible)
            {
                window.ShowNow();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        EnsureIconState(); // also shows icons left hidden by an earlier failed show: Pause "restores icons" (spec §6, M6a review M1)
        UpdateMouseHook();
        UpdatePeekHotkey();
        RefreshSettings(); // a hotkey that cannot be registered on resume shows in an open Settings (final review I5)
        _trayIcon?.SetTooltip(TrayTooltip());
        Log.Information("paused: {Paused}", paused);
    }

    private string TrayTooltip() =>
        _paused ? "NeoFences — paused" : _gameMode ? "NeoFences — idle while a game runs" : "NeoFences";

    /// <summary>Tray menu (spec §6; user choice 2026-10-03: left-click opens it too).</summary>
    private void ShowTrayMenu(int screenX, int screenY)
    {
        var snapshots = ListSnapshots();
        var restorable = snapshots.Where(entry => !entry.IsBeforeRestore).Take(TrayRestoreCount).ToList();
        var beforeRestore = snapshots.FirstOrDefault(entry => entry.IsBeforeRestore);
        // One safe line per snapshot (M13c): no blank, multi-line, tabbed or 300-character entries.
        List<TrayMenuItem> restoreItems = [.. restorable.Select((entry, index) => new TrayMenuItem(TrayRestoreFirst + index, Snapshots.MenuLabel(entry, TimeZoneInfo.Local)))];
        if (beforeRestore is not null)
        {
            if (restoreItems.Count > 0) restoreItems.Add(TrayMenuItem.Separator); // never a separator first (M13c)
            restoreItems.Add(new TrayMenuItem(TrayRestoreBefore, "Undo the last restore or fix")); // a bulk fix shares the slot (M19 §2)
        }
        if (restoreItems.Count > 0) restoreItems.Add(TrayMenuItem.Separator);
        restoreItems.Add(new TrayMenuItem(TraySnapshotsSettings, "More in Settings…"));
        var chosen = TrayMenu.Show(_messages.Handle,
        [
            .. UpdateTrayItems(), // M17: "Restart to update to v…" first while an update waits
            new TrayMenuItem(TrayNewFence, "New fence", Enabled: !_paused),
            new TrayMenuItem(TrayNewLibrary, "New Game Library fence", Enabled: !_paused),
            new TrayMenuItem(TrayAddFromDesktop, "Add from desktop…", Enabled: !_paused),
            new TrayMenuItem(TrayQuickHide, "Quick-hide", Checked: _quickHidden, Enabled: !_paused),
            new TrayMenuItem(TrayPeek, $"Peek\t{PeekHotkeyDisplay}", Checked: _peeking, Enabled: !_paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayTakeSnapshot, "Take snapshot"),
            new TrayMenuItem(TrayRestoreMenu, "Restore snapshot", Enabled: snapshots.Count > 0) { Children = restoreItems },
            TrayMenuItem.Separator,
            new TrayMenuItem(TraySettings, "Settings…"),
            new TrayMenuItem(TrayPause, "Pause NeoFences", Checked: _paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayExit, "Exit NeoFences"),
        ], screenX, screenY);
        switch (chosen)
        {
            case TrayNewFence:
                SetQuickHidden(false); // a new fence must be visible (M6a review I3)
                CreateFence();
                break;
            case TrayNewLibrary: CreateLibraryFence(); break;
            case TrayAddFromDesktop:
                SetQuickHidden(false); // the new items must be seen landing
                ShowDesktopFill();
                break;
            case TrayQuickHide: SetQuickHidden(!_quickHidden); break;
            case TrayPeek: SetPeek(!_peeking); break;
            case TrayPause: SetPaused(!_paused); break;
            case TraySettings: OpenSettings(); break;
            case TrayRestartToUpdate: RestartToUpdate(); break; // M17
            case TraySnapshotsSettings:
                OpenSettings();
                _settingsWindow?.ShowSnapshotsCard();
                break;
            case TrayExit: ExitRequested?.Invoke(); break;
            case TrayTakeSnapshot: TakeSnapshot(); break;
            case TrayRestoreBefore when beforeRestore is not null: RestoreSnapshot(beforeRestore.Path); break;
            case >= TrayRestoreFirst and < TrayRestoreFirst + TrayRestoreCount when chosen - TrayRestoreFirst < restorable.Count:
                RestoreSnapshot(restorable[chosen - TrayRestoreFirst].Path);
                break;
        }
    }

    private void OnDesktopGesture(DesktopGesture gesture, int screenX, int screenY)
    {
        switch (gesture)
        {
            // A double-click on a visible native icon opens it; only empty desktop toggles quick-hide.
            case DesktopGesture.DoubleClick when Current.IconsHidden
                || !DesktopWindows.IsOverDesktopIcon(screenX, screenY, log: message => Log.Warning("{Message}", message)):
                SetQuickHidden(!_quickHidden);
                break;
            case DesktopGesture.RightDragStarted:
                BeginDrawFence(screenX, screenY);
                break;
            case DesktopGesture.RightDragCompleted:
                EndDrawFence(screenX, screenY);
                break;
            case DesktopGesture.RightDragCancelled:
                EndDrawOverlay(); // the drag's right-up was never seen (M5 review I2)
                break;
        }
    }

    /// <summary>Quick-hide (M5, user choice): fences and the native desktop icons go away together and come back together.</summary>
    private void SetQuickHidden(bool hidden)
    {
        if (hidden == _quickHidden) return;
        if (hidden) SetPeek(false);
        // Icons the user had hidden through Explorer stay theirs: quick-hide neither hides nor later shows them (M8a).
        // Hidden now without the icons-hidden marker: the user hid them in Explorer, so they stay the user's. With the marker
        // set, NeoFences hid them (an earlier show failed) and the end of this quick-hide retries the show (M8a review I1).
        if (hidden && !_config.Settings.HideDesktopIcons) _iconsHiddenByUser = DesktopIcons.TryIsHidden() == true && !_watchdog.IsIconsHiddenMarked;
        var iconsWereHidden = Current.IconsHidden;
        _quickHidden = hidden;
        foreach (var window in _windows.Values)
        {
            if (hidden) window.HideFaded(); // 150 ms fade (spec §6)
            else
            {
                window.ShowFaded();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        if (Current.IconsHidden != iconsWereHidden) SetIconsHidden(Current.IconsHidden); // RunState decides, user-hidden icons included
        if (!hidden) _iconsHiddenByUser = false;
        Log.Information("quick-hide: {Hidden}", hidden);
    }

    private void BeginDrawFence(int startX, int startY)
    {
        if (_mouseHook is not { } mouseHook) return;
        EndDrawOverlay();
        _drawStart = (startX, startY);
        var overlay = new DrawFenceOverlay(_lightTheme);
        overlay.Track(DrawFenceOverlay.Between(_drawStart, mouseHook.DragPoint));
        overlay.Show();
        _drawOverlay = overlay;
        _drawTimer = new DispatcherTimer { Interval = DrawFrame };
        // A lost right-up (Win+L, UAC, an elevated window) is ended by the next click (RightDragCancelled). The button
        // state cannot tell: the swallowed right-press never reaches Windows' key state (M5 review I2, smoke finding).
        _drawTimer.Tick += (_, _) => overlay.Track(DrawFenceOverlay.Between(_drawStart, mouseHook.DragPoint));
        _drawTimer.Start();
    }

    /// <summary>The right button came up: a new fence where the rectangle was, its title ready to type.</summary>
    private void EndDrawFence(int endX, int endY)
    {
        if (!EndDrawOverlay()) return; // the start was never seen
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint) return;
        SetQuickHidden(false);
        var pixels = DrawFenceOverlay.Between(_drawStart, (endX, endY));
        var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
        (_config, var fence) = FenceEdits.CreateFence(_config, "New fence");
        // Too small a drag still makes a usable fence: the layout clamps it to the minimum size.
        _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: fence.Id, rect: FencePlacement.FromPixels(pixels, monitor));
        Log.Information("fence drawn on the desktop at {Pixels}", pixels);
        OpenWindow(fence);
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
        if (_windows.TryGetValue(fence.Id, out var window)) window.BeginRename();
    }

    /// <returns>True when an overlay was showing.</returns>
    private bool EndDrawOverlay()
    {
        _drawTimer?.Stop();
        _drawTimer = null;
        if (_drawOverlay is null) return false;
        _drawOverlay.Close();
        _drawOverlay = null;
        return true;
    }

    private void OnHotkey(int hotkeyId)
    {
        CheckGameMode(); // fresh: a game may have gone full screen since the last check (M6a review I1)
        // Paused: the desktop belongs to Windows. Gaming: fences must not rise over the game, and the click-outside hook is off.
        if (_paused || _gameMode) return;
        if (hotkeyId == PeekHotkeyId) SetPeek(!_peeking);
        else if (hotkeyId == PeekEscapeHotkeyId) SetPeek(false);
    }

    /// <summary>Peek (M5): every fence above all windows until the hotkey again, Esc, a click outside, or an item opens.</summary>
    private void SetPeek(bool peeking)
    {
        if (peeking == _peeking) return;
        if (peeking) SetQuickHidden(false);
        _peeking = peeking;
        if (_mouseHook is not null) _mouseHook.PeekActive = peeking;
        // Every fence first: raising one restacks its siblings (all owned by Progman), and a sibling still keeping
        // itself at the bottom would drop back (M5 smoke: only one fence rose).
        foreach (var window in _windows.Values) window.Peeking = peeking;
        foreach (var window in _windows.Values) FenceWindowChrome.SetTopmost(window.Handle, peeking);
        _peekEscapeHotkey?.Dispose();
        _peekEscapeHotkey = null;
        if (peeking)
        {
            _peekEscapeHotkey = new GlobalHotkey(_messages.Handle, PeekEscapeHotkeyId);
            if (!_peekEscapeHotkey.TryRegister(new Hotkey(Ctrl: false, Alt: false, Shift: false, Win: false, Key: "Escape"), virtualKey: 0x1B))
                Log.Warning("Esc is taken by another app; Peek ends with its hotkey or a click outside");
            foreach (var window in _windows.Values) CheckFence(window.FenceId); // the shown fences are checked again (spec §4)
        }
        Log.Information("peek: {Peeking}", peeking);
    }

    /// <summary>Title double-click (M5): rolled up to its title bar, or back. Stored, so it survives a restart.</summary>
    private void ToggleRollUp(FenceWindow window)
    {
        var rolledUp = !_config.Fences.First(fence => fence.Id == window.BoxId).RolledUp; // the box's (M9)
        _config = FenceEdits.SetRolledUp(_config, window.BoxId, rolledUp);
        window.SetRolledUp(rolledUp);
        if (!rolledUp) CheckFence(window.FenceId); // unrolled: its items are checked again (spec §4)
        ScheduleSave();
    }

    /// <summary>Adds the tray icon, retrying while Explorer is still busy (sign-in autostart, Explorer restart).</summary>
    private void ShowTrayIcon()
    {
        if (_trayIcon is not { } trayIcon || trayIcon.Show()) return;
        var attempts = 0;
        var retry = new DispatcherTimer { Interval = TrayRetryInterval };
        retry.Tick += (_, _) =>
        {
            if (trayIcon.Show() || ++attempts >= TrayRetryAttempts)
            {
                retry.Stop();
                Log.Information("tray icon retry finished after {Attempts} attempt(s)", attempts + 1);
            }
        };
        retry.Start();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// Both files, the config first (ADR-041). Item lists are never dropped here: a fence's delete removes its own list, and
    /// a config that is only a fallback (fresh, an old backup) must not cost the user their items (final review I1).
    /// </summary>
    private void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            if (!_store.Save(_config)) Log.Warning("config not saved: config.json is read-only this session");
            else if (_store.LastBackupFailure is { } backupFailure) Log.Warning(backupFailure, "config saved, but the daily backups could not be written or pruned");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "config save failed");
        }
        try
        {
            if (!_itemStore.Save(_items)) Log.Warning("items not saved: items.json is read-only this session");
            else if (_itemStore.LastBackupFailure is { } backupFailure) Log.Warning(backupFailure, "items saved, but the daily backups could not be written or pruned");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "items save failed");
        }
    }

    private static void TryCreateFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Warning(failure, "cannot create {Folder}", folder); // the fence then shows nothing (hard rule 7)
        }
    }
}
