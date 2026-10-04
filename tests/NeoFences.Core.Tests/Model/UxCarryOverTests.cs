using NeoFences.Core.Config;
using NeoFences.Core.Input;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

/// <summary>M13c (v1.6.2): fence and Settings carry-overs from the M9 and M10 reviews that Core can pin.</summary>
public class UxCarryOverTests
{
    private static readonly DateTimeOffset Taken = new(2026, 10, 4, 22, 45, 0, TimeSpan.FromHours(5.5));

    // ---------- tray snapshot labels (M10 carry-over) ----------

    [Theory]
    [InlineData("Before the stream", "Before the stream")]
    [InlineData("Games & Work", "Games && Work")]                 // & is a menu accelerator
    [InlineData("Line one\nline two", "Line one line two")]        // a hand-edited newline
    [InlineData("Name\twith tab", "Name with tab")]                 // a tab would right-align the rest
    [InlineData("   ", "Snapshot 4 Oct 22:45")]                   // blank: the date and time
    public void MenuLabel_IsOneSafeLine(string name, string expected) =>
        Assert.Equal(expected, Snapshots.MenuLabel(new SnapshotEntry(@"C:\s\a.json", name, Taken), TimeZoneInfo.FindSystemTimeZoneById("India Standard Time")));

    [Fact]
    public void MenuLabel_LongNamesAreCut()
    {
        var label = Snapshots.MenuLabel(new SnapshotEntry(@"C:\s\a.json", new string('x', 300), Taken), TimeZoneInfo.Utc);
        Assert.Equal(61, label.Length);
        Assert.EndsWith("…", label);
    }

    // ---------- tabs: nothing changed means the same config (M9 carry-over) ----------

    [Fact]
    public void Tabs_ReorderToTheSamePlace_AndTheSameColour_ChangeNothing()
    {
        var first = Fence.Create("A");
        var second = Fence.Create("B");
        var merged = FenceTabs.Merge(new NeoFencesConfig { Fences = [Fence.Create("Inbox"), first, second] }, second.Id, first.Id, wholeBox: false);

        Assert.Same(merged, FenceTabs.Reorder(merged, second.Id, 1));
        var coloured = FenceTabs.SetColor(merged, second.Id, TabColor.Blue);
        Assert.Same(coloured, FenceTabs.SetColor(coloured, second.Id, TabColor.Blue));
        Assert.NotSame(coloured, FenceTabs.Reorder(coloured, second.Id, 0));
    }

    // ---------- hotkey labels follow the keyboard layout (M8b carry-over) ----------

    [Fact]
    public void HotkeyLabels_UseTheLayoutsCharacter_WhenTheLayoutKnowsIt()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Shift+OemPlus", out var hotkey));
        Assert.Equal("Ctrl+Shift++", hotkey.DisplayTextWith(key => key.Equals("OemPlus", StringComparison.OrdinalIgnoreCase) ? "+" : null)); // a German layout: OemPlus is its "+" key
        Assert.Equal("Ctrl+Shift+=", hotkey.DisplayTextWith(_ => null));                                  // unknown: the US name
        Assert.Equal(hotkey.DisplayText, hotkey.DisplayTextWith(_ => null));
    }
}
