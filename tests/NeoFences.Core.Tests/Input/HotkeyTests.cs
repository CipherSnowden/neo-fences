using NeoFences.Core.Input;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Input;

public class HotkeyTests
{
    [Fact]
    public void Default_ParsesToCtrlAltSpace()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Alt+Space", out var hotkey));
        Assert.Equal(new Hotkey(Ctrl: true, Alt: true, Shift: false, Win: false, Key: "Space"), hotkey);
    }

    [Fact]
    public void SpacesAndCase_AreIgnored()
    {
        Assert.True(Hotkey.TryParse(" ctrl + SHIFT + p ", out var hotkey));
        Assert.Equal(new Hotkey(Ctrl: true, Alt: false, Shift: true, Win: false, Key: "P"), hotkey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]        // no key
    [InlineData("Space")]           // no modifier: would steal a normal key
    [InlineData("Ctrl+Alt+P+Q")]    // two keys
    [InlineData("Ctrl++Space")]
    [InlineData("Ctrl+Alt+999")]    // a number is not a key name (it would parse as a raw key code)
    public void Invalid_IsRejected(string text) => Assert.False(Hotkey.TryParse(text, out _));

    [Theory]
    [InlineData("Ctrl+Alt+1", "D1")] // the top-row digit (WPF Key.D1), not key code 1 (M5 review M4)
    [InlineData("Ctrl+Alt+D1", "D1")]
    [InlineData("Ctrl+Shift+F12", "F12")]
    public void KeyNames_AreWpfKeyNames(string text, string key)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(key, hotkey.Key);
    }

    // A global hotkey swallows its combination in every app, so the recorder only accepts safe ones (M6b review I1).
    [Theory]
    [InlineData("Shift+Tab")]       // backward tabbing everywhere
    [InlineData("Alt+F4")]          // closing windows
    [InlineData("Ctrl+C")]          // copy
    [InlineData("Shift+A")]         // capital letters
    [InlineData("Ctrl+Alt+A")]      // AltGr characters on many layouts (ą, á…)
    [InlineData("Ctrl+Alt+D2")]     // AltGr digits (@, ²…)
    [InlineData("Alt+Tab")]
    [InlineData("Alt+Shift+Tab")]
    [InlineData("Alt+Escape")]
    [InlineData("Alt+Space")]       // the window menu
    [InlineData("Ctrl+Escape")]     // Start
    public void UnsafeCombinations_AreRefusedForRecording(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey)); // still valid in config.json (lenient)
        Assert.False(hotkey.IsSafeToRecord);
    }

    [Theory]
    [InlineData("Ctrl+Alt+Space")]  // the default
    [InlineData("Ctrl+Shift+F9")]   // F-keys are free with any modifier
    [InlineData("Win+Shift+P")]
    [InlineData("Alt+Shift+P")]
    [InlineData("Ctrl+Alt+Shift+P")]
    public void SafeCombinations_AreAccepted(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.True(hotkey.IsSafeToRecord);
    }

    [Theory]
    [InlineData("Ctrl+Alt+Space", "Ctrl+Alt+Space")]
    [InlineData("Ctrl+Alt+1", "Ctrl+Alt+1")]           // stored as D1 (M8b: shown as the key cap)
    [InlineData("Ctrl+Shift+OemPlus", "Ctrl+Shift+=")]
    [InlineData("Alt+Shift+Oem3", "Alt+Shift+`")]
    [InlineData("Ctrl+Alt+NumPad5", "Ctrl+Alt+Num 5")]
    [InlineData("Win+Shift+OemQuestion", "Shift+Win+/")] // canonical modifier order, as stored
    public void DisplayText_ShowsKeyCaps(string stored, string shown)
    {
        Assert.True(Hotkey.TryParse(stored, out var hotkey));
        Assert.Equal(shown, hotkey.DisplayText);
    }

    [Fact]
    public void ToString_RoundTrips() =>
        Assert.Equal("Ctrl+Alt+Space", new Hotkey(Ctrl: true, Alt: true, Shift: false, Win: false, Key: "Space").ToString());
}

public class RollUpTests
{
    [Fact]
    public void SetRolledUp_IsStored_AndUnrollRestores()
    {
        var games = Fence.Create("Games");
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox"), games] };

        var rolled = FenceEdits.SetRolledUp(config, games.Id, rolledUp: true);

        Assert.True(rolled.Fences.Single(fence => fence.Id == games.Id).RolledUp);
        Assert.False(FenceEdits.SetRolledUp(rolled, games.Id, rolledUp: false).Fences.Single(fence => fence.Id == games.Id).RolledUp);
    }
}
