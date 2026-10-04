# M6b — Settings Window and Animations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- A Fluent settings window (General · Fences · Game mode · About and logs), opened from the tray and the fence menu, with a Peek hotkey recorder.
- Rolled-up fences open on hover or click.
- Roll-up animates over 200 ms and quick-hide fades over 150 ms, both off when Windows animations are off or a game runs.

**Architecture:**
- `NeoFences.Core`:
  - `RollUpExpansion` (hover/click open/close state);
  - `Hotkey` digit fix;
  - `RunState.PeekHotkeyWanted`.
- `NeoFences.App`:
  - `SettingsWindow` (XAML, Fluent `ThemeMode="System"`);
  - `FenceWindow` (expansion model, click-to-open, height animation, fade, Settings… item);
  - `FenceHost` (settings wiring, Peek hotkey lifecycle and change, roll-up mode, game-mode toggle, fades).

No Shell changes.

**Tech Stack:** .NET 10, WPF (built-in Fluent theme), CsWin32 0.3.335, Serilog, xUnit. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-02-neofences-v1-design.md` §6 (settings window, animations), §4.7 (no animations in game mode), §5 (`peekHotkey`, `gameMode`, `rollupExpand`). **Decisions:**
- ADR-015, ADR-020, ADR-021;
- **ADR-022** (new, Task 4).

User choices on 2026-10-03:
- roll-up offers hover (default) and click;
- no Appearance section in v1 (tint and opacity come with v2 themes).

**Pre-verified (2026-10-03):** every code block below was compiled together (0 warnings, 0 errors), and **223/223 tests pass**. The Task 2 smoke passed on the real desktop with the user's consent. It covered:
- tray → Settings… opens the window (Fluent dark with the accent colour, screenshot);
- recording Ctrl+Shift+F9: saved, registered, and it opens Peek; then back to Ctrl+Alt+Space;
- game mode off/on, saved;
- roll-up click mode, saved; on "Games": the roll-up animated (223 → 34 mid-way → 32 px), resting did not open it, a click opened it, leaving closed it, unroll;
- quick-hide with the fade, then back.

**Found in the prototype:** UI Automation's Toggle (Narrator) changes a CheckBox without raising Click, so the settings checkboxes listen to Checked/Unchecked.

## Global Constraints

- **Hard rule 2:** "Hide desktop icons" in Settings goes through the same `SetTakeover` path as the fence menu (marker, RunState).
- **Hard rules 4 and 7:** no Win32 in the App (the Fluent theme, `SystemParameters` and `KeyInterop` are WPF). A rejected hotkey keeps the old one and says why; it never throws.
- Settings apply and save at once (no OK button). Pause and game mode stay runtime only.
- Commits: single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- Smokes take the mouse. Ask first, print "TEST RUNNING" / "TEST COMPLETE", never type into Windows Terminal, and refocus it at the end. Run the smoke with Windows PowerShell (`powershell -File`).

## Review Focus

1. **Windows "Animation effects" off, or a slow machine.** Expected: no animation, and never a fence stuck half-rolled or invisible (opacity 0). By hand P9.
2. **Fast toggling** (quick-hide twice within the 150 ms fade; double-click roll-up while a hover animation runs; Explorer restart mid-fade). Expected: the final state always matches the last command. By hand P8.
3. **Hotkey recorder edge cases** (AltGr, Win-key combinations Windows owns, the current hotkey pressed in the box, a plain key). Expected: a clear message, and the old hotkey keeps working. By hand P5.
4. **Settings open while the state changes elsewhere** (fence menu toggles, game mode starting, Pause, exit). Expected: the window shows the current state, and closing it never affects the app. By hand P3, P10.
5. **Click mode with a locked fence, a Portal, a fence on another monitor's DPI.** Expected: the click opens it; dragging still works on the next press. By hand P6, P7.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Layouts/RollUpExpansion.cs`, `Input/Hotkey.cs`, `Lifecycle/RunState.cs` | hover/click expansion, digit keys, Peek hotkey rule | 1 |
| `src/NeoFences.App/SettingsWindow.xaml(.cs)`, `FenceWindow.xaml(.cs)`, `FenceHost.cs`, `NeoFences.App.csproj` | settings, click mode, animations, wiring, version | 2 |
| `docs/TEST-CHECKLIST.md` (section P), `docs/research/m6b-settings-animations.md` | verification | 3 |
| `docs/DECISIONS.md` (ADR-022), `ARCHITECTURE.md`, `FEATURES.md`, `ROADMAP.md`, `SESSION-LOG.md`, hub | docs sync | 4 |

---

### Task 1: Core — roll-up expansion, hotkey digits, Peek hotkey rule

**Files:**
- Modify: `docs/ROADMAP.md` (claim M6b)
- Create: `src/NeoFences.Core/Layouts/RollUpExpansion.cs`, `tests/NeoFences.Core.Tests/Layouts/RollUpExpansionTests.cs`
- Modify (full new content): `src/NeoFences.Core/Input/Hotkey.cs`, `src/NeoFences.Core/Lifecycle/RunState.cs`, `tests/NeoFences.Core.Tests/Input/HotkeyTests.cs`, `tests/NeoFences.Core.Tests/Lifecycle/RunStateTests.cs`

**Interfaces:**
- Produces:
  - `RollUpExpansion(RollupExpand mode)`, with `Mode`, `Expanded`, `Tick(bool pointerInside) -> bool`, `Click() -> bool`, `Reset()`, `OpenTicks` and `CloseTicks`;
  - `RunState.PeekHotkeyWanted`;
  - `Hotkey.TryParse` maps a single digit to `D<n>` and rejects other numbers.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m6b-settings-animations
```
In `docs/ROADMAP.md`, replace `- [ ] M6b implementation plan` with `- [x] M6b implementation plan` (keep the rest of that line) and add `- [~] M6b — claimed by session 2026-10-03 m6b`.
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M6b"
```

- [ ] **Step 2: Write the failing tests**

`tests/NeoFences.Core.Tests/Layouts/RollUpExpansionTests.cs`:
```csharp
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
}
```
`tests/NeoFences.Core.Tests/Input/HotkeyTests.cs`:
```csharp
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
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, games] };

        var rolled = FenceEdits.SetRolledUp(config, games.Id, rolledUp: true);

        Assert.True(rolled.Fences.Single(fence => fence.Id == games.Id).RolledUp);
        Assert.False(FenceEdits.SetRolledUp(rolled, games.Id, rolledUp: false).Fences.Single(fence => fence.Id == games.Id).RolledUp);
    }
}
```
`tests/NeoFences.Core.Tests/Lifecycle/RunStateTests.cs`:
```csharp
using NeoFences.Core.Lifecycle;

namespace NeoFences.Core.Tests.Lifecycle;

public class RunStateTests
{
    [Fact]
    public void Normal_WithTakeover_ShowsFences_HidesIcons_WantsTheHook()
    {
        var state = new RunState(Takeover: true, QuickHidden: false, Paused: false, GameMode: false);
        Assert.True(state.FencesVisible);
        Assert.True(state.IconsHidden);
        Assert.True(state.MouseHookWanted);
        Assert.True(state.PeekHotkeyWanted);
        Assert.False(state.ShellWorkDeferred);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void PausedOrGaming_ReleasesThePeekHotkey(bool paused, bool gameMode) =>
        // Ctrl+Alt+Space goes back to Windows and the game (M6a review M5).
        Assert.False(new RunState(Takeover: true, QuickHidden: false, Paused: paused, GameMode: gameMode).PeekHotkeyWanted);

    [Fact]
    public void QuickHidden_WithoutTakeover_HidesFencesAndIcons()
    {
        var state = new RunState(Takeover: false, QuickHidden: true, Paused: false, GameMode: false);
        Assert.False(state.FencesVisible);
        Assert.True(state.IconsHidden);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Paused_ShowsIcons_HidesFences_DropsTheHook(bool takeover, bool quickHidden)
    {
        // Pause gives the desktop back to Windows, whatever else is on (hard rule 2 never depends on Pause).
        var state = new RunState(Takeover: takeover, QuickHidden: quickHidden, Paused: true, GameMode: false);
        Assert.False(state.FencesVisible);
        Assert.False(state.IconsHidden);
        Assert.False(state.MouseHookWanted);
    }

    [Fact]
    public void GameMode_KeepsFencesAndIcons_DropsTheHook_DefersShellWork()
    {
        var state = new RunState(Takeover: true, QuickHidden: false, Paused: false, GameMode: true);
        Assert.True(state.FencesVisible); // user choice: go idle, fences stay
        Assert.True(state.IconsHidden);
        Assert.False(state.MouseHookWanted);
        Assert.True(state.ShellWorkDeferred);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS with `CS0246` for `RollUpExpansion` and `CS1061` for `PeekHotkeyWanted`.

- [ ] **Step 4: Implement**

`src/NeoFences.Core/Layouts/RollUpExpansion.cs`:
```csharp
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

    /// <summary>Rolled up or unrolled by a double-click: start closed.</summary>
    public void Reset()
    {
        Expanded = false;
        _ticks = 0;
    }
}
```
`src/NeoFences.Core/Input/Hotkey.cs`:
```csharp
namespace NeoFences.Core.Input;

/// <summary>
/// A global hotkey from settings text ("Ctrl+Alt+Space", spec §4.6 Peek). At least one modifier is required, so a
/// plain key is never stolen from games or apps. <see cref="Key"/> is a key name (WPF's <c>Key</c> names: "Space", "P").
/// </summary>
public sealed record Hotkey(bool Ctrl, bool Alt, bool Shift, bool Win, string Key)
{
    public static bool TryParse(string text, out Hotkey hotkey)
    {
        hotkey = new Hotkey(false, false, false, false, "");
        var parts = text.Split('+').Select(part => part.Trim()).ToList();
        if (parts.Count < 2 || parts.Any(part => part.Length == 0)) return false;

        bool ctrl = false, alt = false, shift = false, win = false;
        string? key = null;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "win" or "windows": win = true; break;
                default:
                    if (key is not null) return false; // two keys
                    // A digit is the top-row key (WPF "D1"); any other number would parse as a raw key code (M5 review M4).
                    if (part.All(char.IsAsciiDigit))
                    {
                        if (part.Length != 1) return false;
                        key = "D" + part;
                        break;
                    }
                    key = part.Length == 1 ? part.ToUpperInvariant() : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();
                    break;
            }
        }
        if (key is null || !(ctrl || alt || shift || win)) return false;
        hotkey = new Hotkey(ctrl, alt, shift, win, key);
        return true;
    }

    public override string ToString() =>
        string.Join("+", new[] { Ctrl ? "Ctrl" : null, Alt ? "Alt" : null, Shift ? "Shift" : null, Win ? "Win" : null, Key }.OfType<string>());
}
```
`src/NeoFences.Core/Lifecycle/RunState.cs`:
```csharp
namespace NeoFences.Core.Lifecycle;

/// <summary>
/// The run-time modes of NeoFences and what they mean together (M5 quick-hide, M6a Pause and game mode, ADR-021).
/// None of them is saved: after a restart only Takeover (a setting) applies again.
/// </summary>
public sealed record RunState(bool Takeover, bool QuickHidden, bool Paused, bool GameMode)
{
    public bool FencesVisible => !Paused && !QuickHidden;

    /// <summary>Pause gives the desktop back to Windows: its icons show even with Takeover on (spec §6).</summary>
    public bool IconsHidden => !Paused && (Takeover || QuickHidden);

    /// <summary>The only global hook goes away while paused or gaming (hard rule 3, spec §4.7).</summary>
    public bool MouseHookWanted => !Paused && !GameMode;

    /// <summary>Ctrl+Alt+Space (or the chosen Peek hotkey) is released to Windows and the game while paused or gaming.</summary>
    public bool PeekHotkeyWanted => !Paused && !GameMode;

