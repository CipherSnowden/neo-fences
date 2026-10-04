# M2c — Fence Interactions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make fences comfortable to arrange and use every day. This plan adds:
- the spec's fence menu: Rename, Icon size, Lock position, Delete fence;
- smart snapping while moving and resizing;
- keyboard use: arrows, Ctrl+A, Enter;
- a thin scrollbar and readable labels (soft shadow);
- light/dark that follows Windows.

**Architecture:**
- `NeoFences.Core` gains two pure, tested pieces:
  - `FenceEdits`: rename, icon size, lock.
  - `Snapping`: `Snap` for gap/align snapping, and `Unsnapped` for tracking the drag.
- `NeoFences.Shell` gains `SystemTheme`, plus RECT read/write helpers for `WM_MOVING` / `WM_SIZING`.
- `NeoFences.App`:
  - `FenceWindow` gets the menu, the in-place rename box, icon-size and lock handling, snapping in its message hook, theme resources and the scrollbar style.
  - `FenceHost` applies the edits, supplies the snap targets and follows theme broadcasts from `SystemMessageWindow`.

**Tech Stack:** .NET 10 SDK 10.0.401, WPF, CsWin32 0.3.335, Serilog 4.4.0, xUnit 2.9.3. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-02-neofences-v1-design.md` §6. **Decisions:** ADR-011, ADR-014, and **ADR-015** (new, Task 5). The user chose these on 2026-10-02: fences follow Windows light/dark, and labels get a soft text shadow.

**Pre-verified (2026-10-02):** every code block below was compiled together (0 warnings, 0 errors), and **113/113 tests pass**. A prototype ran on the real desktop, driven by the two smoke scripts in Task 3:
- **Menu actions:** rename (Enter saves, and the title persists), Large icons re-rendered sharp, the Inbox has no Delete, and delete fence works.
- **Snapping:** a slow 120 px drag escapes snapping, and dragging back snaps to (Inbox.X, Inbox.Bottom + 8).
- **Lock:** a locked fence won't move; unlocked, it moves again.
- **Resize:** a right-edge resize of 40 px, then a snap that aligns with the Inbox.
- **Keyboard:** arrows move the selection, and Enter opened Recycle Bin in front.
- **Light/dark:** switching to light mode and back is followed live, and light mode is readable.

Four prototype bugs were found and fixed on the way; each fix is part of the code below:
- the routed `DpiChanged` event caused a reload loop;
- the fence stuck to snap targets during slow drags;
- the lock could not be undone;
- the accent blur ignores its tint colour.

## Global Constraints

- Hard rules (CLAUDE.md):
  - never lose or hide user files (Delete fence moves items to the Inbox);
  - all Win32 in `NeoFences.Shell`;
  - CsWin32;
  - no new NuGet dependency;
  - failures degrade, never crash.
- Icon sizes are exactly `ConfigNormalizer.IconSizes` (32/48/64/96 DIP). Titles are trimmed and at most 64 characters; a blank title keeps the old one.
- Snapping uses an 8 DIP gap and a 12 DIP reach, scaled by the monitor's DPI. It runs only while the user drags; programmatic placement is unchanged.
- Dark mode must look exactly as approved in M2a: no extra veil.
- Commits: single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- The smoke scripts take over the mouse for about 40 s. Ask the user first if they may be at the PC.

## Review Focus

1. **A slow, hand-speed drag near another fence or a screen edge.** Expected: the fence snaps in, and then lets go once the total move exceeds the reach. Pinned by `SlowDragAwayFromASnapTarget_Escapes` (Task 1); live check I6.
2. **Lock, then unlock, then drag.** Expected: dragging works again after the unlock. Live check I4, in the interaction smoke (Task 3).
3. **A DPI or icon-size change while items are shown.** Expected: icons reload exactly once, with no loop. Live check I12; the `DpiChanged` filter is in Task 3's code.
4. **Rename edge cases (Esc, blank, very long).** Expected: Esc cancels, blank keeps the old title, long titles are cut at 64. Pinned by the `Rename_*` tests (Task 1); Esc is live check I2.
5. **Delete fence while the fence holds items.** Expected: the items go to the end of the Inbox, and no file operation happens. Pinned by the existing `DeleteFence` tests (M1); live check I5.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Model/FenceEdits.cs` | rename, icon size, lock | 1 |
| `src/NeoFences.Core/Layouts/Snapping.cs` | `Snap` (gap/align), `Unsnapped` (drag tracking) | 1 |
| `src/NeoFences.Shell/FenceWindowChrome.cs` | `ReadRect` / `WriteRect`; tint comment | 2 |
| `src/NeoFences.Shell/SystemTheme.cs` | Windows app mode | 2 |
| `src/NeoFences.App/FenceWindow.xaml(.cs)` | menu, rename box, icon size, lock, snapping hook, theme, scrollbar, label shadow, Enter | 3 |
| `src/NeoFences.App/FenceHost.cs`, `SystemMessageWindow.cs` | apply edits, snap targets, theme broadcast | 3 |
| `docs/TEST-CHECKLIST.md` (section I), `docs/research/m2c-fence-interactions.md` | verification | 4 |
| `docs/DECISIONS.md` (ADR-015), `ARCHITECTURE.md`, `FEATURES.md`, `ROADMAP.md`, `SESSION-LOG.md`, hub | docs sync | 5 |

---

### Task 1: Core — fence edits and snapping

**Files:**
- Modify: `docs/ROADMAP.md` (claim M2c)
- Create: `src/NeoFences.Core/Model/FenceEdits.cs`, `src/NeoFences.Core/Layouts/Snapping.cs`, `tests/NeoFences.Core.Tests/Model/FenceEditsTests.cs`, `tests/NeoFences.Core.Tests/Layouts/SnappingTests.cs`

**Interfaces:**
- Consumes: `NeoFencesConfig.WithFence`, `ConfigNormalizer.IconSizes`, `PixelRect` (M1/M2a).
- Produces:
  - `FenceEdits.Rename(NeoFencesConfig config, string fenceId, string title)`, `FenceEdits.SetIconSize(config, fenceId, int iconSize)` and `FenceEdits.SetLocked(config, fenceId, bool locked)`, each returning `NeoFencesConfig`. There is also `FenceEdits.MaxTitleLength = 64`;
  - `[Flags] enum SnapEdges { None, Left, Top, Right, Bottom, Move }`;
  - `Snapping.Snap(PixelRect rect, SnapEdges edges, PixelRect workArea, IReadOnlyList<PixelRect> others, int gapPx, int thresholdPx) -> PixelRect`;
  - `Snapping.Unsnapped(PixelRect unsnapped, PixelRect current, PixelRect proposal) -> PixelRect`.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m2c-fence-interactions
