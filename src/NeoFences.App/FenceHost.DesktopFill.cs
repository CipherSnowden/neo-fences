using NeoFences.Core.Items;
using NeoFences.Core.Model;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// "Add from desktop…" (M19, spec 2026-10-05 §3, ADR-042): from the fence menu or the tray. Items point at the desktop
/// entries themselves, exactly like a drag from the desktop; nothing on the desktop is moved or changed.
/// </summary>
public sealed partial class FenceHost
{
    private bool _desktopFillOpen;

    private void ShowDesktopFill()
    {
        if (_desktopFillOpen) return; // one window at a time
        var fences = _config.Fences.Where(fence => fence.Kind == FenceKind.Items).Select(fence => (fence.Id, fence.Title)).ToList(); // not the library or views (M21)
        var titles = fences.ToDictionary(fence => fence.Id, fence => fence.Title, StringComparer.Ordinal);
        // Each target already held by an item → a fence holding it (shown as "already in …", unticked).
        var alreadyIn = new Dictionary<string, string>(ItemKinds.Comparer);
        foreach (var (fenceId, items) in _items.Fences)
        {
            if (!titles.TryGetValue(fenceId, out var title)) continue;
            foreach (var item in items) alreadyIn.TryAdd(item.Target, title);
        }
        var dialog = new DesktopFillWindow(fences, alreadyIn, _config.Library, [.. _library.Items.Select(item => item.Game)], _iconLoader, iconsHidden: _config.Settings.HideDesktopIcons,
            suggestHideIcons: _config.Fences.Any(fence => fence.Welcome)); // a first start: sorting the desktop, its icons would show twice
        _desktopFillOpen = true;
        try
        {
            if (dialog.ShowDialog() == true && dialog.Plan is { } plan) ApplyDesktopFill(plan);
        }
        finally
        {
            _desktopFillOpen = false;
        }
    }

    /// <summary>New fences first (placed in free space like "New fence"), then the items, one save; then the hide option.</summary>
    private void ApplyDesktopFill(DesktopFillPlan plan)
    {
        var added = 0;
        var newFences = 0;
        foreach (var group in plan.Groups)
        {
            var fenceId = group.FenceId;
            if (fenceId is null || _config.Fences.All(fence => fence.Id != fenceId))
            {
                (_config, var fence) = FenceEdits.CreateFence(_config, group.NewFenceTitle ?? "New fence");
                fenceId = fence.Id;
                newFences++;
            }
            var result = ItemEdits.Add(_items, fenceId, [.. group.ItemRefs.Select(VirtualItem.Create)]);
            _items = result.Document;
            added += result.AddedIds.Count;
        }
        Log.Information("Add from desktop: {Added} item(s) added, {NewFences} new fence(s)", added, newFences);
        var beforeWelcome = _config;
        _config = WelcomeEdits.AfterDesktopFill(_config, _items, added, newFences); // M30: the empty welcome fence was only the welcome
        if (!ReferenceEquals(beforeWelcome, _config))
        {
            Log.Information("welcome fence removed: Add from desktop made its fences");
            RefreshSettings(); // M31 (M30 review M3): "New games go to" no longer offers it
        }
        SyncBoxes();
        ItemsChanged(checkTargets: [.. plan.Groups.SelectMany(group => group.ItemRefs)]);
        SaveNow();
        if (plan.HideIcons) SetHideDesktopIcons(true);
    }

    /// <summary>The welcome fence got its first item (M30): from now on an ordinary fence; its window drops the welcome.</summary>
    private void EndWelcomeIfFilled()
    {
        var before = _config;
        _config = WelcomeEdits.ClearIfFilled(_config, _items);
        if (ReferenceEquals(before, _config)) return;
        Log.Information("welcome ended: the welcome fence got its first item");
        var ended = before.Fences.Where(fence => fence.Welcome).Select(fence => fence.Id).ToHashSet();
        foreach (var window in _windows.Values) // M31 (M30 review M6): only the window that showed it
            if (ended.Contains(window.FenceId) && _config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is { } shown) window.Refresh(shown);
    }
}
