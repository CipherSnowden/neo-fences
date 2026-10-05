using System.Windows;
using NeoFences.Core.Items;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Auto-collect rules (M27, spec 2026-10-05-auto-collect-design, ADR-049): one <see cref="FolderLister"/> per watched folder
/// (the desktop is two); each listing is compared with the last one, and what arrived goes to the first matching rule's
/// fence as items. Never a file operation. Paused in game mode and while NeoFences is paused: the next listing catches up.
/// </summary>
public sealed partial class FenceHost
{
    /// <summary>
    /// Arrivals wait this long after the folder's last change before they are collected (final review I2, I3): an item that
    /// follows a rename in place (FenceHost.Watching settles renames after 500 ms) is held by then, a download's temporary
    /// names are gone, and a burst is one batch (one cap, one refresh).
    /// </summary>
    private static readonly TimeSpan CollectSettle = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, (string Source, FolderLister Lister)> _collectListers = new(StringComparer.OrdinalIgnoreCase);
    // The last listing per watched folder; missing or null: none yet, or the folder was not available (the next one catches up).
    private readonly Dictionary<string, IReadOnlyList<ItemInfo>?> _collectListings = new(StringComparer.OrdinalIgnoreCase);
    // Per folder: what its first listing catches up from — its rules' watermark when the lister started (final review C1:
    // the desktop's two folders each catch up, whichever lists first).
    private readonly Dictionary<string, DateTimeOffset?> _collectSince = new(StringComparer.OrdinalIgnoreCase);
    // Per source: arrivals waiting for the folder to settle, and when the last one came (the monotonic clock).
    private readonly Dictionary<string, (Dictionary<string, ItemInfo> Arrivals, TimeSpan LastAt)> _collectPending = new(StringComparer.OrdinalIgnoreCase);
    private System.Windows.Threading.DispatcherTimer? _collectTimer;
    private string? _downloadsFolder;

    /// <summary>Every folder some rule watches, with its rule source (the desktop source is the user's and the Public Desktop).</summary>
    private IEnumerable<(string Source, string Folder)> CollectFolders() =>
        _config.Fences.Where(fence => fence.Kind == FenceKind.Items).SelectMany(fence => fence.Collect).Select(rule => rule.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(FoldersOf);

    private static IEnumerable<(string Source, string Folder)> FoldersOf(string source) =>
        source == CollectRules.DesktopSource
            ? [(source, DesktopItems.UserDesktop), (source, DesktopItems.PublicDesktop)]
            : [(source, source)];

    /// <summary>A lister per watched folder; listers of folders no rule watches any more go (after every config change).</summary>
    private void EnsureCollectListers()
    {
        var wanted = CollectFolders().GroupBy(entry => entry.Folder, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.First().Source, StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, (_, lister)) in _collectListers.ToList())
        {
            if (wanted.ContainsKey(folder)) continue;
            lister.Dispose();
            _collectListers.Remove(folder);
            _collectListings.Remove(folder);
            _collectSince.Remove(folder);
        }
        foreach (var source in _collectPending.Keys.Where(source => !wanted.ContainsValue(source)).ToList()) _collectPending.Remove(source);
        foreach (var (folder, source) in wanted.Where(entry => !_collectListers.ContainsKey(entry.Key)))
        {
            _collectSince[folder] = RulesOf(source).Min(rule => rule.Watermark);
            // Names only: a file being written (a download) does not re-list the folder 4 times a second (final review M5).
            var lister = new FolderLister(folder, noticeOwner: _messages.Handle, label: "auto-collect",
                show: listed => OnCollectListing(source, folder, listed),
                logFailure: failure => Log.Warning(failure, "auto-collect: cannot watch {Folder}", folder), namesOnly: true,
                startPaused: _gameMode || _paused); // M28: no catch-up during a game
            _collectListers[folder] = (source, lister);
        }
    }

    private List<CollectRule> RulesOf(string source) =>
        [.. _config.Fences.Where(fence => fence.Kind == FenceKind.Items).SelectMany(fence => fence.Collect).Where(rule => CollectRules.SameSource(rule.Source, source))];

    private void StopCollectListers()
    {
        foreach (var (_, lister) in _collectListers.Values) lister.Dispose();
        _collectListers.Clear();
        _collectTimer?.Stop();
    }

    /// <summary>Game mode or Pause: changes wait; resuming lists once, and what arrived meanwhile is collected then.</summary>
    private void SetCollectPaused()
    {
        foreach (var (_, lister) in _collectListers.Values) lister.SetPaused(_gameMode || _paused);
    }