```
In `docs/ROADMAP.md`, append ` — [~] claimed by session 2026-10-02 m2c` to the line `### M2c — Fence interactions (keyboard, snap, lock, scroll, icon size, rename, light/dark)`.
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M2c"
```

- [ ] **Step 2: Write the failing tests**

`tests/NeoFences.Core.Tests/Model/FenceEditsTests.cs`:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

public class FenceEditsTests
{
    private static (NeoFencesConfig Config, Fence Games) Sample()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true };
        var games = Fence.Create("Games");
        return (new NeoFencesConfig { Fences = [inbox, games] }, games);
    }

    private static Fence FenceOf(NeoFencesConfig config, string fenceId) => config.Fences.Single(fence => fence.Id == fenceId);

    [Fact]
    public void Rename_TrimsTheNewTitle()
    {
        var (config, games) = Sample();

        var renamed = FenceEdits.Rename(config, games.Id, "  Shooters  ");

        Assert.Equal("Shooters", FenceOf(renamed, games.Id).Title);
    }

    [Fact]
    public void Rename_BlankTitle_KeepsTheOldOne()
    {
        // Enter on an emptied title box must not leave a nameless fence.
        var (config, games) = Sample();

        Assert.Same(config, FenceEdits.Rename(config, games.Id, "   "));
    }

    [Fact]
    public void Rename_LongTitle_IsCut()
    {
        var (config, games) = Sample();

        var renamed = FenceEdits.Rename(config, games.Id, new string('x', 200));

        Assert.Equal(FenceEdits.MaxTitleLength, FenceOf(renamed, games.Id).Title.Length);
    }

    [Fact]
    public void Rename_UnknownFence_Throws()
    {
        var (config, _) = Sample();

        Assert.Throws<ArgumentException>(() => FenceEdits.Rename(config, "missing", "Name"));
    }

    [Theory]
    [InlineData(32)]
    [InlineData(96)]
    public void SetIconSize_SupportedSize_IsStored(int iconSize)
    {
        var (config, games) = Sample();

        Assert.Equal(iconSize, FenceOf(FenceEdits.SetIconSize(config, games.Id, iconSize), games.Id).IconSize);
    }

    [Fact]
    public void SetIconSize_UnsupportedSize_Throws()
    {
        var (config, games) = Sample();

        Assert.Throws<ArgumentOutOfRangeException>(() => FenceEdits.SetIconSize(config, games.Id, 50));
    }

    [Fact]
    public void SetLocked_IsStored_AndUnlockRestores()
    {
        var (config, games) = Sample();

        var locked = FenceEdits.SetLocked(config, games.Id, locked: true);

        Assert.True(FenceOf(locked, games.Id).Locked);
        Assert.False(FenceOf(FenceEdits.SetLocked(locked, games.Id, locked: false), games.Id).Locked);
    }
}
```
`tests/NeoFences.Core.Tests/Layouts/SnappingTests.cs`:
```csharp
using NeoFences.Core.Layouts;

namespace NeoFences.Core.Tests.Layouts;

public class SnappingTests
{
    private const int Gap = 8;
    private const int Threshold = 12;
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1032);

    private static PixelRect Snap(PixelRect rect, SnapEdges edges, params PixelRect[] others) =>
        Snapping.Snap(rect, edges, workArea: WorkArea, others: others, gapPx: Gap, thresholdPx: Threshold);

    [Fact]
    public void Move_NearWorkAreaCorner_KeepsTheGapOnBothAxes() =>
        Assert.Equal(new PixelRect(8, 8, 300, 200), Snap(new PixelRect(3, 4, 300, 200), SnapEdges.Move));

    [Fact]
    public void Move_NearWorkAreaRightEdge_SnapsByTheRightEdge() =>
        Assert.Equal(new PixelRect(1612, 400, 300, 200), Snap(new PixelRect(1605, 400, 300, 200), SnapEdges.Move));

    [Fact]
    public void Move_NearAnotherFence_SnapsBesideItWithTheGap()
    {
        var neighbour = new PixelRect(8, 8, 300, 200); // right edge 308

        Assert.Equal(new PixelRect(316, 50, 300, 200), Snap(new PixelRect(320, 50, 300, 200), SnapEdges.Move, neighbour));
    }

    [Fact]
    public void Move_AlignsTopWithANeighbourBesideIt()
    {
        var neighbour = new PixelRect(8, 100, 300, 200);

        Assert.Equal(100, Snap(new PixelRect(316, 106, 300, 200), SnapEdges.Move, neighbour).Y);
    }

    [Fact]
    public void Move_FarFromEverything_IsUnchanged()
    {
        var rect = new PixelRect(500, 500, 300, 200);

        Assert.Equal(rect, Snap(rect, SnapEdges.Move, new PixelRect(8, 8, 300, 200)));
    }

    [Fact]
    public void Move_NeighbourFarAboveOrBelow_DoesNotPullSideways()
    {
        // Only fences level with the moving one count for left/right snapping.
        var neighbour = new PixelRect(8, 8, 300, 200);
        var rect = new PixelRect(320, 700, 300, 200);

        Assert.Equal(rect, Snap(rect, SnapEdges.Move, neighbour));
    }

    [Fact]
    public void Move_PicksTheClosestCandidate()
    {
        var neighbour = new PixelRect(14, 400, 300, 200); // left edge 14: aligning would be 4 px away, the work-area gap 2 px

        Assert.Equal(8, Snap(new PixelRect(10, 500, 300, 200), SnapEdges.Move, neighbour).X);
    }

    [Fact]
    public void Size_RightEdge_SnapsOnlyThatEdge()
    {
        var neighbour = new PixelRect(418, 100, 300, 200); // a gap before it means right edge 410

        Assert.Equal(new PixelRect(100, 100, 310, 200), Snap(new PixelRect(100, 100, 300, 200), SnapEdges.Right, neighbour));
    }

    [Fact]
    public void Size_TopLeftCorner_SnapsBothOfItsEdges_KeepsTheOthers() =>
        Assert.Equal(new PixelRect(8, 8, 397, 296), Snap(new PixelRect(5, 4, 400, 300), SnapEdges.Left | SnapEdges.Top));

    [Fact]
    public void Unsnapped_AddsEachProposalsStepToTheUnsnappedRect()
    {
        // Windows proposes "where the window is now + this mouse step"; the step is what counts.
        var unsnapped = new PixelRect(1240, 320, 320, 220);
        var current = new PixelRect(1249, 320, 320, 220);   // snapped back last time
        var proposal = new PixelRect(1246, 322, 320, 220);  // current + (-3, +2)

        Assert.Equal(new PixelRect(1237, 322, 320, 220), Snapping.Unsnapped(unsnapped, current: current, proposal: proposal));
    }

    [Fact]
    public void Unsnapped_Resize_MovesOnlyTheChangedEdges()
    {
        var unsnapped = new PixelRect(100, 100, 300, 200);
        var proposal = new PixelRect(100, 100, 305, 200); // right edge +5

        Assert.Equal(new PixelRect(100, 100, 305, 200), Snapping.Unsnapped(unsnapped, current: unsnapped, proposal: proposal));
    }

    [Fact]
    public void SlowDragAwayFromASnapTarget_Escapes()
    {
        // M2c smoke: snapping every WM_MOVING proposal pinned the fence, because each 2-3 px step was snapped back.
        var neighbour = new PixelRect(1249, 548, 320, 223);
        var current = new PixelRect(1249, 320, 320, 220);
        var unsnapped = current;
        for (var step = 0; step < 10; step++)
        {
            unsnapped = Snapping.Unsnapped(unsnapped, current: current, proposal: current with { X = current.X - 3 });
            current = Snap(unsnapped, SnapEdges.Move, neighbour);
        }

        Assert.Equal(1219, current.X);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS with `CS0103`/`CS0246` for `FenceEdits` and `SnapEdges`.

- [ ] **Step 4: Implement**

`src/NeoFences.Core/Model/FenceEdits.cs`:
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

    private static Fence Require(NeoFencesConfig config, string fenceId) =>
        config.Fences.FirstOrDefault(fence => fence.Id == fenceId) ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));
}
```
`src/NeoFences.Core/Layouts/Snapping.cs`:
```csharp
namespace NeoFences.Core.Layouts;

/// <summary>The edges a drag changes: all four when moving, the grabbed ones when resizing.</summary>
[Flags]
public enum SnapEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    Move = Left | Top | Right | Bottom,
}

/// <summary>
/// Smart placement while dragging (spec §6): edges near the work-area edge or another fence snap to a fixed gap
/// beside it, or line up with it. All values in physical pixels of one monitor.
/// </summary>
public static class Snapping
{
    public static PixelRect Snap(PixelRect rect, SnapEdges edges, PixelRect workArea, IReadOnlyList<PixelRect> others, int gapPx, int thresholdPx)
    {
        var left = rect.X;
        var top = rect.Y;
        var right = rect.X + rect.Width;
        var bottom = rect.Y + rect.Height;
        var reach = gapPx + thresholdPx;

        // Only fences level with this one (overlapping, or within reach, on the other axis) pull its edges.
        var besideIt = others.Where(other => other.Y < bottom + reach && other.Y + other.Height > top - reach).ToList();
        var aboveOrBelow = others.Where(other => other.X < right + reach && other.X + other.Width > left - reach).ToList();

        int? Best(int edge, IEnumerable<int> candidates)
        {
            var closest = candidates.OrderBy(candidate => Math.Abs(candidate - edge)).Cast<int?>().FirstOrDefault();
            return closest is { } value && Math.Abs(value - edge) <= thresholdPx ? value : null;
        }

        var leftTarget = Best(left, [workArea.X + gapPx, .. besideIt.Select(other => other.X + other.Width + gapPx), .. besideIt.Select(other => other.X)]);
        var rightTarget = Best(right, [workArea.X + workArea.Width - gapPx, .. besideIt.Select(other => other.X - gapPx), .. besideIt.Select(other => other.X + other.Width)]);
        var topTarget = Best(top, [workArea.Y + gapPx, .. aboveOrBelow.Select(other => other.Y + other.Height + gapPx), .. aboveOrBelow.Select(other => other.Y)]);
        var bottomTarget = Best(bottom, [workArea.Y + workArea.Height - gapPx, .. aboveOrBelow.Select(other => other.Y - gapPx), .. aboveOrBelow.Select(other => other.Y + other.Height)]);

        if (edges == SnapEdges.Move)
        {
            var dx = Closer(leftTarget - left, rightTarget - right);
            var dy = Closer(topTarget - top, bottomTarget - bottom);
            return rect with { X = rect.X + dx, Y = rect.Y + dy };
        }

        if (edges.HasFlag(SnapEdges.Left) && leftTarget is { } newLeft && newLeft < right) left = newLeft;
        if (edges.HasFlag(SnapEdges.Right) && rightTarget is { } newRight && newRight > left) right = newRight;
        if (edges.HasFlag(SnapEdges.Top) && topTarget is { } newTop && newTop < bottom) top = newTop;
        if (edges.HasFlag(SnapEdges.Bottom) && bottomTarget is { } newBottom && newBottom > top) bottom = newBottom;
        return new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Tracks where a drag would put the window without snapping. Windows proposes each WM_MOVING / WM_SIZING rect
    /// as "the window now + this mouse step"; snapping that proposal directly snaps every small step back and the
    /// fence sticks. Instead add the step (proposal minus current, per edge) to the unsnapped rect and snap that.
    /// </summary>
    public static PixelRect Unsnapped(PixelRect unsnapped, PixelRect current, PixelRect proposal)
    {
        var left = unsnapped.X + (proposal.X - current.X);
        var top = unsnapped.Y + (proposal.Y - current.Y);
        var right = unsnapped.X + unsnapped.Width + (proposal.X + proposal.Width - (current.X + current.Width));
        var bottom = unsnapped.Y + unsnapped.Height + (proposal.Y + proposal.Height - (current.Y + current.Height));
        return new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>The smaller of two optional shifts (0 when neither edge snaps).</summary>
    private static int Closer(int? first, int? second) => (first, second) switch
    {
        ({ } a, { } b) => Math.Abs(a) <= Math.Abs(b) ? a : b,
        ({ } a, null) => a,
        (null, { } b) => b,
        _ => 0,
    };
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 113`

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core tests/NeoFences.Core.Tests
git commit -m "feat: added fence edits and drag snapping to core"
```

---

### Task 2: Shell — RECT helpers and Windows app mode

**Files:**
- Modify: `src/NeoFences.Shell/FenceWindowChrome.cs` (full new content below)
- Create: `src/NeoFences.Shell/SystemTheme.cs`

**Interfaces:**
- Produces:
  - `FenceWindowChrome.ReadRect(nint rectPointer) -> PixelRect` and `FenceWindowChrome.WriteRect(nint rectPointer, PixelRect value)`;
  - `SystemTheme.AppsUseLightTheme() -> bool`.

- [ ] **Step 1: Replace and create**

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
`src/NeoFences.Shell/SystemTheme.cs`:
```csharp
using Microsoft.Win32;