    /// <summary>Desktop and Portal changes wait until the game is left (spec §4.7).</summary>
    public bool ShellWorkDeferred => GameMode;
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 223`

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core tests/NeoFences.Core.Tests
git commit -m "feat: added roll-up click and hover expansion, digit hotkeys and the peek hotkey rule to core"
```

---

### Task 2: App — settings window, click mode, animations

**Files:**
- Create: `src/NeoFences.App/SettingsWindow.xaml` and `SettingsWindow.xaml.cs`.
- Replace: `FenceWindow.xaml`, `FenceWindow.xaml.cs`, `FenceHost.cs` and `NeoFences.App.csproj`.

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `record SettingsView(...)`;
  - `SettingsWindow`, with `Show(SettingsView)` and `ShowHotkeyResult(bool, string)`, and the events `StartWithWindowsChanged`, `TakeoverChanged`, `PeekHotkeyChosen`, `RollupExpandChanged`, `GameModeChanged`, `OpenLogsRequested` and `OpenDataRequested`;
  - on `FenceWindow`: the `RollupExpand` constructor parameter, `AnimationsAllowed`, `SetRollupExpand`, `HideFaded`, `ShowFaded`, and the `SettingsRequested` event.

- [ ] **Step 1: Files** (stop a running NeoFences first, or its DLL locks fail the copy: `& <exe> --exit`)

`src/NeoFences.App/NeoFences.App.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <!-- Windows 10 1809+ only (spec). A versioned TFM would pull in the WinRT projections, so silence the 8.1-API check instead. -->
    <NoWarn>$(NoWarn);CA1416</NoWarn>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AssemblyName>NeoFences</AssemblyName>
    <!-- Shown in Settings → About; the packaged release sets its own (M7). -->
    <Version>0.6.0</Version>
    <!-- Resource 32512: the tray icon loads it from the exe (M6a). -->
    <ApplicationIcon>NeoFences.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Serilog" Version="4.4.0" />
    <PackageReference Include="Serilog.Sinks.File" Version="7.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\NeoFences.Core\NeoFences.Core.csproj" />
    <ProjectReference Include="..\NeoFences.Shell\NeoFences.Shell.csproj" />
  </ItemGroup>

</Project>
```
`src/NeoFences.App/SettingsWindow.xaml`:
```xml
<Window x:Class="NeoFences.App.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="NeoFences settings" Width="600" Height="680" MinWidth="460" MinHeight="400"
        WindowStartupLocation="CenterScreen" ThemeMode="System">
    <!-- Fluent (WPF's built-in theme, ThemeMode="System"): follows Windows light/dark and the accent colour (spec §6).
         Set only on this window: the fences keep their own look. Changes apply at once; there is no OK button. -->
    <Window.Resources>
        <Style x:Key="SectionHeader" TargetType="TextBlock">
            <Setter Property="FontSize" Value="14" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="Margin" Value="2,20,0,8" />
        </Style>
        <Style x:Key="Card" TargetType="Border">
            <Setter Property="Background" Value="{DynamicResource CardBackgroundFillColorDefaultBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource CardStrokeColorDefaultBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="6" />
            <Setter Property="Padding" Value="16,12" />
            <Setter Property="Margin" Value="0,0,0,4" />
        </Style>
        <Style x:Key="Description" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource TextFillColorSecondaryBrush}" />
            <Setter Property="FontSize" Value="12" />
            <Setter Property="TextWrapping" Value="Wrap" />
            <Setter Property="Margin" Value="0,2,0,0" />
        </Style>
    </Window.Resources>
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="28,16,28,28">
            <TextBlock Text="Settings" FontSize="28" FontWeight="SemiBold" />

            <TextBlock Text="General" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <CheckBox x:Name="StartupBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Start with Windows" />
                    <StackPanel>
                        <TextBlock Text="Start with Windows" />
                        <TextBlock Style="{StaticResource Description}" Text="Your fences come back by themselves after a restart or a power cut." />
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <CheckBox x:Name="TakeoverBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Hide desktop icons" />
                    <StackPanel>
                        <TextBlock Text="Hide desktop icons" />
                        <TextBlock Style="{StaticResource Description}" Text="Show your desktop items only in fences. They are back on the desktop whenever NeoFences is paused or closed." />
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <TextBox x:Name="HotkeyBox" DockPanel.Dock="Right" Width="190" IsReadOnly="True" IsReadOnlyCaretVisible="False"
                                 VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Peek hotkey" />
                        <StackPanel>
                            <TextBlock Text="Peek hotkey" />
                            <TextBlock Style="{StaticResource Description}" Text="Shows your fences above all windows. Click the box, then press the new combination (with Ctrl, Alt, Shift or Win)." />
                        </StackPanel>
                    </DockPanel>
                    <TextBlock x:Name="HotkeyStatus" Style="{StaticResource Description}" Margin="0,8,0,0" Visibility="Collapsed" />
                </StackPanel>
            </Border>

            <TextBlock Text="Fences" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <ComboBox x:Name="RollupBox" DockPanel.Dock="Right" Width="230" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Rolled-up fences open">
                        <ComboBoxItem Content="When the mouse rests on them" Tag="Hover" />
                        <ComboBoxItem Content="When you click their title" Tag="Click" />
                    </ComboBox>
                    <StackPanel>
                        <TextBlock Text="Rolled-up fences open" />
                        <TextBlock Style="{StaticResource Description}" Text="Double-click a fence's title to roll it up to its title bar." />
                    </StackPanel>
                </DockPanel>
            </Border>

            <TextBlock Text="Game mode" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <CheckBox x:Name="GameModeBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Go idle while a full-screen game runs" />
                        <StackPanel>
                            <TextBlock Text="Go idle while a full-screen game runs" />
                            <TextBlock Style="{StaticResource Description}" Text="NeoFences removes its mouse hook and waits with background work, so games get every bit of input. Fences stay where they are." />
                        </StackPanel>
                    </DockPanel>
                    <TextBlock x:Name="GameModeStatus" Style="{StaticResource Description}" Margin="0,8,0,0" />
                </StackPanel>
            </Border>

            <TextBlock Text="About and logs" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock x:Name="VersionText" />
                    <TextBlock x:Name="DataFolderText" Style="{StaticResource Description}" />
                    <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                        <Button x:Name="OpenLogsButton" Content="Open logs folder" Margin="0,0,8,0" />
                        <Button x:Name="OpenDataButton" Content="Open data folder" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </StackPanel>
    </ScrollViewer>
</Window>
```
`src/NeoFences.App/SettingsWindow.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NeoFences.Core.Model;

namespace NeoFences.App;

/// <summary>What the settings window shows; the host builds it from the config and the run state.</summary>
public sealed record SettingsView(
    bool StartWithWindows, bool Takeover, string PeekHotkey, RollupExpand RollupExpand,
    bool GameModeEnabled, bool GameModeActive, string Version, string DataFolder);

/// <summary>
/// The settings window (spec §6, M6b): General, Fences, Game mode, About and logs. Every change is reported to the host
/// at once; the host applies, saves and calls <see cref="Show(SettingsView)"/> back with the result.
/// </summary>
public partial class SettingsWindow : Window
{
    private bool _updating; // filling the controls from the host must not report changes back

    public event Action<bool>? StartWithWindowsChanged;
    public event Action<bool>? TakeoverChanged;
    /// <summary>A new Peek hotkey was pressed in the box (text like "Ctrl+Alt+P"); answer with <see cref="ShowHotkeyResult"/>.</summary>
    public event Action<string>? PeekHotkeyChosen;
    public event Action<RollupExpand>? RollupExpandChanged;
    public event Action<bool>? GameModeChanged;
    public event Action? OpenLogsRequested;
    public event Action? OpenDataRequested;

    public SettingsWindow()
    {
        InitializeComponent();
        // Checked/Unchecked, not Click: UI Automation (Narrator, Toggle) changes the box without a click (M6b smoke).
        OnToggled(StartupBox, isChecked => StartWithWindowsChanged?.Invoke(isChecked));
        OnToggled(TakeoverBox, isChecked => TakeoverChanged?.Invoke(isChecked));
        OnToggled(GameModeBox, isChecked => GameModeChanged?.Invoke(isChecked));
        RollupBox.SelectionChanged += (_, _) =>
        {
            if (!_updating && RollupBox.SelectedItem is ComboBoxItem { Tag: string mode }) RollupExpandChanged?.Invoke(Enum.Parse<RollupExpand>(mode));
        };
        HotkeyBox.PreviewKeyDown += OnHotkeyKeyDown;
        HotkeyBox.GotKeyboardFocus += (_, _) => ShowHotkeyHint("Press the new combination… (Esc keeps the current one)");
        HotkeyBox.LostKeyboardFocus += (_, _) => { if (HotkeyStatus.Tag is null) HotkeyStatus.Visibility = Visibility.Collapsed; };
        OpenLogsButton.Click += (_, _) => OpenLogsRequested?.Invoke();
        OpenDataButton.Click += (_, _) => OpenDataRequested?.Invoke();
    }

    private void OnToggled(CheckBox box, Action<bool> report)
    {
        box.Checked += (_, _) => { if (!_updating) report(true); };
        box.Unchecked += (_, _) => { if (!_updating) report(false); };
    }

    public void Show(SettingsView view)
    {
        _updating = true;
        StartupBox.IsChecked = view.StartWithWindows;
        TakeoverBox.IsChecked = view.Takeover;
        HotkeyBox.Text = view.PeekHotkey;
        RollupBox.SelectedIndex = view.RollupExpand == RollupExpand.Click ? 1 : 0;
        GameModeBox.IsChecked = view.GameModeEnabled;
        GameModeStatus.Text = !view.GameModeEnabled ? "Off: NeoFences stays fully active during games."
            : view.GameModeActive ? "Right now: idle, a full-screen app is in front." : "Right now: active (no full-screen app in front).";
        VersionText.Text = $"NeoFences {view.Version}";
        DataFolderText.Text = $"Settings, backups and logs: {view.DataFolder}";
        _updating = false;
    }

    /// <summary>The host's answer to <see cref="PeekHotkeyChosen"/>: saved, or why not (invalid, or taken by another app).</summary>
    public void ShowHotkeyResult(bool saved, string message)
    {
        HotkeyStatus.Tag = saved ? null : "error"; // an error stays visible after the box loses focus
        HotkeyStatus.Text = message;
        HotkeyStatus.Foreground = saved ? SecondaryText : System.Windows.Media.Brushes.IndianRed;
        HotkeyStatus.Visibility = Visibility.Visible;
    }

    /// <summary>Fluent's secondary text colour (the grey a missing theme resource falls back to).</summary>
    private System.Windows.Media.Brush SecondaryText => TryFindResource("TextFillColorSecondaryBrush") as System.Windows.Media.Brush ?? SystemColors.GrayTextBrush;

    private void ShowHotkeyHint(string hint)
    {
        HotkeyStatus.Tag = null;
        HotkeyStatus.Text = hint;
        HotkeyStatus.Foreground = SecondaryText;
        HotkeyStatus.Visibility = Visibility.Visible;
    }

    /// <summary>Records a combination: modifiers alone only preview; Esc leaves the box; the first other key decides.</summary>
    private void OnHotkeyKeyDown(object sender, KeyEventArgs pressed)
    {
        pressed.Handled = true;
        var key = pressed.Key == Key.System ? pressed.SystemKey : pressed.Key; // Alt combinations arrive as Key.System
        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }
        var modifiers = Keyboard.Modifiers;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            ShowHotkeyHint(string.Join("+", parts.Append("…")));
            return;
        }
        parts.Add(key.ToString());
        PeekHotkeyChosen?.Invoke(string.Join("+", parts));
    }
}
```
`src/NeoFences.App/FenceWindow.xaml`:
```xml
<Window x:Class="NeoFences.App.FenceWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="NeoFences fence" Width="320" Height="220"
        WindowStyle="None" ResizeMode="CanResize" AllowsTransparency="True"
        Background="#01000000" ShowInTaskbar="False" ShowActivated="False">
    <!-- Layered window (AllowsTransparency) + accent blur, owned by Progman: ADR-011.
         The 1/255-alpha background keeps the empty area hit-testable (fully transparent pixels click through).
         Colours are DynamicResources set by ApplyTheme (light/dark follows Windows, M2c). -->
    <WindowChrome.WindowChrome>
        <WindowChrome GlassFrameThickness="0" CaptionHeight="30" ResizeBorderThickness="6"
                      CornerRadius="0" UseAeroCaptionButtons="False" />
    </WindowChrome.WindowChrome>
    <Window.Resources>
        <sys:Double x:Key="IconSize" xmlns:sys="clr-namespace:System;assembly=System.Runtime">48</sys:Double>
        <sys:Double x:Key="ItemWidth" xmlns:sys="clr-namespace:System;assembly=System.Runtime">76</sys:Double>
        <Style x:Key="BannerButton" TargetType="Button">
            <Setter Property="Foreground" Value="{DynamicResource FenceText}" />
            <Setter Property="Padding" Value="10,3" />
            <Setter Property="Margin" Value="0,0,6,0" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="Chrome" Background="{DynamicResource FenceHover}" CornerRadius="4" Padding="{TemplateBinding Padding}">
                            <ContentPresenter />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        <!-- Thin scrollbar (spec §6): no arrows, a rounded thumb that brightens under the mouse. -->
        <Style TargetType="ScrollBar">
            <Setter Property="Width" Value="6" />
            <Setter Property="MinWidth" Value="6" />
            <Setter Property="Margin" Value="0,4,2,4" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ScrollBar">
                        <Track x:Name="PART_Track" IsDirectionReversed="True">
                            <Track.Thumb>
                                <Thumb>
                                    <Thumb.Template>
                                        <ControlTemplate TargetType="Thumb">
                                            <Border x:Name="ThumbChrome" CornerRadius="3" Background="{DynamicResource FenceScrollThumb}" />
                                            <ControlTemplate.Triggers>
                                                <Trigger Property="IsMouseOver" Value="True">
                                                    <Setter TargetName="ThumbChrome" Property="Background" Value="{DynamicResource FenceSubtleText}" />
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
    <Border CornerRadius="8" BorderBrush="{DynamicResource FenceBorder}" BorderThickness="1" Background="{DynamicResource FenceVeil}">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="30" />
                <RowDefinition Height="Auto" />
                <RowDefinition />
            </Grid.RowDefinitions>
            <DockPanel x:Name="TitleBar" Background="Transparent">
                <!-- Portal browsing (M4): back to the parent folder. Hit-testable inside the caption area. -->
                <Button x:Name="BackButton" DockPanel.Dock="Left" Visibility="Collapsed" Content="‹" ToolTip="Back (Backspace)"
                        Margin="6,3,0,3" Padding="8,0" FontSize="16" Style="{StaticResource BannerButton}"
                        WindowChrome.IsHitTestVisibleInChrome="True" />
                <TextBlock x:Name="TitleText" Foreground="{DynamicResource FenceText}" FontWeight="SemiBold" Margin="12,0"
                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
            </DockPanel>
            <!-- Rename: replaces the title while editing. Hit-testable inside the caption area. -->
            <TextBox x:Name="TitleBox" Visibility="Collapsed" Margin="8,4" Padding="3,1" FontWeight="SemiBold"
                     VerticalContentAlignment="Center" WindowChrome.IsHitTestVisibleInChrome="True"
                     Foreground="{DynamicResource FenceText}" Background="{DynamicResource FenceHover}"
                     BorderBrush="{DynamicResource FenceBorder}" CaretBrush="{DynamicResource FenceText}" />
            <!-- First run (Inbox only): ask once whether NeoFences should take over the desktop icons. -->
            <Border x:Name="TakeoverPrompt" Grid.Row="1" Visibility="Collapsed" Background="{DynamicResource FenceHover}"
                    Margin="8,0,8,6" Padding="10,8" CornerRadius="6">
                <StackPanel>
                    <TextBlock Foreground="{DynamicResource FenceText}" TextWrapping="Wrap"
                               Text="Hide the desktop icons and keep them only in fences?" />
                    <TextBlock Foreground="{DynamicResource FenceSubtleText}" FontSize="11" TextWrapping="Wrap" Margin="0,2,0,6"
                               Text="Your files stay where they are. Turn it off any time from the right-click menu." />
                    <StackPanel Orientation="Horizontal">
                        <Button x:Name="PromptHideButton" Content="Hide them" Style="{StaticResource BannerButton}" />
                        <Button x:Name="PromptLaterButton" Content="Not now" Style="{StaticResource BannerButton}" />
                    </StackPanel>
                </StackPanel>
            </Border>
            <Border x:Name="Body" Grid.Row="2" BorderBrush="{DynamicResource FenceDivider}" BorderThickness="0,1,0,0" Background="#01000000">
                <Border.ContextMenu>
                    <ContextMenu x:Name="BodyContextMenu">
                        <MenuItem x:Name="NewFenceItem" Header="New fence" />
                        <MenuItem x:Name="NewPortalItem" Header="New Portal fence…" />
                        <MenuItem x:Name="RenameItem" Header="Rename fence" />
                        <MenuItem x:Name="IconSizeItem" Header="Icon size" />
                        <MenuItem x:Name="SortItem" Header="Sort by" />
                        <MenuItem x:Name="OpenFolderItem" Header="Open folder in Explorer" Visibility="Collapsed" />
                        <MenuItem x:Name="LockItem" Header="Lock position" IsCheckable="True" />
                        <MenuItem x:Name="DeleteItem" Header="Delete fence (items go to the Inbox)" />
                        <Separator />
                        <MenuItem x:Name="TakeoverItem" Header="Hide desktop icons" IsCheckable="True" />
                        <MenuItem x:Name="StartupItem" Header="Start with Windows" IsCheckable="True" />
                        <MenuItem x:Name="SettingsItem" Header="Settings…" />
                        <Separator />
                        <MenuItem x:Name="ExitItem" Header="Exit NeoFences" />
                    </ContextMenu>
                </Border.ContextMenu>
                <Grid>
                    <ListBox x:Name="ItemList" Background="Transparent" BorderThickness="0" Padding="4"
                             SelectionMode="Extended" ScrollViewer.HorizontalScrollBarVisibility="Disabled"
                             ScrollViewer.VerticalScrollBarVisibility="Auto">
                        <ListBox.ItemsPanel>
                            <ItemsPanelTemplate>
                                <WrapPanel />
                            </ItemsPanelTemplate>
                        </ListBox.ItemsPanel>
                        <ListBox.ItemContainerStyle>
                            <Style TargetType="ListBoxItem">
                                <Setter Property="ToolTip" Value="{Binding Label}" />
                                <Setter Property="AutomationProperties.Name" Value="{Binding Label}" />
                                <Setter Property="FocusVisualStyle" Value="{x:Null}" />
                                <Setter Property="Template">
                                    <Setter.Value>
                                        <ControlTemplate TargetType="ListBoxItem">
                                            <Border x:Name="Chrome" Background="Transparent" CornerRadius="4" Margin="2" Padding="2,4">
                                                <ContentPresenter />
                                            </Border>
                                            <ControlTemplate.Triggers>
                                                <Trigger Property="IsMouseOver" Value="True">
                                                    <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceHover}" />
                                                </Trigger>
                                                <Trigger Property="IsSelected" Value="True">
                                                    <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceSelected}" />
                                                </Trigger>
                                            </ControlTemplate.Triggers>
                                        </ControlTemplate>
                                    </Setter.Value>
                                </Setter>
                            </Style>
                        </ListBox.ItemContainerStyle>
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <StackPanel Width="{DynamicResource ItemWidth}">
                                    <Image Source="{Binding Icon}" Width="{DynamicResource IconSize}" Height="{DynamicResource IconSize}"
                                           HorizontalAlignment="Center" />
                                    <Grid Margin="0,4,0,0">
                                        <TextBlock x:Name="Label" Text="{Binding Label}" Foreground="{DynamicResource FenceText}" FontSize="12"
                                                   TextAlignment="Center" TextWrapping="Wrap" TextTrimming="CharacterEllipsis"
                                                   MaxHeight="32" Effect="{DynamicResource LabelShadow}" />
                                        <!-- In-place rename (M3a): Enter renames through Windows, Esc cancels. -->
                                        <TextBox x:Name="LabelBox" Visibility="Collapsed" FontSize="12" TextAlignment="Center" TextWrapping="Wrap"
                                                 MaxHeight="48" Text="{Binding EditName, UpdateSourceTrigger=PropertyChanged}"
                                                 KeyDown="OnLabelBoxKeyDown" LostKeyboardFocus="OnLabelBoxLostFocus"
                                                 IsVisibleChanged="OnLabelBoxVisibleChanged" />
                                    </Grid>
                                </StackPanel>
                                <DataTemplate.Triggers>
                                    <DataTrigger Binding="{Binding IsEditing}" Value="True">
                                        <Setter TargetName="LabelBox" Property="Visibility" Value="Visible" />
                                        <Setter TargetName="Label" Property="Visibility" Value="Hidden" />
                                    </DataTrigger>
                                </DataTemplate.Triggers>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                    </ListBox>
                    <!-- Portal whose folder cannot be read (missing, offline, denied). -->
                    <TextBlock x:Name="PortalMessage" Visibility="Collapsed" Margin="12" TextWrapping="Wrap"
                               Foreground="{DynamicResource FenceSubtleText}" IsHitTestVisible="False" />
                    <!-- Rubber-band selection (M3b): drawn while dragging on empty space. -->
                    <Canvas IsHitTestVisible="False">
                        <Rectangle x:Name="SelectionBand" Visibility="Collapsed" Fill="{DynamicResource FenceHover}"
                                   Stroke="{DynamicResource FenceSubtleText}" StrokeThickness="1" RadiusX="2" RadiusY="2" />
                        <!-- Drag-drop feedback (M3b review I2): where a drop lands, or which folder takes it. -->
                        <Rectangle x:Name="InsertCaret" Visibility="Collapsed" Width="2" Fill="{DynamicResource FenceText}" RadiusX="1" RadiusY="1" />
                        <Rectangle x:Name="DropHighlight" Visibility="Collapsed" Fill="{DynamicResource FenceSelected}"
                                   Stroke="{DynamicResource FenceText}" StrokeThickness="1" RadiusX="4" RadiusY="4" />
                    </Canvas>
                </Grid>
            </Border>
        </Grid>
    </Border>
</Window>
```
`src/NeoFences.App/FenceWindow.xaml.cs`:
```csharp
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using NeoFences.Core.Config;
using NeoFences.Core.Layouts;
using NeoFences.Core.Model;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>One fence on the desktop. Placement and persistence are the host's job; this window reports what the user did.</summary>
public partial class FenceWindow : Window
{
    private const int WmWindowPosChanging = 0x0046;
    private const int WmSizing = 0x0214;
    private const int WmMoving = 0x0216;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int WmNcLeftButtonDoubleClick = 0x00A3;
    private const int HitTestCaption = 2;
    private const double CornerRadiusDips = 8;
    private const double CaptionHeightDips = 30;
    private const double ResizeBorderDips = 6;

    private static readonly (int Size, string Name)[] IconSizeNames = [(32, "Small"), (48, "Medium"), (64, "Large"), (96, "Extra large")];

    private readonly ObservableCollection<FenceItemView> _items = [];
    private readonly IconLoader _iconLoader;
    private IReadOnlyList<string> _itemRefs = [];
    private int _iconSizeDips;
    private bool _renaming;
    private string _title = "";
    private readonly bool _isPortal;
    private DragTracker? _drag;
    private bool _locked;
    private bool _rolledUp;
    private readonly RollUpExpansion _expansion; // rolled up, but open right now (hover or click, M5/M6b)
    private int _fullHeightPx;              // height when not rolled up (physical pixels)
    private readonly System.Windows.Threading.DispatcherTimer _hoverTimer;
    private readonly System.Windows.Threading.DispatcherTimer _heightAnimation;
    private System.Diagnostics.Stopwatch _heightClock = new();
    private int _heightFrom;
    private int _heightTo;
    private int _fadeGeneration;
    private static readonly TimeSpan RollUpDuration = TimeSpan.FromMilliseconds(200); // spec §6
    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(150)); // spec §6

    public string FenceId { get; }

    public nint Handle { get; private set; }

    /// <summary>Peek (M5): while set the fence may rise above apps instead of staying at the bottom.</summary>
    public bool Peeking { get; set; }

    /// <summary>
    /// Asked before each roll-up or fade (spec §6): off when Windows' "Animation effects" are off, and in game mode
    /// (spec §4.7). Set by the host.
    /// </summary>
    public Func<bool> AnimationsAllowed { get; set; } = () => false;

    /// <summary>Asked while the user drags an edge or the title: returns where the window should go (snapping).</summary>
    public Func<PixelRect, SnapEdges, PixelRect>? SnapRect { get; set; }

    /// <summary>Raised after the user finishes moving or resizing, with the new physical-pixel rect.</summary>
    public event Action<FenceWindow, PixelRect>? MovedByUser;

    public event Action? NewFenceRequested;
    public event Action<bool>? TakeoverToggled;
    public event Action? ExitRequested;
    public event Action<string>? OpenRequested;
    /// <summary>The first-run question was answered: true = hide the desktop icons.</summary>
    public event Action<bool>? TakeoverPromptAnswered;
    public event Action<string>? RenameRequested;
    public event Action<int>? IconSizeRequested;
    public event Action<bool>? LockToggled;
    public event Action? DeleteRequested;
    /// <summary>Right-click (or the menu key) on items: show Windows' item menu for these refs at this screen point (px).</summary>
    public event Action<IReadOnlyList<string>, int, int>? ItemMenuRequested;
    /// <summary>Del: send these items to the Recycle Bin.</summary>
    public event Action<IReadOnlyList<string>>? RecycleRequested;
    /// <summary>An in-place rename was confirmed: item ref, new name as typed.</summary>
    public event Action<string, string>? ItemRenameRequested;
    /// <summary>The user started dragging these items out of the fence (M3b).</summary>
    public event Action<IReadOnlyList<string>>? DragRequested;
    /// <summary>Portal (M4): back to the parent folder (Back button, Backspace).</summary>
    public event Action? BackRequested;
    public event Action? NewPortalRequested;
    public event Action<FenceSort>? SortRequested;
    public event Action? OpenFolderRequested;
    /// <summary>The "Start with Windows" toggle changed (ADR-019).</summary>
    public event Action<bool>? StartupToggled;
    /// <summary>"Settings…" in the fence menu (M6b).</summary>
    public event Action? SettingsRequested;
    /// <summary>Double-click on the title: roll up to the title bar, or back down (M5).</summary>
    public event Action? RollUpToggled;

    private Point? _pressPoint;                 // left button pressed on an item: a drag may start
    private ListBoxItem? _deferredSelect;       // pressed on an already selected item: select it alone only on release
    private Point? _bandStart;                  // left button pressed on empty space: rubber band

    public FenceWindow(Fence fence, bool takeoverActive, bool lightTheme, IconLoader iconLoader, RollupExpand rollupExpand)
    {
        _expansion = new RollUpExpansion(rollupExpand);
        FenceId = fence.Id;
        _iconLoader = iconLoader;
        InitializeComponent();
        _title = fence.Title;
        TitleText.Text = fence.Title;
        _isPortal = fence.Source.Kind == FenceSourceKind.Portal;
        OpenFolderItem.Visibility = _isPortal ? Visibility.Visible : Visibility.Collapsed;
        DeleteItem.Header = _isPortal ? "Delete fence (the folder is not touched)" : "Delete fence (items go to the Inbox)";
        // Desktop fences sort once (dragging keeps working); Portals keep the chosen order live, so it is checked.
        foreach (var (sort, name) in new[] { (FenceSort.Name, "Name"), (FenceSort.Type, "Type"), (FenceSort.Date, "Date (newest first)") })
        {
            var sortItem = new MenuItem { Header = name, Tag = sort, IsCheckable = _isPortal, IsChecked = _isPortal && fence.Sort == sort };
            sortItem.Click += (_, _) => SortRequested?.Invoke(sort);
            SortItem.Items.Add(sortItem);
        }
        NewPortalItem.Click += (_, _) => NewPortalRequested?.Invoke();
        OpenFolderItem.Click += (_, _) => OpenFolderRequested?.Invoke();
        BackButton.Click += (_, _) => BackRequested?.Invoke();
        TakeoverItem.IsChecked = takeoverActive;
        DeleteItem.Visibility = fence.IsInbox ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (size, name) in IconSizeNames)
        {
            var sizeItem = new MenuItem { Header = name, Tag = size, IsCheckable = true };
            sizeItem.Click += (_, _) => IconSizeRequested?.Invoke(size);
            IconSizeItem.Items.Add(sizeItem);
        }
        NewFenceItem.Click += (_, _) => NewFenceRequested?.Invoke();
        RenameItem.Click += (_, _) => BeginRename();
        LockItem.Click += (_, _) => LockToggled?.Invoke(LockItem.IsChecked);
        DeleteItem.Click += (_, _) => DeleteRequested?.Invoke();
        TakeoverItem.Click += (_, _) => TakeoverToggled?.Invoke(TakeoverItem.IsChecked);
        ExitItem.Click += (_, _) => ExitRequested?.Invoke();
        StartupItem.Click += (_, _) => StartupToggled?.Invoke(StartupItem.IsChecked);
        SettingsItem.Click += (_, _) => SettingsRequested?.Invoke();
        ItemList.ItemsSource = _items;
        ItemList.MouseDoubleClick += OnItemDoubleClick;
        ItemList.PreviewMouseLeftButtonDown += OnListPress;
        ItemList.PreviewMouseMove += OnListMove;
        ItemList.PreviewMouseLeftButtonUp += OnListRelease;
        ItemList.LostMouseCapture += (_, _) => EndBand();
        ItemList.KeyDown += OnItemListKeyDown;
        Body.ContextMenuOpening += OnBodyContextMenuOpening;
        TitleBox.KeyDown += OnTitleBoxKeyDown;
        TitleBox.LostKeyboardFocus += (_, _) => EndRename(commit: true);
        PromptHideButton.Click += (_, _) => TakeoverPromptAnswered?.Invoke(true);
        PromptLaterButton.Click += (_, _) => TakeoverPromptAnswered?.Invoke(false);
#if DEBUG
        // Checklist B11: a hung fence UI thread must not freeze the desktop or taskbar (owner input-queue attachment, ADR-011).
        var freezeItem = new MenuItem { Header = "Debug: freeze this UI thread for 10 s (B11)" };
        freezeItem.Click += (_, _) => Thread.Sleep(TimeSpan.FromSeconds(10));
        BodyContextMenu.Items.Add(freezeItem);
#endif
        ApplyTheme(lightTheme);
        SetIconSize(fence.IconSize);
        _rolledUp = fence.RolledUp;
        // Rolled up, the fence opens on hover or click (setting) and closes again shortly after the pointer leaves.
        _hoverTimer = new System.Windows.Threading.DispatcherTimer { Interval = HoverTick };
        _hoverTimer.Tick += (_, _) => OnHoverTick();
        if (_rolledUp) _hoverTimer.Start();
        _heightAnimation = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _heightAnimation.Tick += (_, _) => StepHeight();
        Closed += (_, _) =>
        {
            _hoverTimer.Stop(); // a deleted fence must not keep ticking on its dead handle (M5 review M1)
            _heightAnimation.Stop();
        };
        SetLocked(fence.Locked);
        // Unlocked, the title is caption (WM_NCLBUTTONDBLCLK); locked, it is client area.
        TitleBar.MouseLeftButtonDown += (_, click) =>
        {
            if (click.ClickCount == 2) RollUpToggled?.Invoke();
            else if (click.ClickCount == 1) ClickToOpen();
        };
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => ApplyRoundedCorners();
        // Icons are rendered for one DPI. DpiChanged is routed: every new item raises it too, so react only to the window's own.
        DpiChanged += (_, dpiChange) =>
        {
            if (dpiChange.OriginalSource == this && dpiChange.OldDpi.PixelsPerDip != dpiChange.NewDpi.PixelsPerDip) ReloadIcons();
        };
    }

    public void SetTakeoverChecked(bool active) => TakeoverItem.IsChecked = active;

    public void SetStartupChecked(bool startWithWindows) => StartupItem.IsChecked = startWithWindows;

    public void ShowTakeoverPrompt(bool visible) => TakeoverPrompt.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void SetTitle(string title)
    {
        _title = title;
        TitleText.Text = title;
    }

    /// <summary>Portal (M4): what the title shows while browsing ("Downloads › Mods"), and whether Back is offered.</summary>
    public void SetPortalLocation(string breadcrumb, bool canGoBack)
    {
        TitleText.Text = breadcrumb;
        BackButton.Visibility = canGoBack ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Portal (M4): a message instead of items (folder missing or unreadable); null hides it.</summary>
    public void ShowPortalMessage(string? message)
    {
        PortalMessage.Text = message ?? "";
        PortalMessage.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Portal (M4): the sort shown as checked.</summary>
    public void SetSortChecked(FenceSort sort)
    {
        foreach (var sortItem in SortItem.Items.OfType<MenuItem>()) sortItem.IsChecked = _isPortal && (FenceSort)sortItem.Tag == sort;
    }

    /// <summary>Shows exactly these items in this order. Items already shown keep their loaded name and icon.</summary>
    public void SetItems(IReadOnlyList<string> itemRefs)
    {
        _itemRefs = itemRefs;
        if (_items.Select(view => view.ItemRef).SequenceEqual(itemRefs, StringComparer.Ordinal)) return;
        CancelItemRenames();
        var existing = _items.ToDictionary(view => view.ItemRef, StringComparer.Ordinal);
        var iconSizePx = (int)Math.Round(_iconSizeDips * VisualTreeHelper.GetDpi(this).DpiScaleX);
        _items.Clear();
        foreach (var itemRef in itemRefs)
        {
            if (!existing.TryGetValue(itemRef, out var view))
            {
                view = new FenceItemView(itemRef);
                _iconLoader.Request(view, iconSizePx);
            }
            _items.Add(view);
        }
    }

    /// <summary>One of <see cref="ConfigNormalizer.IconSizes"/> (DIPs). Icons are reloaded at the new size.</summary>
    public void SetIconSize(int iconSizeDips)
    {
        _iconSizeDips = iconSizeDips;
        Resources["IconSize"] = (double)iconSizeDips;
        Resources["ItemWidth"] = Math.Max(76.0, iconSizeDips + 28.0); // room for two short label words under small icons
        foreach (var sizeItem in IconSizeItem.Items.OfType<MenuItem>()) sizeItem.IsChecked = (int)sizeItem.Tag == iconSizeDips;
        ReloadIcons();
    }

    /// <summary>Locked: the title no longer drags and the edges no longer resize.</summary>
    public void SetLocked(bool locked)
    {
        LockItem.IsChecked = locked;
        _locked = locked;
        ApplyChrome();
    }

    /// <summary>Locked: no drag, no resize. Rolled up: drag, no resize (its height is the stored full height).</summary>
    private void ApplyChrome()
    {
        // A fresh WindowChrome each time: editing the attached one in place is not re-applied after an unlock.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            GlassFrameThickness = new Thickness(0),
            CaptionHeight = _locked ? 0 : CaptionHeightDips,
            ResizeBorderThickness = new Thickness(_locked || _rolledUp ? 0 : ResizeBorderDips),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
    }

    /// <summary>Puts the fence at its full rect (physical pixels); rolled up, only the title bar of it shows.</summary>
    public void Place(PixelRect fullRect)
    {
        _heightAnimation.Stop();
        _fullHeightPx = fullRect.Height;
        FenceWindowChrome.SetPixelRect(Handle, _rolledUp && !_expansion.Expanded ? fullRect with { Height = RolledUpHeightPx } : fullRect);
    }

    public void SetRolledUp(bool rolledUp)
    {
        if (rolledUp == _rolledUp) return;
        var current = FenceWindowChrome.GetPixelRect(Handle);
        if (rolledUp && !_expansion.Expanded) _fullHeightPx = _heightAnimation.IsEnabled ? _heightTo : current.Height;
        _rolledUp = rolledUp;
        _expansion.Reset();
        ApplyChrome();
        AnimateHeight(rolledUp ? RolledUpHeightPx : _fullHeightPx);
        if (rolledUp) _hoverTimer.Start();
        else _hoverTimer.Stop();
    }

    /// <summary>Roll-up expand mode from settings (M6b): hover or click.</summary>
    public void SetRollupExpand(RollupExpand mode) => _expansion.Mode = mode;

    /// <summary>Quick-hide (spec §6): a 150 ms fade, then hidden. Without animations it hides at once.</summary>
    public void HideFaded()
    {
        var generation = ++_fadeGeneration;
        if (!IsVisible || !AnimationsAllowed())
        {
            BeginAnimation(OpacityProperty, null);
            Hide();
            return;
        }
        var fade = new DoubleAnimation(0, FadeDuration);
        fade.Completed += (_, _) =>
        {
            if (generation != _fadeGeneration) return; // shown again meanwhile
            Hide();
            BeginAnimation(OpacityProperty, null);
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Shows the fence (fading in when animations are on). The host sends it to the bottom afterwards.</summary>
    public void ShowFaded()
    {
        ++_fadeGeneration;
        var fadeIn = AnimationsAllowed();
        BeginAnimation(OpacityProperty, null);
        Show();
        if (fadeIn) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, FadeDuration));
    }

    /// <summary>Click mode: a single click on the rolled-up title opens it.</summary>
    private bool ClickToOpen()
    {
        if (!_rolledUp || !_expansion.Click()) return false;
        AnimateHeight(_fullHeightPx);
        return true;
    }

    /// <summary>Roll-up and roll-down move the bottom edge over 200 ms (ease-out); at once without animations.</summary>
    private void AnimateHeight(int targetPx)
    {
        var current = FenceWindowChrome.GetPixelRect(Handle);
        if (!AnimationsAllowed() || _drag is not null || current.Height == targetPx)
        {
            _heightAnimation.Stop();
            FenceWindowChrome.SetPixelRect(Handle, current with { Height = targetPx });
            return;
        }
        _heightFrom = current.Height;
        _heightTo = targetPx;
        _heightClock = System.Diagnostics.Stopwatch.StartNew();
        _heightAnimation.Start();
    }

    private void StepHeight()
    {
        var current = FenceWindowChrome.GetPixelRect(Handle);
        var progress = Math.Min(1.0, _heightClock.Elapsed / RollUpDuration);
        if (_drag is not null) progress = 1; // never fight a move: jump to the end
        var eased = 1 - Math.Pow(1 - progress, 3);
        FenceWindowChrome.SetPixelRect(Handle, current with { Height = (int)Math.Round(_heightFrom + (_heightTo - _heightFrom) * eased) });
        if (progress >= 1) _heightAnimation.Stop();
    }

    private static readonly TimeSpan HoverTick = TimeSpan.FromMilliseconds(100);

    /// <summary>Title row plus the 1-DIP border above and below it.</summary>
    private int RolledUpHeightPx => (int)Math.Round((CaptionHeightDips + 2) * VisualTreeHelper.GetDpi(this).DpiScaleY);

    // ponytail: polls the cursor every 100 ms while rolled up (no mouse-leave on a no-activate layered window when
    // the pointer leaves fast); in hover mode it also opens during a file drag, which is wanted.
    private void OnHoverTick()
    {
        if (Handle == 0 || !_rolledUp) return;
        if (_drag is not null || BodyContextMenu.IsOpen || _renaming || _items.Any(view => view.IsEditing)) return; // never close under the user
        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
        var rect = FenceWindowChrome.GetPixelRect(Handle);
        // While the height animates, judge "inside" against where the fence is going, so it does not flicker shut.
        var height = _heightAnimation.IsEnabled ? Math.Max(rect.Height, _heightTo) : rect.Height;
        var inside = cursorX >= rect.X && cursorX < rect.X + rect.Width && cursorY >= rect.Y && cursorY < rect.Y + height;
        if (!_expansion.Tick(inside)) return;
        AnimateHeight(_expansion.Expanded ? _fullHeightPx : RolledUpHeightPx);
    }

    /// <summary>Colours for Windows' light or dark app mode (M2c: fences follow Windows).</summary>
    public void ApplyTheme(bool light)
    {
        var ink = light ? Colors.Black : Colors.White;
        SolidColorBrush Ink(byte alpha)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, ink.R, ink.G, ink.B));
            brush.Freeze();
            return brush;
        }
        // The accent blur ignores its tint colour (ACCENT_ENABLE_BLURBEHIND), so the veil is drawn here: none in dark mode
        // (the look the user approved in M2a), a dense light veil in light mode so dark text reads on any wallpaper.
        var veil = new SolidColorBrush(light ? Color.FromArgb(0xB8, 0xF2, 0xF2, 0xF2) : Colors.Transparent);
        veil.Freeze();
        Resources["FenceVeil"] = veil;
        Resources["FenceText"] = Ink(light ? (byte)0xE6 : (byte)0xFF);
        Resources["FenceSubtleText"] = Ink(0xA0);
        Resources["FenceBorder"] = Ink(light ? (byte)0x33 : (byte)0x40);
        Resources["FenceDivider"] = Ink(light ? (byte)0x1F : (byte)0x26);
        Resources["FenceHover"] = Ink(light ? (byte)0x14 : (byte)0x22);
        Resources["FenceSelected"] = Ink(light ? (byte)0x2A : (byte)0x44);
        Resources["FenceScrollThumb"] = Ink(light ? (byte)0x44 : (byte)0x55);
        // Soft halo behind labels, like desktop icon labels: readable on busy or bright wallpapers (user choice).
        var shadow = new DropShadowEffect
        {
            Color = light ? Colors.White : Colors.Black,
            ShadowDepth = light ? 0 : 1,
            BlurRadius = 4,
            Opacity = 0.9,
        };
        shadow.Freeze();
        Resources["LabelShadow"] = shadow;
    }

    /// <summary>
    /// A rebuild regenerates every item: an open rename box would lose focus (and commit half-typed text) or come back
    /// and swallow the next keystroke. Cancel it first; with IsEditing already false the focus loss commits nothing
    /// (M3a review I3).
    /// </summary>
    private void CancelItemRenames()
    {
        foreach (var view in _items.Where(view => view.IsEditing)) view.IsEditing = false;
    }

    private void ReloadIcons()
    {
        CancelItemRenames();
        _items.Clear(); // SetItems then requests every icon again
        SetItems(_itemRefs);
    }

    /// <summary>Starts renaming the fence title (menu, or a freshly drawn fence).</summary>
    public void BeginRename()
    {
        _renaming = true;
        TitleBox.Text = _title;
        TitleText.Visibility = Visibility.Hidden;
        TitleBox.Visibility = Visibility.Visible;
        Activate(); // keyboard input needs the fence active; it stays at the bottom (owned by Progman, ADR-011)
        // With another app in front the fence may not get focus (ADR-015); then typing would go elsewhere and the box
        // could never close, so give up instead (M2c review).
        if (!TitleBox.Focus() || !IsActive)
        {
            EndRename(commit: false);
            return;
        }
        TitleBox.SelectAll();
    }

    private void EndRename(bool commit)
    {
        if (!_renaming) return;
        _renaming = false;
        TitleBox.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        if (commit && TitleBox.Text != _title) RenameRequested?.Invoke(TitleBox.Text);
    }

    private void OnTitleBoxKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key is not (Key.Enter or Key.Escape)) return;
        EndRename(commit: args.Key == Key.Enter);
        ItemList.Focus();
        args.Handled = true;
    }

    private void OnItemListKeyDown(object sender, KeyEventArgs args)
    {
        if (args.OriginalSource is TextBox) return; // keys typed into the rename box
        var selected = ItemList.SelectedItems.OfType<FenceItemView>().ToList();
        switch (args.Key)
        {
            case Key.Enter:
                foreach (var view in selected) OpenRequested?.Invoke(view.ItemRef);
                break;
            case Key.Delete when selected.Count > 0:
                RecycleRequested?.Invoke(selected.Select(view => view.ItemRef).ToList()); // Shift+Del too: always the Recycle Bin
                break;
            case Key.Back when _isPortal:
                BackRequested?.Invoke();
                break;
            case Key.F2 when selected.Count == 1:
                BeginItemRename(selected[0].ItemRef);
                break;
            default:
                return;
        }
        args.Handled = true;
    }

    /// <summary>Starts renaming one item in place (F2, or Rename in Windows' item menu). Special items cannot be renamed.</summary>
    public void BeginItemRename(string itemRef)
    {
        var view = _items.FirstOrDefault(candidate => candidate.ItemRef == itemRef);
        if (view is null || itemRef.StartsWith("::", StringComparison.Ordinal)) return;
        ItemList.ScrollIntoView(view);
        view.EditName = view.Label;
        view.IsEditing = true; // the box shows; OnLabelBoxVisibleChanged focuses it
    }

    private void OnLabelBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not TextBox { IsVisible: true, DataContext: FenceItemView view } box) return;
        Activate();
        // Without focus typing would go to another app and the box could never close (ADR-015): give up instead.
        if (!box.Focus() || !IsActive)
        {
            view.IsEditing = false;
            return;
        }
        // Like Explorer: select the name, not the extension.
        var extensionStart = box.Text.LastIndexOf('.');
        box.Select(0, extensionStart > 0 ? extensionStart : box.Text.Length);
    }

    private void OnLabelBoxKeyDown(object sender, KeyEventArgs args)
    {
        if (sender is not TextBox { DataContext: FenceItemView view } || args.Key is not (Key.Enter or Key.Escape)) return;
        EndItemRename(view, commit: args.Key == Key.Enter);
        ItemList.Focus();
        args.Handled = true;
    }

    private void OnLabelBoxLostFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (sender is TextBox { DataContext: FenceItemView view }) EndItemRename(view, commit: true);
    }

    private void EndItemRename(FenceItemView view, bool commit)
    {
        if (!view.IsEditing) return;
        view.IsEditing = false;
        var newName = view.EditName.Trim();
        if (commit && newName.Length > 0 && newName != view.Label) ItemRenameRequested?.Invoke(view.ItemRef, newName);
    }

    /// <summary>Right-click on an item opens Windows' item menu instead of the fence menu.</summary>
    private void OnBodyContextMenuOpening(object sender, ContextMenuEventArgs args)
    {
        if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is not ListBoxItem { DataContext: FenceItemView clicked } container) return;
        args.Handled = true;
        if (!container.IsSelected)
        {
            ItemList.SelectedItems.Clear();
            container.IsSelected = true;
        }
        // From the mouse, or (menu key: CursorLeft < 0) from the item's corner. PointToScreen gives physical pixels.
        var anchor = args.CursorLeft >= 0 ? PointToScreen(Mouse.GetPosition(this)) : container.PointToScreen(new Point(container.ActualWidth / 2, container.ActualHeight / 2));
        var refs = ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.ItemRef).ToList();
        if (refs.Count == 0) refs = [clicked.ItemRef];
        ItemMenuRequested?.Invoke(refs, (int)anchor.X, (int)anchor.Y);
    }

    /// <summary>The item under a screen point (physical pixels) and the index to insert before when dropping there.</summary>
    public FenceDropPoint HitTest(int screenX, int screenY)
    {
        var point = ItemList.PointFromScreen(new Point(screenX, screenY));
        string? hovered = null;
        var insertAt = _items.Count;
        for (var index = 0; index < _items.Count; index++)
        {
            if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) continue;
            var bounds = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
            // Only the middle of an item means "into it" (folders, Recycle Bin); its edges reorder (M3b review I2).
            if (DropZones.IsInto(bounds.Left, bounds.Top, bounds.Width, bounds.Height, point.X, point.Y)) hovered = _items[index].ItemRef;
            // Reading order (rows, then left to right): the first item that comes after the point.
            var sameRow = point.Y >= bounds.Top && point.Y < bounds.Bottom;
            if (point.Y < bounds.Top || (sameRow && point.X < bounds.Left + bounds.Width / 2))
            {
                insertAt = Math.Min(insertAt, index);
            }
        }
        return new FenceDropPoint(hovered, insertAt);
    }

    /// <summary>Shows where a drag would land: a caret before the insert position, or a highlight on the container taking it.</summary>
    public void ShowDropFeedback(FenceDropPoint? drop, bool into)
    {
        InsertCaret.Visibility = Visibility.Collapsed;
        DropHighlight.Visibility = Visibility.Collapsed;
        if (drop is not { } point) return;
        if (into && _items.FirstOrDefault(view => view.ItemRef == point.ItemRef) is { } target
            && ItemList.ItemContainerGenerator.ContainerFromItem(target) is ListBoxItem targetContainer)
        {
            var cell = targetContainer.TransformToAncestor(ItemList).TransformBounds(new Rect(targetContainer.RenderSize));
            Canvas.SetLeft(DropHighlight, cell.Left);
            Canvas.SetTop(DropHighlight, cell.Top);
            DropHighlight.Width = cell.Width;
            DropHighlight.Height = cell.Height;
            DropHighlight.Visibility = Visibility.Visible;
            return;
        }
        // Caret at the left edge of the item it goes before, or after the last item.
        var before = point.InsertAt < _items.Count ? ItemList.ItemContainerGenerator.ContainerFromIndex(point.InsertAt) as ListBoxItem : null;
        var anchor = before ?? (_items.Count > 0 ? ItemList.ItemContainerGenerator.ContainerFromIndex(_items.Count - 1) as ListBoxItem : null);
        if (anchor is null) return;
        var bounds = anchor.TransformToAncestor(ItemList).TransformBounds(new Rect(anchor.RenderSize));
        Canvas.SetLeft(InsertCaret, (before is null ? bounds.Right : bounds.Left) - 1);
        Canvas.SetTop(InsertCaret, bounds.Top + 4);
        InsertCaret.Height = Math.Max(0, bounds.Height - 8);
        InsertCaret.Visibility = Visibility.Visible;
    }

    private static TAncestor? FindAncestor<TAncestor>(DependencyObject source) where TAncestor : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TAncestor match) return match;
        }
        return null;
    }

    private void OnListPress(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is DependencyObject source && FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null) return;
        // Text selection in the rename box must never start a drag of the file (M3b review I3).
        if (args.OriginalSource is DependencyObject pressed && FindAncestor<TextBox>(pressed) is not null) return;
        var container = args.OriginalSource is DependencyObject element ? FindAncestor<ListBoxItem>(element) : null;
        if (container is null)
        {
            // Empty space: rubber band. Without Ctrl it starts a new selection.
            _bandStart = args.GetPosition(ItemList);
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) ItemList.SelectedItems.Clear();
            ItemList.CaptureMouse();
            args.Handled = true;
            return;
        }
        _pressPoint = args.GetPosition(ItemList);
        // Pressing one of several selected items must keep the selection, so they can be dragged together.
        if (container.IsSelected && ItemList.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None && args.ClickCount == 1)
        {
            _deferredSelect = container;
            container.Focus();
            args.Handled = true;
        }
    }

    private void OnListMove(object sender, MouseEventArgs args)
    {
        if (args.LeftButton != MouseButtonState.Pressed)
        {
            _pressPoint = null;
            return;
        }
        var position = args.GetPosition(ItemList);
        if (_bandStart is { } bandStart)
        {
            UpdateBand(bandStart, position);
            return;
        }
        if (_pressPoint is not { } pressPoint) return;
        if (Math.Abs(position.X - pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressPoint = null;
        _deferredSelect = null;
        var dragged = ItemList.SelectedItems.OfType<FenceItemView>().Select(view => view.ItemRef).ToList();
        if (dragged.Count > 0) DragRequested?.Invoke(dragged); // returns when the drag ends (Windows' modal loop)
    }

    private void OnListRelease(object sender, MouseButtonEventArgs args)
    {
        _pressPoint = null;
        if (_deferredSelect is { } container)
        {
            ItemList.SelectedItems.Clear();
            container.IsSelected = true;
            _deferredSelect = null;
        }
        if (_bandStart is not null)
        {
            ItemList.ReleaseMouseCapture(); // ends the band via LostMouseCapture
            args.Handled = true;
        }
    }

    /// <summary>Selects every item the band touches (added to the selection when Ctrl was held at the start).</summary>
    private void UpdateBand(Point start, Point current)
    {
        var band = new Rect(start, current);
        Canvas.SetLeft(SelectionBand, band.Left);
        Canvas.SetTop(SelectionBand, band.Top);
        SelectionBand.Width = band.Width;
        SelectionBand.Height = band.Height;
        SelectionBand.Visibility = Visibility.Visible;
        for (var index = 0; index < _items.Count; index++)
        {
            if (ItemList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) continue;
            var bounds = container.TransformToAncestor(ItemList).TransformBounds(new Rect(container.RenderSize));
            if (bounds.IntersectsWith(band)) container.IsSelected = true;
            else if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) container.IsSelected = false;
        }
    }

    private void EndBand()
    {
        _bandStart = null;
        SelectionBand.Visibility = Visibility.Collapsed;
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left) return;
        if (ItemsControl.ContainerFromElement(ItemList, (DependencyObject)args.OriginalSource) is ListBoxItem { DataContext: FenceItemView view })
            OpenRequested?.Invoke(view.ItemRef);
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        Handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(Handle).AddHook(OnMessage);
        FenceWindowChrome.ApplyToolWindowStyles(Handle);
        FenceWindowChrome.ApplyAccentBlur(Handle);
    }

    private void ApplyRoundedCorners()
    {
        if (Handle == 0) return;
        var pixels = FenceWindowChrome.GetPixelRect(Handle);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        FenceWindowChrome.ApplyRoundedCorners(Handle, pixels.Width, pixels.Height, (int)Math.Round(CornerRadiusDips * scale));
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Windows' item menu draws "Send to", "Open with" and shell-extension entries through its owner window.
        if (ShellItemMenu.HandleMenuMessage(message, wParam, lParam, out var menuResult))
        {
            handled = true;
            return menuResult;
        }
        switch (message)
        {
            case WmWindowPosChanging:
                if (!Peeking) FenceWindowChrome.KeepAtBottom(lParam); // fences never rise above apps, except during Peek
                break;
            // Click mode: the first press on a rolled-up title opens it instead of starting a move (M6b).
            case WmNcLeftButtonDown when wParam == HitTestCaption && ClickToOpen():
                handled = true;
                return 0;
            case WmNcLeftButtonDoubleClick when wParam == HitTestCaption:
                RollUpToggled?.Invoke();
                handled = true;
                return 0;
            case WmEnterSizeMove:
                _drag = new DragTracker(FenceWindowChrome.GetPixelRect(Handle));
                break;
            case WmMoving when SnapRect is not null && _drag is not null:
                FenceWindowChrome.WriteRect(lParam, _drag.Step(FenceWindowChrome.ReadRect(lParam), snap: rect => SnapRect(rect, SnapEdges.Move)));
                handled = true;
                return 1;
            case WmSizing when SnapRect is not null && _drag is not null:
                var edges = SizingEdges((int)wParam);
                FenceWindowChrome.WriteRect(lParam, _drag.Step(FenceWindowChrome.ReadRect(lParam), snap: rect => SnapRect(rect, edges)));
                handled = true;
                return 1;
            case WmExitSizeMove:
                _drag = null;
                // Rolled up, the window is shorter than the fence: the stored rect keeps the full height.
                var moved = FenceWindowChrome.GetPixelRect(Handle);
                MovedByUser?.Invoke(this, _rolledUp ? moved with { Height = _fullHeightPx } : moved);
                if (_rolledUp && !_expansion.Expanded && moved.Height != RolledUpHeightPx) AnimateHeight(RolledUpHeightPx); // a move during an animation
                break;
        }
        return 0;
    }

    /// <summary>WM_SIZING's WMSZ_* value as the edges being dragged.</summary>
    private static SnapEdges SizingEdges(int sizingEdge) => sizingEdge switch
    {
        1 => SnapEdges.Left,
        2 => SnapEdges.Right,
        3 => SnapEdges.Top,
        4 => SnapEdges.Top | SnapEdges.Left,
        5 => SnapEdges.Top | SnapEdges.Right,
        6 => SnapEdges.Bottom,
        7 => SnapEdges.Bottom | SnapEdges.Left,
        8 => SnapEdges.Bottom | SnapEdges.Right,
        _ => SnapEdges.None,
    };
}
```
`src/NeoFences.App/FenceHost.cs`:
```csharp
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using NeoFences.Core.Config;
using NeoFences.Core.Input;
using NeoFences.Core.Layouts;
using NeoFences.Core.Lifecycle;
using NeoFences.Core.Membership;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Owns the fences on the desktop: loads the config, places one <see cref="FenceWindow"/> per fence on the current
/// monitors, keeps fence contents in step with the Desktop folders (reconcile at start, then watcher events),
/// saves changes (debounced 500 ms, ADR-006), and keeps everything attached through display changes,
/// Explorer restarts and sign-out (ADR-011, ADR-013). Every Win32/COM call goes through NeoFences.Shell.
/// </summary>
public sealed class FenceHost
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReattachInterval = TimeSpan.FromMilliseconds(500);
    private const double SnapGapDips = 8;        // spec §6: 8 px spacing from other fences and screen edges
    private const double SnapThresholdDips = 12; // how close an edge must come before it snaps
    private const int ReattachAttempts = 10;
    private const int PeekHotkeyId = 1;
    private const int PeekEscapeHotkeyId = 2;
    private static readonly TimeSpan DrawFrame = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan TrayRetryInterval = TimeSpan.FromSeconds(2);
    private const int TrayRetryAttempts = 15;

    private readonly ConfigStore _store = new(AppPaths.DataDirectory);
    private readonly Watchdog _watchdog = new(AppPaths.DataDirectory, message => Log.Information("watchdog: {Message}", message));
    private readonly SystemMessageWindow _messages = new();
    private readonly Dictionary<string, FenceWindow> _windows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _saveTimer;
    private readonly IconLoader _iconLoader = new(Dispatcher.CurrentDispatcher);
    private DesktopWatcher? _desktopWatcher;
    // Safe-save memory and expected drop arrivals (FenceMembership.SafeSaveWindow).
    private IReadOnlyList<RememberedPlacement> _rememberedPlacements = [];
    private readonly Dictionary<string, IDisposable> _dropRegistrations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PortalState> _portals = new(StringComparer.Ordinal); // M4: Portal fences by id
    private NeoFencesConfig _config = NeoFencesConfig.CreateDefault();
    private IReadOnlyList<MonitorPlacement> _monitors = [];
    private bool _takeoverActive;
    private bool _lightTheme = SystemTheme.AppsUseLightTheme();
    private bool _sessionEnding;
    // M5 desktop gestures
    private DesktopMouseHook? _mouseHook;
    private bool _quickHidden;              // double-click on the desktop: fences (and icons) hidden until the next one
    private DrawFenceOverlay? _drawOverlay; // right-drag on the desktop: the fence being drawn
    private DispatcherTimer? _drawTimer;
    private (int X, int Y) _drawStart;
    private GlobalHotkey? _peekHotkey;
    private GlobalHotkey? _peekEscapeHotkey; // Esc ends Peek; registered only while peeking
    private bool _peeking;
    // M6a
    private bool _paused;                    // tray: Pause NeoFences (not saved)
    private bool _gameMode;                  // a full-screen app is in front: idle (spec §4.7, ADR-021)
    private ForegroundWatcher? _foregroundWatcher;
    private TrayIcon? _trayIcon;
    private readonly List<DesktopChange> _deferredDesktopChanges = [];
    private bool _reconcileDeferred;
    private const int TrayNewFence = 1, TrayQuickHide = 2, TrayPeek = 3, TrayPause = 4, TrayExit = 5, TraySettings = 6;
    private SettingsWindow? _settingsWindow; // M6b: one at a time

    public event Action? ExitRequested;

    public FenceHost()
    {
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();
        _messages.ExplorerRestarted += OnExplorerRestarted;
        _messages.DisplayChanged += OnDisplayChanged;
        _messages.ThemeChanged += OnThemeChanged;
        _messages.HotkeyPressed += OnHotkey;
        _messages.TrayMenuRequested += ShowTrayMenu;
        _messages.SessionUnlocked += OnSessionUnlocked;
    }

    public void Start()
    {
        var loaded = _store.Load();
        Log.Information("config loaded from {Source} (read-only: {IsReadOnly}, corrupt copy: {CorruptCopyPath})",
            loaded.Source, loaded.IsReadOnly, loaded.CorruptCopyPath);
        _config = loaded.Config;
        _watchdog.LaunchDetached(Environment.ProcessId);
        ApplyStartup(); // after a power loss NeoFences must come back by itself (ADR-019)

        RefreshMonitors();
        foreach (var fence in _config.Fences) OpenWindow(fence);
        ReconcileDesktop();
        StartDesktopWatcher();
        ApplyLayout();
        if (_config.Settings.Takeover) SetTakeover(true);
        else if (_watchdog.IsTakeoverActiveMarked)
        {
            // Icons may still be hidden from a run whose Takeover-off never got saved (both processes killed): show them.
            Log.Warning("takeover-active marker found while Takeover is off; showing desktop icons");
            SetIconsHidden(false);
        }
        StartGestures();
        try
        {
            _trayIcon = new TrayIcon(_messages.Handle, TrayTooltip(), log: message => Log.Warning("{Message}", message));
            ShowTrayIcon();
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "tray icon unavailable; fences and gestures keep working"); // hard rule 7: only the tray is lost
        }
        StartGameMode();
        ScheduleSave();
    }

    /// <summary>All of NeoFences' run-time modes together (Core rules: which fences, icons and hooks they imply).</summary>
    private RunState Current => new(Takeover: _takeoverActive, QuickHidden: _quickHidden, Paused: _paused, GameMode: _gameMode);

    /// <summary>
    /// Windows asked to end the session (WPF's SessionEnding, inside WM_QUERYENDSESSION). WPF then shuts the app
    /// down, which may be the last thing that ever runs, so the icons come back now and the watchdog is told it is
    /// a session end, not a user exit: if the user cancels the shutdown, the watchdog restarts NeoFences (ADR-013).
    /// </summary>
    public void OnSessionEnding()
    {
        _sessionEnding = true;
        ApplyDeferredShellWork(); // Desktop changes queued during a game must be saved too (M6a review M2)
        SaveNow();
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkSessionEnding(Environment.ProcessId), what: "session-ending marker");
    }

    /// <summary>Orderly exit: save, bring icons back, tell the watchdog all is well.</summary>
    public void Shutdown()
    {
        ApplyDeferredShellWork(); // Desktop changes queued during a game must be saved too (M6a review M2)
        SaveNow();
        if (_sessionEnding) return; // OnSessionEnding already restored and marked; no clean marker, so a cancel restarts us
        // The marker also catches a show that failed earlier (quick-hide ending while Explorer was busy; M5 review M6).
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkCleanShutdown(Environment.ProcessId), what: "clean-shutdown marker");
        _trayIcon?.Dispose();
        _foregroundWatcher?.Dispose();
        _mouseHook?.Dispose();
        _peekHotkey?.Dispose();
        _peekEscapeHotkey?.Dispose();
        _desktopWatcher?.Dispose();
        _desktopWatcher = null;
        _iconLoader.Dispose();
        _messages.Dispose();
    }

    /// <summary>Best effort from the crash handler; the watchdog restores too.</summary>
    public void EmergencyRestoreIcons()
    {
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) DesktopIcons.TrySetHidden(false);
    }

    private void OpenWindow(Fence fence)
    {
        var window = new FenceWindow(fence, takeoverActive: _takeoverActive, lightTheme: _lightTheme, iconLoader: _iconLoader, rollupExpand: _config.Settings.RollupExpand)
        {
            // Spec §6: no animations when Windows' "Animation effects" are off; spec §4.7: none while gaming.
            AnimationsAllowed = () => !_gameMode && SystemParameters.ClientAreaAnimation,
        };
        window.SnapRect = (rect, edges) => SnapFence(window, rect, edges);
        window.MovedByUser += OnFenceMoved;
        window.RenameRequested += title => RenameFence(window, title);
        window.IconSizeRequested += iconSize => SetFenceIconSize(window, iconSize);
        window.LockToggled += locked => SetFenceLocked(window, locked);
        window.DeleteRequested += () => DeleteFence(window);
        window.NewFenceRequested += CreateFence;
        window.TakeoverToggled += SetTakeover;
        window.ExitRequested += () => ExitRequested?.Invoke();
        window.OpenRequested += itemRef => OpenOrBrowse(window, itemRef);
        window.ItemMenuRequested += (itemRefs, screenX, screenY) => ShowItemMenu(window, itemRefs, screenX, screenY);
        window.RecycleRequested += itemRefs => RecycleItems(window, itemRefs);
        window.ItemRenameRequested += (itemRef, newName) => RenameItem(window, itemRef, newName);
        window.BackRequested += () => BrowsePortal(window, back: true);
        window.NewPortalRequested += () => CreatePortal(window);
        window.StartupToggled += SetStartWithWindows;
        window.SettingsRequested += OpenSettings;
        window.SetStartupChecked(_config.Settings.StartWithWindows);
        window.SortRequested += sort => SortFence(window, sort);
        window.OpenFolderRequested += () => { if (_portals.TryGetValue(window.FenceId, out var portal)) OpenItem(portal.Current, ownerHandle: window.Handle); };
        if (fence.Source is { Kind: FenceSourceKind.Portal, Path: { } portalPath })
        {
            _portals[fence.Id] = new PortalState(portalPath, show: items => ShowPortal(window, items),
                logFailure: failure => Log.Warning(failure, "cannot watch Portal folder of {FenceId}", fence.Id));
        }
        window.DragRequested += itemRefs =>
            ShellDragDrop.TryDrag(window.Handle, itemRefs, logFailure: failure => Log.Warning(failure, "could not start dragging {ItemRefs}", itemRefs));
        window.TakeoverPromptAnswered += AnswerTakeoverPrompt;
        window.RollUpToggled += () => ToggleRollUp(window);
        new WindowInteropHelper(window).EnsureHandle(); // HWND exists (styles, blur) before the first Show
        RegisterDrops(window); // needs the HWND
        if (!DesktopHost.AttachToDesktop(window.Handle)) Log.Warning("fence {FenceId}: not attached to the desktop yet (no Progman)", fence.Id);
        _windows[fence.Id] = window;
    }

    /// <summary>Full pass over the Desktop folders: at start and whenever watcher events were lost.</summary>
    private void ReconcileDesktop()
    {
        var listing = DesktopItems.Enumerate();
        var (reconciled, report) = FenceMembership.Reconcile(_config, listing.ItemRefs, listing.UnavailableFolders);
        _config = reconciled;
        if (listing.UnavailableFolders.Count > 0) Log.Warning("desktop folders not readable: {Folders}", listing.UnavailableFolders);
        if (report.Suspicious) Log.Warning("kept fenced items from an unreadable or empty desktop listing until a later reconcile");
        Log.Information("desktop reconciled: {AddedCount} added to the Inbox, {RemovedCount} removed", report.AddedToInbox.Count, report.Removed.Count);
        RefreshWindows();
        ScheduleSave();
    }

    private void StartDesktopWatcher()
    {
        // A folder that cannot be watched degrades to "its changes show after a restart" (hard rule 7).
        _desktopWatcher = new DesktopWatcher((folder, failure) => Log.Error(failure, "cannot watch {Folder}; its changes show after a restart", folder));
        var dispatcher = Dispatcher.CurrentDispatcher;
        _desktopWatcher.Changed += change => dispatcher.BeginInvoke(() => OnDesktopChanged(change));
        _desktopWatcher.Overflowed += () => dispatcher.BeginInvoke(OnDesktopWatcherError);
    }

    /// <summary>Events were lost, or the watcher stopped (.NET disables it after a non-overflow error): start over.</summary>
    private void OnDesktopWatcherError()
    {
        if (_desktopWatcher is null) return; // shut down meanwhile
        Log.Warning("desktop watcher lost events or stopped; re-arming and reconciling");
        _desktopWatcher.Dispose();
        StartDesktopWatcher();
        if (Current.ShellWorkDeferred) _reconcileDeferred = true; // after the game
        else ReconcileDesktop();
    }

    private void OnDesktopChanged(DesktopChange change)
    {
        if (Current.ShellWorkDeferred)
        {
            _deferredDesktopChanges.Add(change); // applied in order when the game is left
            return;
        }
        (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, DateTimeOffset.Now);
        RefreshWindows();
        ScheduleSave();
    }

    private void RefreshWindows()
    {
        var showPrompt = !_config.Settings.TakeoverPromptAnswered && !_takeoverActive;
        foreach (var fence in _config.Fences)
        {
            if (!_windows.TryGetValue(fence.Id, out var window)) continue;
            if (!_portals.ContainsKey(fence.Id)) window.SetItems(fence.Items); // Portals refresh on their own (M4 review I1)
            window.ShowTakeoverPrompt(showPrompt && fence.IsInbox);
        }
    }

    private static void OpenItem(string itemRef, nint ownerHandle)
    {
        // Off the UI thread: ShellExecute can block on a network timeout or a UAC prompt, freezing every fence (M2b review I6).
        Task.Run(() =>
        {
            if (!ShellItems.TryOpen(itemRef, ownerHandle)) Log.Warning("could not open {ItemRef}", itemRef);
        });
    }

    /// <summary>Makes the fence accept drops (M3b): fence items and Desktop files move membership; other files go to Windows.</summary>
    private void RegisterDrops(FenceWindow window)
    {
        try
        {
            _dropRegistrations[window.FenceId] = ShellDragDrop.RegisterFence(window.Handle, new FenceDropHandlers(
                HitTest: window.HitTest,
                MoveItems: (itemRefs, insertAt) =>
                {
                    _config = FenceMembership.MoveItems(_config, itemRefs, window.FenceId, insertAt);
                    RefreshWindows();
                    ScheduleSave();
                },
                ExpectArrivals: (itemRefs, insertAt) =>
                    _rememberedPlacements = FenceMembership.ExpectArrivals(_rememberedPlacements, itemRefs, window.FenceId, insertAt, DateTimeOffset.Now),
                Recycle: itemRefs => RecycleItems(window, itemRefs),
                ShowFeedback: window.ShowDropFeedback,
                LogFailure: failure => Log.Warning(failure, "drop on fence {FenceId} failed", window.FenceId)),
                portalFolder: () => _portals.TryGetValue(window.FenceId, out var portal) ? portal.Current : null);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "fence {FenceId} cannot accept drops", window.FenceId); // degrade: everything else still works
        }
    }

    private void ShowItemMenu(FenceWindow window, IReadOnlyList<string> itemRefs, int screenX, int screenY)
    {
        var extended = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        var choice = ShellItemMenu.Show(window.Handle, itemRefs, screenX, screenY, extended,
            logFailure: failure => Log.Warning(failure, "item menu or its command failed for {ItemRefs}", itemRefs));
        switch (choice)
        {
            case ItemMenuChoice.Rename:
                window.BeginItemRename(itemRefs[0]);
                break;
            case ItemMenuChoice.Delete:
                RecycleItems(window, itemRefs); // always the Recycle Bin, even with Shift held (hard rule 1)
                break;
        }
    }

    /// <summary>Windows moves them to the Recycle Bin (with its own dialogs); the watcher then removes them from the fence.</summary>
    private static void RecycleItems(FenceWindow window, IReadOnlyList<string> itemRefs)
    {
        Log.Information("recycling {Count} item(s)", itemRefs.Count);
        var (started, refused) = ShellFileOps.TryRecycle(window.Handle, itemRefs);
        if (!started) Log.Warning("recycle did not run or was cancelled: {ItemRefs}", itemRefs);
        if (refused.Count == 0) return;
        Log.Information("not deleting {Count} item(s) on drives without a Recycle Bin", refused.Count);
        System.Windows.MessageBox.Show(window,
            (refused.Count == 1 ? $"\"{Path.GetFileName(refused[0])}\" is" : $"{refused.Count} items are") +
            " on a drive without a Recycle Bin (a USB stick or network drive), so NeoFences won't delete it." +
            " Deleting there would be permanent; if you really mean it, delete it in Explorer.",
            "NeoFences", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    /// <summary>Windows renames the file; the watcher's rename event keeps it in its fence and position.</summary>
    private static void RenameItem(FenceWindow window, string itemRef, string newName)
    {
        if (!ShellFileOps.TryRename(window.Handle, itemRef, newName)) Log.Warning("rename did not run or was cancelled: {ItemRef}", itemRef);
    }

    private void AnswerTakeoverPrompt(bool hideIcons)
    {
        Log.Information("first-run question answered: hide desktop icons {HideIcons}", hideIcons);
        if (hideIcons) SetTakeover(true);
        else
        {
            _config = _config with { Settings = _config.Settings with { TakeoverPromptAnswered = true } };
            RefreshWindows();
            SaveNow();
        }
    }

    private void ApplyLayout()
    {
        if (_monitors.Count == 0)
        {
            Log.Warning("no monitors reported; keeping the current layout");
            return;
        }
        try
        {
            var (resolved, layout) = LayoutEngine.Resolve(_config, _monitors.Select(monitor => monitor.ToDisplayMonitor()).ToList());
            _config = resolved;
            foreach (var (fenceId, rect) in layout.Fences)
            {
                if (!_windows.TryGetValue(fenceId, out var window)) continue;
                var monitor = _monitors.First(candidate => candidate.DeviceId == rect.Monitor);
                window.Place(FencePlacement.ToPixels(rect, monitor));
                if (!window.IsVisible && Current.FencesVisible)
                {
                    window.Show();
                    if (_peeking)
                    {
                        // A fence made during Peek (its menus no longer end it, M5 review I1) joins the others on top.
                        window.Peeking = true;
                        FenceWindowChrome.SetTopmost(window.Handle, topmost: true);
                    }
                    else FenceWindowChrome.SendToBack(window.Handle); // Show puts it above every app; fences live just above the desktop
                }
            }
        }
        catch (ArgumentException unusableDisplay)
        {
            // Garbage from a monitor query mid-change (M1 review): skip this resolve, the next display event retries.
            Log.Warning(unusableDisplay, "display query unusable; keeping the current layout");
        }
    }

    private void RefreshMonitors()
    {
        _monitors = Monitors.Enumerate();
        Log.Information("monitors: {Monitors}", string.Join("; ", _monitors.Select(monitor =>
            $"{monitor.DeviceId} {monitor.PixelWidth}x{monitor.PixelHeight}@{monitor.ScalePercent}% " +
            $"work {monitor.WorkLeftPx},{monitor.WorkTopPx} {monitor.WorkWidthPx}x{monitor.WorkHeightPx}{(monitor.IsPrimary ? " primary" : "")}")));
    }

    private void OnFenceMoved(FenceWindow window, PixelRect pixels)
    {
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint) return;
        var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
        _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: window.FenceId, rect: FencePlacement.FromPixels(pixels, monitor));
        ScheduleSave();
    }

    private PixelRect SnapFence(FenceWindow window, PixelRect rect, SnapEdges edges)
    {
        if (_monitors.Count == 0) return rect;
        var monitor = FencePlacement.ContainingMonitor(rect, _monitors);
        var others = _windows.Values.Where(other => other != window && other.IsVisible).Select(other => FenceWindowChrome.GetPixelRect(other.Handle)).ToList();
        return Snapping.Snap(rect, edges,
            workArea: new PixelRect(monitor.WorkLeftPx, monitor.WorkTopPx, monitor.WorkWidthPx, monitor.WorkHeightPx),
            others: others,
            gapPx: (int)Math.Round(SnapGapDips * monitor.Scale),
            thresholdPx: (int)Math.Round(SnapThresholdDips * monitor.Scale));
    }

    private void RenameFence(FenceWindow window, string title)
    {
        _config = FenceEdits.Rename(_config, window.FenceId, title);
        window.SetTitle(_config.Fences.First(fence => fence.Id == window.FenceId).Title);
        RefreshPortal(window); // a Portal browsing a subfolder shows its breadcrumb again
        ScheduleSave();
    }

    private void SetFenceIconSize(FenceWindow window, int iconSize)
    {
        _config = FenceEdits.SetIconSize(_config, window.FenceId, iconSize);
        window.SetIconSize(iconSize);
        ScheduleSave();
    }

    private void SetFenceLocked(FenceWindow window, bool locked)
    {
        _config = FenceEdits.SetLocked(_config, window.FenceId, locked);
        window.SetLocked(locked);
        ScheduleSave();
    }

    /// <summary>Removes the fence; its items go to the Inbox. Files are never touched (hard rule 1).</summary>
    private void DeleteFence(FenceWindow window)
    {
        _config = FenceMembership.DeleteFence(_config, window.FenceId);
        _windows.Remove(window.FenceId);
        if (_dropRegistrations.Remove(window.FenceId, out var registration)) registration.Dispose();
        if (_portals.Remove(window.FenceId, out var portal)) portal.Dispose(); // the folder itself is never touched
        window.Close();
        RefreshWindows();
        ScheduleSave();
    }

    private void OnThemeChanged()
    {
        var light = SystemTheme.AppsUseLightTheme();
        if (light == _lightTheme) return;
        _lightTheme = light;
        Log.Information("Windows app mode changed; light: {Light}", light);
        foreach (var window in _windows.Values) window.ApplyTheme(light);
    }

    private void OnDisplayChanged()
    {
        RefreshMonitors();
        ApplyLayout();
        ScheduleSave();
    }

    /// <summary>"New Portal fence…": Windows' folder dialog, then a fence that mirrors the folder (M4).</summary>
    private void CreatePortal(FenceWindow owner)
    {
        var folder = FolderPicker.TryPick(owner.Handle, "Choose the folder for the new Portal fence",
            logFailure: failure => Log.Warning(failure, "folder dialog failed"));
        if (folder is null) return;
        var title = Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } name ? name : folder;
        (_config, var portal) = FenceMembership.CreatePortal(_config, title: title, folderPath: folder);
        Log.Information("Portal fence created for {Folder}", folder);
        OpenWindow(portal);
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
    }

    /// <summary>Asks a Portal to re-list its folder (in the background; ShowPortal follows).</summary>
    private void RefreshPortal(FenceWindow window)
    {
        if (_portals.TryGetValue(window.FenceId, out var portal)) portal.Refresh();
    }

    /// <summary>Shows a Portal's listing (null: the folder cannot be read) in its sort order (M4).</summary>
    private void ShowPortal(FenceWindow window, IReadOnlyList<ItemInfo>? items)
    {
        if (!_portals.TryGetValue(window.FenceId, out var portal)) return;
        if (_config.Fences.FirstOrDefault(candidate => candidate.Id == window.FenceId) is not { } fence) return;
        window.SetPortalLocation(portal.Breadcrumb(fence.Title), portal.CanGoBack);
        window.SetSortChecked(fence.Sort);
        window.ShowPortalMessage(items is null ? $"This folder is not available right now:\n{portal.Current}" : null);
        window.SetItems(items is null ? [] : ItemSorting.Order(items, fence.Sort));
    }

    /// <summary>Double-click / Enter: inside a Portal a folder is browsed in place (Ctrl opens it in Explorer, user choice 2026-10-03).</summary>
    private void OpenOrBrowse(FenceWindow window, string itemRef)
    {
        var inExplorer = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
        if (_portals.TryGetValue(window.FenceId, out var portal) && !inExplorer && portal.IsListedFolder(itemRef))
        {
            portal.Browse(itemRef); // re-lists in the background
            return;
        }
        OpenItem(itemRef, ownerHandle: window.Handle);
        SetPeek(false); // like Fences: Peek ends once something is opened from it
    }

    private void BrowsePortal(FenceWindow window, bool back)
    {
        if (!back || !_portals.TryGetValue(window.FenceId, out var portal) || !portal.CanGoBack) return;
        portal.Back(); // re-lists in the background
    }

    /// <summary>"Sort by": a Portal keeps the order live; a desktop fence is sorted once (M4).</summary>
    private void SortFence(FenceWindow window, FenceSort sort)
    {
        var fence = _config.Fences.First(candidate => candidate.Id == window.FenceId);
        if (_portals.ContainsKey(fence.Id))
        {
            _config = FenceEdits.SetSort(_config, fence.Id, sort);
            RefreshPortal(window);
        }
        else
        {
            try
            {
                _config = FenceEdits.SetItemOrder(_config, fence.Id, ItemSorting.Order(FolderItems.Describe(fence.Items), sort));
            }
            catch (ArgumentException mismatch)
            {
                Log.Warning(mismatch, "sort of fence {FenceId} refused: the sorted list did not match its items", fence.Id); // never crash (M4 review I3)
                return;
            }
            RefreshWindows();
        }
        ScheduleSave();
    }

    private void ApplyStartup() =>
        StartupRegistration.Apply(_config.Settings.StartWithWindows, Environment.ProcessPath ?? "", log: message => Log.Information("{Message}", message));

    private void SetStartWithWindows(bool startWithWindows)
    {
        _config = _config with { Settings = _config.Settings with { StartWithWindows = startWithWindows } };
        ApplyStartup();
        foreach (var window in _windows.Values) window.SetStartupChecked(startWithWindows);
        SaveNow();
        RefreshSettings();
    }

    private void CreateFence()
    {
        (_config, var fence) = FenceMembership.CreateFence(_config, "New fence");
        OpenWindow(fence);
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
    }

    private void SetTakeover(bool active)
    {
        SetQuickHidden(false); // an explicit icons choice ends quick-hide first, so the two never disagree
        _takeoverActive = active;
        // Any explicit choice (banner or menu) answers the first-run question.
        _config = _config with { Settings = _config.Settings with { Takeover = active, TakeoverPromptAnswered = true } };
        SetIconsHidden(Current.IconsHidden);
        foreach (var window in _windows.Values) window.SetTakeoverChecked(active);
        RefreshSettings();
        RefreshWindows();
        SaveNow(); // not debounced: the saved setting must match the takeover-active marker if we are killed next
    }

    /// <returns>True when Windows confirmed the new state.</returns>
    private bool SetIconsHidden(bool hidden)
    {
        // The watchdog must know whenever icons may be hidden: mark before hiding, unmark only after a confirmed show.
        if (hidden) TryMarker(() => _watchdog.SetTakeoverActive(true), what: "takeover-active marker");
        var applied = DesktopIcons.TrySetHidden(hidden);
        if (applied && !hidden) TryMarker(() => _watchdog.SetTakeoverActive(false), what: "takeover-active marker");
        if (applied) Log.Information("desktop icons hidden: {Hidden}", hidden);
        else Log.Warning("could not set desktop icons hidden: {Hidden}", hidden);
        return applied;
    }

    /// <summary>Icons as they should be: hidden while Takeover is on, shown (and unmarked) otherwise.</summary>
    private bool EnsureIconState() =>
        Current.IconsHidden ? SetIconsHidden(true)
        : !_watchdog.IsTakeoverActiveMarked || SetIconsHidden(false);

    private static void TryMarker(Action writeMarker, string what)
    {
        try
        {
            writeMarker();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "could not update the {What}", what);
        }
    }

    private void OnExplorerRestarted()
    {
        Log.Information("Explorer restarted; re-attaching fences");
        SetPeek(false); // re-attaching sends fences to the bottom; Peek (and its global Esc) must end with it (M5 review M2)
        ShowTrayIcon(); // Explorer forgot every tray icon
        ReinstallMouseHook(); // a hook Windows dropped silently comes back here at the latest (M5 review)
        var attempts = 0;
        var retryTimer = new DispatcherTimer { Interval = ReattachInterval };
        retryTimer.Tick += (_, _) =>
        {
            attempts++;
            var unattachedCount = _windows.Values.Count(window => !DesktopHost.AttachToDesktop(window.Handle));
            foreach (var window in _windows.Values) FenceWindowChrome.SendToBack(window.Handle);
            var iconsOk = EnsureIconState();
            if ((unattachedCount == 0 && iconsOk) || attempts >= ReattachAttempts)
            {
                retryTimer.Stop();
                Log.Information("re-attach after Explorer restart: {Attempts} attempt(s), unattached {UnattachedCount}, icons ok {IconsOk}",
                    attempts, unattachedCount, iconsOk);
            }
        };
        retryTimer.Start();
    }

    /// <summary>The WH_MOUSE_LL desktop gestures and the Peek hotkey (M5). Either failing only turns that feature off.</summary>
    private void StartGestures()
    {
        UpdateMouseHook();
        UpdatePeekHotkey();
    }

    /// <summary>The Peek hotkey is registered exactly while wanted: released to Windows and the game while paused or gaming.</summary>
    private void UpdatePeekHotkey()
    {
        if (Current.PeekHotkeyWanted == _peekHotkey is not null) return;
        if (_peekHotkey is not null)
        {
            _peekHotkey.Dispose();
            _peekHotkey = null;
            return;
        }
        _peekHotkey = TryRegisterPeekHotkey(_config.Settings.PeekHotkey, out var problem);
        if (_peekHotkey is null) Log.Warning("Peek is off: {Problem}", problem);
    }

    private GlobalHotkey? TryRegisterPeekHotkey(string text, out string problem)
    {
        if (!Hotkey.TryParse(text, out var hotkey) || !Enum.TryParse<System.Windows.Input.Key>(hotkey.Key, ignoreCase: true, out var key))
        {
            problem = $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.";
            return null;
        }
        var registration = new GlobalHotkey(_messages.Handle, PeekHotkeyId);
        if (registration.TryRegister(hotkey, (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key)))
        {
            problem = "";
            Log.Information("Peek hotkey {Hotkey} registered", hotkey);
            return registration;
        }
        registration.Dispose();
        problem = $"{hotkey} is already used by another app. Pick another combination.";
        return null;
    }

    /// <summary>Settings: a new Peek hotkey. It is kept only if Windows accepts it; otherwise the old one stays.</summary>
    private (bool Saved, string Message) SetPeekHotkey(string text)
    {
        if (!Hotkey.TryParse(text, out var parsed)) return (false, $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.");
        var normalized = parsed.ToString();
        if (normalized == _config.Settings.PeekHotkey) return (true, $"Peek: {normalized}");
        _peekHotkey?.Dispose();
        _peekHotkey = null;
        var registration = TryRegisterPeekHotkey(normalized, out var problem);
        if (registration is null)
        {
            UpdatePeekHotkey(); // the old one again
            return (false, problem);
        }
        registration.Dispose(); // proven free; UpdatePeekHotkey registers it whenever it is wanted
        _config = _config with { Settings = _config.Settings with { PeekHotkey = normalized } };
        UpdatePeekHotkey();
        SaveNow();
        return (true, $"Saved. Peek: {normalized}");
    }

    /// <summary>Tray or fence menu → Settings… (M6b). One window; a second request brings it to the front.</summary>
    private void OpenSettings()
    {
        if (_settingsWindow is { } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }
        var window = new SettingsWindow();
        window.StartWithWindowsChanged += SetStartWithWindows;
        window.TakeoverChanged += SetTakeover;
        window.PeekHotkeyChosen += text =>
        {
            var (saved, message) = SetPeekHotkey(text);
            RefreshSettings();
            window.ShowHotkeyResult(saved, message);
        };
        window.RollupExpandChanged += SetRollupExpand;
        window.GameModeChanged += SetGameModeEnabled;
        window.OpenLogsRequested += () => OpenItem(AppPaths.LogsDirectory, ownerHandle: 0);
        window.OpenDataRequested += () => OpenItem(AppPaths.DataDirectory, ownerHandle: 0);
        window.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow = window;
        RefreshSettings();
        window.Show();
        window.Activate();
    }

    private void RefreshSettings() => _settingsWindow?.Show(new SettingsView(
        StartWithWindows: _config.Settings.StartWithWindows,
        Takeover: _takeoverActive,
        PeekHotkey: _config.Settings.PeekHotkey,
        RollupExpand: _config.Settings.RollupExpand,
        GameModeEnabled: _config.Settings.GameMode,
        GameModeActive: _gameMode,
        Version: typeof(FenceHost).Assembly.GetName().Version?.ToString(3) ?? "",
        DataFolder: AppPaths.DataDirectory));

    private void SetRollupExpand(RollupExpand mode)
    {
        _config = _config with { Settings = _config.Settings with { RollupExpand = mode } };
        foreach (var window in _windows.Values) window.SetRollupExpand(mode);
        Log.Information("roll-up expands on: {Mode}", mode);
        SaveNow();
    }

    private void SetGameModeEnabled(bool enabled)
    {
        _config = _config with { Settings = _config.Settings with { GameMode = enabled } };
        Log.Information("game mode enabled: {Enabled}", enabled);
        SaveNow();
        CheckGameMode(); // turning it off in a game ends idle at once
        RefreshSettings();
    }

    /// <summary>The hook exists exactly while it is wanted: not while paused or gaming (hard rule 3, spec §4.7).</summary>
    private void UpdateMouseHook()
    {
        if (Current.MouseHookWanted == _mouseHook is not null) return;
        if (_mouseHook is not null)
        {
            EndDrawOverlay();
            _mouseHook.Dispose();
            _mouseHook = null;
            return;
        }
        var dispatcher = Dispatcher.CurrentDispatcher;
        _mouseHook = new DesktopMouseHook(
            onGesture: (gesture, screenX, screenY) => dispatcher.BeginInvoke(() => OnDesktopGesture(gesture, screenX, screenY)),
            onPeekClickOutside: () => dispatcher.BeginInvoke(() => SetPeek(false)),
            log: message => Log.Information("{Message}", message));
    }

    /// <summary>Windows drops a low-level hook silently (LowLevelHooksTimeout): a fresh one after Explorer restarts and on unlock.</summary>
    private void ReinstallMouseHook()
    {
        if (_mouseHook is null) return; // not wanted now; it comes back when it is
        EndDrawOverlay();
        _mouseHook.Dispose();
        _mouseHook = null;
        UpdateMouseHook();
    }

    private void OnSessionUnlocked()
    {
        Log.Information("session unlocked; re-installing the mouse hook");
        ReinstallMouseHook();
        CheckGameMode();
    }

    /// <summary>Game mode (spec §4.7, ADR-021): checked on every foreground change, and again shortly after (the signal lags).</summary>
    private void StartGameMode()
    {
        // Backstop for a game that goes full screen after the last re-check (M6a review I1).
        var poll = new DispatcherTimer { Interval = GameModePolicy.PollInterval };
        poll.Tick += (_, _) => CheckGameMode();
        poll.Start();
        _foregroundWatcher = new ForegroundWatcher(onForegroundChanged: OnForegroundChanged,
            logFailure: failure => Log.Error(failure, "game mode check failed"));
        if (!_foregroundWatcher.IsWatching) Log.Warning("game mode: foreground changes cannot be watched; game mode is off");
        CheckGameMode();
    }

    private void OnForegroundChanged()
    {
        CheckGameMode();
        foreach (var delay in GameModePolicy.RecheckDelays)
        {
            // ponytail: one short-lived timer per foreground change and delay; a coalescing timer if switching storms ever show up in a profile.
            var recheck = new DispatcherTimer { Interval = delay };
            recheck.Tick += (_, _) =>
            {
                recheck.Stop();
                CheckGameMode();
            };
            recheck.Start();
        }
    }

    private void CheckGameMode()
    {
        var gameMode = GameModePolicy.IsGameActive(enabled: _config.Settings.GameMode, foreground: GameDetection.TakeSnapshot());
        if (gameMode == _gameMode) return;
        _gameMode = gameMode;
        Log.Information("game mode: {GameMode}", gameMode);
        if (gameMode) SetPeek(false);
        UpdateMouseHook();
        foreach (var portal in _portals.Values) portal.SetPaused(gameMode);
        if (!gameMode) ApplyDeferredShellWork();
        UpdatePeekHotkey();
        _trayIcon?.SetTooltip(TrayTooltip());
        RefreshSettings();
    }

    /// <summary>The game was left: Desktop changes made meanwhile apply in order (or one reconcile if events were lost).</summary>
    private void ApplyDeferredShellWork()
    {
        if (_reconcileDeferred)
        {
            _reconcileDeferred = false;
            _deferredDesktopChanges.Clear();
            ReconcileDesktop();
            return;
        }
        if (_deferredDesktopChanges.Count == 0) return;
        Log.Information("applying {Count} desktop change(s) from game mode", _deferredDesktopChanges.Count);
        foreach (var change in _deferredDesktopChanges)
            (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, DateTimeOffset.Now);
        _deferredDesktopChanges.Clear();
        RefreshWindows();
        ScheduleSave();
    }

    /// <summary>Pause (tray): the desktop goes back to Windows — fences hidden, icons shown, the hook gone — until resumed.</summary>
    private void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        if (paused)
        {
            SetPeek(false);
            EndDrawOverlay();
        }
        _paused = paused;
        if (paused) _quickHidden = false; // resuming shows everything
        foreach (var window in _windows.Values)
        {
            if (!Current.FencesVisible) window.Hide();
            else if (!window.IsVisible)
            {
                window.Show();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        EnsureIconState(); // also shows icons left hidden by an earlier failed show: Pause "restores icons" (spec §6, M6a review M1)
        UpdateMouseHook();
        UpdatePeekHotkey();
        _trayIcon?.SetTooltip(TrayTooltip());
        Log.Information("paused: {Paused}", paused);
    }

    private string TrayTooltip() =>
        _paused ? "NeoFences — paused" : _gameMode ? "NeoFences — idle while a game runs" : "NeoFences";

    /// <summary>Tray menu (spec §6; user choice 2026-10-03: left-click opens it too).</summary>
    private void ShowTrayMenu(int screenX, int screenY)
    {
        var chosen = TrayMenu.Show(_messages.Handle,
        [
            new TrayMenuItem(TrayNewFence, "New fence", Enabled: !_paused),
            new TrayMenuItem(TrayQuickHide, "Quick-hide", Checked: _quickHidden, Enabled: !_paused),
            new TrayMenuItem(TrayPeek, $"Peek\t{_config.Settings.PeekHotkey}", Checked: _peeking, Enabled: !_paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TraySettings, "Settings…"),
            new TrayMenuItem(TrayPause, "Pause NeoFences", Checked: _paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayExit, "Exit NeoFences"),
        ], screenX, screenY);
        switch (chosen)
        {
            case TrayNewFence:
                SetQuickHidden(false); // a new fence must be visible (M6a review I3)
                CreateFence();
                break;
            case TrayQuickHide: SetQuickHidden(!_quickHidden); break;
            case TrayPeek: SetPeek(!_peeking); break;
            case TrayPause: SetPaused(!_paused); break;
            case TraySettings: OpenSettings(); break;
            case TrayExit: ExitRequested?.Invoke(); break;
        }
    }

    private void OnDesktopGesture(DesktopGesture gesture, int screenX, int screenY)
    {
        switch (gesture)
        {
            // A double-click on a visible native icon opens it; only empty desktop toggles quick-hide.
            case DesktopGesture.DoubleClick when _takeoverActive || _quickHidden
                || !DesktopWindows.IsOverDesktopIcon(screenX, screenY, log: message => Log.Warning("{Message}", message)):
                SetQuickHidden(!_quickHidden);
                break;
            case DesktopGesture.RightDragStarted:
                BeginDrawFence(screenX, screenY);
                break;
            case DesktopGesture.RightDragCompleted:
                EndDrawFence(screenX, screenY);
                break;
            case DesktopGesture.RightDragCancelled:
                EndDrawOverlay(); // the drag's right-up was never seen (M5 review I2)
                break;
        }
    }

    /// <summary>Quick-hide (M5, user choice): fences and the native desktop icons go away together and come back together.</summary>
    private void SetQuickHidden(bool hidden)
    {
        if (hidden == _quickHidden) return;
        if (hidden) SetPeek(false);
        _quickHidden = hidden;
        foreach (var window in _windows.Values)
        {
            if (hidden) window.HideFaded(); // 150 ms fade (spec §6)
            else
            {
                window.ShowFaded();
                FenceWindowChrome.SendToBack(window.Handle);
            }
        }
        if (!_takeoverActive) SetIconsHidden(hidden); // with Takeover the icons are hidden anyway
        Log.Information("quick-hide: {Hidden}", hidden);
    }

    private void BeginDrawFence(int startX, int startY)
    {
        if (_mouseHook is not { } mouseHook) return;
        EndDrawOverlay();
        _drawStart = (startX, startY);
        var overlay = new DrawFenceOverlay(_lightTheme);
        overlay.Track(DrawFenceOverlay.Between(_drawStart, mouseHook.DragPoint));
        overlay.Show();
        _drawOverlay = overlay;
        _drawTimer = new DispatcherTimer { Interval = DrawFrame };
        // A lost right-up (Win+L, UAC, an elevated window) is ended by the next click (RightDragCancelled). The button
        // state cannot tell: the swallowed right-press never reaches Windows' key state (M5 review I2, smoke finding).
        _drawTimer.Tick += (_, _) => overlay.Track(DrawFenceOverlay.Between(_drawStart, mouseHook.DragPoint));
        _drawTimer.Start();
    }

    /// <summary>The right button came up: a new fence where the rectangle was, its title ready to type.</summary>
    private void EndDrawFence(int endX, int endY)
    {
        if (!EndDrawOverlay()) return; // the start was never seen
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint) return;
        SetQuickHidden(false);
        var pixels = DrawFenceOverlay.Between(_drawStart, (endX, endY));
        var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
        (_config, var fence) = FenceMembership.CreateFence(_config, "New fence");
        // Too small a drag still makes a usable fence: the layout clamps it to the minimum size.
        _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: fence.Id, rect: FencePlacement.FromPixels(pixels, monitor));
        Log.Information("fence drawn on the desktop at {Pixels}", pixels);
        OpenWindow(fence);
        RefreshWindows();
        ApplyLayout();
        ScheduleSave();
        if (_windows.TryGetValue(fence.Id, out var window)) window.BeginRename();
    }

    /// <returns>True when an overlay was showing.</returns>
    private bool EndDrawOverlay()
    {
        _drawTimer?.Stop();
        _drawTimer = null;
        if (_drawOverlay is null) return false;
        _drawOverlay.Close();
        _drawOverlay = null;
        return true;
    }

    private void OnHotkey(int hotkeyId)
    {
        CheckGameMode(); // fresh: a game may have gone full screen since the last check (M6a review I1)
        // Paused: the desktop belongs to Windows. Gaming: fences must not rise over the game, and the click-outside hook is off.
        if (_paused || _gameMode) return;
        if (hotkeyId == PeekHotkeyId) SetPeek(!_peeking);
        else if (hotkeyId == PeekEscapeHotkeyId) SetPeek(false);
    }

    /// <summary>Peek (M5): every fence above all windows until the hotkey again, Esc, a click outside, or an item opens.</summary>
    private void SetPeek(bool peeking)
    {
        if (peeking == _peeking) return;
        if (peeking) SetQuickHidden(false);
        _peeking = peeking;
        if (_mouseHook is not null) _mouseHook.PeekActive = peeking;
        // Every fence first: raising one restacks its siblings (all owned by Progman), and a sibling still keeping
        // itself at the bottom would drop back (M5 smoke: only one fence rose).
        foreach (var window in _windows.Values) window.Peeking = peeking;
        foreach (var window in _windows.Values) FenceWindowChrome.SetTopmost(window.Handle, peeking);
        _peekEscapeHotkey?.Dispose();
        _peekEscapeHotkey = null;
        if (peeking)
        {
            _peekEscapeHotkey = new GlobalHotkey(_messages.Handle, PeekEscapeHotkeyId);
            if (!_peekEscapeHotkey.TryRegister(new Hotkey(Ctrl: false, Alt: false, Shift: false, Win: false, Key: "Escape"), virtualKey: 0x1B))
                Log.Warning("Esc is taken by another app; Peek ends with its hotkey or a click outside");
        }
        Log.Information("peek: {Peeking}", peeking);
    }

    /// <summary>Title double-click (M5): rolled up to its title bar, or back. Stored, so it survives a restart.</summary>
    private void ToggleRollUp(FenceWindow window)
    {
        var rolledUp = !_config.Fences.First(fence => fence.Id == window.FenceId).RolledUp;
        _config = FenceEdits.SetRolledUp(_config, window.FenceId, rolledUp);
        window.SetRolledUp(rolledUp);
        ScheduleSave();
    }

    /// <summary>Adds the tray icon, retrying while Explorer is still busy (sign-in autostart, Explorer restart).</summary>
    private void ShowTrayIcon()
    {
        if (_trayIcon is not { } trayIcon || trayIcon.Show()) return;
        var attempts = 0;
        var retry = new DispatcherTimer { Interval = TrayRetryInterval };
        retry.Tick += (_, _) =>
        {
            if (trayIcon.Show() || ++attempts >= TrayRetryAttempts)
            {
                retry.Stop();
                Log.Information("tray icon retry finished after {Attempts} attempt(s)", attempts + 1);
            }
        };
        retry.Start();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        try
        {
            if (!_store.Save(_config)) Log.Warning("config not saved: config.json is read-only this session");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "config save failed");
        }
    }
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 223`.

- [ ] **Step 3: Live smoke (ask first: about 70 s of mouse; print TEST RUNNING / TEST COMPLETE)**

Save as `<scratchpad>\m6b-smoke.ps1` and run it with Windows PowerShell: `powershell -NoProfile -ExecutionPolicy Bypass -File "<scratchpad>\m6b-smoke.ps1"`. It needs the "Games" fence at 1069,562 (the user's layout). It runs the branch build and leaves it running.
```powershell
# M6b live smoke: the settings window (opened from the tray; screenshot), a recorded Peek hotkey that works, roll-up
# click mode and the roll-up animation, the game-mode toggle, quick-hide fade end state. Backs up config.json, runs
# -Exe, restores the config and starts -RestoreExe. Run with Windows PowerShell (UI Automation + Add-Type). Never types
# into Windows Terminal; refocuses it at the end.
param(
  [string]$Exe = "F:\projects\neo_fences\src\NeoFences.App\bin\Debug\net10.0-windows\NeoFences.exe",
  [string]$RestoreExe = $Exe)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type -Namespace NfM6 -Name N -MemberDefinition @'
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string t);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
public delegate bool EnumProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern int GetMenuItemCount(IntPtr menu);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetMenuString(IntPtr menu, uint item, System.Text.StringBuilder s, int n, uint flags);
[DllImport("user32.dll")] public static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
[DllImport("user32.dll")] public static extern bool GetMenuItemRect(IntPtr h, IntPtr menu, uint item, out RECT r);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
'@
# A stand-in game: a borderless window covering the primary monitor, on its own UI thread, closed on demand.
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @'
public static class NfFakeGame {
    static System.Windows.Forms.Form form;
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(System.IntPtr h);
    public static void Start() {
        var ready = new System.Threading.ManualResetEvent(false);
        var thread = new System.Threading.Thread(() => {
            form = new System.Windows.Forms.Form { FormBorderStyle = System.Windows.Forms.FormBorderStyle.None, StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                Bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds, BackColor = System.Drawing.Color.FromArgb(20, 20, 30), Text = "nf-fake-game", ShowInTaskbar = true };
            form.Shown += (s, e) => { SetForegroundWindow(form.Handle); ready.Set(); };
            System.Windows.Forms.Application.Run(form);
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA); thread.IsBackground = true; thread.Start(); ready.WaitOne(5000);
    }
    public static void Stop() { if (form != null) form.Invoke(new System.Action(() => form.Close())); }
}
'@
function Pause-Ms([int]$ms) { [System.Threading.Thread]::Sleep($ms) }
# Never type into Windows Terminal: an Esc there cancels Claude Code's running tool call.
function TerminalHasFocus { $processId = 0; [void][NfM6.N]::GetWindowThreadProcessId([NfM6.N]::GetForegroundWindow(), [ref]$processId); (Get-Process -Id $processId -ErrorAction SilentlyContinue).ProcessName -in 'WindowsTerminal', 'OpenConsole', 'conhost' }
function Chord([byte[]]$vks) { if (TerminalHasFocus) { "  (skipped keys: Terminal has focus)" | Out-Host; return }; foreach ($vk in $vks) { [NfM6.N]::keybd_event($vk,0,0,[UIntPtr]::Zero) }; Pause-Ms 50; [array]::Reverse($vks); foreach ($vk in $vks) { [NfM6.N]::keybd_event($vk,0,2,[UIntPtr]::Zero) }; Pause-Ms 400 }
function InputMove([int]$x, [int]$y) { [NfM6.N]::mouse_event(0x8001, [int]($x * 65535 / 1919), [int]($y * 65535 / 1079), 0, [UIntPtr]::Zero) }
function Click([int]$x, [int]$y) { InputMove $x $y; Pause-Ms 150; [NfM6.N]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [NfM6.N]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Pause-Ms 700 }
function Wait-Until([scriptblock]$condition, [int]$seconds = 8) { $deadline = (Get-Date).AddSeconds($seconds); while (-not (& $condition) -and (Get-Date) -lt $deadline) { Pause-Ms 200 }; [bool](& $condition) }
function Fences { $found = New-Object System.Collections.Generic.List[IntPtr]
  [void][NfM6.N]::EnumWindows({ param($h, $l) if ([NfM6.N]::IsWindowVisible($h)) { $s = New-Object System.Text.StringBuilder 64; [void][NfM6.N]::GetWindowText($h, $s, 64); if ("$s" -eq 'NeoFences fence') { $found.Add($h) } }; $true }, [IntPtr]::Zero); @($found) }
function MenuWindow { [NfM6.N]::FindWindow('#32768', [NullString]::Value) }
function OpenTrayMenu {
  $messages = [NfM6.N]::FindWindow([NullString]::Value, 'NeoFences.SystemMessages')
  # WM_APP+2 with NIN_SELECT (left click, icon id 1); the point near the clock.
  [void][NfM6.N]::PostMessage($messages, 0x8002, [IntPtr](1800 -bor (1050 -shl 16)), [IntPtr](0x400 -bor (1 -shl 16)))
  if (-not (Wait-Until { (MenuWindow) -ne [IntPtr]::Zero } 4)) { return $null }
  Pause-Ms 300; [NfM6.N]::SendMessage((MenuWindow), 0x01E1, [IntPtr]::Zero, [IntPtr]::Zero) }  # MN_GETHMENU
function MenuItems([IntPtr]$menu) { foreach ($index in 0..([NfM6.N]::GetMenuItemCount($menu) - 1)) { $s = New-Object System.Text.StringBuilder 128; [void][NfM6.N]::GetMenuString($menu, $index, $s, 128, 0x400); $state = [NfM6.N]::GetMenuState($menu, $index, 0x400)
  [pscustomobject]@{ Index = $index; Text = "$s" -replace "`t", ' '; Checked = ($state -band 8) -ne 0; Grayed = ($state -band 1) -ne 0 } } }
function ClickMenuItem([IntPtr]$menu, [string]$prefix) { $item = MenuItems $menu | Where-Object { $_.Text -like "$prefix*" } | Select-Object -First 1
  $r = New-Object NfM6.N+RECT; [void][NfM6.N]::GetMenuItemRect([IntPtr]::Zero, $menu, $item.Index, [ref]$r); Click ([int](($r.Left + $r.Right) / 2)) ([int](($r.Top + $r.Bottom) / 2)) }
function CloseMenu { if ((MenuWindow) -ne [IntPtr]::Zero) { Click 960 300; Pause-Ms 300 } }
$log = Get-ChildItem "$env:LOCALAPPDATA\NeoFences\logs" -Filter 'neofences*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
function LogSince([int]$line) { $current = Get-ChildItem "$env:LOCALAPPDATA\NeoFences\logs" -Filter 'neofences*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1; @(Get-Content $current.FullName | Select-Object -Skip $line) }
function LogLines { $current = Get-ChildItem "$env:LOCALAPPDATA\NeoFences\logs" -Filter 'neofences*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1; @(Get-Content $current.FullName).Count }
function Logged([int]$since, [string]$pattern) { [bool](LogSince $since | Where-Object { $_ -like "*$pattern*" }) }
$configPath = "$env:LOCALAPPDATA\NeoFences\config.json"
function Config { Get-Content $configPath -Raw | ConvertFrom-Json }
function Stop-App([string]$path) { & $path --exit; [void](Wait-Until { -not (Get-Process NeoFences -ErrorAction SilentlyContinue) } 15) }

# Arrange.
Stop-App $RestoreExe
$backup = "$PSScriptRoot\config-before-m6b.json"; Copy-Item $configPath $backup -Force
Start-Process $Exe; Pause-Ms 5000
$shell = New-Object -ComObject Shell.Application; $shell.MinimizeAll(); Pause-Ms 1500
Click 1000 400; "keyboard focus off Terminal: $(-not (TerminalHasFocus))"
$fenceCount = (Fences).Count; "fences shown: $fenceCount"

function Rect([IntPtr]$window) { $r = New-Object NfM6.N+RECT; [void][NfM6.N]::GetWindowRect($window, [ref]$r); $r }
function Key([byte]$vk) { Chord @($vk) }
function Element([IntPtr]$window, [string]$automationId) { [System.Windows.Automation.AutomationElement]::FromHandle($window).FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId))) }
function Toggle([IntPtr]$window, [string]$automationId) { (Element $window $automationId).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Pause-Ms 600 }
function DoubleClick([int]$x, [int]$y) { InputMove $x $y; Pause-Ms 150; foreach ($press in 1..2) { [NfM6.N]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [NfM6.N]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Pause-Ms 60 } }
function Height([IntPtr]$window) { $r = Rect $window; $r.Bottom - $r.Top }

# 1. Tray → Settings…: the window opens (Fluent); a screenshot for the user.
$menu = OpenTrayMenu
if (-not $menu) { "tray menu did not open; aborting" } else {
  "tray menu: $((MenuItems $menu | ForEach-Object { $_.Text }) -join ' | ')"
  ClickMenuItem $menu 'Settings'
  $settings = [IntPtr]::Zero
  [void](Wait-Until { $script:settings = [NfM6.N]::FindWindow([NullString]::Value, 'NeoFences settings'); $script:settings -ne [IntPtr]::Zero } 6)
  "settings window opened: $($settings -ne [IntPtr]::Zero)"
  Pause-Ms 1200
  $r = Rect $settings; $shot = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
  $g = [System.Drawing.Graphics]::FromImage($shot); $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $shot.Size); $g.Dispose(); $shot.Save("$PSScriptRoot\m6b-settings.png"); $shot.Dispose()
  "screenshot: $PSScriptRoot\m6b-settings.png"

