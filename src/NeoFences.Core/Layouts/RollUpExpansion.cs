using NeoFences.Core.Model;

namespace NeoFences.Core.Layouts;

/// <summary>
/// Whether a rolled-up fence is open right now (M5 hover, M6b click; user choice 2026-10-03: both offered, hover
/// default). Driven by a 100 ms pointer poll and by single clicks on the title. Either way it closes about half a
/// second after the pointer leaves, so a brief slip does not close it.
/// </summary>
public sealed class RollUpExpansion(RollupExpand mode)
{
    public const int OpenTicks = 3;  // ~300 ms resting on the fence (hover mode)
    public const int CloseTicks = 5; // ~500 ms away

    private int _ticks;

    public RollupExpand Mode { get; set; } = mode;

    public bool Expanded { get; private set; }

    /// <summary>One poll with the pointer inside or outside the fence.</summary>
    /// <returns>True when <see cref="Expanded"/> changed.</returns>
    public bool Tick(bool pointerInside)
    {
        if (!Expanded && Mode == RollupExpand.Click)
        {
            _ticks = 0; // click mode never opens by resting
            return false;
        }
        _ticks = pointerInside == Expanded ? 0 : _ticks + 1;
        if (_ticks < (Expanded ? CloseTicks : OpenTicks)) return false;
        _ticks = 0;
        Expanded = !Expanded;
        return true;
    }

    /// <summary>A single click on the rolled-up title.</summary>
    /// <returns>True when it opened the fence (click mode only; hover mode opens by resting).</returns>
    public bool Click()
    {
        if (Expanded || Mode != RollupExpand.Click) return false;
        Expanded = true;
        _ticks = 0;
        return true;
    }

    /// <summary>Peek gave the fence the keyboard (M38): open in either mode; it closes as usual once the pointer stays away.</summary>
    /// <returns>True when it opened the fence.</returns>
    public bool Open()
    {
        if (Expanded) return false;
        Expanded = true;
        _ticks = 0;
        return true;
    }

    /// <summary>Rolled up or unrolled by a double-click: start closed.</summary>
    public void Reset()
    {
        Expanded = false;
        _ticks = 0;
    }
}