namespace NeoFences.Shell;

/// <summary>The Windows "app mode" (Settings → Personalization → Colors), which fences follow (M2c, user choice).</summary>
public static class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>True for light mode. Missing value (older builds, policy-stripped profiles): light, like Windows.</summary>
    public static bool AppsUseLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 113`.

- [ ] **Step 3: Commit**

```powershell
git add src/NeoFences.Shell
git commit -m "feat: added drag rect helpers and windows app mode to shell"
```

---

### Task 3: App — fence menu, rename, icon size, lock, snapping, keyboard, theme

**Files:**
- Modify (full new content below): `src/NeoFences.App/FenceWindow.xaml`, `FenceWindow.xaml.cs`, `FenceHost.cs`, `SystemMessageWindow.cs`

**Interfaces:**
- Consumes: Task 1 and Task 2.
- Produces:
  - `FenceWindow(Fence fence, bool takeoverActive, bool lightTheme, IconLoader iconLoader)`. `SetItems(IReadOnlyList<string>)` no longer takes the loader;
  - `SetTitle`, `SetIconSize`, `SetLocked`, `ApplyTheme`;
  - the `SnapRect` callback;
  - events `RenameRequested(string)`, `IconSizeRequested(int)`, `LockToggled(bool)`, `DeleteRequested()`;
  - `SystemMessageWindow.ThemeChanged`.

Notes for the implementer:
- `DpiChanged` is a routed event, so filter on `OriginalSource == this`.
- Lock replaces the whole `WindowChrome`.
- Snap the tracked unsnapped rect, never the raw `WM_MOVING` proposal.
- The light veil is a WPF brush, because the accent tint is ignored.

- [ ] **Step 1: Replace the four files**

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
            <TextBlock x:Name="TitleText" Foreground="{DynamicResource FenceText}" FontWeight="SemiBold" Margin="12,0"
                       VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
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
            <Border Grid.Row="2" BorderBrush="{DynamicResource FenceDivider}" BorderThickness="0,1,0,0" Background="#01000000">
                <Border.ContextMenu>
                    <ContextMenu x:Name="BodyContextMenu">
                        <MenuItem x:Name="NewFenceItem" Header="New fence" />
                        <MenuItem x:Name="RenameItem" Header="Rename fence" />
                        <MenuItem x:Name="IconSizeItem" Header="Icon size" />
                        <MenuItem x:Name="LockItem" Header="Lock position" IsCheckable="True" />
                        <MenuItem x:Name="DeleteItem" Header="Delete fence (items go to the Inbox)" />
                        <Separator />
                        <MenuItem x:Name="TakeoverItem" Header="Hide desktop icons" IsCheckable="True" />
                        <Separator />
                        <MenuItem x:Name="ExitItem" Header="Exit NeoFences" />
                    </ContextMenu>
                </Border.ContextMenu>
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
                                <TextBlock Text="{Binding Label}" Foreground="{DynamicResource FenceText}" FontSize="12" Margin="0,4,0,0"
                                           TextAlignment="Center" TextWrapping="Wrap" TextTrimming="CharacterEllipsis"
                                           MaxHeight="32" Effect="{DynamicResource LabelShadow}" />
                            </StackPanel>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>
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
    private const int WmSizing = 0x0214;
    private const int WmMoving = 0x0216;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const double CornerRadiusDips = 8;
    private const double CaptionHeightDips = 30;
    private const double ResizeBorderDips = 6;

    private static readonly (int Size, string Name)[] IconSizeNames = [(32, "Small"), (48, "Medium"), (64, "Large"), (96, "Extra large")];

    private readonly ObservableCollection<FenceItemView> _items = [];
    private readonly IconLoader _iconLoader;
    private IReadOnlyList<string> _itemRefs = [];
    private int _iconSizeDips;
    private bool _renaming;
    private PixelRect? _unsnappedDrag;

    public string FenceId { get; }

    public nint Handle { get; private set; }

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

    public FenceWindow(Fence fence, bool takeoverActive, bool lightTheme, IconLoader iconLoader)
    {
        FenceId = fence.Id;
        _iconLoader = iconLoader;
        InitializeComponent();
        TitleText.Text = fence.Title;
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
        ItemList.ItemsSource = _items;
        ItemList.MouseDoubleClick += OnItemDoubleClick;
        ItemList.KeyDown += OnItemListKeyDown;
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
        SetLocked(fence.Locked);
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => ApplyRoundedCorners();
        // Icons are rendered for one DPI. DpiChanged is routed: every new item raises it too, so react only to the window's own.
        DpiChanged += (_, dpiChange) =>
        {
            if (dpiChange.OriginalSource == this && dpiChange.OldDpi.PixelsPerDip != dpiChange.NewDpi.PixelsPerDip) ReloadIcons();
        };
    }

    public void SetTakeoverChecked(bool active) => TakeoverItem.IsChecked = active;

    public void ShowTakeoverPrompt(bool visible) => TakeoverPrompt.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    public void SetTitle(string title) => TitleText.Text = title;

    /// <summary>Shows exactly these items in this order. Items already shown keep their loaded name and icon.</summary>
    public void SetItems(IReadOnlyList<string> itemRefs)
    {
        _itemRefs = itemRefs;
        if (_items.Select(view => view.ItemRef).SequenceEqual(itemRefs, StringComparer.Ordinal)) return;
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
        // A fresh WindowChrome each time: editing the attached one in place is not re-applied after an unlock.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            GlassFrameThickness = new Thickness(0),
            CaptionHeight = locked ? 0 : CaptionHeightDips,
            ResizeBorderThickness = new Thickness(locked ? 0 : ResizeBorderDips),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
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

    private void ReloadIcons()
    {
        _items.Clear(); // SetItems then requests every icon again
        SetItems(_itemRefs);
    }

    private void BeginRename()
    {
        _renaming = true;
        TitleBox.Text = TitleText.Text;
        TitleText.Visibility = Visibility.Hidden;
        TitleBox.Visibility = Visibility.Visible;
        Activate(); // keyboard input needs the fence active; it stays at the bottom (owned by Progman, ADR-011)
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void EndRename(bool commit)
    {
        if (!_renaming) return;
        _renaming = false;
        TitleBox.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
        if (commit && TitleBox.Text != TitleText.Text) RenameRequested?.Invoke(TitleBox.Text);
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
        if (args.Key != Key.Enter) return;
        foreach (var view in ItemList.SelectedItems.OfType<FenceItemView>().ToList()) OpenRequested?.Invoke(view.ItemRef);
        args.Handled = true;
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
        switch (message)
        {
            case WmEnterSizeMove:
                _unsnappedDrag = FenceWindowChrome.GetPixelRect(Handle);
                break;
            case WmMoving when SnapRect is not null && _unsnappedDrag is not null:
                FenceWindowChrome.WriteRect(lParam, SnapRect(TrackDrag(lParam), SnapEdges.Move));
                handled = true;
                return 1;
            case WmSizing when SnapRect is not null && _unsnappedDrag is not null:
                FenceWindowChrome.WriteRect(lParam, SnapRect(TrackDrag(lParam), SizingEdges((int)wParam)));
                handled = true;
                return 1;
            case WmExitSizeMove:
                _unsnappedDrag = null;
                MovedByUser?.Invoke(this, FenceWindowChrome.GetPixelRect(Handle));
                break;
        }
        return 0;
    }

    /// <summary>Adds this message's mouse step to the unsnapped drag rect and returns it (see <see cref="Snapping.Unsnapped"/>).</summary>
    private PixelRect TrackDrag(nint proposalPointer)
    {
        _unsnappedDrag = Snapping.Unsnapped(_unsnappedDrag!.Value,
            current: FenceWindowChrome.GetPixelRect(Handle),
            proposal: FenceWindowChrome.ReadRect(proposalPointer));
        return _unsnappedDrag.Value;
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

    private readonly ConfigStore _store = new(AppPaths.DataDirectory);
    private readonly Watchdog _watchdog = new(AppPaths.DataDirectory, message => Log.Information("watchdog: {Message}", message));
    private readonly SystemMessageWindow _messages = new();
    private readonly Dictionary<string, FenceWindow> _windows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _saveTimer;
    private readonly IconLoader _iconLoader = new(Dispatcher.CurrentDispatcher);
    private DesktopWatcher? _desktopWatcher;
    private NeoFencesConfig _config = NeoFencesConfig.CreateDefault();
    private IReadOnlyList<MonitorPlacement> _monitors = [];
    private bool _takeoverActive;
    private bool _lightTheme = SystemTheme.AppsUseLightTheme();
    private bool _sessionEnding;

    public event Action? ExitRequested;

    public FenceHost()
    {
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();
        _messages.ExplorerRestarted += OnExplorerRestarted;
        _messages.DisplayChanged += OnDisplayChanged;
        _messages.ThemeChanged += OnThemeChanged;
    }

    public void Start()
    {
        var loaded = _store.Load();
        Log.Information("config loaded from {Source} (read-only: {IsReadOnly}, corrupt copy: {CorruptCopyPath})",
            loaded.Source, loaded.IsReadOnly, loaded.CorruptCopyPath);
        _config = loaded.Config;
        _watchdog.LaunchDetached(Environment.ProcessId);

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
        if (_takeoverActive) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkSessionEnding(Environment.ProcessId), what: "session-ending marker");
    }

    /// <summary>Orderly exit: save, bring icons back, tell the watchdog all is well.</summary>
    public void Shutdown()
    {
        SaveNow();
        if (_sessionEnding) return; // OnSessionEnding already restored and marked; no clean marker, so a cancel restarts us
        if (_takeoverActive) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkCleanShutdown(Environment.ProcessId), what: "clean-shutdown marker");
        _desktopWatcher?.Dispose();
        _desktopWatcher = null;
        _iconLoader.Dispose();
        _messages.Dispose();
    }

    /// <summary>Best effort from the crash handler; the watchdog restores too.</summary>
    public void EmergencyRestoreIcons()
    {
        if (_takeoverActive) DesktopIcons.TrySetHidden(false);
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
        window.OpenRequested += OpenItem;
        window.TakeoverPromptAnswered += AnswerTakeoverPrompt;
        new WindowInteropHelper(window).EnsureHandle(); // HWND exists (styles, blur) before the first Show
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
        _config = FenceMembership.Apply(_config, change);
        RefreshWindows();
        ScheduleSave();
    }

    private void RefreshWindows()
    {
        var showPrompt = !_config.Settings.TakeoverPromptAnswered && !_takeoverActive;
        foreach (var fence in _config.Fences)
        {
            if (!_windows.TryGetValue(fence.Id, out var window)) continue;
            window.SetItems(fence.Items);
            window.ShowTakeoverPrompt(showPrompt && fence.IsInbox);
        }
    }

    private static void OpenItem(string itemRef)
    {
        // Off the UI thread: ShellExecute can block on a network timeout or a UAC prompt, freezing every fence (M2b review I6).
        Task.Run(() =>
        {
            if (!ShellItems.TryOpen(itemRef)) Log.Warning("could not open {ItemRef}", itemRef);
        });
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
                FenceWindowChrome.SetPixelRect(window.Handle, FencePlacement.ToPixels(rect, monitor));
                if (!window.IsVisible)
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
        _takeoverActive ? SetIconsHidden(true)
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

    public SystemMessageWindow()
    {
        _source = new HwndSource(new HwndSourceParameters("NeoFences.SystemMessages") { WindowStyle = 0, Width = 0, Height = 0 });
        _source.AddHook(OnMessage);
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
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

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 113`.