  # 2. Peek hotkey: record Ctrl+Shift+F9 in the box; saved, registered, and it opens Peek; then back to Ctrl+Alt+Space.
  $hotkeyBox = Element $settings 'HotkeyBox'; $hotkeyBox.SetFocus(); Pause-Ms 400
  $mark = LogLines
  Chord @(0x11, 0x10, 0x78); Pause-Ms 800
  "hotkey recorded: saved $(((Config).settings.peekHotkey) -eq 'Ctrl+Shift+F9'), registered $(Logged $mark 'Peek hotkey Ctrl+Shift+F9 registered')"
  Click 1000 400   # desktop focus, so the next keys do not land in the settings box
  Chord @(0x11, 0x10, 0x78); Pause-Ms 600
  "new hotkey opens Peek: $(Logged $mark 'peek: True')"
  Chord @(0x11, 0x10, 0x78); Pause-Ms 400
  [NfM6.N]::SetForegroundWindow($settings) | Out-Null
  $hotkeyBox = Element $settings 'HotkeyBox'; $hotkeyBox.SetFocus(); Pause-Ms 400
  Chord @(0x11, 0x12, 0x20); Pause-Ms 800
  "hotkey back to Ctrl+Alt+Space: $(((Config).settings.peekHotkey) -eq 'Ctrl+Alt+Space')"

