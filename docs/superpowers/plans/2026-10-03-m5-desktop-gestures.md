# M5 — Desktop Gestures Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fences' desktop gestures:
- double-click the desktop to quick-hide fences and icons;
- right-drag on the desktop to draw a new fence;
- Peek (`Ctrl+Alt+Space`) to show fences above every window;
- double-click a title to roll a fence up (it opens on hover);
- new fences take the first free spot.

**Architecture:**
- `NeoFences.Core`:
  - `DesktopGestureTracker` (raw low-level mouse events → gestures; ported from the M0 spike);
  - `Hotkey` (settings text ↔ modifiers + key);
  - `LayoutEngine.FreeSpot` (smart placement);
  - `FenceEdits.SetRolledUp`.
- `NeoFences.Shell`:
  - `DesktopMouseHook` (`WH_MOUSE_LL` thread, S2 right-click swallow/replay, Peek click-outside);
  - `DesktopWindows` (desktop / fence / native-icon hit tests);
  - `GlobalHotkey` (`RegisterHotKey`);
  - `FenceWindowChrome.SetTopmost` / `MakeOverlay` / `GetCursorPosition`.
- `NeoFences.App`:
  - `DrawFenceOverlay`;
  - `SystemMessageWindow` (WM_HOTKEY);
  - `FenceWindow` (roll-up, hover, Peek flag, `Place`);
  - `FenceHost` (quick-hide, draw, Peek, roll-up wiring).

**Tech Stack:** .NET 10, WPF, CsWin32 0.3.335, Serilog, xUnit. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-02-neofences-v1-design.md` §4.5 (desktop gestures), §4.6 (Peek), §5 (`rolledUp`, `peekHotkey`, `rollupExpand`), §6 (roll-up). **Decisions:**
- ADR-007 and ADR-011 (S2 right-click suppression);
- ADR-019;
- **ADR-020** (new, Task 5).

The user chose on 2026-10-03: quick-hide hides fences **and** desktop icons; roll-up expands on **hover**.

**Pre-verified (2026-10-03):** every code block below was compiled together (0 warnings, 0 errors), and **188/188 tests pass**. The Task 3 smoke passed on the real desktop with the user's consent, while they were remote. It covered:
- a plain right-click on the desktop (Explorer's menu still appears);
- a right-drag drawing a 300×180 fence exactly where it was drawn (saved);
- quick-hide on and off;
- Peek over a maximized Explorer window (on, Esc, click outside);
- roll-up 180 → 32 px, hover open, close after leaving, and unroll.

**Found in the prototype:**
- Only one fence rose on Peek. Raising one Progman-owned fence restacks its siblings, and a sibling still running `KeepAtBottom` dropped back. The fix: mark every fence as peeking first, then raise them.
- `SetCursorPos` never reaches `WH_MOUSE_LL`, so scripted drags must use `mouse_event`.

## Global Constraints

- **Hard rule 2:** quick-hide's hidden icons go through `SetIconsHidden` (the takeover-active marker), so the watchdog, a crash, a session end and an exit all bring them back.
- **Hard rule 3:** `WH_MOUSE_LL` is the only global hook. Peek uses `RegisterHotKey`, not a keyboard hook.
- **Hard rules 4 and 7:**
  - all Win32/COM is in `NeoFences.Shell`;
  - a hook that cannot be installed, or a hotkey that is invalid or taken, is logged and only that feature is off;
  - the hook callback never throws.
- The hook callback stays cheap: no COM, no UI, and `WindowFromPoint` only on button events. Gestures are marshalled to the UI thread.
- Commits: single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- The smokes take the mouse; ask first, and refocus Windows Terminal at the end.

## Review Focus

1. **A slow or blocked hook callback** (a heavy foreground app, debugger breaks). Expected: the mouse never lags, and Windows does not silently drop the hook. The callback does only the class/title look-ups; by hand N13.
2. **Quick-hide with Takeover off, then a crash, kill or power cut.** Expected: the icons come back (watchdog marker, then ADR-019 at sign-in). By hand N6.
3. **Takeover off, mouse on native icons.** Expected:
   - a right-click shows the icon's menu (replayed);
   - a double-click opens the icon without hiding.

   By hand N5.
4. **A rolled-up fence moved, resized, or hit by a display change.** Expected: the stored height stays full, and it unrolls where it now is. By hand N9–N10.
5. **Peek hotkey taken or invalid; Peek while an app is full-screen.** Expected: logged, Peek off, and nothing else breaks. By hand N7–N8.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Input/DesktopGestureTracker.cs`, `Input/Hotkey.cs`, `Layouts/LayoutEngine.cs`, `Model/FenceEdits.cs` | gestures, hotkey text, smart placement, roll-up edit | 1 |
| `src/NeoFences.Shell/DesktopMouseHook.cs`, `GlobalHotkey.cs`, `FenceWindowChrome.cs`, `NativeMethods.txt` | hook, hit tests, hotkey, topmost/overlay | 2 |
| `src/NeoFences.App/DrawFenceOverlay.cs`, `SystemMessageWindow.cs`, `FenceWindow.xaml(.cs)`, `FenceHost.cs` | overlay, WM_HOTKEY, roll-up/hover/Peek, wiring | 3 |
| `docs/TEST-CHECKLIST.md` (section N), `docs/research/m5-desktop-gestures.md` | verification | 4 |
| `docs/DECISIONS.md` (ADR-020), `ARCHITECTURE.md`, `FEATURES.md`, `ROADMAP.md`, `SESSION-LOG.md`, hub | docs sync | 5 |

---

### Task 1: Core — gestures, hotkey, smart placement, roll-up

**Files:**
- Modify: `docs/ROADMAP.md` (claim M5)
- Create: `src/NeoFences.Core/Input/DesktopGestureTracker.cs`, `src/NeoFences.Core/Input/Hotkey.cs`, `tests/NeoFences.Core.Tests/Input/DesktopGestureTrackerTests.cs`, `tests/NeoFences.Core.Tests/Input/HotkeyTests.cs`
- Modify (full new content): `src/NeoFences.Core/Layouts/LayoutEngine.cs`, `src/NeoFences.Core/Model/FenceEdits.cs`, `tests/NeoFences.Core.Tests/Layouts/LayoutEngineTests.cs`

**Interfaces:**
- Produces:
  - `enum MouseAction { LeftDown, RightDown, RightUp, Move }`;
  - `enum DesktopGesture { None, DoubleClick, RightDragStarted, RightDragCompleted }`;
  - `DesktopGestureTracker(uint doubleClickMilliseconds, int doubleClickSlopPixels, int dragThresholdPixels)`, with `OnMouse(MouseAction, int x, int y, uint timeMilliseconds, bool overDesktop) -> DesktopGesture` and `DragStart`;
  - `record Hotkey(bool Ctrl, bool Alt, bool Shift, bool Win, string Key)` with `TryParse(string, out Hotkey)`;
  - `LayoutEngine.FreeSpot(string monitor, MonitorArea area, IReadOnlyList<FenceRect> occupied) -> FenceRect?` and `LayoutEngine.PlacementGap`;
  - `FenceEdits.SetRolledUp(config, fenceId, bool rolledUp)`.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m5-gestures
```
In `docs/ROADMAP.md`, under `## M5 — Desktop gestures`, replace `- [ ] M5 implementation plan` with `- [x] M5 implementation plan` and add `- [~] M5 — claimed by session 2026-10-03 m5`.
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M5"
```

- [ ] **Step 2: Write the failing tests**

`tests/NeoFences.Core.Tests/Input/DesktopGestureTrackerTests.cs`:
```csharp
using NeoFences.Core.Input;

namespace NeoFences.Core.Tests.Input;

public class DesktopGestureTrackerTests
{
    private static DesktopGestureTracker NewTracker() =>
        new(doubleClickMilliseconds: 500, doubleClickSlopPixels: 2, dragThresholdPixels: 8);