- [ ] **Step 3: Smoke runs (agent; ask the user first, because these take over the mouse)**

Restart NeoFences from the repo build (`NeoFences.exe --exit`, then start it). The interaction smoke needs a fence titled "Games" besides the Inbox. It moves that fence below the Inbox through the config and restores it afterwards.

Save as `<scratchpad>\m2c-smoke-interactions.ps1`:
```powershell
# M2c interaction smoke on the real desktop: slow drags escape snapping, snap gap, lock, unlock, edge resize snap.
# Uses the Inbox and a fence titled "Games" sitting below it (as on the dev machine); moves them back at the end.
param([string]$Exe = "F:\projects\neo_fences\src\NeoFences.App\bin\Debug\net10.0-windows\NeoFences.exe")
Add-Type -Namespace NfSmoke -Name Input -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
'@
function Pause-Ms([int]$ms) { [System.Threading.Thread]::Sleep($ms) }
function Show-Desktop { [NfSmoke.Input]::keybd_event(0x5B,0,0,[UIntPtr]::Zero); [NfSmoke.Input]::keybd_event(0x44,0,0,[UIntPtr]::Zero); [NfSmoke.Input]::keybd_event(0x44,0,2,[UIntPtr]::Zero); [NfSmoke.Input]::keybd_event(0x5B,0,2,[UIntPtr]::Zero); Pause-Ms 1200 }
function RightClick([int]$x, [int]$y) { [void][NfSmoke.Input]::SetCursorPos($x, $y); [NfSmoke.Input]::mouse_event(8,0,0,0,[UIntPtr]::Zero); [NfSmoke.Input]::mouse_event(16,0,0,0,[UIntPtr]::Zero); Pause-Ms 700 }
function LeftClick([int]$x, [int]$y) { [void][NfSmoke.Input]::SetCursorPos($x, $y); Pause-Ms 150; [NfSmoke.Input]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [NfSmoke.Input]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Pause-Ms 700 }
# Slow drag: 2 px per step, like a real hand, so every WM_MOVING step is below the snap threshold.
function Drag([int]$x, [int]$y, [int]$dx, [int]$dy) {
  [void][NfSmoke.Input]::SetCursorPos($x, $y); Pause-Ms 200; [NfSmoke.Input]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Pause-Ms 200
  $steps = [Math]::Max([Math]::Abs($dx), [Math]::Abs($dy)) / 2
  foreach ($step in 1..$steps) { [void][NfSmoke.Input]::SetCursorPos([int]($x + $dx * $step / $steps), [int]($y + $dy * $step / $steps)); Pause-Ms 15 }
  [NfSmoke.Input]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Pause-Ms 1200 }
function Get-FenceRect([string]$title) {
  $config = Get-Content "$env:LOCALAPPDATA\NeoFences\config.json" -Raw | ConvertFrom-Json
  $fence = $config.fences | Where-Object title -eq $title | Select-Object -First 1
  $rect = $config.layouts.($config.lastLayoutFingerprint).fences.($fence.id)
  [pscustomobject]@{ X = [int]$rect.x; Y = [int]$rect.y; W = [int]$rect.w; H = [int]$rect.h; Locked = [bool]$fence.locked } }

# Arrange: with the app stopped, put Games 12 px below the Inbox and 4 px right of its left edge (unsnapped), unlocked.
function Set-GamesRect([int]$x, [int]$y, [int]$w, [int]$h, [bool]$locked) {
  & $Exe --exit; $deadline = (Get-Date).AddSeconds(15); while ((Get-Process NeoFences -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Pause-Ms 300 }
  $path = "$env:LOCALAPPDATA\NeoFences\config.json"; $config = Get-Content $path -Raw | ConvertFrom-Json
  $fence = $config.fences | Where-Object title -eq 'Games' | Select-Object -First 1; $fence.locked = $locked
  $rect = $config.layouts.($config.lastLayoutFingerprint).fences.($fence.id); $rect.x = $x; $rect.y = $y; $rect.w = $w; $rect.h = $h
  [System.IO.File]::WriteAllText($path, ($config | ConvertTo-Json -Depth 20)); Start-Process $Exe; Pause-Ms 5000 }
$original = Get-FenceRect 'Games'; $inbox = Get-FenceRect 'Inbox'
if (-not $original) { "no fence titled Games; aborting"; return }
Set-GamesRect ($inbox.X + 4) ($inbox.Y + $inbox.H + 12) 320 223 $false
Show-Desktop
$games = Get-FenceRect 'Games'
$titleX = $games.X + 70; $titleY = $games.Y + 15
"start: Games $($games.X),$($games.Y)  Inbox $($inbox.X),$($inbox.Y) bottom $($inbox.Y + $inbox.H)"
Drag $titleX $titleY -120 0
$moved = Get-FenceRect 'Games'; "slow drag 120 px left escaped snapping: $($moved.X -eq $games.X - 120) (x=$($moved.X))"
Drag ($titleX - 120) $titleY 117 -4
$snapped = Get-FenceRect 'Games'; "dragged back near the Inbox snaps to x=$($inbox.X), y=$($inbox.Y + $inbox.H + 8): $($snapped.X -eq $inbox.X -and $snapped.Y -eq $inbox.Y + $inbox.H + 8) (got $($snapped.X),$($snapped.Y))"
$bodyX = $snapped.X + 150; $bodyY = $snapped.Y + 130
RightClick $bodyX $bodyY; LeftClick ($bodyX + 50) ($bodyY + 80)   # Lock position
Drag ($snapped.X + 70) ($snapped.Y + 15) -60 0
$locked = Get-FenceRect 'Games'; "locked: $($locked.Locked), did not move: $($locked.X -eq $snapped.X)"
RightClick $bodyX $bodyY; LeftClick ($bodyX + 50) ($bodyY + 80)   # unlock
Drag ($snapped.X + 70) ($snapped.Y + 15) -60 0
$unlocked = Get-FenceRect 'Games'; "unlocked: $(-not $unlocked.Locked), moves again: $($unlocked.X -eq $snapped.X - 60)"
Drag ($unlocked.X + 70) ($unlocked.Y + 15) 60 0
$right = Get-FenceRect 'Games'; $edgeY = $right.Y + 120
Drag ($right.X + $right.W - 2) $edgeY 40 0
$wider = Get-FenceRect 'Games'; "right edge slow-dragged 40 px: w=$($wider.W) (expected $($right.W + 40))"
Drag ($wider.X + $wider.W - 2) $edgeY -34 0
$resized = Get-FenceRect 'Games'; "dragged back to 6 px past the Inbox's right edge ($($inbox.X + $inbox.W)) aligns with it: $($resized.X + $resized.W -eq $inbox.X + $inbox.W) (w=$($resized.W))"
Set-GamesRect $original.X $original.Y $original.W $original.H $original.Locked
"restored: $((Get-FenceRect 'Games') | ConvertTo-Json -Compress)"
```
Run: `& "<scratchpad>\m2c-smoke-interactions.ps1"`
Expected: every line ends `True`, `w=360 (expected 360)`, and `restored:` shows the original rect.