  # 3. Game mode toggle: off is saved, on again.
  Toggle $settings 'GameModeBox'
  "game mode off saved: $(((Config).settings.gameMode) -eq $false)"
  Toggle $settings 'GameModeBox'
  "game mode on again: $(((Config).settings.gameMode) -eq $true)"

  # 4. Roll-up opens on click: choose it in the combo box (keyboard, the settings window is in front).
  $rollup = Element $settings 'RollupBox'; $rollup.SetFocus(); Pause-Ms 300
  Key 0x28; Pause-Ms 600
  "roll-up mode click saved: $(((Config).settings.rollupExpand) -eq 'click')"
  [void][NfM6.N]::PostMessage($settings, 0x10, [IntPtr]::Zero, [IntPtr]::Zero); Pause-Ms 800
  "settings closed: $([NfM6.N]::FindWindow([NullString]::Value, 'NeoFences settings') -eq [IntPtr]::Zero)"
}

# 5. Click mode on a real fence ("Games"): roll up (animated), resting does not open, a click opens, leaving closes.
$games = Fences | Where-Object { $r = Rect $_; $r.Left -eq 1069 -and $r.Top -eq 562 } | Select-Object -First 1
if (-not $games) { "Games fence not found at 1069,562" } else {
  $full = Height $games
  DoubleClick 1229 576; Pause-Ms 90; $mid = Height $games; Pause-Ms 400
  $rolled = Height $games
  "roll-up animated: $full → mid $mid → $rolled  →  $($mid -gt $rolled -and $mid -lt $full)"
  InputMove 1229 576; Pause-Ms 1200
  "click mode: resting does not open $((Height $games) -eq $rolled)"
  Click 1229 576; Pause-Ms 500
  "click opens: $((Height $games) -eq $full)"
  InputMove 1229 980; Pause-Ms 1300
  "leaving closes: $((Height $games) -eq $rolled)"
  DoubleClick 1229 576; Pause-Ms 600; InputMove 1000 1000; Pause-Ms 400
  "unrolled: $((Height $games) -eq $full)"
}

