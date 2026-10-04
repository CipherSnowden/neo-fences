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
        var fences = _config.Fences.Where(fence => !fence.IsLibrary).Select(fence => (fence.Id, fence.Title)).ToList();
        var titles = fences.ToDictionary(fence => fence.Id, fence => fence.Title, StringComparer.Ordinal);
        // Each target already held by an item → a fence holding it (shown as "already in …", unticked).
        var alreadyIn = new Dictionary<string, string>(ItemKinds.Comparer);
        foreach (var (fenceId, items) in _items.Fences)
        {
            if (!titles.TryGetValue(fenceId, out var title)) continue;
            foreach (var item in items) alreadyIn.TryAdd(item.Target, title);
        }
        var dialog = new DesktopFillWindow(fences, alreadyIn, _config.Library, [.. _library.Items.Select(item => item.Game)], _iconLoader, iconsHidden: _config.Settings.HideDesktopIcons);
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
        SyncBoxes();
        ItemsChanged(checkTargets: [.. plan.Groups.SelectMany(group => group.ItemRefs)]);
        SaveNow();
        if (plan.HideIcons) SetHideDesktopIcons(true);
    }
}