Save as `<scratchpad>\m2c-smoke-final.ps1`:
```powershell
# M2c remaining checks: Enter opens the selection, delete fence, light/dark follows Windows.
param([string]$Scratch, [switch]$SkipDelete)
Add-Type -Namespace NfFinal -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, int data, UIntPtr extra);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
[DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, string l, uint flags, uint timeout, out IntPtr result);
'@
Add-Type -AssemblyName System.Drawing
function Pause-Ms([int]$ms) { [System.Threading.Thread]::Sleep($ms) }
function Key([byte]$vk) { [NfFinal.Native]::keybd_event($vk,0,0,[UIntPtr]::Zero); [NfFinal.Native]::keybd_event($vk,0,2,[UIntPtr]::Zero) }
function Show-Desktop { (New-Object -ComObject Shell.Application).MinimizeAll(); Pause-Ms 1200 } # not a toggle, unlike Win+D
function LeftClick([int]$x, [int]$y) { [void][NfFinal.Native]::SetCursorPos($x, $y); Pause-Ms 150; [NfFinal.Native]::mouse_event(2,0,0,0,[UIntPtr]::Zero); [NfFinal.Native]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Pause-Ms 700 }
function RightClick([int]$x, [int]$y) { [void][NfFinal.Native]::SetCursorPos($x, $y); [NfFinal.Native]::mouse_event(8,0,0,0,[UIntPtr]::Zero); [NfFinal.Native]::mouse_event(16,0,0,0,[UIntPtr]::Zero); Pause-Ms 700 }
function Foreground { $text = New-Object System.Text.StringBuilder 256; [void][NfFinal.Native]::GetWindowText([NfFinal.Native]::GetForegroundWindow(), $text, 256); "$text" }
function Get-Config { Get-Content "$env:LOCALAPPDATA\NeoFences\config.json" -Raw | ConvertFrom-Json }
function Get-Rect($config, $fence) { $config.layouts.($config.lastLayoutFingerprint).fences.($fence.id) }
function Save-Shot([string]$name, [int]$x, [int]$y, [int]$w, [int]$h) { $bitmap = New-Object System.Drawing.Bitmap $w, $h; $g = [System.Drawing.Graphics]::FromImage($bitmap); $g.CopyFromScreen($x, $y, 0, 0, $bitmap.Size); $bitmap.Save("$Scratch\$name.png"); $g.Dispose(); $bitmap.Dispose(); "$Scratch\$name.png" }
function Set-AppMode([int]$light) {
  Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name AppsUseLightTheme -Value $light
  $result = [IntPtr]::Zero; [void][NfFinal.Native]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [IntPtr]::Zero, "ImmersiveColorSet", 2, 3000, [ref]$result); Pause-Ms 2500 }

Show-Desktop
$config = Get-Config; $inbox = Get-Rect $config ($config.fences | Where-Object isInbox)
# 1. Enter opens the selection: click the first Inbox item (Recycle Bin), press Enter.
LeftClick ([int]$inbox.x + 50) ([int]$inbox.y + 70); Key 0x0D; Pause-Ms 2500
$opened = Foreground; "Enter opened: '$opened'"
if ($opened -like 'Recycle Bin*') { [void][NfFinal.Native]::PostMessage([NfFinal.Native]::GetForegroundWindow(), 0x10, [IntPtr]::Zero, [IntPtr]::Zero); Pause-Ms 800 }

# 2. New fence from the Inbox menu, then delete it from its own menu.
if (-not $SkipDelete) {
Show-Desktop
$before = (Get-Config).fences.Count
RightClick ([int]$inbox.x + 150) ([int]$inbox.y + 150); LeftClick ([int]$inbox.x + 200) ([int]$inbox.y + 164); Pause-Ms 1200
$config = Get-Config; $created = $config.fences[-1]; $rect = Get-Rect $config $created
"created: $($config.fences.Count -eq $before + 1) ('$($created.title)' at $($rect.x),$($rect.y))"
RightClick ([int]$rect.x + 150) ([int]$rect.y + 100); LeftClick ([int]$rect.x + 200) ([int]$rect.y + 202); Pause-Ms 1200
$config = Get-Config; "deleted: $($config.fences.Count -eq $before) (no fence $($created.id): $(-not ($config.fences.id -contains $created.id)))"
}

# 3. Light mode: switch Windows to light for a screenshot, then back.
$original = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize').AppsUseLightTheme
Set-AppMode 1; Show-Desktop
Save-Shot "m2c-light" ([int]$inbox.x - 40) ([int]$inbox.y - 40) 420 320
Set-AppMode $original
(New-Object -ComObject Shell.Application).UndoMinimizeALL()
"app mode restored to $original"
Select-String -Path "$env:LOCALAPPDATA\NeoFences\logs\neofences-*.log" -Pattern 'app mode changed' | Select-Object -Last 2 | ForEach-Object { $_.Line.Substring(30) }
```
Run: `& "<scratchpad>\m2c-smoke-final.ps1" -Scratch "<scratchpad>"`
Expected:
- `Enter opened: 'Recycle Bin - File Explorer'`;
- `created: True` and `deleted: True (… True)`. If a new fence lands on top of an older one, delete it from its own position;
- two `app mode changed` log lines;
- `m2c-light.png` shows a light veil with dark, readable text.

