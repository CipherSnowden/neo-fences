using System.Windows.Controls;
using NeoFences.Core.Items;
using NeoFences.Core.Library;
using NeoFences.Core.Model;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Element sizes and the fence grid (M24, spec 2026-10-05-element-sizes-design, ADR-046): Size ▸ on items, Layout ▸ Flow /
/// Free per fence, and where dropped or sorted elements land in Free fences. Only NeoFences' own records change.
/// </summary>
public sealed partial class FenceHost
{
    /// <summary>The Size ▸ entry for these items (an items fence only).</summary>
    private MenuItem SizeMenu(ContextMenu menu, IReadOnlyList<VirtualItem> items)
    {
        var size = SizePicker.Create(menu, FenceGrid.CommonSize([.. items.Select(item => item.Size)]), size => SetSize([.. items.Select(item => item.Id)], size)); // M28: mixed sizes check nothing
        MenuGlyph.SetGlyph(size, MenuGlyph.Size); // M35
        return size;
    }

    private void SetSize(IReadOnlyList<string> itemIds, GridSpan? size)
    {
        _items = ItemEdits.SetSize(_items, itemIds, size);
        Log.Information("{Count} item(s) sized {Size}", itemIds.Count, size is { } span ? $"{span.Columns}x{span.Rows}" : "default");
        ItemsChanged(checkTargets: []);
    }

    /// <summary>
    /// Layout ▸ (spec §2): to Free, every element first stores the cell it shows at now, so nothing moves; to Flow, the
    /// order of the list stays (stored cells stay, unused).
    /// </summary>
    private void SetFenceLayout(FenceWindow window, FenceLayout layout)
    {
        if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { Kind: FenceKind.Items } fence || fence.Layout == layout) return;
        if (layout == FenceLayout.Free) _items = ItemEdits.Place(_items, window.CurrentCells());
        _config = FenceEdits.SetLayout(_config, fence.Id, layout);
        Log.Information("fence {FenceId} layout: {Layout}", fence.Id, layout);
        window.Refresh(_config.Fences.First(candidate => candidate.Id == fence.Id));
        ItemsChanged(checkTargets: []);
    }

    private bool IsFree(string fenceId) => _config.Fences.FirstOrDefault(fence => fence.Id == fenceId)?.Layout == FenceLayout.Free;

    /// <summary>
    /// Items that just arrived in a Free fence (a drag, a drop from outside, Ctrl+drag) land on the drop cell, the others of
    /// the drag keeping their offsets (their cells where they came from); a taken spot → the nearest free one.
    /// </summary>
    /// <param name="anchorIndex">The dropped element that was under the pointer (M28): it lands on the drop cell.</param>
    private void PlaceDropped(FenceWindow window, IReadOnlyList<string> itemIds, IReadOnlyList<string?> sourceKeys, int anchorIndex = 0)
    {
        if (!IsFree(window.FenceId) || window.LastDropCell is not { } dropCell || itemIds.Count == 0) return;
        var arriving = itemIds.ToHashSet(StringComparer.Ordinal);
        // Only the arriving items are taken out: a Ctrl+drag's originals stay where they are (final review I1).
        var others = window.CurrentLayout().Where(entry => !arriving.Contains(entry.Key))
            .Select(entry => (entry.Cell, entry.Span)).ToList();
        var shownCells = _windows.Values.SelectMany(shown => shown.CurrentCells()).GroupBy(entry => entry.Key).ToDictionary(group => group.Key, group => group.First().Value);
        var dropped = itemIds.Select((id, index) => (Span: _items.Find(id) is { } item ? FenceGrid.SpanOf(item) : GridSpan.One,
            From: sourceKeys.ElementAtOrDefault(index) is { } key && shownCells.TryGetValue(key, out var from) ? from : (GridCell?)null)).ToList();
        var cells = FenceGrid.PlaceDropped(others, dropped, dropCell, window.Columns, anchorIndex);
        _items = ItemEdits.Place(_items, itemIds.Select((id, index) => (id, cells[index])).ToDictionary(pair => pair.id, pair => pair.Item2, StringComparer.Ordinal));
        window.ForgetDropCell(); // a later addition (Add item…) never lands on an old drop cell
    }

    /// <summary>
    /// A Free fence's elements without a stored cell are stored where they show once its window has laid out (M32): not only
    /// at the next item change (a fence written with a Free layout and no cells kept them unsaved until then).
    /// </summary>
    private void PinAfterLayout()
    {
        var before = _items;
        PinFreeCells();
        if (ReferenceEquals(before, _items)) return;
        Log.Information("free layout: stored the cells shown at first layout");
        RefreshWindows();
        ScheduleSave();
    }

    /// <summary>
    /// Elements of a Free fence that have no stored cell yet (added by Add item…, Add games…, new games) store the free spot
    /// they show at, so a later addition never moves them.
    /// </summary>
    private void PinFreeCells()
    {
        var cells = new Dictionary<string, GridCell>(StringComparer.Ordinal);
        foreach (var fence in _config.Fences.Where(fence => fence.Kind == FenceKind.Items && fence.Layout == FenceLayout.Free))
        {
            var items = _items.Of(fence.Id);
            if (items.All(item => item.Cell is not null)) continue;
            // The shown tab: its laid-out columns; a hidden tab: its own columns at its box's width (M28), not the shown tab's.
            var window = _windows.Values.FirstOrDefault(candidate => candidate.FenceId == fence.Id);
            var columns = window is not null ? (window.IsLoaded ? window.Columns : 0) // not laid out yet: pinned once it is (final review M4)
                : FenceTabs.HostOf(_config, fence.Id) is { } host && _windows.TryGetValue(host.Id, out var box) ? box.ColumnsFor(fence, covers: items.Any(GameItems.ShowsCover))
                : 0;
            if (columns == 0) continue; // no window for it (yet): pinned when it shows
            var arrangement = FenceGrid.Arrange([.. items.Select(item => new GridElement(FenceGrid.SpanOf(item), item.Cell))], columns, FenceLayout.Free);
            for (var index = 0; index < items.Count; index++)
            {
                if (items[index].Cell is null) cells[items[index].Id] = arrangement.Cells[index];
            }
        }
        if (cells.Count > 0) _items = ItemEdits.Place(_items, cells);
    }

    /// <summary>
    /// Sort by in a Free fence: packed from the top-left in the new order, and those cells stored (spec §2) — the fence the
    /// sort was for, while the window still shows it (a tab switch during the sort leaves the new tab alone; final review I5).
    /// </summary>
    private void PackFreeFence(FenceWindow window, string fenceId)
    {
        if (!IsFree(fenceId) || window.FenceId != fenceId) return;
        var items = _items.Of(fenceId);
        var arrangement = FenceGrid.Arrange([.. items.Select(item => new GridElement(FenceGrid.SpanOf(item), null))], window.Columns, FenceLayout.Flow);
        _items = ItemEdits.Place(_items, items.Select((item, index) => (item.Id, arrangement.Cells[index])).ToDictionary(pair => pair.Id, pair => pair.Item2, StringComparer.Ordinal));
    }
}