    private bool ReleaseCollectForRemoval(nint handle) => _collectListers.Values.Aggregate(false, (released, entry) => entry.Lister.ReleaseForRemoval(handle) | released);

    /// <summary>
    /// A watched folder was listed (on the UI thread): what arrived since its last listing — or, with none (start, a drive
    /// back), what was created after its rules last looked — waits for the folder to settle (<see cref="CollectSettle"/>).
    /// </summary>
    private void OnCollectListing(string source, string folder, IReadOnlyList<ItemInfo>? listed)
    {
        if (!_collectListers.ContainsKey(folder)) return; // its rules went meanwhile
        var hadListing = _collectListings.TryGetValue(folder, out var before) && before is not null;
        _collectListings[folder] = listed;
        if (listed is null) return; // not available: the next listing catches up from the folder's watermark
        var arrivals = CollectRules.Arrivals(hadListing ? before : null, listed, _collectSince.GetValueOrDefault(folder));
        if (arrivals.Count == 0) return;
        if (!_collectPending.TryGetValue(source, out var pending)) pending = (new Dictionary<string, ItemInfo>(StringComparer.OrdinalIgnoreCase), TimeSpan.Zero);
        foreach (var arrival in arrivals) pending.Arrivals[arrival.ItemRef] = arrival;
        _collectPending[source] = (pending.Arrivals, Clock.Elapsed);
        if (_collectTimer is null)
        {
            _collectTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _collectTimer.Tick += (_, _) => OnCollectTick();
        }
        _collectTimer.Start();
    }

    /// <summary>
    /// Sources quiet for <see cref="CollectSettle"/>: their arrivals that still exist go to the first matching rule's fence
    /// (nothing any fence holds; at most 200 per rule), once, with one refresh. Not during a game or while paused.
    /// </summary>
    private void OnCollectTick()
    {
        if (_gameMode || _paused) return;
        var collected = new List<string>();
        foreach (var (source, pending) in _collectPending.Where(entry => Clock.Elapsed - entry.Value.LastAt >= CollectSettle).ToList())
        {
            _collectPending.Remove(source);
            var folders = FoldersOf(source).Select(entry => entry.Folder).ToList();
            var present = folders.SelectMany(folder => _collectListings.GetValueOrDefault(folder) ?? []).Select(entry => entry.ItemRef).ToHashSet(ItemKinds.Comparer);
            var arrivals = pending.Arrivals.Values.Where(arrival => present.Contains(arrival.ItemRef)).ToList(); // renamed or deleted since: not collected
            var plan = CollectRules.Plan(_config, _items, source, arrivals);
            foreach (var fence in plan.GroupBy(entry => entry.FenceId))
            {
                _items = ItemEdits.Add(_items, fence.Key, [.. fence.Select(entry => VirtualItem.Create(entry.Path))]).Document;
                foreach (var entry in fence) Log.Information("auto-collect: {Path} -> fence {FenceId} (rule {RuleId})", entry.Path, entry.FenceId, entry.RuleId);
            }
            if (pending.Arrivals.Count > plan.Count) Log.Information("auto-collect: {Skipped} of {Count} new entries of {Source} not collected (gone again, no rule matched, already held, or too many at once)", pending.Arrivals.Count - plan.Count, pending.Arrivals.Count, source);
            collected.AddRange(plan.Select(entry => entry.Path));
            // What the rules of this source have seen — once each of its folders was listed (the desktop is two): a restart
            // does not collect it again, so an item the user removed stays removed.
            var now = DateTimeOffset.Now;
            if (folders.All(folder => _collectListings.GetValueOrDefault(folder) is not null)) AdvanceWatermark(source, now);
            // A folder that goes away and comes back (a pendrive) catches up from here, not from the start again.
            foreach (var folder in folders.Where(folder => _collectListings.GetValueOrDefault(folder) is not null)) _collectSince[folder] = now;
        }
        if (_collectPending.Count == 0) _collectTimer?.Stop();
        if (collected.Count > 0)
        {
            ItemsChanged(checkTargets: collected);
            SaveNow(itemsFirst: true); // M28: the items before the advanced watermark — a power cut in between collects them again, never skips them
        }
        else ScheduleSave();
    }

    private void AdvanceWatermark(string source, DateTimeOffset now) =>
        _config = _config with
        {
            Fences = [.. _config.Fences.Select(fence => fence.Collect.Any(rule => CollectRules.SameSource(rule.Source, source))
                ? fence with { Collect = [.. fence.Collect.Select(rule => CollectRules.SameSource(rule.Source, source) ? rule with { Watermark = now } : rule)] }
                : fence)],
        };

