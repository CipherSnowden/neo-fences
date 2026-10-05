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
    private readonly Dictionary<string, (string Source, FolderLister Lister)> _collectListers = new(StringComparer.OrdinalIgnoreCase);
    // The last listing per watched folder; missing or null: none yet, or the folder was not available (the next one catches up).
    private readonly Dictionary<string, IReadOnlyList<ItemInfo>?> _collectListings = new(StringComparer.OrdinalIgnoreCase);
    private string? _downloadsFolder;

    /// <summary>Every folder some rule watches, with its rule source (the desktop source is the user's and the Public Desktop).</summary>
    private IEnumerable<(string Source, string Folder)> CollectFolders() =>
        _config.Fences.Where(fence => fence.Kind == FenceKind.Items).SelectMany(fence => fence.Collect).Select(rule => rule.Source)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(source => source == CollectRules.DesktopSource
                ? new[] { (source, DesktopItems.UserDesktop), (source, DesktopItems.PublicDesktop) }
                : [(source, source)]);

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
        }
        foreach (var (folder, source) in wanted.Where(entry => !_collectListers.ContainsKey(entry.Key)))
        {
            var lister = new FolderLister(folder, noticeOwner: _messages.Handle, label: "auto-collect",
                show: listed => OnCollectListing(source, folder, listed),
                logFailure: failure => Log.Warning(failure, "auto-collect: cannot watch {Folder}", folder));
            if (_gameMode || _paused) lister.SetPaused(true);
            _collectListers[folder] = (source, lister);
        }
    }

    private void StopCollectListers()
    {
        foreach (var (_, lister) in _collectListers.Values) lister.Dispose();
        _collectListers.Clear();
    }

    /// <summary>Game mode or Pause: changes wait; resuming lists once, and what arrived meanwhile is collected then.</summary>
    private void SetCollectPaused()
    {
        foreach (var (_, lister) in _collectListers.Values) lister.SetPaused(_gameMode || _paused);
    }

    private bool ReleaseCollectForRemoval(nint handle) => _collectListers.Values.Aggregate(false, (released, entry) => entry.Lister.ReleaseForRemoval(handle) | released);

    /// <summary>
    /// A watched folder was listed (on the UI thread): what arrived since the last listing — or, with none (start, a drive
    /// back), what was created after its rules last looked — goes to the rules' fences.
    /// </summary>
    private void OnCollectListing(string source, string folder, IReadOnlyList<ItemInfo>? listed)
    {
        if (!_collectListers.ContainsKey(folder)) return; // its rules went meanwhile
        var hadListing = _collectListings.TryGetValue(folder, out var before) && before is not null;
        _collectListings[folder] = listed;
        if (listed is null) return; // not available: the next listing catches up by the watermark
        var rules = _config.Fences.SelectMany(fence => fence.Collect).Where(rule => CollectRules.SameSource(rule.Source, source)).ToList();
        if (rules.Count == 0) return;
        var watermark = rules.Min(rule => rule.Watermark);
        var arrivals = CollectRules.Arrivals(hadListing ? before : null, listed, watermark);
        if (hadListing && arrivals.Count == 0) return; // nothing new: the watermark stays (no config write per change)
        var plan = CollectRules.Plan(_config, _items, source, arrivals);
        foreach (var fence in plan.GroupBy(entry => entry.FenceId))
        {
            _items = ItemEdits.Add(_items, fence.Key, [.. fence.Select(entry => VirtualItem.Create(entry.Path))]).Document;
            foreach (var entry in fence) Log.Information("auto-collect: {Path} -> fence {FenceId} (rule {RuleId})", entry.Path, entry.FenceId, entry.RuleId);
        }
        if (arrivals.Count > plan.Count) Log.Information("auto-collect: {Skipped} of {Count} new entries in {Folder} not collected (no rule matched, already held, or too many at once)", arrivals.Count - plan.Count, arrivals.Count, folder);
        // What the rules of this source have seen: a restart does not collect it again (an item the user removed stays removed).
        var now = DateTimeOffset.Now;
        _config = _config with
        {
            Fences = [.. _config.Fences.Select(fence => fence.Collect.Any(rule => CollectRules.SameSource(rule.Source, source))
                ? fence with { Collect = [.. fence.Collect.Select(rule => CollectRules.SameSource(rule.Source, source) ? rule with { Watermark = now } : rule)] }
                : fence)],
        };
        if (plan.Count > 0) ItemsChanged(checkTargets: [.. plan.Select(entry => entry.Path)]);
        else ScheduleSave();
    }

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
        var known = fence.Collect.Select(rule => rule.Id).ToHashSet(StringComparer.Ordinal);
        // A new or changed rule starts looking now: what is there already is offered once, not collected as "new".
        rules = [.. rules.Select(rule => fence.Collect.FirstOrDefault(old => old.Id == rule.Id) is { } old && old with { Watermark = null } == rule with { Watermark = null } ? old : rule with { Watermark = now })];
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
