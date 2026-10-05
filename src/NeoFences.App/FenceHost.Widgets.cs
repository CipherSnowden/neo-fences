using System.Windows.Controls;
using System.Windows.Threading;
using NeoFences.Core.Items;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Widgets (M25, spec 2026-10-05-widgets-design, ADR-047): Add widget ▸, the widget menu, double-clicks, and one shared
/// timer — on whole seconds while any widget exists, doing nothing while no widget can be seen (paused, quick-hidden,
/// game mode, rolled up, a hidden tab). System stats are read every 2 s on a worker, one reading at a time.
/// </summary>
public sealed partial class FenceHost
{
    private static readonly TimeSpan StatsEvery = TimeSpan.FromSeconds(2);
    private DispatcherTimer? _widgetTimer;
    private SystemStats? _systemStats;
    private StatsSample? _lastStats;
    private string? _statsSource = ""; // M31: the outside monitor last read ("" before the first reading)
    private bool _statsInFlight;
    private TimeSpan? _lastStatsAt; // on the monotonic clock: a clock change never freezes the stats (final review I4)
    private readonly HashSet<string> _statsFailuresLogged = new(StringComparer.Ordinal);

    private bool HasWidgets => _items.Fences.Values.Any(items => items.Any(item => item.Kind == ItemKind.Widget));

    /// <summary>The timer exists exactly while some fence holds a widget (after every item change).</summary>
    private void UpdateWidgetTimer()
    {
        if (!HasWidgets || !Current.ExtrasWanted) // M33: no widget work in safe mode
        {
            _widgetTimer?.Stop();
            _widgetTimer = null;
            return;
        }
        if (_widgetTimer is not null) return;
        _widgetTimer = new DispatcherTimer(DispatcherPriority.Background);
        _widgetTimer.Tick += (_, _) => OnWidgetTick();
        ScheduleWidgetTick();
        _widgetTimer.Start();
        OnWidgetTick();
    }

    /// <summary>The next whole second: the clock never lags a second behind, and a time change re-aligns it.</summary>
    private void ScheduleWidgetTick()
    {
        var now = DateTime.Now;
        if (_widgetTimer is not null) _widgetTimer.Interval = Widgets.NextTick(now, seconds: true) - now + TimeSpan.FromMilliseconds(5);
    }

    /// <summary>Windows a widget can be seen in now; none while paused, quick-hidden or in game mode.</summary>
    private IReadOnlyList<FenceWindow> WidgetWindows() =>
        !Current.FencesVisible || _gameMode || !Current.ExtrasWanted ? [] // M33 review I2: no sensor reads in safe mode
        : [.. _windows.Values.Where(window => window.IsVisible && window.HasWidgets && window.ItemsShown)]; // a hovered-open roll-up counts (final review I3)

    private void OnWidgetTick()
    {
        ScheduleWidgetTick();
        var windows = WidgetWindows();
        if (windows.Count == 0) return; // nobody can see a widget: no work at all
        var now = DateTime.Now;
        foreach (var window in windows) window.UpdateWidgets(now, _lastStats);
        var elapsed = Clock.Elapsed;
        if (!_statsInFlight && (_lastStatsAt is not { } last || elapsed - last >= StatsEvery - TimeSpan.FromMilliseconds(100)) // timer jitter: every 2 s, not 3
            && windows.Any(window => window.ShowsWidget(WidgetKind.Stats)))
        {
            // The last reading is old (stats hidden — the whole fence, a roll-up, a tab — while time went on): a CPU/GPU rate would
            // average over that spell, so this reading only primes and the shown one comes 2 s later (M28, final review M1).
            var stale = _lastStatsAt is { } previous && elapsed - previous > StatsEvery * 2;
            _lastStatsAt = elapsed;
            if (stale && _systemStats is { } primed)
            {
                _statsInFlight = true;
                Task.Run(primed.Sample).ContinueWith(_ => _statsInFlight = false, TaskScheduler.FromCurrentSynchronizationContext());
            }
            else SampleStats();
        }
    }