- [ ] **Step 4: Commit**

```powershell
git add src/NeoFences.App
git commit -m "feat: added fence menu, rename, icon size, lock, snapping, keyboard open and light/dark"
```

---

### Task 4: Verification

**Files:** `docs/TEST-CHECKLIST.md` (append section I), `docs/research/m2c-fence-interactions.md` (create)

- [ ] **Step 1: Append section I**

```markdown
## I — Fence interactions (M2c+)
| ID | Steps | Expected |
|---|---|---|
| I1 | Right-click a fence's body; then the Inbox's | menu: New fence, Rename fence, Icon size ►, Lock position, Delete fence (items go to the Inbox), Hide desktop icons, Exit; the Inbox has no Delete |
| I2 | Rename fence: type a name + Enter; again + Esc; again, clear the box + Enter; restart | Enter saves, Esc cancels, empty keeps the old title; title survives restart |
| I3 | Icon size ► Small / Large / Extra large | icons re-render sharp at the new size, labels re-wrap; the size survives restart |
| I4 | Lock position, then drag the title and an edge; unlock and drag again | locked: nothing moves or resizes; unlocked: both work; lock survives restart |
| I5 | Create a fence, move some items into it (M3) or use an empty one, Delete fence | fence gone; its items at the end of the Inbox; no file touched |
| I6 | Slowly drag a fence near another fence and near the screen edge; then slowly away | snaps to an 8 px gap or lines up with the other fence's edge within ~12 px; slow drags away escape (no sticking) |
| I7 | Drag a fence's right edge near another fence's right edge | the edge lines up; the left edge stays |
| I8 | Click an item; arrow keys; Ctrl+A; Enter | selection moves; all selected; Enter opens every selected item in front; the fence stays behind other windows |
| I9 | Switch Windows to light mode and back (Settings → Personalization → Colors) | fences follow live: light veil + dark text in light mode, unchanged dark look in dark mode; both readable |
| I10 | Labels over a bright part of the wallpaper | soft shadow (dark mode) / white halo (light mode) keeps them readable |
| I11 | Fence with more items than fit; hover and drag the scrollbar | thin rounded scrollbar without arrows; thumb brightens on hover; dragging scrolls |
| I12 | Change display scaling (or move a fence to a monitor with another DPI) | icons re-render sharp at the new DPI |
```

