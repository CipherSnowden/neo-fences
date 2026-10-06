using NeoFences.Core.Layouts;

namespace NeoFences.Core.Tests.Layouts;

/// <summary>M38 (spec 2026-10-06-keyboard-and-older-windows-design §1): which fence the keyboard goes to.</summary>
public class KeyboardOrderTests
{
    private static FenceSpot Spot(string id, double x, double y, double w = 300, double h = 200, bool primary = true, double monitorLeft = 0) =>
        new(id, x, y, w, h, primary, monitorLeft);

    [Fact]
    public void ReadingOrder_IsLeftToRightThenTopToBottom_FencesOfARowTogetherEvenIfNotLevel()
    {
        var order = KeyboardOrder.ReadingOrder(
        [
            Spot("bottom-left", 20, 600),
            Spot("top-right", 700, 30),
            Spot("top-left", 20, 24),
            Spot("top-middle", 360, 60, h: 300), // a little lower, still the first row
        ]);
        Assert.Equal(["top-left", "top-middle", "top-right", "bottom-left"], order);
    }

    [Fact]
    public void ReadingOrder_TakesTheMainScreenFirst_ThenTheOthersFromLeftToRight()
    {
        var order = KeyboardOrder.ReadingOrder(
        [
            Spot("right-screen", 1940, 20, primary: false, monitorLeft: 1920),
            Spot("left-screen", -1900, 20, primary: false, monitorLeft: -1920),
            Spot("main", 20, 20),
        ]);
        Assert.Equal(["main", "left-screen", "right-screen"], order);
    }

    [Fact]
    public void ReadingOrder_OfNothing_IsEmpty() => Assert.Empty(KeyboardOrder.ReadingOrder([]));

    [Fact]
    public void Start_IsTheFenceUnderTheMouse_ElseTheLastUsed_ElseTheFirst()
    {
        string[] order = ["a", "b", "c"];
        Assert.Equal("c", KeyboardOrder.Start(order, underMouse: "c", lastUsed: "b"));
        Assert.Equal("b", KeyboardOrder.Start(order, underMouse: null, lastUsed: "b"));
        Assert.Equal("a", KeyboardOrder.Start(order, underMouse: null, lastUsed: "gone")); // deleted since
        Assert.Equal("a", KeyboardOrder.Start(order, underMouse: "not-a-fence", lastUsed: null));
        Assert.Null(KeyboardOrder.Start([], underMouse: null, lastUsed: null));
    }

    [Fact]
    public void Next_GoesForwardAndBack_AndWrapsAtTheEnds()
    {
        string[] order = ["a", "b", "c"];
        Assert.Equal("b", KeyboardOrder.Next(order, current: "a", step: 1));
        Assert.Equal("a", KeyboardOrder.Next(order, current: "c", step: 1));
        Assert.Equal("c", KeyboardOrder.Next(order, current: "a", step: -1));
        Assert.Equal("a", KeyboardOrder.Next(order, current: "gone", step: 1)); // a fence that went: the first
        Assert.Equal("a", KeyboardOrder.Next(["a"], current: "a", step: 1));
        Assert.Null(KeyboardOrder.Next([], current: "a", step: 1));
    }
}