# 6. Quick-hide with the fade: hidden afterwards, then back.
$menu = OpenTrayMenu; ClickMenuItem $menu 'Quick-hide'
"quick-hide (faded): hidden " + (Wait-Until { (Fences).Count -eq 0 } 3)
$menu = OpenTrayMenu; ClickMenuItem $menu 'Quick-hide'
"quick-hide off: back " + (Wait-Until { (Fences).Count -eq $fenceCount } 3)

# Clean up: the test file is ours; the config comes back; the build to keep runs again.
Stop-App $Exe; Copy-Item $backup $configPath -Force; Start-Process $RestoreExe; Pause-Ms 4000
$shell.UndoMinimizeALL(); Pause-Ms 1000
$terminal = Get-Process WindowsTerminal -ErrorAction SilentlyContinue | Select-Object -First 1
if ($terminal) { [NfM6.N]::keybd_event(0x12,0,0,[UIntPtr]::Zero); [void](New-Object -ComObject WScript.Shell).AppActivate($terminal.Id); [NfM6.N]::keybd_event(0x12,0,2,[UIntPtr]::Zero) }
"NeoFences alive: " + [bool](Get-Process NeoFences -ErrorAction SilentlyContinue)
```
Expected:
- the tray menu line includes `Settings…`;
- every other line ends `True`;
- the roll-up line shows a mid-way height between the full and the rolled-up height;
- look at `m6b-settings.png`: a Fluent window in the Windows theme.

Regression: re-run `<scratchpad>\m6a-smoke.ps1` (tray, Pause, game mode) and `<scratchpad>\m5-smoke-branch.ps1` (gestures). Expected: as before (the M5 roll-up-after-Explorer line is the known carry-over).

- [ ] **Step 4: Commit**

```powershell
git add src/NeoFences.App
git commit -m "feat: added the settings window, roll-up click mode and roll-up and quick-hide animations"
```

---

### Task 3: Verification

**Files:** `docs/TEST-CHECKLIST.md` (append section P), `docs/research/m6b-settings-animations.md` (create)

- [ ] **Step 1: Append section P**

```markdown
## P — Settings window and animations (M6b, ADR-022)
| ID | Steps | Expected |
|---|---|---|
| P1 | Tray → Settings…; fence menu → Settings…; both again | one Fluent window (follows light/dark + accent), brought to the front the second time |
| P2 | Switch Windows between light and dark with Settings open | the window follows; fences follow as before |
| P3 | Toggle Start with Windows / Hide desktop icons in Settings; look at a fence menu | applied at once; the fence menu checkmarks agree (and the other way round) |
| P4 | Click the Peek hotkey box; press Ctrl+Shift+F9; use it; set it back to Ctrl+Alt+Space | "Saved"; the new combination opens Peek; restart keeps it |
| P5 | Record a combination another app owns (or a plain letter) | an explanation in the window; the old hotkey keeps working |
| P6 | Fences → "When you click their title"; roll up a fence; rest on it; click its title; move away | resting does nothing; the click opens it; it closes ~0.5 s after leaving |
| P7 | Same with a locked fence | the click opens it too |
| P8 | Roll up / unroll; quick-hide on/off | the roll-up slides (~0.2 s); quick-hide fades (~0.15 s) |
| P9 | Windows Settings → Accessibility → Visual effects → Animation effects off; repeat P8 | no animation, everything happens at once |
| P10 | Game mode off in Settings while a game runs | NeoFences leaves idle at once (hook back); on again → idle again |
| P11 | About: Open logs folder / Open data folder | Explorer opens `%LOCALAPPDATA%\NeoFences\logs` / `%LOCALAPPDATA%\NeoFences` |
| P12 | Narrator (or another screen reader) on the settings checkboxes | toggling works and is applied |
```

- [ ] **Step 2: Run P1–P12.**
  - The smoke covers P1 (tray), P4, P6, P8 and P10 (toggle saved).
  - **[USER]**: P2, P3, P5, P7, P9, P11, P12, and P10 with a real game.

- [ ] **Step 3: Write `docs/research/m6b-settings-animations.md`** with:
  - the results;
  - the UI Automation Toggle / Click finding;
  - the screenshot description.

- [ ] **Step 4: Commit**

```powershell
git add docs/TEST-CHECKLIST.md docs/research/m6b-settings-animations.md
git commit -m "docs: added M6b settings and animation checks and results"
```

---

### Task 4: Docs sync

- [ ] **Step 1: Append ADR-022 to `docs/DECISIONS.md`**

```markdown
## ADR-022 — Settings window, roll-up click mode, animations (M6b)
**Date:** 2026-10-03 · **Status:** Accepted · **Builds on:** ADR-015, ADR-020, ADR-021 · **Refines:** spec §6