    [Fact]
    public void TwoLeftDownsOnDesktopWithinTimeAndSlop_IsDoubleClick()
    {
        var tracker = NewTracker();
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: true));
        Assert.Equal(DesktopGesture.DoubleClick, tracker.OnMouse(MouseAction.LeftDown, 101, 99, 1_300, overDesktop: true));
    }

    [Fact]
    public void SecondClickTooLate_IsNotDoubleClick()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_501, overDesktop: true));
    }

    [Fact]
    public void SecondClickTooFar_IsNotDoubleClick()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 110, 100, 1_100, overDesktop: true));
    }

    [Fact]
    public void ClicksNotOverDesktop_AreIgnored()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: false);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_100, overDesktop: true));
    }

    [Fact]
    public void FirstClickOnAppThenDesktop_IsNotDoubleClick()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: true);
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_100, overDesktop: false);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_200, overDesktop: true));
    }

    [Fact]
    public void TripleClick_YieldsOneDoubleClickOnly()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.DoubleClick, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_100, overDesktop: true));
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 1_200, overDesktop: true));
    }

    [Fact]
    public void DoubleClickAcrossTickCountWrap_IsDetected()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.LeftDown, 100, 100, uint.MaxValue - 100, overDesktop: true);
        Assert.Equal(DesktopGesture.DoubleClick, tracker.OnMouse(MouseAction.LeftDown, 100, 100, 100, overDesktop: true));
    }

    [Fact]
    public void RightDragBeyondThreshold_StartsOnceThenCompletes()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.RightDown, 200, 200, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.Move, 205, 203, 1_010, overDesktop: false));
        Assert.Equal(DesktopGesture.RightDragStarted, tracker.OnMouse(MouseAction.Move, 208, 200, 1_020, overDesktop: false));
        Assert.Equal((200, 200), tracker.DragStart);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.Move, 300, 300, 1_030, overDesktop: false));
        Assert.Equal(DesktopGesture.RightDragCompleted, tracker.OnMouse(MouseAction.RightUp, 300, 300, 1_040, overDesktop: true));
        Assert.Null(tracker.DragStart);
    }

    [Fact]
    public void RightClickWithoutMovement_CompletesNothing()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.RightDown, 200, 200, 1_000, overDesktop: true);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.RightUp, 201, 200, 1_050, overDesktop: true));
    }

    [Fact]
    public void RightDragStartedOverApp_IsIgnored()
    {
        var tracker = NewTracker();
        tracker.OnMouse(MouseAction.RightDown, 200, 200, 1_000, overDesktop: false);
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.Move, 400, 400, 1_010, overDesktop: false));
        Assert.Equal(DesktopGesture.None, tracker.OnMouse(MouseAction.RightUp, 400, 400, 1_020, overDesktop: false));
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
    public void Invalid_IsRejected(string text) => Assert.False(Hotkey.TryParse(text, out _));

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
Replace `tests/NeoFences.Core.Tests/Layouts/LayoutEngineTests.cs` with (first-run placement now uses free spots; two new tests):
```csharp
using NeoFences.Core.Config;
using NeoFences.Core.Layouts;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Layouts;

public class LayoutEngineTests
{
    private static readonly DisplayMonitor Dell4K = new("DELL", 3840, 2160, 150, WorkWidth: 2560, WorkHeight: 1400, IsPrimary: true);
    private static readonly DisplayMonitor Dell1080 = new("DELL", 1920, 1080, 100, WorkWidth: 1280, WorkHeight: 700, IsPrimary: true);
    private static readonly DisplayMonitor Lg = new("LG", 1920, 1080, 100, WorkWidth: 1920, WorkHeight: 1032, IsPrimary: false);

    private static (NeoFencesConfig Config, Fence Games) ConfigWithGames()
    {
        var games = Fence.Create("Games");
        var config = NeoFencesConfig.CreateDefault();
        return (config with { Fences = [config.Inbox, games] }, games);
    }

    private static NeoFencesConfig WithSavedLayout(NeoFencesConfig config, string fingerprint, Layout layout) =>
        config with { Layouts = new Dictionary<string, Layout> { [fingerprint] = layout }, LastLayoutFingerprint = fingerprint };

    [Fact]
    public void Fingerprint_IsOrderIndependentAndReadable()
    {
        Assert.Equal("2mon:DELL-3840x2160@150%+LG-1920x1080@100%", DisplayFingerprint.Of([Lg, Dell4K]));
        Assert.Equal(DisplayFingerprint.Of([Dell4K, Lg]), DisplayFingerprint.Of([Lg, Dell4K]));
        Assert.NotEqual(DisplayFingerprint.Of([Dell4K]), DisplayFingerprint.Of([Dell1080]));
    }

    [Fact]
    public void FirstRun_PlacesFencesSideBySideOnPrimary_AndRemembersFingerprint()
    {
        var (config, games) = ConfigWithGames();

        var (resolved, layout) = LayoutEngine.Resolve(config, [Lg, Dell4K]);

        Assert.Equal(new FenceRect("DELL", 24, 24, 320, 220), layout.Fences[config.Inbox.Id]);
        // M5 smart placement: beside the Inbox with an 8 DIP gap, never stacked on top of it (the old cascade overlapped).
        Assert.Equal(new FenceRect("DELL", 352, 24, 320, 220), layout.Fences[games.Id]);
        Assert.Equal("2mon:DELL-3840x2160@150%+LG-1920x1080@100%", resolved.LastLayoutFingerprint);
        Assert.Equal(layout.Fences, resolved.Layouts[resolved.LastLayoutFingerprint!].Fences);
    }

    [Fact]
    public void KnownFingerprint_RestoresExactly()
    {
        var (config, games) = ConfigWithGames();
        var saved = new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1400), ["LG"] = new(1920, 1032) },
            Fences = new Dictionary<string, FenceRect>
            {
                [config.Inbox.Id] = new("LG", 100, 200, 400, 300),
                [games.Id] = new("DELL", 1000, 500, 420, 260),
            },
        };
        config = WithSavedLayout(config, DisplayFingerprint.Of([Dell4K, Lg]), saved);

        var (_, layout) = LayoutEngine.Resolve(config, [Dell4K, Lg]);

        Assert.Equal(saved.Fences[config.Inbox.Id], layout.Fences[config.Inbox.Id]);
        Assert.Equal(saved.Fences[games.Id], layout.Fences[games.Id]);
    }

    [Fact]
    public void NewResolution_ScalesFromLastLayout_AndKeepsOldLayoutForLater()
    {
        var (config, games) = ConfigWithGames();
        var fourKFingerprint = DisplayFingerprint.Of([Dell4K]);
        config = WithSavedLayout(config, fourKFingerprint, new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1400) },
            Fences = new Dictionary<string, FenceRect>
            {
                [config.Inbox.Id] = new("DELL", 0, 0, 640, 350),
                [games.Id] = new("DELL", 1280, 700, 512, 280),
            },
        });

        var (resolved, layout) = LayoutEngine.Resolve(config, [Dell1080]);

        Assert.Equal(new FenceRect("DELL", 0, 0, 320, 175), layout.Fences[config.Inbox.Id]);
        Assert.Equal(new FenceRect("DELL", 640, 350, 256, 140), layout.Fences[games.Id]);
        Assert.True(resolved.Layouts.ContainsKey(fourKFingerprint)); // going back to 4K restores the original

        var (_, backTo4K) = LayoutEngine.Resolve(resolved, [Dell4K]);
        Assert.Equal(new FenceRect("DELL", 1280, 700, 512, 280), backTo4K.Fences[games.Id]);
    }

    [Fact]
    public void MonitorUnplugged_FenceMovesToPrimary_ScaledAndFullyOnScreen()
    {
        var (config, games) = ConfigWithGames();
        config = WithSavedLayout(config, DisplayFingerprint.Of([Dell1080, Lg]), new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(1280, 700), ["LG"] = new(1920, 1032) },
            Fences = new Dictionary<string, FenceRect> { [games.Id] = new("LG", 1800, 900, 1900, 1000) },
        });

        var (_, layout) = LayoutEngine.Resolve(config, [Dell1080]);

        var rect = layout.Fences[games.Id];
        Assert.Equal("DELL", rect.Monitor);
        Assert.True(rect.X >= 0 && rect.Y >= 0, "fence starts on-screen");
        Assert.True(rect.X + rect.W <= 1280 && rect.Y + rect.H <= 700, "fence ends on-screen");
    }

    [Fact]
    public void DeletedFences_AreDroppedFromLayout_NewFencesGetPlaced()
    {
        var (config, games) = ConfigWithGames();
        var fingerprint = DisplayFingerprint.Of([Dell4K]);
        config = WithSavedLayout(config, fingerprint, new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1400) },
            Fences = new Dictionary<string, FenceRect>
            {
                [config.Inbox.Id] = new("DELL", 500, 500, 300, 200),
                ["deleted-fence"] = new("DELL", 0, 0, 300, 200),
            },
        });

        var (_, layout) = LayoutEngine.Resolve(config, [Dell4K]);

        Assert.False(layout.Fences.ContainsKey("deleted-fence"));
        Assert.Equal(new FenceRect("DELL", 500, 500, 300, 200), layout.Fences[config.Inbox.Id]);
        Assert.Equal(new FenceRect("DELL", 24, 24, 320, 220), layout.Fences[games.Id]);
    }

    [Theory]
    [InlineData(-50, -50, 300, 200, 0, 0, 300, 200)]          // off the top-left
    [InlineData(1200, 650, 300, 200, 980, 500, 300, 200)]     // off the bottom-right
    [InlineData(10, 10, 50, 20, 10, 10, 120, 60)]             // below minimum size
    [InlineData(10, 10, 5000, 3000, 0, 0, 1280, 700)]         // larger than the work area
    public void Clamp_KeepsFenceInsideWorkArea(double x, double y, double w, double h, double expectedX, double expectedY, double expectedW, double expectedH)
    {
        var clamped = LayoutEngine.Clamp(new FenceRect("DELL", x, y, w, h), new MonitorArea(1280, 700));

        Assert.Equal(new FenceRect("DELL", expectedX, expectedY, expectedW, expectedH), clamped);
    }

    [Fact]
    public void NoMonitors_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => LayoutEngine.Resolve(NeoFencesConfig.CreateDefault(), []));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(double.NaN, 700)]
    [InlineData(-1280, 700)]
    [InlineData(double.PositiveInfinity, 700)]
    public void InvalidWorkArea_IsRejected(double workWidth, double workHeight)
    {
        // A monitor query during an Explorer restart or driver reset can return garbage; it must never be saved.
        var badMonitor = Dell1080 with { WorkWidth = workWidth, WorkHeight = workHeight };

        Assert.Throws<ArgumentException>(() => LayoutEngine.Resolve(NeoFencesConfig.CreateDefault(), [badMonitor]));
    }

    [Fact]
    public void SavedMonitorAreaOfZero_ScalesAsOne_AndNeverProducesNonFiniteRects()
    {
        var (config, games) = ConfigWithGames();
        config = WithSavedLayout(config, "old", new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(0, 0) },
            Fences = new Dictionary<string, FenceRect> { [games.Id] = new("DELL", 0, 0, 300, 200) },
        });

        var (resolved, layout) = LayoutEngine.Resolve(config, [Dell1080]);

        Assert.Equal(new FenceRect("DELL", 0, 0, 300, 200), layout.Fences[games.Id]);
        ConfigJson.Serialize(resolved); // throws on NaN/Infinity
    }

    [Fact]
    public void KnownLayout_StoredRectsStayUnclamped_DisplayRectsAreClamped()
    {
        // Review deferral: a temporarily smaller work area (taskbar moved) must not shift stored positions for good.
        var (config, games) = ConfigWithGames();
        var fingerprint = DisplayFingerprint.Of([Dell1080]);
        config = WithSavedLayout(config, fingerprint, new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(1280, 700) },
            Fences = new Dictionary<string, FenceRect> { [games.Id] = new("DELL", 1000, 500, 300, 200) },
        });
        var narrowerWorkArea = Dell1080 with { WorkWidth = 1200 };

        var (resolved, layout) = LayoutEngine.Resolve(config, [narrowerWorkArea]);

        Assert.Equal(new FenceRect("DELL", 900, 500, 300, 200), layout.Fences[games.Id]);
        Assert.Equal(new FenceRect("DELL", 1000, 500, 300, 200), resolved.Layouts[fingerprint].Fences[games.Id]);
    }

    [Fact]
    public void WithFenceRect_StoresMovedRectUnderCurrentFingerprint()
    {
        var (config, games) = ConfigWithGames();
        var (resolved, _) = LayoutEngine.Resolve(config, [Dell1080]);
        var moved = new FenceRect("DELL", 700, 300, 400, 250);

        var updated = LayoutEngine.WithFenceRect(resolved, fingerprint: resolved.LastLayoutFingerprint!, fenceId: games.Id, rect: moved);

        Assert.Equal(moved, updated.Layouts[resolved.LastLayoutFingerprint!].Fences[games.Id]);
        Assert.Equal(resolved.Layouts[resolved.LastLayoutFingerprint!].Fences[config.Inbox.Id], updated.Layouts[resolved.LastLayoutFingerprint!].Fences[config.Inbox.Id]);
    }

    [Fact]
    public void NewFence_GoesToTheNextRow_WhenTheTopRowIsFull()
    {
        var (config, games) = ConfigWithGames();
        var fingerprint = DisplayFingerprint.Of([Dell1080]);
        config = WithSavedLayout(config, fingerprint, new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(1280, 700) },
            Fences = new Dictionary<string, FenceRect>
            {
                [config.Inbox.Id] = new("DELL", 24, 24, 1232, 220), // the whole top row
            },
        });

        var (_, layout) = LayoutEngine.Resolve(config, [Dell1080]);

        Assert.Equal(new FenceRect("DELL", 24, 252, 320, 220), layout.Fences[games.Id]);
    }

    [Fact]
    public void NewFence_OnAFullScreen_StillLandsOnScreen()
    {
        var (config, games) = ConfigWithGames();
        var fingerprint = DisplayFingerprint.Of([Dell1080]);
        config = WithSavedLayout(config, fingerprint, new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(1280, 700) },
            Fences = new Dictionary<string, FenceRect> { [config.Inbox.Id] = new("DELL", 0, 0, 1280, 700) },
        });

        var (_, layout) = LayoutEngine.Resolve(config, [Dell1080]);

        var rect = layout.Fences[games.Id];
        Assert.True(rect.X >= 0 && rect.Y >= 0 && rect.X + rect.W <= 1280 && rect.Y + rect.H <= 700);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS with `CS0234: The type or namespace name 'Input' does not exist in the namespace 'NeoFences.Core'`.

- [ ] **Step 4: Implement**

`src/NeoFences.Core/Input/DesktopGestureTracker.cs`:
```csharp
namespace NeoFences.Core.Input;

