using NeoFences.Core.Layouts;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Layouts;

public class RollUpExpansionTests
{
    private static int TicksUntilChange(RollUpExpansion expansion, bool pointerInside, int limit = 20)
    {
        for (var tick = 1; tick <= limit; tick++)
        {
            if (expansion.Tick(pointerInside)) return tick;
        }
        return -1;
    }

    [Fact]
    public void Hover_OpensAfterResting_ClosesAfterLeaving()
    {
        var expansion = new RollUpExpansion(RollupExpand.Hover);
        Assert.Equal(RollUpExpansion.OpenTicks, TicksUntilChange(expansion, pointerInside: true));
        Assert.True(expansion.Expanded);
        Assert.Equal(RollUpExpansion.CloseTicks, TicksUntilChange(expansion, pointerInside: false));
        Assert.False(expansion.Expanded);
    }

    [Fact]
    public void Hover_ABriefSlip_DoesNotClose()
    {
        var expansion = new RollUpExpansion(RollupExpand.Hover);
        TicksUntilChange(expansion, pointerInside: true);
        for (var tick = 0; tick < RollUpExpansion.CloseTicks - 1; tick++) Assert.False(expansion.Tick(pointerInside: false));
        Assert.False(expansion.Tick(pointerInside: true)); // back in time: the count starts over
        for (var tick = 0; tick < RollUpExpansion.CloseTicks - 1; tick++) Assert.False(expansion.Tick(pointerInside: false));
        Assert.True(expansion.Expanded);
    }

    [Fact]
    public void Hover_AClickDoesNothing()
    {
        var expansion = new RollUpExpansion(RollupExpand.Hover);
        Assert.False(expansion.Click());
        Assert.False(expansion.Expanded);
    }

    [Fact]
    public void Click_RestingNeverOpens()
    {
        var expansion = new RollUpExpansion(RollupExpand.Click);
        Assert.Equal(-1, TicksUntilChange(expansion, pointerInside: true));
        Assert.False(expansion.Expanded);
    }

    [Fact]
    public void Click_OpensAtOnce_ClosesAfterLeaving()
    {
        var expansion = new RollUpExpansion(RollupExpand.Click);
        Assert.True(expansion.Click());
        Assert.True(expansion.Expanded);
        Assert.False(expansion.Click()); // already open
        Assert.Equal(-1, TicksUntilChange(expansion, pointerInside: true, limit: 10)); // stays open while inside
        Assert.Equal(RollUpExpansion.CloseTicks, TicksUntilChange(expansion, pointerInside: false));
        Assert.False(expansion.Expanded);
    }

    [Fact]
    public void Reset_Closes()
    {
        var expansion = new RollUpExpansion(RollupExpand.Click);
        expansion.Click();
        expansion.Reset();
        Assert.False(expansion.Expanded);
    }

    [Fact]
    public void ModeChange_ToClick_WhileClosed_StopsHoverOpening()
    {
        var expansion = new RollUpExpansion(RollupExpand.Hover) { Mode = RollupExpand.Click };
        Assert.Equal(-1, TicksUntilChange(expansion, pointerInside: true));
    }

    [Theory] // M38 review I4: Peek's keyboard opens a rolled-up fence in either mode; it closes as usual afterwards
    [InlineData(RollupExpand.Hover)]
    [InlineData(RollupExpand.Click)]
    public void Open_OpensInEitherMode_AndClosesOnceThePointerStaysAway(RollupExpand mode)
    {
        var expansion = new RollUpExpansion(mode);
        Assert.True(expansion.Open());
        Assert.True(expansion.Expanded);
        Assert.False(expansion.Open()); // already open
        Assert.Equal(RollUpExpansion.CloseTicks, TicksUntilChange(expansion, pointerInside: false));
        Assert.False(expansion.Expanded);
    }
}