**Context.** M6b completes spec §6: a settings window and animations. The user chose on 2026-10-03:
- roll-up offers **hover (default) and click**;
- **no Appearance section in v1**: tint and opacity preferences come with v2 themes. The user said earlier that the current tint is fine.

**Decision.**
- **Settings window** (`SettingsWindow`). It uses WPF's built-in Fluent theme, set as `ThemeMode="System"` on this window only, so it follows Windows light/dark and the accent colour while the fences keep their own look. No new dependency.
  - It opens from the tray (Settings…) and from the fence menu (Settings…). There is one window; a second request brings it to the front.
  - Sections: **General** (Start with Windows, Hide desktop icons, Peek hotkey), **Fences** (rolled-up fences open on hover or click), **Game mode** (on/off, plus whether it is idle right now), **About and logs** (version, data folder, open logs or data folder).
  - Changes apply at once and are saved; there is no OK button. Checkboxes react to Checked/Unchecked, not Click, so UI Automation and screen readers work too.
- **Peek hotkey recorder.** Click the box and press a combination: modifiers alone only preview, Esc leaves.
  - The new hotkey is kept only if Windows registers it; otherwise the old one stays and the window says why (invalid, or used by another app).
  - Hotkey text uses WPF key names: a digit is the top-row key (`Ctrl+Alt+1` → `D1`), and other numbers are rejected (M5 review M4).