    /// <summary>One reading on a worker; shown at once in the windows that show stats.</summary>
    private void SampleStats()
    {
        _statsInFlight = true;
        var dispatcher = Dispatcher.CurrentDispatcher; // the UI's: the readings report failures from a worker thread (final review I1)
        var stats = _systemStats ??= new SystemStats(logOnce: (what, failure) =>
            dispatcher.BeginInvoke(() => { if (_statsFailuresLogged.Add(what)) Log.Information(failure, "system stats: {What} not available", what); }));
        Task.Run(stats.Sample).ContinueWith(sampling =>
        {
            _statsInFlight = false;
            if (sampling.IsFaulted)
            {
                Log.Warning(sampling.Exception, "system stats: reading failed; the last values stay");
                return;
            }
            _lastStats = sampling.Result;
            if (stats.Source != _statsSource) Log.Information("system stats: sensors from {Source}", (_statsSource = stats.Source) ?? "Windows only"); // M31
            foreach (var window in WidgetWindows().Where(window => window.ShowsWidget(WidgetKind.Stats))) window.UpdateWidgets(DateTime.Now, _lastStats);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Fence menu → Add widget ▸: at the end (Flow) or the first free spot (Free); several of a kind are fine.</summary>
    private void AddWidget(FenceWindow window, WidgetKind kind)
    {
        if (window.Kind != Core.Model.FenceKind.Items) return;
        var widget = VirtualItem.Create(Widgets.Target(kind));
        _items = _items.With(window.FenceId, [.. _items.Of(window.FenceId), widget]); // not ItemEdits.Add: it would merge a second clock into the first
        Log.Information("{Widget} widget added to fence {FenceId}", kind, window.FenceId);
        ItemsChanged(checkTargets: []);
        window.SelectItems([widget.Id]);
    }

    /// <summary>Double-click: the clock opens Windows' Clock app, System stats Task Manager; the date page nothing.</summary>
    private void OpenWidget(VirtualItem item, FenceWindow window)
    {
        switch (Widgets.Of(item.Target))
        {
            case WidgetKind.Clock: OpenItem("ms-clock:", ownerHandle: window.Handle); break;
            case WidgetKind.Stats: OpenItem("taskmgr.exe", ownerHandle: window.Handle); break;
        }
    }

    /// <summary>A widget's menu: the clock's options (M31: the stats' °F), Size ▸, Properties…, Remove from fence.</summary>
    private void ShowWidgetMenu(FenceWindow window, VirtualItem item, bool fromKeyboard)
    {
        var menu = new ContextMenu();
        void Command(string header, Action run, bool? isChecked = null)
        {
            var command = new MenuItem { Header = header, IsChecked = isChecked == true };
            command.Click += (_, _) => run();
            menu.Items.Add(command);
        }
        if (Widgets.Of(item.Target) == WidgetKind.Clock)
        {
            var options = item.Widget ?? new WidgetOptions();
            Command("Show seconds", () => SetWidgetOptions(item.Id, options with { Seconds = !options.Seconds }), isChecked: options.Seconds);
            Command("Show date", () => SetWidgetOptions(item.Id, options with { Date = !options.Date }), isChecked: options.Date);
            menu.Items.Add(new Separator());
        }
        if (Widgets.Of(item.Target) == WidgetKind.Stats) // M31
        {
            var options = item.Widget ?? new WidgetOptions();
            Command("Temperature in °F", () => SetWidgetOptions(item.Id, options with { Fahrenheit = !options.Fahrenheit }), isChecked: options.Fahrenheit);
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(SizeMenu(menu, [item]));
        Command("Properties…", () => ShowProperties(window, item.Id, focusName: false));
        Command("Remove from fence", () => RemoveItems(window, [item.Id]));
        window.ShowItemMenu(menu, fromKeyboard);
    }

    private void SetWidgetOptions(string itemId, WidgetOptions options)
    {
        if (_items.Find(itemId) is not { } item) return;
        _items = ItemEdits.Replace(_items, item with { Widget = options == new WidgetOptions() ? null : options });
        ItemsChanged(checkTargets: []);
    }
}