public enum MouseAction { LeftDown, RightDown, RightUp, Move }

public enum DesktopGesture { None, DoubleClick, RightDragStarted, RightDragCompleted }

/// <summary>
/// Pure state machine turning raw low-level mouse events into desktop gestures.
/// Low-level hooks never see WM_LBUTTONDBLCLK, so double-clicks are rebuilt from two LeftDowns.
/// </summary>
public sealed class DesktopGestureTracker(uint doubleClickMilliseconds, int doubleClickSlopPixels, int dragThresholdPixels)
{
    private (int X, int Y, uint Time)? _lastLeftDown;
    private (int X, int Y)? _rightDownAt;
    private bool _rightDragging;

    public (int X, int Y)? DragStart => _rightDownAt;

    public DesktopGesture OnMouse(MouseAction action, int x, int y, uint timeMilliseconds, bool overDesktop)
    {
        switch (action)
        {
            case MouseAction.LeftDown:
                if (!overDesktop)
                {
                    _lastLeftDown = null;
                    return DesktopGesture.None;
                }
                if (_lastLeftDown is { } previous
                    && unchecked(timeMilliseconds - previous.Time) <= doubleClickMilliseconds
                    && Math.Abs(x - previous.X) <= doubleClickSlopPixels
                    && Math.Abs(y - previous.Y) <= doubleClickSlopPixels)
                {
                    _lastLeftDown = null;
                    return DesktopGesture.DoubleClick;
                }
                _lastLeftDown = (x, y, timeMilliseconds);
                return DesktopGesture.None;

            case MouseAction.RightDown:
                _rightDownAt = overDesktop ? (x, y) : null;
                _rightDragging = false;
                return DesktopGesture.None;

            case MouseAction.Move:
                if (_rightDownAt is { } start && !_rightDragging
                    && (Math.Abs(x - start.X) >= dragThresholdPixels || Math.Abs(y - start.Y) >= dragThresholdPixels))
                {
                    _rightDragging = true;
                    return DesktopGesture.RightDragStarted;
                }
                return DesktopGesture.None;

            case MouseAction.RightUp:
                var completedDrag = _rightDragging;
                _rightDownAt = null;
                _rightDragging = false;
                return completedDrag ? DesktopGesture.RightDragCompleted : DesktopGesture.None;

            default:
                return DesktopGesture.None;
        }
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
Replace `src/NeoFences.Core/Layouts/LayoutEngine.cs` with:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Layouts;

/// <summary>
/// Picks fence rectangles for the current display configuration (spec §5): a known fingerprint is
/// restored exactly; an unknown one is derived from the last layout, scaled per monitor, clamped
/// on-screen and saved under the new fingerprint.
/// </summary>
public static class LayoutEngine
{
    public const double DefaultWidth = 320;
    public const double DefaultHeight = 220;
    public const double MinWidth = 120;
    public const double MinHeight = 60;
    public const double NewFenceMargin = 24;
    public const double CascadeStep = 32;
    /// <summary>Space kept between a new fence and the others (the snapping gap, spec §6 smart placement).</summary>
    public const double PlacementGap = 8;

    public static (NeoFencesConfig Config, Layout Layout) Resolve(NeoFencesConfig config, IReadOnlyList<DisplayMonitor> monitors)
    {
        if (monitors.Count == 0) throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        // A monitor query during an Explorer restart or driver reset can return garbage: never let it reach a saved layout.
        if (monitors.FirstOrDefault(monitor => !new MonitorArea(monitor.WorkWidth, monitor.WorkHeight).IsUsable) is { } unusable)
        {
            throw new ArgumentException($"Monitor {unusable.DeviceId} reported an unusable work area {unusable.WorkWidth}x{unusable.WorkHeight}.", nameof(monitors));
        }

        var fingerprint = DisplayFingerprint.Of(monitors);
        var areas = monitors.ToDictionary(monitor => monitor.DeviceId, monitor => new MonitorArea(monitor.WorkWidth, monitor.WorkHeight));
        var primary = monitors.FirstOrDefault(monitor => monitor.IsPrimary) ?? monitors[0];

        var rects = config.Layouts.TryGetValue(fingerprint, out var known)
            ? new Dictionary<string, FenceRect>(known.Fences)
            : config.LastLayoutFingerprint is { } lastFingerprint && config.Layouts.TryGetValue(lastFingerprint, out var previous)
                ? Adapt(previous: previous, areas: areas, primaryId: primary.DeviceId)
                : new Dictionary<string, FenceRect>();

        var fenceIds = config.Fences.Select(fence => fence.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var removedFenceId in rects.Keys.Where(fenceId => !fenceIds.Contains(fenceId)).ToList())
        {
            rects.Remove(removedFenceId);
        }

        // Stored rects stay as the user left them; only the returned (display) layout is clamped, so a
        // temporarily smaller work area (taskbar moved, resolution blip) never shifts positions for good.
        var displayRects = new Dictionary<string, FenceRect>();
        var cascadeIndex = 0;
        foreach (var fence in config.Fences)
        {
            if (!rects.TryGetValue(fence.Id, out var rect) || !areas.ContainsKey(rect.Monitor))
            {
                var occupied = rects.Values.Where(placed => placed.Monitor == primary.DeviceId).ToList();
                var offset = NewFenceMargin + CascadeStep * cascadeIndex++;
                rect = FreeSpot(primary.DeviceId, areas[primary.DeviceId], occupied)
                       ?? new FenceRect(primary.DeviceId, offset, offset, DefaultWidth, DefaultHeight); // full screen: cascade, clamped below
                rects[fence.Id] = rect;
            }
            displayRects[fence.Id] = Clamp(rect, areas[rect.Monitor]);
        }

        var storedLayout = new Layout { Monitors = areas, Fences = rects };
        var layouts = new Dictionary<string, Layout>(config.Layouts) { [fingerprint] = storedLayout };
        return (config with { Layouts = layouts, LastLayoutFingerprint = fingerprint }, new Layout { Monitors = areas, Fences = displayRects });
    }

    /// <summary>
    /// Smart placement (M5): the first spot, reading top-left to bottom-right, where a default-size fence fits on the
    /// monitor without touching any other (keeping <see cref="PlacementGap"/>). Candidates are the margin and the spots
    /// just right of / below existing fences, so new fences line up with them. Null when the monitor is full.
    /// </summary>
    public static FenceRect? FreeSpot(string monitor, MonitorArea area, IReadOnlyList<FenceRect> occupied)
    {
        var columns = occupied.Select(other => other.X + other.W + PlacementGap).Prepend(NewFenceMargin).Distinct().Order().ToList();
        var rows = occupied.Select(other => other.Y + other.H + PlacementGap).Prepend(NewFenceMargin).Distinct().Order().ToList();
        foreach (var y in rows)
        {
            foreach (var x in columns)
            {
                if (x + DefaultWidth > area.WorkWidth || y + DefaultHeight > area.WorkHeight) continue;
                var touches = occupied.Any(other =>
                    x < other.X + other.W + PlacementGap && x + DefaultWidth + PlacementGap > other.X
                    && y < other.Y + other.H + PlacementGap && y + DefaultHeight + PlacementGap > other.Y);
                if (!touches) return new FenceRect(monitor, x, y, DefaultWidth, DefaultHeight);
            }
        }
        return null;
    }

    /// <summary>Stores a fence's rect (e.g. after the user moved it) under <paramref name="fingerprint"/>.</summary>
    public static NeoFencesConfig WithFenceRect(NeoFencesConfig config, string fingerprint, string fenceId, FenceRect rect)
    {
        var layout = config.Layouts.TryGetValue(fingerprint, out var existing) ? existing : new Layout();
        var fences = new Dictionary<string, FenceRect>(layout.Fences) { [fenceId] = rect };
        var layouts = new Dictionary<string, Layout>(config.Layouts) { [fingerprint] = layout with { Fences = fences } };
        return config with { Layouts = layouts };
    }

    /// <summary>Keeps each fence on its monitor if that monitor still exists (else the primary), scaled to the new work area.</summary>
    private static Dictionary<string, FenceRect> Adapt(Layout previous, IReadOnlyDictionary<string, MonitorArea> areas, string primaryId)
    {
        var adapted = new Dictionary<string, FenceRect>();
        foreach (var (fenceId, rect) in previous.Fences)
        {
            var targetId = areas.ContainsKey(rect.Monitor) ? rect.Monitor : primaryId;
            var target = areas[targetId];
            var source = previous.Monitors.TryGetValue(rect.Monitor, out var savedArea) && savedArea.IsUsable ? savedArea : target;
            var scaleX = target.WorkWidth / source.WorkWidth;
            var scaleY = target.WorkHeight / source.WorkHeight;
            adapted[fenceId] = new FenceRect(targetId, rect.X * scaleX, rect.Y * scaleY, rect.W * scaleX, rect.H * scaleY);
        }
        return adapted;
    }

    /// <summary>Fits the rect inside the work area: never larger than it, never past its edges.</summary>
    public static FenceRect Clamp(FenceRect rect, MonitorArea area)
    {
        var width = Math.Min(Math.Max(rect.W, MinWidth), area.WorkWidth);
        var height = Math.Min(Math.Max(rect.H, MinHeight), area.WorkHeight);
        var x = Math.Clamp(rect.X, 0, area.WorkWidth - width);
        var y = Math.Clamp(rect.Y, 0, area.WorkHeight - height);
        return rect with { X = x, Y = y, W = width, H = height };
    }
}
```
Replace `src/NeoFences.Core/Model/FenceEdits.cs` with:
```csharp
using NeoFences.Core.Config;

namespace NeoFences.Core.Model;

/// <summary>Changes to one fence's own settings (title, icon size, lock). Pure: each returns a new config.</summary>
public static class FenceEdits
{
    public const int MaxTitleLength = 64;

    /// <summary>Trims and cuts the title; a blank title keeps the old one (returns the same config).</summary>
    public static NeoFencesConfig Rename(NeoFencesConfig config, string fenceId, string title)
    {
        var fence = Require(config, fenceId);
        var trimmed = title.Trim();
        if (trimmed.Length == 0) return config;
        return config.WithFence(fence with { Title = trimmed.Length > MaxTitleLength ? trimmed[..MaxTitleLength] : trimmed });
    }

    /// <exception cref="ArgumentOutOfRangeException">Not one of <see cref="ConfigNormalizer.IconSizes"/>.</exception>
    public static NeoFencesConfig SetIconSize(NeoFencesConfig config, string fenceId, int iconSize)
    {
        var fence = Require(config, fenceId);
        if (!ConfigNormalizer.IconSizes.Contains(iconSize)) throw new ArgumentOutOfRangeException(nameof(iconSize), iconSize, "Unsupported icon size.");
        return config.WithFence(fence with { IconSize = iconSize });
    }

    public static NeoFencesConfig SetLocked(NeoFencesConfig config, string fenceId, bool locked) =>
        config.WithFence(Require(config, fenceId) with { Locked = locked });

    /// <summary>Roll-up (M5): the fence shows only its title bar until hovered (spec §6, Settings.RollupExpand).</summary>
    public static NeoFencesConfig SetRolledUp(NeoFencesConfig config, string fenceId, bool rolledUp) =>
        config.WithFence(Require(config, fenceId) with { RolledUp = rolledUp });

    public static NeoFencesConfig SetSort(NeoFencesConfig config, string fenceId, FenceSort sort) =>
        config.WithFence(Require(config, fenceId) with { Sort = sort });

    /// <summary>
    /// "Sort by" on a desktop fence: a one-time reorder (dragging still works afterwards). The new order must hold exactly
    /// the fence's items (compared ignoring case; the stored spelling is kept), so a sort can never drop or add an item.
    /// </summary>
    /// <exception cref="ArgumentException">The order is not a permutation of the fence's items.</exception>
    public static NeoFencesConfig SetItemOrder(NeoFencesConfig config, string fenceId, IReadOnlyList<string> orderedRefs)
    {
        var fence = Require(config, fenceId);
        var spelling = fence.Items.ToDictionary(itemRef => itemRef, ItemRef.Comparer);
        if (orderedRefs.Count != fence.Items.Count || orderedRefs.Distinct(ItemRef.Comparer).Count() != orderedRefs.Count
            || !orderedRefs.All(spelling.ContainsKey))
            throw new ArgumentException("The new order must contain exactly the fence's items.", nameof(orderedRefs));
        return config.WithFence(fence with { Items = orderedRefs.Select(itemRef => spelling[itemRef]).ToList() });
    }

    private static Fence Require(NeoFencesConfig config, string fenceId) =>
        config.Fences.FirstOrDefault(fence => fence.Id == fenceId) ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 188`

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core tests/NeoFences.Core.Tests
git commit -m "feat: added desktop gesture tracking, hotkey parsing, smart placement and roll-up to core"
```

---

### Task 2: Shell — mouse hook, hotkey, topmost and overlay chrome

**Files:**
- Create: `src/NeoFences.Shell/DesktopMouseHook.cs`, `src/NeoFences.Shell/GlobalHotkey.cs`
- Modify (full new content): `src/NeoFences.Shell/FenceWindowChrome.cs`, `NativeMethods.txt`

**Interfaces:**
- Consumes: Task 1 (`DesktopGestureTracker`, `Hotkey`).
- Produces:
  - `DesktopMouseHook(Action<DesktopGesture,int,int> onGesture, Action onPeekClickOutside, Action<string> log)`, with `PeekActive`, `IsInstalled`, `DragPoint` and `Dispose()`. `RightDragStarted` reports the press point.
  - `DesktopWindows.IsOverDesktop(Point)`, `IsOverFence(Point)` and `IsOverDesktopIcon(int x, int y, Action<string> log)`.
  - `GlobalHotkey(nint windowHandle, int id)`, with `TryRegister(Hotkey, uint virtualKey)`, `WmHotkey` and `Dispose()`.
  - `FenceWindowChrome.SetTopmost(nint, bool)`, `MakeOverlay(nint)` and `GetCursorPosition()`.

- [ ] **Step 1: Files**

`src/NeoFences.Shell/NativeMethods.txt`:
```
// Win32 APIs used by NeoFences.Shell. CsWin32 generates bindings for each name.
AllowSetForegroundWindow
BHID_SFObject
BITMAP
BITMAPINFO
CallNextHookEx
CLIPBOARD_FORMAT
CLSID_DragDropHelper
CMF_CANRENAME
CMF_EXTENDEDVERBS
CMF_NORMAL
CMIC_MASK_PTINVOKE
CMINVOKECOMMANDINFOEX
CreatePopupMenu
CreateRoundRectRgn
CUIAutomation
DeleteObject
DestroyMenu
DIB_USAGE
DISPLAY_DEVICEW
DragQueryFile
DROPEFFECT
DVASPECT
EnumDisplayDevices
EnumDisplayMonitors
FileOpenDialog
FILEOPENDIALOGOPTIONS
FileOperation
FILEOPERATION_FLAGS
FindWindow
FOLDERFLAGS
FORMATETC
GCS_VERBW
GET_ANCESTOR_FLAGS
GET_WINDOW_CMD
GetAncestor
GetClassName
GetCurrentThreadId
GetCursorPos
GetDC
GetDIBits
GetDoubleClickTime
GetDpiForMonitor
GetMessage
GetModuleHandle
GetMonitorInfo
GetObject
GetSystemMetrics
GetWindow
GetWindowLongPtr
GetWindowRect
GetWindowText
HDROP
HOT_KEY_MODIFIERS
HWND_BOTTOM
HWND_NOTOPMOST
HWND_TOPMOST
IContextMenu
IContextMenu2
IContextMenu3
IDataObject
IDropTarget
IDropTargetHelper
IFileOpenDialog
IFileOperation
IFolderView2
INPUT
IServiceProvider
IShellBrowser
IShellFolder
IShellItem
IShellItemImageFactory
IShellView
IShellWindows
IUIAutomation
IUIAutomationElement
MODIFIERKEYS_FLAGS
MONITOR_DPI_TYPE
MONITORINFOEXW
MONITORINFOF_PRIMARY
MSLLHOOKSTRUCT
POINTL
PostThreadMessage
RegisterDragDrop
RegisterHotKey
RegisterWindowMessage
ReleaseDC
ReleaseStgMedium
RevokeDragDrop
SendInput
SET_WINDOW_POS_FLAGS
SetForegroundWindow
SetWindowLongPtr
SetWindowPos
SetWindowRgn
SetWindowsHookEx
SFGAO_FLAGS
SHCreateItemFromParsingName
SHDoDragDrop
ShellWindows
SHGetDesktopFolder
SHOW_WINDOW_CMD
SID_STopLevelBrowser
SIGDN
SIIGBF
STGMEDIUM
SYSTEM_METRICS_INDEX
TRACK_POPUP_MENU_FLAGS
TrackPopupMenuEx
TYMED
UIA_CONTROLTYPE_ID
UnregisterHotKey
WINDOW_EX_STYLE
WINDOW_LONG_PTR_INDEX
WINDOW_STYLE
WindowFromPoint
WINDOWPOS
WINDOWS_HOOK_ID
WM_APP
WM_LBUTTONDOWN
WM_MOUSEMOVE
WM_QUIT
WM_RBUTTONDOWN
WM_RBUTTONUP
```
`src/NeoFences.Shell/DesktopMouseHook.cs`:
```csharp
using NeoFences.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>
/// Desktop gestures (spec §4.5, M5): a <c>WH_MOUSE_LL</c> hook on its own thread with its own message loop — the only
/// global hook NeoFences uses (hard rule 3; game mode removes it in M6). It acts only when the pointer is over the
/// desktop itself (not a fence or an app):
/// <list type="bullet">
/// <item>double-click → <see cref="DesktopGesture.DoubleClick"/> (quick-hide);</item>
/// <item>right-drag of 8 px or more → <see cref="DesktopGesture.RightDragStarted"/> / <see cref="DesktopGesture.RightDragCompleted"/>
/// (draw a fence). Every desktop right-press is swallowed and, if it was a plain click, replayed so Explorer's desktop
/// menu still appears (S2, proven in M0; S1 broke Explorer's desktop state).</item>
/// </list>
/// While <see cref="PeekActive"/>, a click outside the fences raises <see cref="PeekClickOutside"/> (and goes through).
/// Callbacks run on the hook thread: marshal to the UI thread and return fast.
/// </summary>
public sealed class DesktopMouseHook : IDisposable
{
    /// <summary>dwExtraInfo stamped on the replayed right-click so the hook lets it through ("NFNC").</summary>
    private const nuint ReplayMarker = 0x4E464E43;

    private static readonly uint ReplayRightClickMessage = PInvoke.WM_APP + 1;

    private readonly HOOKPROC _hookCallback; // the field keeps the delegate alive while the hook exists
    private readonly DesktopGestureTracker _tracker;
    private readonly Action<DesktopGesture, int, int> _onGesture;
    private readonly Action _onPeekClickOutside;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();
    private uint _threadId;
    private bool _swallowedRightDown;
    private long _dragPoint; // latest pointer position during a right-drag, packed (x << 32 | y), read by the overlay

    /// <summary>Set by the UI while fences are shown over windows (Peek).</summary>
    public volatile bool PeekActive;

    public bool IsInstalled { get; private set; }

    /// <summary>The pointer during a right-drag (screen pixels), for the draw-a-fence overlay.</summary>
    public (int X, int Y) DragPoint
    {
        get
        {
            var packed = Interlocked.Read(ref _dragPoint);
            return ((int)(packed >> 32), (int)(packed & 0xFFFFFFFF));
        }
    }

    public DesktopMouseHook(Action<DesktopGesture, int, int> onGesture, Action onPeekClickOutside, Action<string> log)
    {
        _onGesture = onGesture;
        _onPeekClickOutside = onPeekClickOutside;
        _hookCallback = OnLowLevelMouse;
        _tracker = new DesktopGestureTracker(
            doubleClickMilliseconds: PInvoke.GetDoubleClickTime(),
            doubleClickSlopPixels: PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXDOUBLECLK) / 2,
            dragThresholdPixels: 8);
        _thread = new Thread(() => RunHookLoop(log)) { IsBackground = true, Name = "NeoFences.MouseHook" };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(5));
    }

    private void RunHookLoop(Action<string> log)
    {
        _threadId = PInvoke.GetCurrentThreadId();
        using var module = PInvoke.GetModuleHandle((string?)null);
        using var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _hookCallback, module, 0);
        IsInstalled = !hook.IsInvalid;
        log(IsInstalled ? "desktop gestures: WH_MOUSE_LL installed" : "desktop gestures: WH_MOUSE_LL could not be installed");
        _started.Set();
        if (!IsInstalled) return;

        while (PInvoke.GetMessage(out MSG message, HWND.Null, 0, 0) > 0)
        {
            if (message.message == ReplayRightClickMessage) ReplayRightClick();
        }
        log("desktop gestures: WH_MOUSE_LL removed");
    }

    private unsafe LRESULT OnLowLevelMouse(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code < 0) return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
        try
        {
            var hookData = (MSLLHOOKSTRUCT*)lParam.Value;
            if (hookData->dwExtraInfo == ReplayMarker) return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);

            MouseAction? action = (uint)wParam.Value switch
            {
                PInvoke.WM_LBUTTONDOWN => MouseAction.LeftDown,
                PInvoke.WM_RBUTTONDOWN => MouseAction.RightDown,
                PInvoke.WM_RBUTTONUP => MouseAction.RightUp,
                PInvoke.WM_MOUSEMOVE => MouseAction.Move,
                _ => null,
            };
            if (action is null) return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);

            var point = hookData->pt;
            if (action == MouseAction.Move && _tracker.DragStart is not null)
                Interlocked.Exchange(ref _dragPoint, ((long)point.X << 32) | (uint)point.Y);

            // WindowFromPoint only on button events: moves arrive at up to 1000 Hz and must stay cheap.
            var overDesktop = action != MouseAction.Move && DesktopWindows.IsOverDesktop(point);
            if (PeekActive && action is MouseAction.LeftDown or MouseAction.RightDown && !DesktopWindows.IsOverFence(point))
                _onPeekClickOutside();

            var gesture = _tracker.OnMouse(action.Value, point.X, point.Y, hookData->time, overDesktop);
            // RightDragStarted reports where the drag began (the press), every other gesture the pointer now.
            var (gestureX, gestureY) = gesture == DesktopGesture.RightDragStarted && _tracker.DragStart is { } start ? start : (point.X, point.Y);
            if (gesture != DesktopGesture.None) _onGesture(gesture, gestureX, gestureY);

            // S2: swallow every desktop right-press; replay it as a plain right-click if it did not become a drag.
            if (action == MouseAction.RightDown && overDesktop)
            {
                Interlocked.Exchange(ref _dragPoint, ((long)point.X << 32) | (uint)point.Y);
                _swallowedRightDown = true;
                return (LRESULT)1;
            }
            if (action == MouseAction.RightUp && _swallowedRightDown)
            {
                _swallowedRightDown = false;
                // SendInput re-enters low-level hooks, so replay from the message loop, not inside this callback.
                if (gesture != DesktopGesture.RightDragCompleted) PInvoke.PostThreadMessage(_threadId, ReplayRightClickMessage, 0, 0);
                return (LRESULT)1;
            }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // Never let an exception unwind into Windows' input pipeline: drop the gesture, keep the mouse working.
        }
        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    private static void ReplayRightClick()
    {
        Span<INPUT> inputs =
        [
            new INPUT { type = INPUT_TYPE.INPUT_MOUSE, Anonymous = { mi = new MOUSEINPUT { dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN, dwExtraInfo = ReplayMarker } } },
            new INPUT { type = INPUT_TYPE.INPUT_MOUSE, Anonymous = { mi = new MOUSEINPUT { dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP, dwExtraInfo = ReplayMarker } } },
        ];
        PInvoke.SendInput(inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        if (_threadId != 0) PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(1));
        _started.Dispose();
    }
}

/// <summary>What is under a screen point: the desktop itself, or a NeoFences fence.</summary>
public static class DesktopWindows
{
    private static unsafe string ClassOf(HWND hwnd)
    {
        char* buffer = stackalloc char[256];
        var length = PInvoke.GetClassName(hwnd, buffer, 256);
        return new string(buffer, 0, length);
    }

    private static unsafe string TitleOf(HWND hwnd)
    {
        char* buffer = stackalloc char[64];
        var length = PInvoke.GetWindowText(hwnd, buffer, 64);
        return new string(buffer, 0, length);
    }

    private static HWND RootAt(System.Drawing.Point screenPoint) =>
        PInvoke.GetAncestor(PInvoke.WindowFromPoint(screenPoint), GET_ANCESTOR_FLAGS.GA_ROOT);

    /// <summary>Progman (24H2+ icons host) or WorkerW (older icons host, wallpaper layer): the desktop itself.</summary>
    public static bool IsOverDesktop(System.Drawing.Point screenPoint) => ClassOf(RootAt(screenPoint)) is "Progman" or "WorkerW";

    public static bool IsOverFence(System.Drawing.Point screenPoint) => TitleOf(RootAt(screenPoint)) == "NeoFences fence";

    /// <summary>
    /// A visible native desktop icon is under the point (UI Automation list item). A double-click there opens the
    /// icon, so it must not also quick-hide. Cross-process COM: call from the UI thread, never inside the hook.
    /// </summary>
    public static bool IsOverDesktopIcon(int x, int y, Action<string> log)
    {
        Windows.Win32.UI.Accessibility.IUIAutomation? automation = null;
        Windows.Win32.UI.Accessibility.IUIAutomationElement? element = null;
        try
        {
            automation = (Windows.Win32.UI.Accessibility.IUIAutomation)Activator.CreateInstance(
                Type.GetTypeFromCLSID(typeof(Windows.Win32.UI.Accessibility.CUIAutomation).GUID)!)!;
            element = automation.ElementFromPoint(new System.Drawing.Point(x, y));
            return element.CurrentControlType == Windows.Win32.UI.Accessibility.UIA_CONTROLTYPE_ID.UIA_ListItemControlTypeId;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            log($"desktop gestures: icon hit-test failed ({failure.Message}); treating the point as empty desktop");
            return false;
        }
        finally
        {
            if (element is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(element);
            if (automation is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(automation);
        }
    }
}
```
`src/NeoFences.Shell/GlobalHotkey.cs`:
```csharp
using NeoFences.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace NeoFences.Shell;

/// <summary>A system-wide hotkey (RegisterHotKey, no hook), delivered as WM_HOTKEY to <paramref name="windowHandle"/> (Peek, spec §4.6).</summary>
public sealed class GlobalHotkey(nint windowHandle, int id) : IDisposable
{
    public const int WmHotkey = 0x0312;

    public int Id => id;

    /// <param name="virtualKey">The key's virtual-key code (the App maps <see cref="Hotkey.Key"/> with WPF's KeyInterop).</param>
    /// <returns>False when another app already owns this combination (the user can pick another in settings).</returns>
    public bool TryRegister(Hotkey hotkey, uint virtualKey)
    {
        var modifiers = HOT_KEY_MODIFIERS.MOD_NOREPEAT
            | (hotkey.Ctrl ? HOT_KEY_MODIFIERS.MOD_CONTROL : 0)
            | (hotkey.Alt ? HOT_KEY_MODIFIERS.MOD_ALT : 0)
            | (hotkey.Shift ? HOT_KEY_MODIFIERS.MOD_SHIFT : 0)
            | (hotkey.Win ? HOT_KEY_MODIFIERS.MOD_WIN : 0);
        return PInvoke.RegisterHotKey((HWND)windowHandle, id, modifiers, virtualKey);
    }

    public void Dispose() => PInvoke.UnregisterHotKey((HWND)windowHandle, id);
}
```
`src/NeoFences.Shell/FenceWindowChrome.cs`:
```csharp
using System.Runtime.InteropServices;
using NeoFences.Core.Layouts;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>Native look and placement of a fence window: tool-window styles, accent blur, rounded corners, pixel placement.</summary>
public static class FenceWindowChrome
{
    /// <summary>
    /// Tint (AABBGGRR) passed with the accent blur. M2c found Windows ignores it for ACCENT_ENABLE_BLURBEHIND (changing it
    /// changed nothing); the light-mode veil is drawn by the fence itself (ADR-015).
    /// </summary>
    public const uint DefaultTint = 0x40201A16;

    /// <summary>
    /// Not in the taskbar or Alt+Tab, not activated just by being shown, and no maximize/minimize boxes: without
    /// them a double-click on the title (a Fences habit, roll-up in M5) or Aero Snap cannot maximize the fence
    /// and save a full-screen rect (M2a review).
    /// </summary>
    public static void ApplyToolWindowStyles(nint handle)
    {
        var style = PInvoke.GetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        PInvoke.SetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_STYLE,
            style & ~(nint)(WINDOW_STYLE.WS_MAXIMIZEBOX | WINDOW_STYLE.WS_MINIMIZEBOX));
        var exStyle = PInvoke.GetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
            exStyle | (nint)(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE));
    }

    /// <summary>Blur of whatever is behind the window, focus-independent. Requires a layered (AllowsTransparency) window.</summary>
    /// <returns>False if Windows rejected the call; the fence then shows its plain tint (fallback).</returns>
    public static unsafe bool ApplyAccentBlur(nint handle, uint tintAbgr = DefaultTint)
    {
        var policy = new AccentPolicy { AccentState = AccentEnableBlurBehind, GradientColor = tintAbgr };
        var data = new WindowCompositionAttributeData { Attribute = WcaAccentPolicy, Data = (nint)(&policy), SizeOfData = sizeof(AccentPolicy) };
        return SetWindowCompositionAttribute(handle, ref data) != 0;
    }

    /// <summary>Clips the window (and its blur) to a rounded rectangle. Call again after every resize.</summary>
    public static void ApplyRoundedCorners(nint handle, int widthPx, int heightPx, int radiusPx)
    {
        var region = PInvoke.CreateRoundRectRgn(0, 0, widthPx + 1, heightPx + 1, radiusPx * 2, radiusPx * 2);
        // The system owns the region after a successful SetWindowRgn; do not delete it.
        PInvoke.SetWindowRgn((HWND)handle, region, true);
    }

    /// <summary>Reads the RECT that WM_MOVING / WM_SIZING point to (lParam).</summary>
    public static unsafe PixelRect ReadRect(nint rectPointer)
    {
        var rect = (RECT*)rectPointer;
        return new PixelRect(rect->left, rect->top, rect->right - rect->left, rect->bottom - rect->top);
    }

    /// <summary>Writes back the RECT of WM_MOVING / WM_SIZING; Windows then moves or sizes the window there.</summary>
    public static unsafe void WriteRect(nint rectPointer, PixelRect value) =>
        *(RECT*)rectPointer = new RECT(value.X, value.Y, value.X + value.Width, value.Y + value.Height);

    public static PixelRect GetPixelRect(nint handle)
    {
        PInvoke.GetWindowRect((HWND)handle, out var rect);
        return new PixelRect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
    }

    /// <summary>
    /// Sends the fence below every app window. Showing a window puts it on top of the z-order; because the fence is
    /// owned by Progman, "bottom" ends just above the desktop (an owned window always stays above its owner).
    /// </summary>
    public static void SendToBack(nint handle) =>
        PInvoke.SetWindowPos((HWND)handle, HWND.HWND_BOTTOM, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    /// <summary>
    /// Call from WM_WINDOWPOSCHANGING (lParam): any z-order change (activation by a click, Show, another app being
    /// restored) ends at the bottom, just above the desktop. Being owned by Progman alone does not keep an activated
    /// fence below apps (user report 2026-10-02: the Inbox drew over Firefox).
    /// </summary>
    public static unsafe void KeepAtBottom(nint windowPosPointer)
    {
        var windowPos = (WINDOWPOS*)windowPosPointer;
        if ((windowPos->flags & SET_WINDOW_POS_FLAGS.SWP_NOZORDER) == 0) windowPos->hwndInsertAfter = HWND.HWND_BOTTOM;
    }

    /// <summary>Peek (spec §4.6): fences above every window while on; back to the bottom (above the desktop) when off.</summary>
    public static void SetTopmost(nint handle, bool topmost)
    {
        const SET_WINDOW_POS_FLAGS flags = SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        PInvoke.SetWindowPos((HWND)handle, topmost ? HWND.HWND_TOPMOST : HWND.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
        if (!topmost) SendToBack(handle);
    }

    /// <summary>A topmost window the mouse passes through (the draw-a-fence rectangle): never activated, never hit.</summary>
    public static void MakeOverlay(nint handle)
    {
        var exStyle = PInvoke.GetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr((HWND)handle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, exStyle | (nint)(WINDOW_EX_STYLE.WS_EX_TRANSPARENT
            | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOPMOST));
    }

    /// <summary>Mouse position in physical screen pixels (roll-up hover).</summary>
    public static (int X, int Y) GetCursorPosition()
    {
        PInvoke.GetCursorPos(out var position);
        return (position.X, position.Y);
    }

    /// <summary>Moves and sizes without changing z-order or activating.</summary>
    public static void SetPixelRect(nint handle, PixelRect rect)
    {
        const SET_WINDOW_POS_FLAGS flags = SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;
        // ponytail: set twice so a move onto a monitor with another DPI (WM_DPICHANGED resizes us) still ends at the requested size.
        PInvoke.SetWindowPos((HWND)handle, HWND.Null, rect.X, rect.Y, rect.Width, rect.Height, flags);
        PInvoke.SetWindowPos((HWND)handle, HWND.Null, rect.X, rect.Y, rect.Width, rect.Height, flags);
    }

    // ponytail: undocumented user32 API with no CsWin32 metadata (CLAUDE.md rule 5 exception, ADR-011); fallback is the tint.
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    private const int WcaAccentPolicy = 19;
    private const int AccentEnableBlurBehind = 3;
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 188`.

- [ ] **Step 3: Commit**

```powershell
git add src/NeoFences.Shell
git commit -m "feat: added desktop mouse hook, global hotkey and topmost chrome to shell"
```

---

### Task 3: App — quick-hide, draw a fence, Peek, roll-up

**Files:**
- Create: `src/NeoFences.App/DrawFenceOverlay.cs`.
- Replace: `SystemMessageWindow.cs`, `FenceWindow.xaml`, `FenceWindow.xaml.cs` and `FenceHost.cs`.

**Interfaces:**
- Consumes: Tasks 1 and 2.
- Produces:
  - `DrawFenceOverlay(bool lightTheme)`, with `Track(PixelRect)` and `Between(start, end)`;
  - `SystemMessageWindow.Handle` and `HotkeyPressed(int)`;
  - on `FenceWindow`: `Place(PixelRect)`, `SetRolledUp(bool)`, `Peeking`, the public `BeginRename()`, and the event `RollUpToggled`.

- [ ] **Step 1: Files**

`src/NeoFences.App/DrawFenceOverlay.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using NeoFences.Core.Layouts;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// The rectangle shown while the user right-drags on the desktop to draw a fence (M5). Topmost, never activated,
/// and the mouse passes through it, so the drag keeps going to the desktop.
/// </summary>
public sealed class DrawFenceOverlay : Window
{
    private readonly nint _handle;

    public DrawFenceOverlay(bool lightTheme)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        var ink = lightTheme ? Colors.Black : Colors.White;
        Content = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xC0, ink.R, ink.G, ink.B)),
            Background = new SolidColorBrush(Color.FromArgb(0x30, ink.R, ink.G, ink.B)),
        };
        _handle = new WindowInteropHelper(this).EnsureHandle();
        FenceWindowChrome.MakeOverlay(_handle);
    }

    /// <summary>Physical-pixel rect between the drag start and the pointer.</summary>
    public void Track(PixelRect rect) => FenceWindowChrome.SetPixelRect(_handle, rect);

    public static PixelRect Between((int X, int Y) start, (int X, int Y) end) => new(
        Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
}
```
`src/NeoFences.App/SystemMessageWindow.cs`:
```csharp
using System.Runtime.InteropServices;
using System.Windows.Interop;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// Hidden top-level window that receives system broadcasts: Explorer restarts (TaskbarCreated), display or
/// work-area changes, and light/dark mode switches. Message-only windows do not get broadcasts, so this is an
/// invisible normal window.
/// It also receives global hotkeys (WM_HOTKEY).
/// Sign-out / shutdown is WPF's job (Application.SessionEnding, ADR-013), not this window's.
/// </summary>
public sealed class SystemMessageWindow : IDisposable
{
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmDpiChanged = 0x02E0;
    private const int SpiSetWorkArea = 0x002F;

    private readonly HwndSource _source;

    public event Action? ExplorerRestarted;
    public event Action? DisplayChanged;
    public event Action? ThemeChanged;
    /// <summary>A RegisterHotKey hotkey of this window was pressed (its id).</summary>
    public event Action<int>? HotkeyPressed;

    /// <summary>Receives the global hotkeys (Peek, M5).</summary>
    public nint Handle => _source.Handle;

    public SystemMessageWindow()
    {
        _source = new HwndSource(new HwndSourceParameters("NeoFences.SystemMessages") { WindowStyle = 0, Width = 0, Height = 0 });
        _source.AddHook(OnMessage);
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case GlobalHotkey.WmHotkey:
                HotkeyPressed?.Invoke((int)wParam);
                return 0;
            case WmDisplayChange:
            case WmDpiChanged:
            case WmSettingChange when wParam == SpiSetWorkArea:
                DisplayChanged?.Invoke();
                return 0;
            // Light/dark switch: WM_SETTINGCHANGE with the string "ImmersiveColorSet".
            case WmSettingChange when lParam != 0 && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet":
                ThemeChanged?.Invoke();
                return 0;
        }
        if ((uint)message == DesktopHost.TaskbarCreatedMessage) ExplorerRestarted?.Invoke();
        return 0;
    }

    public void Dispose() => _source.Dispose();
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
    private bool _hoverExpanded;            // rolled up, but opened while the pointer rests on it (M5, hover)
    private int _fullHeightPx;              // height when not rolled up (physical pixels)
    private int _hoverTicks;
    private readonly System.Windows.Threading.DispatcherTimer _hoverTimer;

    public string FenceId { get; }

    public nint Handle { get; private set; }

    /// <summary>Peek (M5): while set the fence may rise above apps instead of staying at the bottom.</summary>
    public bool Peeking { get; set; }

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
    /// <summary>Double-click on the title: roll up to the title bar, or back down (M5).</summary>
    public event Action? RollUpToggled;

    private Point? _pressPoint;                 // left button pressed on an item: a drag may start
    private ListBoxItem? _deferredSelect;       // pressed on an already selected item: select it alone only on release
    private Point? _bandStart;                  // left button pressed on empty space: rubber band

    public FenceWindow(Fence fence, bool takeoverActive, bool lightTheme, IconLoader iconLoader)
    {
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
        // Rolled up, the fence opens while the pointer rests on it and closes again shortly after it leaves.
        _hoverTimer = new System.Windows.Threading.DispatcherTimer { Interval = HoverTick };
        _hoverTimer.Tick += (_, _) => OnHoverTick();
        if (_rolledUp) _hoverTimer.Start();
        SetLocked(fence.Locked);
        // Unlocked, the title is caption (WM_NCLBUTTONDBLCLK); locked, it is client area.
        TitleBar.MouseLeftButtonDown += (_, click) => { if (click.ClickCount == 2) RollUpToggled?.Invoke(); };
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
        _fullHeightPx = fullRect.Height;
        FenceWindowChrome.SetPixelRect(Handle, _rolledUp && !_hoverExpanded ? fullRect with { Height = RolledUpHeightPx } : fullRect);
    }

    public void SetRolledUp(bool rolledUp)
    {
        if (rolledUp == _rolledUp) return;
        var current = FenceWindowChrome.GetPixelRect(Handle);
        if (rolledUp && !_hoverExpanded) _fullHeightPx = current.Height;
        _rolledUp = rolledUp;
        _hoverExpanded = false;
        _hoverTicks = 0;
        ApplyChrome();
        FenceWindowChrome.SetPixelRect(Handle, current with { Height = rolledUp ? RolledUpHeightPx : _fullHeightPx });
        if (rolledUp) _hoverTimer.Start();
        else _hoverTimer.Stop();
    }

    private static readonly TimeSpan HoverTick = TimeSpan.FromMilliseconds(100);
    private const int HoverOpenTicks = 3;   // ~300 ms resting on the title before it opens
    private const int HoverCloseTicks = 5;  // ~500 ms away before it closes, so a brief slip does not close it

    /// <summary>Title row plus the 1-DIP border above and below it.</summary>
    private int RolledUpHeightPx => (int)Math.Round((CaptionHeightDips + 2) * VisualTreeHelper.GetDpi(this).DpiScaleY);

    // ponytail: polls the cursor every 100 ms while rolled up (no mouse-leave on a no-activate layered window when
    // the pointer leaves fast); also opens during a file drag, which is wanted. Settings.RollupExpand = Click is not
    // offered yet (user chose Hover); add a click path when it is.
    private void OnHoverTick()
    {
        if (Handle == 0 || !_rolledUp) return;
        if (_drag is not null || BodyContextMenu.IsOpen || _renaming || _items.Any(view => view.IsEditing)) return; // never close under the user
        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
        var rect = FenceWindowChrome.GetPixelRect(Handle);
        var inside = cursorX >= rect.X && cursorX < rect.X + rect.Width && cursorY >= rect.Y && cursorY < rect.Y + rect.Height;
        _hoverTicks = inside == _hoverExpanded ? 0 : _hoverTicks + 1;
        if (_hoverTicks < (_hoverExpanded ? HoverCloseTicks : HoverOpenTicks)) return;
        _hoverTicks = 0;
        _hoverExpanded = !_hoverExpanded;
        FenceWindowChrome.SetPixelRect(Handle, rect with { Height = _hoverExpanded ? _fullHeightPx : RolledUpHeightPx });
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
using System.Windows.Interop;
using System.Windows.Threading;
using NeoFences.Core.Config;
using NeoFences.Core.Input;
using NeoFences.Core.Layouts;
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

    public event Action? ExitRequested;

    public FenceHost()
    {
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();
        _messages.ExplorerRestarted += OnExplorerRestarted;
        _messages.DisplayChanged += OnDisplayChanged;
        _messages.ThemeChanged += OnThemeChanged;
        _messages.HotkeyPressed += OnHotkey;
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
        ScheduleSave();
    }

    /// <summary>
    /// Windows asked to end the session (WPF's SessionEnding, inside WM_QUERYENDSESSION). WPF then shuts the app
    /// down, which may be the last thing that ever runs, so the icons come back now and the watchdog is told it is
    /// a session end, not a user exit: if the user cancels the shutdown, the watchdog restarts NeoFences (ADR-013).
    /// </summary>
    public void OnSessionEnding()
    {
        _sessionEnding = true;
        SaveNow();
        if (_takeoverActive || _quickHidden) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkSessionEnding(Environment.ProcessId), what: "session-ending marker");
    }

    /// <summary>Orderly exit: save, bring icons back, tell the watchdog all is well.</summary>
    public void Shutdown()
    {
        SaveNow();
        if (_sessionEnding) return; // OnSessionEnding already restored and marked; no clean marker, so a cancel restarts us
        if (_takeoverActive || _quickHidden) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkCleanShutdown(Environment.ProcessId), what: "clean-shutdown marker");
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
        if (_takeoverActive || _quickHidden) DesktopIcons.TrySetHidden(false);
    }

    private void OpenWindow(Fence fence)
    {
        var window = new FenceWindow(fence, takeoverActive: _takeoverActive, lightTheme: _lightTheme, iconLoader: _iconLoader);
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
        ReconcileDesktop();
    }

    private void OnDesktopChanged(DesktopChange change)
    {
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
                if (!window.IsVisible && !_quickHidden)
                {
                    window.Show();
                    FenceWindowChrome.SendToBack(window.Handle); // Show puts it above every app; fences live just above the desktop
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
        SetIconsHidden(active);
        foreach (var window in _windows.Values) window.SetTakeoverChecked(active);
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
        _takeoverActive || _quickHidden ? SetIconsHidden(true)
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
        var dispatcher = Dispatcher.CurrentDispatcher;
        _mouseHook = new DesktopMouseHook(
            onGesture: (gesture, screenX, screenY) => dispatcher.BeginInvoke(() => OnDesktopGesture(gesture, screenX, screenY)),
            onPeekClickOutside: () => dispatcher.BeginInvoke(() => SetPeek(false)),
            log: message => Log.Information("{Message}", message));
        if (!Hotkey.TryParse(_config.Settings.PeekHotkey, out var hotkey) || !Enum.TryParse<System.Windows.Input.Key>(hotkey.Key, ignoreCase: true, out var key))
        {
            Log.Warning("Peek hotkey {PeekHotkey} is not valid; Peek is off", _config.Settings.PeekHotkey);
            return;
        }
        _peekHotkey = new GlobalHotkey(_messages.Handle, PeekHotkeyId);
        if (_peekHotkey.TryRegister(hotkey, (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key))) Log.Information("Peek hotkey {Hotkey} registered", hotkey);
        else Log.Warning("Peek hotkey {Hotkey} is taken by another app; Peek is off", hotkey);
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
            if (hidden) window.Hide();
            else
            {
                window.Show();
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
Expected: 0 warnings, 0 errors; `Passed: 188`.

- [ ] **Step 3: Live smoke (ask first: about 1 min of mouse; refocuses Windows Terminal)**

Save as `<scratchpad>\m5-smoke.ps1` and run `& "<scratchpad>\m5-smoke.ps1"`. It runs the branch build and leaves it running.
```powershell
# M5 live smoke: desktop gestures. Plain right-click on the desktop (Explorer menu still
# shows), right-drag draws a fence, double-click quick-hides, Peek (hotkey / Esc / click outside), roll-up with hover.
# Backs up config.json, runs -Exe, then restores the config and starts -RepoExe again. Refocuses Terminal.
# Moves use mouse_event: SetCursorPos never reaches WH_MOUSE_LL, so a drag made with it is invisible to the hook.
param(
  [string]$Exe = "F:\projects\neo_fences\src\NeoFences.App\bin\Debug\net10.0-windows\NeoFences.exe",
  [string]$RepoExe = $Exe)  # the build left running afterwards
Add-Type -Namespace NfM5 -Name N -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
public delegate bool EnumProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
[DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
[DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
'@
function Pause-Ms([int]$ms) { [System.Threading.Thread]::Sleep($ms) }
function Key([byte]$vk) { [NfM5.N]::keybd_event($vk,0,0,[UIntPtr]::Zero); [NfM5.N]::keybd_event($vk,0,2,[UIntPtr]::Zero); Pause-Ms 120 }
function KeyDown([byte]$vk) { [NfM5.N]::keybd_event($vk,0,0,[UIntPtr]::Zero) }
function KeyUp([byte]$vk) { [NfM5.N]::keybd_event($vk,0,2,[UIntPtr]::Zero) }
function MoveTo([int]$x, [int]$y) { [void][NfM5.N]::SetCursorPos($x, $y) }
function Click([int]$x, [int]$y) { MoveTo $x $y; Pause-Ms 150; [NfM5.N]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [NfM5.N]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Pause-Ms 600 }
function DoubleClick([int]$x, [int]$y) { MoveTo $x $y; Pause-Ms 150; foreach ($press in 1..2) { [NfM5.N]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [NfM5.N]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Pause-Ms 60 }; Pause-Ms 1200 }
function RightClick([int]$x, [int]$y) { MoveTo $x $y; Pause-Ms 150; [NfM5.N]::mouse_event(8,0,0,0,[UIntPtr]::Zero); Pause-Ms 50; [NfM5.N]::mouse_event(16,0,0,0,[UIntPtr]::Zero); Pause-Ms 1200 }
# SetCursorPos bypasses WH_MOUSE_LL; a real move (like a mouse) goes through mouse_event (absolute, 0..65535).
function InputMove([int]$x, [int]$y) { [NfM5.N]::mouse_event(0x8001, [int]($x * 65535 / 1919), [int]($y * 65535 / 1079), 0, [UIntPtr]::Zero) }
function RightDrag([int]$fromX, [int]$fromY, [int]$toX, [int]$toY) {
  MoveTo $fromX $fromY; Pause-Ms 200; [NfM5.N]::mouse_event(8,0,0,0,[UIntPtr]::Zero); Pause-Ms 150
  foreach ($step in 1..30) { InputMove ([int]($fromX + ($toX - $fromX) * $step / 30)) ([int]($fromY + ($toY - $fromY) * $step / 30)); Pause-Ms 25 }
  Pause-Ms 300; [NfM5.N]::mouse_event(16,0,0,0,[UIntPtr]::Zero); Pause-Ms 1500 }
function Wait-Until([scriptblock]$condition, [int]$seconds = 6) { $deadline = (Get-Date).AddSeconds($seconds); while (-not (& $condition) -and (Get-Date) -lt $deadline) { Pause-Ms 200 }; [bool](& $condition) }
function Visible-Windows { $found = New-Object System.Collections.Generic.List[object]
  [void][NfM5.N]::EnumWindows({ param($h, $l) if ([NfM5.N]::IsWindowVisible($h)) { $t = New-Object System.Text.StringBuilder 128; [void][NfM5.N]::GetWindowText($h, $t, 128); $c = New-Object System.Text.StringBuilder 128; [void][NfM5.N]::GetClassName($h, $c, 128); $found.Add([pscustomobject]@{ Handle = $h; Title = "$t"; Class = "$c" }) }; $true }, [IntPtr]::Zero); $found }
function Fences { @(Visible-Windows | Where-Object Title -eq 'NeoFences fence' | ForEach-Object { $_.Handle }) }
function Rect([IntPtr]$window) { $r = New-Object NfM5.N+RECT; [void][NfM5.N]::GetWindowRect($window, [ref]$r); $r }
function IsTopmost([IntPtr]$window) { ([NfM5.N]::GetWindowLong($window, -20) -band 8) -ne 0 }
function RootAt([int]$x, [int]$y) { $p = New-Object NfM5.N+POINT; $p.X = $x; $p.Y = $y; [NfM5.N]::GetAncestor([NfM5.N]::WindowFromPoint($p), 2) }
$configPath = "$env:LOCALAPPDATA\NeoFences\config.json"
function Config { Get-Content $configPath -Raw | ConvertFrom-Json }
function Stop-App([string]$path) { & $path --exit; [void](Wait-Until { -not (Get-Process NeoFences -ErrorAction SilentlyContinue) } 15) }

# Arrange: stop the build, keep the config, start the test build, clear the desktop.
Stop-App $RepoExe
$backup = "$PSScriptRoot\config-before-m5.json"; Copy-Item $configPath $backup -Force
Start-Process $Exe; Pause-Ms 5000
$shell = New-Object -ComObject Shell.Application; $shell.MinimizeAll(); Pause-Ms 1500
$fenceCount = (Fences).Count
"fences shown: $fenceCount"
# An empty desktop spot: away from every fence (Takeover is on, so no native icons).
$spot = $null
foreach ($y in @(700, 850, 450, 300)) { foreach ($x in @(300, 700, 1500, 1750)) {
  if ($spot) { continue }
  $clear = $true; foreach ($fence in (Fences)) { $r = Rect $fence; if ($x -gt $r.Left - 40 -and $x -lt $r.Right + 340 -and $y -gt $r.Top - 40 -and $y -lt $r.Bottom + 240) { $clear = $false } }
  if ($clear -and (RootAt $x $y) -ne [IntPtr]::Zero) { $spot = [pscustomobject]@{ X = $x; Y = $y } } } }
if (-not $spot) { "no empty desktop spot; aborting"; Stop-App $Exe; Copy-Item $backup $configPath -Force; Start-Process $RepoExe; return }
"empty spot: $($spot.X),$($spot.Y)"

# 1. A plain right-click on the desktop still opens Explorer's menu (S2 replay).
$before = @(Visible-Windows | ForEach-Object { $_.Handle })
RightClick $spot.X $spot.Y
$menu = @(Visible-Windows | Where-Object { $before -notcontains $_.Handle })
"right-click: new window(s) $(($menu | ForEach-Object { $_.Class }) -join ', ')  →  menu shown $($menu.Count -gt 0)"
Key 0x1B; Pause-Ms 500

# 2. Right-drag draws a fence: the new fence lands on the drawn rect, its title ready to type.
RightDrag $spot.X $spot.Y ($spot.X + 300) ($spot.Y + 180)
"right-drag: a new fence " + (Wait-Until { (Fences).Count -eq $fenceCount + 1 })
$drawn = Fences | Where-Object { $r = Rect $_; [math]::Abs($r.Left - $spot.X) -lt 6 -and [math]::Abs($r.Top - $spot.Y) -lt 6 } | Select-Object -First 1
if ($drawn) { $r = Rect $drawn; "drawn fence at $($r.Left),$($r.Top) $($r.Right - $r.Left)x$($r.Bottom - $r.Top) (expected $($spot.X),$($spot.Y) 300x180)" } else { "drawn fence not at the drawn spot" }
Key 0x1B; Pause-Ms 400   # leave the title as "New fence"
"saved: " + (Wait-Until { @((Config).fences).Count -eq @((Get-Content $backup -Raw | ConvertFrom-Json).fences).Count + 1 })

# 3. Double-click on the empty desktop: quick-hide, and again: back.
$hideSpot = [pscustomobject]@{ X = $spot.X + 360; Y = $spot.Y + 60 }
if ((RootAt $hideSpot.X $hideSpot.Y) -in (Fences)) { $hideSpot = [pscustomobject]@{ X = $spot.X - 120; Y = $spot.Y + 60 } }
DoubleClick $hideSpot.X $hideSpot.Y
"quick-hide: all fences hidden " + (Wait-Until { (Fences).Count -eq 0 })
DoubleClick $hideSpot.X $hideSpot.Y
"quick-hide off: fences back " + (Wait-Until { (Fences).Count -eq $fenceCount + 1 })

# 4. Peek over an app: Ctrl+Alt+Space lifts fences above it; Esc drops them; a click outside ends it too.
Start-Process explorer.exe 'C:\Windows'; [void](Wait-Until { @(Visible-Windows | Where-Object Title -like 'Windows*').Count -gt 0 }); Pause-Ms 800
$explorer = (Visible-Windows | Where-Object Title -like 'Windows*' | Select-Object -First 1).Handle
[void][NfM5.N]::PostMessage($explorer, 0x0112, [IntPtr]0xF030, [IntPtr]::Zero); Pause-Ms 1000   # SC_MAXIMIZE
$probe = Rect $drawn; $probeX = [int](($probe.Left + $probe.Right) / 2); $probeY = [int](($probe.Top + $probe.Bottom) / 2)
"before Peek: app on top at the fence $((RootAt $probeX $probeY) -eq $explorer)"
KeyDown 0x11; KeyDown 0x12; Key 0x20; KeyUp 0x12; KeyUp 0x11; Pause-Ms 600
"Peek on: fence topmost $(IsTopmost $drawn), fence on top $((RootAt $probeX $probeY) -eq $drawn)"
Key 0x1B; Pause-Ms 600
"Esc: topmost off $(-not (IsTopmost $drawn)), app on top again $((RootAt $probeX $probeY) -eq $explorer)"
KeyDown 0x11; KeyDown 0x12; Key 0x20; KeyUp 0x12; KeyUp 0x11; Pause-Ms 600
$outsideX = if ($probe.Left -gt 400) { 200 } else { 1700 }
Click $outsideX 500
"click outside: topmost off $(-not (IsTopmost $drawn)), app on top $((RootAt $probeX $probeY) -eq $explorer)"
[void][NfM5.N]::PostMessage($explorer, 0x10, [IntPtr]::Zero, [IntPtr]::Zero); Pause-Ms 1000

# 5. Roll-up: title double-click shrinks it to the title; hover opens it; leaving closes it; double-click restores.
$full = Rect $drawn; $fullHeight = $full.Bottom - $full.Top
MoveTo 1000 1020; Pause-Ms 300
DoubleClick ($full.Left + 150) ($full.Top + 14); MoveTo ($full.Left + 150) ($full.Bottom + 200); Pause-Ms 900
$rolled = Rect $drawn
"roll-up: height $fullHeight → $($rolled.Bottom - $rolled.Top), saved " + (Wait-Until { ((Config).fences | Where-Object { $_.title -eq 'New fence' -and $_.rolledUp }).Count -gt 0 })
MoveTo ($full.Left + 150) ($full.Top + 14); Pause-Ms 900
$hover = Rect $drawn; "hover: opened to $($hover.Bottom - $hover.Top) $(($hover.Bottom - $hover.Top) -eq $fullHeight)"
MoveTo ($full.Left + 150) ($full.Bottom + 200); Pause-Ms 1200
$left = Rect $drawn; "left: closed again $(($left.Bottom - $left.Top) -eq ($rolled.Bottom - $rolled.Top))"
DoubleClick ($full.Left + 150) ($full.Top + 14); MoveTo ($full.Left + 150) ($full.Bottom + 200); Pause-Ms 900
$back = Rect $drawn; "unroll: height $($back.Bottom - $back.Top) $(($back.Bottom - $back.Top) -eq $fullHeight)"

# Clean up: the test build stops, the config comes back, the build runs again.
Stop-App $Exe
Copy-Item $backup $configPath -Force
Start-Process $RepoExe; Pause-Ms 4000
$shell.UndoMinimizeALL(); Pause-Ms 1000
$terminal = Get-Process WindowsTerminal -ErrorAction SilentlyContinue | Select-Object -First 1
if ($terminal) { KeyDown 0x12; [void](New-Object -ComObject WScript.Shell).AppActivate($terminal.Id); KeyUp 0x12 }
"NeoFences (build) alive: " + [bool](Get-CimInstance Win32_Process -Filter "Name='NeoFences.exe'" | Where-Object { $_.CommandLine -notmatch '--watchdog' -and $_.ExecutablePath -eq $RepoExe })
```
Expected:
- every check line ends `True`;
- `drawn fence at X,Y 300x180 (expected X,Y 300x180)`;
- `roll-up: height 180 → 32`.

Regression: re-run `<scratchpad>\m4-smoke.ps1` (Portals), because `FenceHost` and `FenceWindow` changed. Expected: as in M4.

- [ ] **Step 4: Commit**

```powershell
git add src/NeoFences.App
git commit -m "feat: added quick-hide, draw a fence, peek and roll-up with hover to the app"
```

---

### Task 4: Verification

**Files:** `docs/TEST-CHECKLIST.md` (append section N), `docs/research/m5-desktop-gestures.md` (create)

- [ ] **Step 1: Append section N**

```markdown
## N — Desktop gestures in the app (M5, ADR-020)
| ID | Steps | Expected |
|---|---|---|
| N1 | Right-click empty desktop | Explorer's desktop menu appears as before (replayed); Esc closes it |
| N2 | Right-drag on empty desktop, release | a rectangle follows the pointer; a "New fence" appears there with its title ready to type |
| N3 | Right-drag starting on an app window or a fence | nothing drawn; the app/fence gets its normal right-drag |
| N4 | Double-click empty desktop; again | every fence (and, with Takeover off, every icon) hides; the second double-click brings them back |
| N5 | Takeover off: double-click a native icon | it opens; nothing hides |
| N6 | Quick-hidden with Takeover off: kill NeoFences in Task Manager | the watchdog shows the icons again |
| N7 | App maximized; Ctrl+Alt+Space; Esc; Ctrl+Alt+Space; click the app; Ctrl+Alt+Space; open an item from a fence | fences over the app; back under it after Esc, after the click, and after the item opens |
| N8 | Set `peekHotkey` to a combination another app owns (or "Space"); restart | log says Peek is off; nothing else breaks |
| N9 | Double-click a fence title; hover it; move away; double-click again; restart while rolled up | rolls up to the title; opens after ~0.3 s; closes ~0.5 s after leaving; unrolls; stays rolled up after restart at its full height |
| N10 | Move a rolled-up fence, then unroll it | it unrolls at the new position with its full height |
| N11 | Lock a fence; double-click its title | it rolls up / unrolls too |
| N12 | "New fence" from the menu with several fences on screen | lands in the first free spot (8 px gaps), not on top of another fence |
| N13 | **[USER]** Normal use with the hook for a while, a fast mouse, a game | no lag; the right-click menu is never lost (D8) |
| N14 | **[USER]** Elevated app focused (Task Manager as admin); double-click desktop | record whether the gesture fires (D7) |
```

- [ ] **Step 2: Run N1–N14.**
  - The smoke covers N1, N2, N4 and N7 (hotkey, Esc, click outside), plus N9 without the restart.
  - By hand: N3, N10–N12, and N6 (needs Takeover off).
  - **[USER]**: N5 and N8 (they change the user's setup), N13 and N14.

- [ ] **Step 3: Write `docs/research/m5-desktop-gestures.md`**, with:
  - the results;
  - the Peek sibling-restack finding;
  - the `SetCursorPos` / `WH_MOUSE_LL` finding;
  - the Windows 11 desktop menu showing as `Microsoft.UI.Content.PopupWindowSiteBridge` windows.

- [ ] **Step 4: Commit**

```powershell
git add docs/TEST-CHECKLIST.md docs/research/m5-desktop-gestures.md
git commit -m "docs: added M5 desktop gesture checks and results"
```

---

### Task 5: Docs sync

- [ ] **Step 1: Append ADR-020 to `docs/DECISIONS.md`**

```markdown
## ADR-020 — Desktop gestures: quick-hide, draw a fence, Peek, roll-up
**Date:** 2026-10-03 · **Status:** Accepted · **Builds on:** ADR-007, ADR-011 (S2), ADR-019

**Context.** M5 brings Fences' desktop gestures (spec §4.5–4.6). Choices the user made on 2026-10-03:
- Quick-hide hides the fences **and** the native desktop icons.
- A rolled-up fence opens while the pointer rests on it (hover).

**Decision.**
- **One hook.** One `WH_MOUSE_LL` runs on its own thread with its own message loop (`DesktopMouseHook`).
  - It acts only when the root window under the pointer is Progman or WorkerW.
  - `WindowFromPoint` runs on button events only; moves arrive at up to 1000 Hz.
  - The callback never throws.
  - Every desktop right-press is swallowed. If the press did not become a drag, a right-click marked "NFNC" is replayed from the hook thread's loop (S2), so Explorer's desktop menu still shows.
- **Quick-hide.** A double-click on empty desktop hides every fence. With Takeover off it also hides the native icons, through the same marked path as Takeover, so the watchdog, a crash or a session end bring them back. The next double-click shows them again.
  - The state is not saved.
  - A double-click on a visible native icon opens it and does not toggle. This is a UI Automation hit test on the UI thread, never inside the hook.
  - Turning Takeover on or off, Peek and drawing a fence all end quick-hide first.
- **Draw a fence.** A right-drag of 8 px or more shows a topmost, click-through rectangle that follows the pointer (polled every 15 ms).
  - On release a fence titled "New fence" is created at that rect. The layout clamps it to the minimum size.
  - Its title rename starts right away.
- **Peek.** `RegisterHotKey` (default `Ctrl+Alt+Space`) is sent to the system message window.
  - Every fence is marked as peeking first, then raised `HWND_TOPMOST`. All fences are owned by Progman, so raising one restacks its siblings, and a sibling still keeping itself at the bottom drops back (found in the smoke).
  - Peek ends on the same hotkey, on Esc (a hotkey registered only while Peek is on), on a click outside the fences (seen by the hook), or when an item is opened from a fence.
  - If the hotkey is invalid or another app already owns it, the problem is logged and Peek is off.
- **Roll-up.** Double-clicking the title rolls the fence up to its title bar; double-clicking again unrolls it. The title is a caption (`WM_NCLBUTTONDBLCLK`) normally, and client area when the fence is locked.
  - `Fence.RolledUp` is saved. The stored rect keeps the full height, so a move while rolled up does not shrink the fence.
  - Resizing is off while rolled up.
  - Hover: after about 300 ms on the fence it opens; after about 500 ms away it closes. This uses 100 ms cursor polling, which also opens it during a file drag.
  - It never closes during a move, a menu or a rename.
- **Smart placement.** A new fence goes to the first free spot, reading top-left to bottom-right, that keeps the 8 px gap from other fences (`LayoutEngine.FreeSpot`). Only a full monitor falls back to the cascade.

**Consequences.**
- With Takeover off, a right-drag that starts on a visible native icon draws a fence instead of starting the shell's icon right-drag. A right-click on an icon still opens its menu (replayed).
- Test scripts must move the pointer with `SendInput`/`mouse_event`: `SetCursorPos` never reaches `WH_MOUSE_LL`.
- Not in M5:
  - the roll-up and quick-hide animations (spec §6, M6 polish);
  - the tray entries (M6);
  - `RollupExpand = Click` (stored, not offered yet);
  - removing the hook in game mode (M6).
```

- [ ] **Step 2: Update the other docs**
  - `ARCHITECTURE.md`: add `DesktopGestureTracker`, `Hotkey`, `DesktopMouseHook`/`DesktopWindows`, `GlobalHotkey`, `DrawFenceOverlay` and `FreeSpot`; replace "Next: M5" with "M5 complete … Next: M6".
  - `FEATURES.md`: set Draw fence, Quick-hide, Peek, Roll-up and Smart placement spacing to done (M5). Set Roll-up expand to "hover done; click later".
  - `ROADMAP.md`: tick M5.
  - `TEST-CHECKLIST.md`: section D notes that D2/D7 moved to N.
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 3: Refresh the hub** (`node --check`, publish with `url`).

- [ ] **Step 4: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-020 and synced docs for M5"
```