- **Peek hotkey lifetime.** It is registered only while `RunState.PeekHotkeyWanted` (not paused, not gaming), so Ctrl+Alt+Space goes back to Windows and the game (M6a review M5).
- **Roll-up expansion** (`RollUpExpansion`, Core, tested). Hover opens after ~300 ms of resting. Click opens on a single click on the title: for an unlocked fence that is `WM_NCLBUTTONDOWN` on the caption, handled so it does not start a move; for a locked fence it is a client click. Both close ~500 ms after the pointer leaves.
- **Animations** (spec §6):
  - roll-up, unroll and hover/click opening move the bottom edge over 200 ms with ease-out;
  - quick-hide fades over 150 ms;
  - both are off when Windows' "Animation effects" are off (`SystemParameters.ClientAreaAnimation`) and in game mode (spec §4.7);
  - a move during a height animation jumps it to the end;
  - Pause and Peek do not animate.

**Consequences.**
- In click mode, the first press on a closed rolled-up fence opens it instead of dragging it; the next press drags as usual.
- The version shown in About is the App's `<Version>` (0.6.0); packaging sets the release version (M7).
- Appearance settings wait for v2 (FEATURES: themes).
```

- [ ] **Step 2: Update the other docs**
  - `ARCHITECTURE.md`:
    - add `App/SettingsWindow` and `Core/RollUpExpansion`;
    - replace "Next: M6b" with "M6 complete … Next: M7 (package)".
  - `FEATURES.md`:
    - "Settings window, tray" → done;
    - "Roll-up expand: hover / click" → done.
  - `ROADMAP.md`:
    - tick M6b and M6;
    - tick "peekHotkey digits" (M5 carry-over) and "Peek hotkey stays registered while paused or gaming" (M6a carry-over).
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 3: Refresh the hub** (`node --check`, publish with `url`).

- [ ] **Step 4: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-022 and synced docs for M6b"
```