    /// <summary>Fence menu → Auto-collect…: the fence's rules; a new rule offers the files that match now ("Add these N too?").</summary>
    private void EditCollectRules(FenceWindow window)
    {
        var fenceId = window.FenceId;
        if (_config.Fences.FirstOrDefault(fence => fence.Id == fenceId) is not { Kind: FenceKind.Items } fence) return;
        _downloadsFolder ??= KnownFolders.Downloads(failure => Log.Information(failure, "auto-collect: the Downloads folder is not known"));
        var dialog = new AutoCollectWindow(fence.Title, fence.Collect, _downloadsFolder) { Owner = window };
        if (dialog.ShowDialog() != true || dialog.Result is not { } rules) return;
        if (!_config.Fences.Any(candidate => candidate.Id == fenceId)) return; // a restore took the fence meanwhile
        var now = DateTimeOffset.Now;
        // The rules as they are now: listings during the dialog advanced their watermarks (M28: an unchanged rule keeps those).
        var current = _config.Fences.First(candidate => candidate.Id == fenceId).Collect;
        var known = current.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);
        // A new or changed rule starts looking now: what is there already is offered once, not collected as "new".
        rules = [.. rules.Select(rule => current.FirstOrDefault(old => old.Id == rule.Id) is { } old && old with { Watermark = null } == rule with { Watermark = null } ? old : rule with { Watermark = now })];
        _config = FenceEdits.SetCollect(_config, fenceId, rules);
        Log.Information("fence {FenceId} auto-collect rules: {Rules}", fenceId, rules.Select(CollectRules.Summary));
        EnsureCollectListers();
        ScheduleSave();
        RefreshFenceMenus();
        foreach (var rule in rules.Where(rule => !known.Contains(rule.Id))) OfferExisting(window, fenceId, rule, dialog.Listings);
    }

    /// <summary>"Add these N too?" for a new rule: the files of its folder that match now and no fence holds (at most 200).</summary>
    private void OfferExisting(FenceWindow window, string fenceId, CollectRule rule, IReadOnlyDictionary<string, IReadOnlyList<ItemInfo>?> listings)
    {
        if (!listings.Keys.Any(source => CollectRules.SameSource(source, rule.Source)))
        {
            // OK while the dialog still said "Looking…" (a slow share, M28): listed now, off the UI thread, then asked.
            Task.Run(() => AutoCollectWindow.ListSource(rule.Source)).ContinueWith(listing =>
            {
                if (listing.IsFaulted) Log.Warning(listing.Exception, "auto-collect: {Source} could not be listed for the offer", rule.Source);
                else if (_windows.ContainsValue(window)) OfferExisting(window, fenceId, rule, new Dictionary<string, IReadOnlyList<ItemInfo>?> { [rule.Source] = listing.Result });
            }, TaskScheduler.FromCurrentSynchronizationContext());
            return;
        }
        if (!_config.Fences.Any(fence => fence.Id == fenceId)) return; // the fence went meanwhile
        if (_gameMode)
        {
            Log.Information("auto-collect: the offer for a new rule was dropped: a game is in front"); // never a question over a game (final review M7)
            return;
        }
        var entries = listings.Where(listing => listing.Value is not null && CollectRules.SameSource(listing.Key, rule.Source)).SelectMany(listing => listing.Value!).ToList();
        var lone = _config with { Fences = [.. _config.Fences.Where(fence => fence.Id == fenceId).Select(fence => fence with { Collect = [rule] })] };
        var plan = CollectRules.Plan(lone, _items, rule.Source, entries);
        if (plan.Count == 0) return;
        var answer = MessageBox.Show(window, $"{plan.Count}{(plan.Count == CollectRules.MaxPerBurst ? " (the first)" : "")} item{(plan.Count == 1 ? "" : "s")} in {CollectRules.Summary(rule).Split(" · ")[0]} match this rule already.\n\nAdd these {plan.Count} too?\n\nYour files are not moved: the fence only shows them.",
            "NeoFences — auto-collect", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _items = ItemEdits.Add(_items, fenceId, [.. plan.Select(entry => VirtualItem.Create(entry.Path))]).Document;
        Log.Information("auto-collect: {Count} existing item(s) added to fence {FenceId} by a new rule", plan.Count, fenceId);
        ItemsChanged(checkTargets: [.. plan.Select(entry => entry.Path)]);
    }

    /// <summary>The fence menus say how many rules each fence has.</summary>
    private void RefreshFenceMenus()
    {
        foreach (var window in _windows.Values)
        {
            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is { } shown) window.Refresh(shown);
        }
    }
}
