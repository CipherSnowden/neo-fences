using System.Windows.Controls;
using NeoFences.Core.Items;
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
    private MenuItem SizeMenu(ContextMenu menu, IReadOnlyList<VirtualItem> items) =>
        SizePicker.Create(menu, items.Count == 1 ? items[0].Size : null, size => SetSize([.. items.Select(item => item.Id)], size));

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
    private void PlaceDropped(FenceWindow window, IReadOnlyList<string> itemIds, IReadOnlyList<string?> sourceKeys)
    {
        if (!IsFree(window.FenceId) || window.LastDropCell is not { } dropCell || itemIds.Count == 0) return;
        var arriving = itemIds.ToHashSet(StringComparer.Ordinal);
        // Only the arriving items are taken out: a Ctrl+drag's originals stay where they are (final review I1).
        var others = window.CurrentLayout().Where(entry => !arriving.Contains(entry.Key))
            .Select(entry => (entry.Cell, entry.Span)).ToList();
        var shownCells = _windows.Values.SelectMany(shown => shown.CurrentCells()).GroupBy(entry => entry.Key).ToDictionary(group => group.Key, group => group.First().Value);
        var dropped = itemIds.Select((id, index) => (Span: _items.Find(id) is { } item ? FenceGrid.SpanOf(item) : GridSpan.One,
            From: sourceKeys.ElementAtOrDefault(index) is { } key && shownCells.TryGetValue(key, out var from) ? from : (GridCell?)null)).ToList();
        var cells = FenceGrid.PlaceDropped(others, dropped, dropCell, window.Columns);
        _items = ItemEdits.Place(_items, itemIds.Select((id, index) => (id, cells[index])).ToDictionary(pair => pair.id, pair => pair.Item2, StringComparer.Ordinal));
        window.ForgetDropCell(); // a later addition (Add item…) never lands on an old drop cell
    }

    /// <summary>
    /// Elements of a Free fence that have no stored cell yet (added by Add item…, Add games…, new games) store the free spot
    /// they show at, so a later addition never moves them.
    /// </summary>
    private void PinFreeCells()
    {
        var cells = new Dictionary<string, GridCell>(StringComparer.Ordinal);
        foreach (var window in _windows.Values.Where(window => IsFree(window.FenceId)))
        {
            var items = _items.Of(window.FenceId);
            if (items.All(item => item.Cell is not null)) continue;
            var arrangement = FenceGrid.Arrange([.. items.Select(item => new GridElement(FenceGrid.SpanOf(item), item.Cell))], window.Columns, FenceLayout.Free);
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