- [ ] **Step 2: Run I1–I12.** The agent runs what the smoke scripts cover. **[USER]** runs I10 (wallpaper-dependent), I12 (scaling change), and the drag feel.

- [ ] **Step 3: Write `docs/research/m2c-fence-interactions.md`.** Include the results and the four prototype findings: the routed `DpiChanged` loop, snapping on the proposal, the in-place chrome edit, and the ignored accent tint.

- [ ] **Step 4: Commit**

```powershell
git add docs/TEST-CHECKLIST.md docs/research/m2c-fence-interactions.md
git commit -m "docs: added M2c fence interaction checks and results"
```

---

### Task 5: Docs sync

**Files:** `docs/DECISIONS.md`, `ARCHITECTURE.md`, `FEATURES.md`, `ROADMAP.md`, `SESSION-LOG.md`, hub

- [ ] **Step 1: Append ADR-015 to `docs/DECISIONS.md`**

```markdown
## ADR-015 — Fence interactions: activation for keyboard, snapping on the unsnapped drag, theme veil drawn by the fence
**Date:** 2026-10-02 · **Status:** Accepted · **Corrects:** ADR-014's keyboard and "fences never activate" statements

**Context.** M2c adds keyboard, snap, lock, rename, icon size, delete, a styled scrollbar, label shadows and
light/dark. The user chose: fences follow Windows' light/dark mode, and labels get a soft shadow (2026-10-02).

**Decision.**
- **Keyboard.** As M0 found (B2), a click activates a fence despite `WS_EX_NOACTIVATE`. The fence still stays below
  apps, because Progman owns it (ADR-011). Arrows and Ctrl+A therefore work through the ListBox, and Enter opens
  the selection. ADR-014 said fences never activate, which was wrong. F2 and Del on items are shell actions (M3).
- **Snapping** (spec §6: 8 px gap, 12 px reach, line-up with neighbouring edges) runs in `WM_MOVING` / `WM_SIZING`.
  Windows proposes each rect as "the window now + this mouse step". Snapping that proposal snaps every 2–3 px step
  back, and the fence sticks. The fence therefore tracks the unsnapped drag rect (`Snapping.Unsnapped`, adding
  each step per edge) and snaps that.
- **Lock** sets a fresh `WindowChrome` with caption height 0 and no resize border. Editing the attached chrome in
  place was not re-applied after unlocking.
- **Theme.** The fence follows `AppsUseLightTheme` live, via `WM_SETTINGCHANGE "ImmersiveColorSet"`. The accent
  blur ignores its tint colour (`ACCENT_ENABLE_BLURBEHIND`), so the fence draws its own veil: none in dark mode
  (the M2a look the user approved) and ~72 % light in light mode. Text, borders and hover colours come from
  DynamicResources.
- **Icons** re-render after an icon-size change and after the window's own `DpiChanged`. That event is routed:
  every new item raises it too, and reacting to those reloaded the items forever (a 1.4 GB spin in the prototype).
- **Delete fence** moves its items to the Inbox and never touches files. There is no confirmation, because nothing
  is lost.

**Consequences.** Every fence menu action is undoable by hand. Rubber-band selection moves to M3 with drag-drop.
A light veil over a bright wallpaper looks flatter than Windows' acrylic. The tint preference planned for v2
covers that.
```

- [ ] **Step 2: Update the other docs**
  - `ARCHITECTURE.md`: add `FenceEdits`, `Snapping`, `SystemTheme` and the theme broadcast. Note the keyboard/activation correction.
  - `FEATURES.md`: set these rows to done: move/resize with snap, scrolling, per-fence icon size, keyboard navigation (minus F2/Del, which are M3), lock fences, and light/dark (follows Windows). Add rename and delete fence.
  - `ROADMAP.md`: tick M2c and list the carry-overs (rubber-band selection → M3).
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 3: Refresh the hub.** Update the `HUB` object, run `node --check`, and publish with `url`.

- [ ] **Step 4: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-015 and synced docs for M2c"
```
