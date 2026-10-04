# M2a — Fence Host Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put real fence windows on the desktop and keep them there. M2a delivers blurred, rounded, layered windows owned by Progman, one per configured fence. They are placed on the real monitors and saved when moved, and they survive Win+D, display changes, Explorer restarts, sign-out and crashes. Every re-check M0 left open runs on this exact window type. No icons yet (M2b).

**Architecture:** Three projects. `NeoFences.Core` (pure, tested) gains:
- pixel/DIP placement math;
- unclamped layout storage;
- session-end and restart policies;
- readable JSON.

`NeoFences.Shell` (new, net10.0-windows, CsWin32) wraps every Win32/COM call behind `nint` handles: monitors, desktop ownership, accent blur, rounded corners, desktop icons and a detached watchdog. `NeoFences.App` (new, WPF) adds:
- `FenceHost`, which orchestrates config, monitors, windows, debounced saves, display changes, Explorer restarts and session end;
- `FenceWindow`, the fence itself;
- `SystemMessageWindow`, a hidden top-level window that receives broadcasts;
- single-instance startup, logging and the `--exit` / `--watchdog` modes.

**Tech Stack:** .NET 10 SDK 10.0.401, WPF, `Microsoft.Windows.CsWin32` 0.3.335, `Serilog` 4.4.0 + `Serilog.Sinks.File` 7.0.0 (both listed in ADR-001), xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-10-02-neofences-v1-design.md` §4, §5, §7, §10 erratum. **Decisions:** ADR-002, ADR-005, ADR-006 (amended), **ADR-011** (layered + accent blur, owner = Progman, detached watchdog, session-end rules, Takeover off by default). **Research:** `docs/research/desktop-layer.md` → "M2 re-verification".

**Pre-verified (2026-10-02):** every code block was compiled together (0 errors, 0 warnings), **87/87 tests pass**, and a smoke run of the built app succeeded:
- it started and loaded a fresh config;
- it logged the real monitor as `\\?\DISPLAY#GSM5B71#…` (device interface path);
- it saved `config.json` and launched the watchdog **detached**: its parent, the launcher, had already exited;
- `NeoFences.exe --exit` produced a clean shutdown, and the watchdog logged "clean shutdown".

**Not yet verified:** the fence's look (rounded region on a layered + accent window) and all the behaviour in Task 5. That is what Task 5 is for.

## Global Constraints

- Windows 10 1809+ x64 only: `PlatformTarget` x64 everywhere. Shell and App target `net10.0-windows` with `<NoWarn>$(NoWarn);CA1416</NoWarn>`, because a versioned TFM would pull in the WinRT projections.
- Hard rules (CLAUDE.md):
  - never lose or hide user files;
  - icons always come back;
  - no injection; all Win32/COM in `NeoFences.Shell`; `Core` has no Windows references;
  - bindings via CsWin32 (the only hand-written `DllImport` is `SetWindowCompositionAttribute`, with a `ponytail:` comment);
  - no NuGet dependency without an ADR (CsWin32 and Serilog are in ADR-001);
  - shell failures are logged and degrade one feature, never crash.
- Fence window = **layered** WPF window (`AllowsTransparency`) + accent blur (`ACCENT_ENABLE_BLURBEHIND`, tint `0x40201A16`), owned by Progman via `GWLP_HWNDPARENT`, re-applied and verified (`GW_OWNER`) after `TaskbarCreated` (ADR-011).
- `Settings.Takeover` defaults to off. In M2a icon hiding is only a **preview** menu item, used to test watchdog and sign-out. It must not be presented as a feature until M2b shows icons in fences.
- Session end: restore icons on `WM_QUERYENDSESSION`; write the clean marker only on `WM_ENDSESSION(TRUE)`; re-hide on `WM_ENDSESSION(FALSE)` (ADR-011).
- The watchdog restores icons **only** while the `takeover-active` marker exists. It retries for ~5 s if Explorer is down, and starts detached via a `--watchdog-launch` hop.
- Runtime data: `%LOCALAPPDATA%\NeoFences\` (`config.json`, `.bak`, `backups\`, `logs\neofences-*.log`, `logs\watchdog-*.log`, `takeover-active`, `clean-shutdown-<pid>`, `watchdog-restarts.txt`).
- Commits: single line, Conventional Commits, past tense, **no** `Co-Authored-By` trailer, one logical change each. Test command: `dotnet test NeoFences.slnx` (root).
- Steps marked **[USER]** need the human at the keyboard.

## Review Focus

1. **Real sign-out (C8) with the icon-hiding preview on.** Expected: icons visible after sign-in, and the logs show query → restore → end(TRUE) → marker. Task 5 (manual); logic pinned by `SessionEndHandlerTests` (Task 2).
2. **Shutdown cancelled** on the "apps are blocking shutdown" screen. Expected: icons hidden again, no clean marker, so a later crash is still recovered. Pinned by `QueryThenCancelled_ReappliesTakeover_AndNeverMarksClean` (Task 2); manual check in Task 5.
3. **Graceful Explorer restart** (Task Manager → Windows Explorer → Restart). Expected: the log shows "re-attach … unattached 0", and Win+D still keeps the fence. Task 5, checklist B10.
4. **Fence UI thread hung for 10 s** (debug menu). Expected: the desktop right-click menu and the taskbar stay responsive. Task 5, checklist B11.
5. **Taskbar resized or moved, or display scaling changed, while running.** Expected: fences are re-placed on screen, and stored positions do not change. Pinned by `KnownLayout_StoredRectsStayUnclamped_DisplayRectsAreClamped` (Task 1); manual check in Task 5.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Layouts/FencePlacement.cs` | `PixelRect`, `MonitorPlacement` (+`ToDisplayMonitor`), px↔DIP conversion, containing monitor | 1 |
| `src/NeoFences.Core/Layouts/LayoutEngine.cs` | store rects unclamped, return clamped; `WithFenceRect` | 1 |
| `src/NeoFences.Core/Lifecycle/SessionEndHandler.cs` | query/end/cancel policy | 2 |
| `src/NeoFences.Core/Lifecycle/RestartThrottle.cs` | 3-per-10-min (ported from the spike) | 2 |
| `src/NeoFences.Core/Config/ConfigJson.cs` | relaxed encoder (readable `+`, `&`) | 2 |
| `src/NeoFences.Shell/*` | `Monitors`, `DesktopHost`, `FenceWindowChrome`, `DesktopIcons`, `Watchdog`, `NativeMethods.txt` | 3 |
| `src/NeoFences.App/*` | `App`, `AppPaths`, `SystemMessageWindow`, `FenceWindow`, `FenceHost`, `app.manifest` | 4 |
| `docs/research/m2a-fence-host.md`, `docs/TEST-CHECKLIST.md` | verification results, new G rows | 5 |
| `docs/DECISIONS.md` (ADR-012), `ARCHITECTURE.md`, `ROADMAP.md`, `FEATURES.md`, `SETUP.md`, `SESSION-LOG.md`, hub | docs sync | 6 |

---

### Task 1: Core — pixel placement and unclamped layout storage

**Files:**
- Modify: `docs/ROADMAP.md` (split M2, claim M2a)
- Create: `src/NeoFences.Core/Layouts/FencePlacement.cs`, `tests/NeoFences.Core.Tests/Layouts/FencePlacementTests.cs`
- Modify: `src/NeoFences.Core/Layouts/LayoutEngine.cs` (full new content below), `tests/NeoFences.Core.Tests/Layouts/LayoutEngineTests.cs`

**Interfaces:**
- Produces:
  - `record struct PixelRect(int X, int Y, int Width, int Height)`.
  - `record MonitorPlacement(string DeviceId, int PixelWidth, int PixelHeight, int WorkLeftPx, int WorkTopPx, int WorkWidthPx, int WorkHeightPx, int ScalePercent, bool IsPrimary)` with `Scale` and `ToDisplayMonitor()`.
  - `FencePlacement.ToPixels(FenceRect, MonitorPlacement) -> PixelRect`.
  - `FencePlacement.FromPixels(PixelRect, MonitorPlacement) -> FenceRect`.
  - `FencePlacement.ContainingMonitor(PixelRect, IReadOnlyList<MonitorPlacement>) -> MonitorPlacement`.
  - `LayoutEngine.WithFenceRect(NeoFencesConfig config, string fingerprint, string fenceId, FenceRect rect) -> NeoFencesConfig`.
- Changes: `LayoutEngine.Resolve` now **stores** rects unclamped and **returns** a clamped display layout. The returned `Layout` is no longer the same instance as the stored one. This resolves the M1-review deferral "clamped positions are stored".

- [ ] **Step 1: Branch, split M2 and claim M2a**

```powershell
git switch -c m2a-fence-host
```
In `docs/ROADMAP.md`, replace the line `## M2 — Fences render items (daily usable)` with:
```
## M2 — Fences render items (daily usable) — split into M2a / M2b / M2c (ADR-012)
### M2a — Fence host — [~] claimed by session 2026-10-02 m2a
### M2b — Desktop items (icons, open/select/keyboard, change notifications, Takeover + first run)
### M2c — Fence interactions (snap, lock, scroll, icon size, rename, light/dark)
```
Leave the existing M2 carry-over checkboxes underneath, unchanged.
```powershell
git add docs/ROADMAP.md
git commit -m "docs: split M2 into M2a, M2b, M2c and claimed M2a"
```

- [ ] **Step 2: Write the failing tests**

Create `tests/NeoFences.Core.Tests/Layouts/FencePlacementTests.cs`:
```csharp
using NeoFences.Core.Layouts;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Layouts;

public class FencePlacementTests
{
    // 4K at 150%, taskbar at the bottom (work area 3840x2088 px), primary.
    private static readonly MonitorPlacement Dell = new("DELL", 3840, 2160, WorkLeftPx: 0, WorkTopPx: 0, WorkWidthPx: 3840, WorkHeightPx: 2088, ScalePercent: 150, IsPrimary: true);
    // 1080p at 100%, to the right of the 4K, taskbar on its left (work area starts 48 px in).
    private static readonly MonitorPlacement Lg = new("LG", 1920, 1080, WorkLeftPx: 3888, WorkTopPx: 0, WorkWidthPx: 1872, WorkHeightPx: 1080, ScalePercent: 100, IsPrimary: false);

    [Fact]
    public void ToDisplayMonitor_ConvertsWorkAreaToDips()
    {
        Assert.Equal(new DisplayMonitor("DELL", 3840, 2160, 150, 2560, 1392, IsPrimary: true), Dell.ToDisplayMonitor());
    }

    [Fact]
    public void ToPixels_OffsetsByWorkAreaAndScales()
    {
        Assert.Equal(new PixelRect(60, 90, 480, 330), FencePlacement.ToPixels(new FenceRect("DELL", 40, 60, 320, 220), Dell));
        Assert.Equal(new PixelRect(3928, 24, 320, 220), FencePlacement.ToPixels(new FenceRect("LG", 40, 24, 320, 220), Lg));
    }

    [Fact]
    public void FromPixels_IsTheInverse()
    {
        var rect = new FenceRect("DELL", 40, 60, 320, 220);

        Assert.Equal(rect, FencePlacement.FromPixels(FencePlacement.ToPixels(rect, Dell), Dell));
    }

    [Fact]
    public void ContainingMonitor_UsesTheRectCentre()
    {
        Assert.Equal("LG", FencePlacement.ContainingMonitor(new PixelRect(3800, 100, 400, 200), [Dell, Lg]).DeviceId);
        Assert.Equal("DELL", FencePlacement.ContainingMonitor(new PixelRect(3500, 100, 400, 200), [Dell, Lg]).DeviceId);
    }

    [Fact]
    public void ContainingMonitor_CentreOffEveryScreen_PicksNearest()
    {
        Assert.Equal("LG", FencePlacement.ContainingMonitor(new PixelRect(9000, 100, 200, 100), [Dell, Lg]).DeviceId);
    }
}
```

In `tests/NeoFences.Core.Tests/Layouts/LayoutEngineTests.cs`, replace the line
```csharp
        Assert.Same(layout, resolved.Layouts[resolved.LastLayoutFingerprint!]);
```
with
```csharp
        Assert.Equal(layout.Fences, resolved.Layouts[resolved.LastLayoutFingerprint!].Fences);
```
and append these two tests before the class's closing `}`:
```csharp
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
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS with `CS0246: The type or namespace name 'MonitorPlacement' could not be found` and `CS0117: 'LayoutEngine' does not contain a definition for 'WithFenceRect'`.

- [ ] **Step 4: Implement**

`src/NeoFences.Core/Layouts/FencePlacement.cs`:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Layouts;

/// <summary>A rectangle in physical screen pixels (virtual-screen coordinates).</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>A monitor in physical pixels, as NeoFences.Shell measures it. <see cref="ToDisplayMonitor"/> gives Core's DIP view.</summary>
/// <param name="DeviceId">Unique per physical monitor and port (the device interface path).</param>
public sealed record MonitorPlacement(
    string DeviceId,
    int PixelWidth,
    int PixelHeight,
    int WorkLeftPx,
    int WorkTopPx,
    int WorkWidthPx,
    int WorkHeightPx,
    int ScalePercent,
    bool IsPrimary)
{
    public double Scale => ScalePercent / 100.0;

    public DisplayMonitor ToDisplayMonitor() =>
        new(DeviceId, PixelWidth, PixelHeight, ScalePercent, WorkWidthPx / Scale, WorkHeightPx / Scale, IsPrimary);
}

/// <summary>Converts fence rects (DIPs relative to a monitor's work area) to and from physical pixels.</summary>
public static class FencePlacement
{
    public static PixelRect ToPixels(FenceRect rect, MonitorPlacement monitor) => new(
        monitor.WorkLeftPx + (int)Math.Round(rect.X * monitor.Scale),
        monitor.WorkTopPx + (int)Math.Round(rect.Y * monitor.Scale),
        (int)Math.Round(rect.W * monitor.Scale),
        (int)Math.Round(rect.H * monitor.Scale));

    public static FenceRect FromPixels(PixelRect pixels, MonitorPlacement monitor) => new(
        monitor.DeviceId,
        (pixels.X - monitor.WorkLeftPx) / monitor.Scale,
        (pixels.Y - monitor.WorkTopPx) / monitor.Scale,
        pixels.Width / monitor.Scale,
        pixels.Height / monitor.Scale);

    /// <summary>The monitor whose work area holds the rect's centre; if none does, the nearest one.</summary>
    public static MonitorPlacement ContainingMonitor(PixelRect pixels, IReadOnlyList<MonitorPlacement> monitors)
    {
        var centreX = pixels.X + pixels.Width / 2.0;
        var centreY = pixels.Y + pixels.Height / 2.0;
        return monitors.MinBy(monitor =>
        {
            var dx = Math.Max(Math.Max(monitor.WorkLeftPx - centreX, 0), centreX - (monitor.WorkLeftPx + monitor.WorkWidthPx));
            var dy = Math.Max(Math.Max(monitor.WorkTopPx - centreY, 0), centreY - (monitor.WorkTopPx + monitor.WorkHeightPx));
            return dx * dx + dy * dy;
        }) ?? throw new ArgumentException("At least one monitor is required.", nameof(monitors));
    }
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
                var offset = NewFenceMargin + CascadeStep * cascadeIndex++;
                rect = new FenceRect(primary.DeviceId, offset, offset, DefaultWidth, DefaultHeight);
                rects[fence.Id] = rect;
            }
            displayRects[fence.Id] = Clamp(rect, areas[rect.Monitor]);
        }

        var storedLayout = new Layout { Monitors = areas, Fences = rects };
        var layouts = new Dictionary<string, Layout>(config.Layouts) { [fingerprint] = storedLayout };
        return (config with { Layouts = layouts, LastLayoutFingerprint = fingerprint }, new Layout { Monitors = areas, Fences = displayRects });
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

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 78`

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core/Layouts tests/NeoFences.Core.Tests/Layouts
git commit -m "feat: added pixel placement and unclamped layout storage to core"
```

---

### Task 2: Core — session-end policy, restart throttle, readable JSON

**Files:**
- Create: `src/NeoFences.Core/Lifecycle/SessionEndHandler.cs`, `src/NeoFences.Core/Lifecycle/RestartThrottle.cs`, `tests/NeoFences.Core.Tests/Lifecycle/LifecycleTests.cs`
- Modify: `src/NeoFences.Core/Config/ConfigJson.cs` (full new content below), `tests/NeoFences.Core.Tests/Config/ConfigJsonTests.cs`

**Interfaces:**
- Produces:
  - `SessionEndHandler(Action restoreIcons, Action reapplyTakeover, Action markCleanShutdown, Func<bool> isTakeoverActive)` with `OnQueryEndSession()` and `OnEndSession(bool sessionIsEnding)`.
  - `RestartThrottle.ShouldRestart(IReadOnlyList<DateTimeOffset> recentRestarts, DateTimeOffset now, int maxRestarts = 3, TimeSpan? window = null) -> bool`.
- Changes: `ConfigJson` writes `+` and `&` literally (`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`). Monitor device paths contain `&`, and the hotkey contains `+`. The file is never embedded in HTML.

- [ ] **Step 1: Write the failing lifecycle tests** — `tests/NeoFences.Core.Tests/Lifecycle/LifecycleTests.cs`

```csharp
using NeoFences.Core.Lifecycle;

namespace NeoFences.Core.Tests.Lifecycle;

public class SessionEndHandlerTests
{
    private readonly List<string> _calls = [];
    private bool _takeoverActive = true;

    private SessionEndHandler NewHandler() => new(
        restoreIcons: () => _calls.Add("restore"),
        reapplyTakeover: () => _calls.Add("reapply"),
        markCleanShutdown: () => _calls.Add("mark"),
        isTakeoverActive: () => _takeoverActive);

    [Fact]
    public void Query_RestoresIcons_WithoutMarkingClean()
    {
        NewHandler().OnQueryEndSession();

        Assert.Equal(["restore"], _calls);
    }

    [Fact]
    public void QueryThenEnding_MarksCleanOnlyAtTheEnd()
    {
        var handler = NewHandler();

        handler.OnQueryEndSession();
        handler.OnEndSession(sessionIsEnding: true);

        Assert.Equal(["restore", "mark"], _calls);
    }

    [Fact]
    public void QueryThenCancelled_ReappliesTakeover_AndNeverMarksClean()
    {
        // Review finding: a cancelled shutdown must not leave a stale clean marker or visible icons behind.
        var handler = NewHandler();

        handler.OnQueryEndSession();
        handler.OnEndSession(sessionIsEnding: false);

        Assert.Equal(["restore", "reapply"], _calls);
    }

    [Fact]
    public void EndingWithoutQuery_StillRestoresBeforeMarking()
    {
        NewHandler().OnEndSession(sessionIsEnding: true);

        Assert.Equal(["restore", "mark"], _calls);
    }

    [Fact]
    public void TakeoverOff_OnlyMarksClean()
    {
        _takeoverActive = false;
        var handler = NewHandler();

        handler.OnQueryEndSession();
        handler.OnEndSession(sessionIsEnding: false);
        handler.OnEndSession(sessionIsEnding: true);

        Assert.Equal(["mark"], _calls);
    }
}

public class RestartThrottleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoRecentRestarts_Restarts() =>
        Assert.True(RestartThrottle.ShouldRestart(recentRestarts: [], now: Now));

    [Fact]
    public void ThreeRestartsInTenMinutes_StaysDown() =>
        Assert.False(RestartThrottle.ShouldRestart(recentRestarts: [Now.AddMinutes(-1), Now.AddMinutes(-2), Now.AddMinutes(-9)], now: Now));

    [Fact]
    public void OldRestartsOutsideWindow_DoNotCount() =>
        Assert.True(RestartThrottle.ShouldRestart(recentRestarts: [Now.AddMinutes(-1), Now.AddMinutes(-11), Now.AddHours(-2)], now: Now));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS with `CS0234: The type or namespace name 'Lifecycle' does not exist in the namespace 'NeoFences.Core'`.

- [ ] **Step 3: Implement**

`src/NeoFences.Core/Lifecycle/SessionEndHandler.cs`:
```csharp
namespace NeoFences.Core.Lifecycle;

/// <summary>
/// Sign-out / shutdown handling (ADR-011). Windows sends WM_QUERYENDSESSION, then WM_ENDSESSION with
/// wParam TRUE (the session really ends) or FALSE (shutdown was cancelled, e.g. on the "apps are
/// blocking shutdown" screen). Icons come back on the query; the clean-shutdown marker is written only
/// when the session really ends; a cancelled shutdown re-hides the icons and writes no marker.
/// </summary>
public sealed class SessionEndHandler(Action restoreIcons, Action reapplyTakeover, Action markCleanShutdown, Func<bool> isTakeoverActive)
{
    private bool _iconsRestoredForQuery;

    public void OnQueryEndSession()
    {
        if (!isTakeoverActive()) return;
        restoreIcons();
        _iconsRestoredForQuery = true;
    }

    public void OnEndSession(bool sessionIsEnding)
    {
        if (sessionIsEnding)
        {
            if (isTakeoverActive() && !_iconsRestoredForQuery) restoreIcons();
            markCleanShutdown();
            return;
        }

        if (_iconsRestoredForQuery)
        {
            reapplyTakeover();
            _iconsRestoredForQuery = false;
        }
    }
}
```

`src/NeoFences.Core/Lifecycle/RestartThrottle.cs`:
```csharp
namespace NeoFences.Core.Lifecycle;

/// <summary>Watchdog restart policy: at most <c>maxRestarts</c> within <c>window</c> (ADR-005: 3 per 10 min).</summary>
public static class RestartThrottle
{
    public static bool ShouldRestart(IReadOnlyList<DateTimeOffset> recentRestarts, DateTimeOffset now, int maxRestarts = 3, TimeSpan? window = null)
    {
        var span = window ?? TimeSpan.FromMinutes(10);
        return recentRestarts.Count(restartTime => now - restartTime < span) < maxRestarts;
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 86`

- [ ] **Step 5: Write the failing JSON test** — append before the closing `}` of `tests/NeoFences.Core.Tests/Config/ConfigJsonTests.cs`:

```csharp
    [Fact]
    public void Json_IsReadableForHandEditing_NoEscapedPlusOrAmpersand()
    {
        var config = NeoFencesConfig.CreateDefault() with
        {
            LastLayoutFingerprint = @"1mon:\\?\DISPLAY#GSM5B71#5&66efef6&1&UID4353-1920x1080@100%",
        };

        var json = ConfigJson.Serialize(config);

        Assert.Contains("\"peekHotkey\": \"Ctrl+Alt+Space\"", json);
        Assert.Contains("5&66efef6&1&UID4353", json);
    }
```

- [ ] **Step 6: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: `Failed! - Failed: 1, Passed: 86`, with the failure in `Json_IsReadableForHandEditing_NoEscapedPlusOrAmpersand`.

- [ ] **Step 7: Implement** — replace `src/NeoFences.Core/Config/ConfigJson.cs` with:

```csharp
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

/// <summary>
/// JSON for <see cref="NeoFencesConfig"/>: camelCase names, camelCase enum values (spec §5 shape).
/// Reflection-based on purpose: the source generator assigns every init-only property, so a property
/// missing from a hand-edited file would lose its default (e.g. iconSize 0 instead of 48). ADR-006, amended in M1.
/// </summary>
public static class ConfigJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Hand-edited file, never embedded in HTML: write + and & as-is instead of + and &.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(NeoFencesConfig config) => JsonSerializer.Serialize(config, Options);

    /// <summary>Raw parse; explicit nulls and odd values survive. Run the result through <c>ConfigNormalizer</c>.</summary>
    /// <exception cref="JsonException">The text is not a valid config document.</exception>
    public static NeoFencesConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<NeoFencesConfig>(json, Options)
        ?? throw new JsonException("config.json contains null");
}
```

- [ ] **Step 8: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 87`

- [ ] **Step 9: Commit (two commits)**

```powershell
git add src/NeoFences.Core/Lifecycle tests/NeoFences.Core.Tests/Lifecycle
git commit -m "feat: added session-end policy and restart throttle to core"
git add src/NeoFences.Core/Config/ConfigJson.cs tests/NeoFences.Core.Tests/Config/ConfigJsonTests.cs
git commit -m "fix: wrote config json without escaping plus and ampersand"
```

---

### Task 3: NeoFences.Shell

**Files:**
- Create: `src/NeoFences.Shell/NeoFences.Shell.csproj`, `NativeMethods.txt`, `Monitors.cs`, `DesktopHost.cs`, `FenceWindowChrome.cs`, `DesktopIcons.cs`, `Watchdog.cs`

**Interfaces:**
- Consumes: `MonitorPlacement`, `PixelRect` (Task 1), `RestartThrottle` (Task 2).
- Produces (all public, handles as `nint`):
  - `Monitors.Enumerate() -> IReadOnlyList<MonitorPlacement>`. `DeviceId` is the device interface path, with a fallback to the GDI name.
  - `DesktopHost.TaskbarCreatedMessage` (uint), `DesktopHost.FindProgman()`, `DesktopHost.AttachToDesktop(nint) -> bool` (sets the owner, then verifies it), `DesktopHost.IsAttached(nint) -> bool`.
  - `FenceWindowChrome.ApplyToolWindowStyles(nint)`, `ApplyAccentBlur(nint, uint tintAbgr = DefaultTint) -> bool`, `ApplyRoundedCorners(nint, int widthPx, int heightPx, int radiusPx)`, `GetPixelRect(nint) -> PixelRect`, `SetPixelRect(nint, PixelRect)`, `const uint DefaultTint = 0x40201A16`.
  - `DesktopIcons.TrySetHidden(bool) -> bool` and `DesktopIcons.TryIsHidden() -> bool?`.
  - `Watchdog(string dataDirectory, Action<string> log)` with `LaunchDetached(int mainProcessId)`, static `RunLauncher(int)`, `SetTakeoverActive(bool)`, `MarkCleanShutdown(int)`, `Run(int mainProcessId)`, and consts `LaunchArgument` = "--watchdog-launch" and `RunArgument` = "--watchdog".

- [ ] **Step 1: Create the project**

```powershell
dotnet new classlib -n NeoFences.Shell -o src/NeoFences.Shell
Remove-Item src/NeoFences.Shell/Class1.cs
dotnet sln NeoFences.slnx add src/NeoFences.Shell/NeoFences.Shell.csproj
```
Replace `src/NeoFences.Shell/NeoFences.Shell.csproj` with:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <!-- Windows 10 1809+ only (spec). A versioned TFM would pull in the WinRT projections, so silence the 8.1-API check instead. -->
    <NoWarn>$(NoWarn);CA1416</NoWarn>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Windows.CsWin32" Version="0.3.335">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\NeoFences.Core\NeoFences.Core.csproj" />
  </ItemGroup>

</Project>
```
Create `src/NeoFences.Shell/NativeMethods.txt`:
```
// Win32 APIs used by NeoFences.Shell. CsWin32 generates bindings for each name.
CreateRoundRectRgn
DISPLAY_DEVICEW
EnumDisplayDevices
EnumDisplayMonitors
FindWindow
FOLDERFLAGS
GET_WINDOW_CMD
GetDpiForMonitor
GetMonitorInfo
GetWindow
GetWindowLongPtr
GetWindowRect
IFolderView2
IServiceProvider
IShellBrowser
IShellView
IShellWindows
MONITOR_DPI_TYPE
MONITORINFOEXW
MONITORINFOF_PRIMARY
RegisterWindowMessage
SET_WINDOW_POS_FLAGS
SetWindowLongPtr
SetWindowPos
SetWindowRgn
ShellWindows
SID_STopLevelBrowser
WINDOW_EX_STYLE
WINDOW_LONG_PTR_INDEX
```

- [ ] **Step 2: Create the source files**

`src/NeoFences.Shell/Monitors.cs`:
```csharp
using NeoFences.Core.Layouts;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace NeoFences.Shell;

/// <summary>Enumerates monitors in physical pixels (requires per-monitor DPI awareness, see app.manifest).</summary>
public static class Monitors
{
    private const uint GetDeviceInterfaceName = 0x1; // EDD_GET_DEVICE_INTERFACE_NAME

    public static unsafe IReadOnlyList<MonitorPlacement> Enumerate()
    {
        var handles = new List<HMONITOR>();
        PInvoke.EnumDisplayMonitors(HDC.Null, (RECT?)null, (monitor, _, _, _) =>
        {
            handles.Add(monitor);
            return true;
        }, 0);

        var placements = new List<MonitorPlacement>();
        foreach (var handle in handles)
        {
            var info = new MONITORINFOEXW();
            info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
            if (!PInvoke.GetMonitorInfo(handle, (MONITORINFO*)&info)) continue;

            PInvoke.GetDpiForMonitor(handle, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _);
            var bounds = info.monitorInfo.rcMonitor;
            var work = info.monitorInfo.rcWork;
            var gdiDeviceName = info.szDevice.ToString();

            placements.Add(new MonitorPlacement(
                DeviceId: StableDeviceId(gdiDeviceName),
                PixelWidth: bounds.right - bounds.left,
                PixelHeight: bounds.bottom - bounds.top,
                WorkLeftPx: work.left,
                WorkTopPx: work.top,
                WorkWidthPx: work.right - work.left,
                WorkHeightPx: work.bottom - work.top,
                ScalePercent: dpiX == 0 ? 100 : (int)Math.Round(dpiX * 100.0 / 96),
                IsPrimary: (info.monitorInfo.dwFlags & PInvoke.MONITORINFOF_PRIMARY) != 0));
        }
        return placements;
    }

    /// <summary>
    /// The monitor's device interface path (unique per physical monitor and port, stable across reboots).
    /// Falls back to the GDI name ("\\.\DISPLAY1"), which is unique but can change when ports change.
    /// </summary>
    private static unsafe string StableDeviceId(string gdiDeviceName)
    {
        var device = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
        if (PInvoke.EnumDisplayDevices(gdiDeviceName, 0, ref device, GetDeviceInterfaceName))
        {
            var interfacePath = device.DeviceID.ToString();
            if (!string.IsNullOrWhiteSpace(interfacePath)) return interfacePath;
        }
        return gdiDeviceName;
    }
}
```

`src/NeoFences.Shell/DesktopHost.cs`:
```csharp
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>
/// Keeps fence windows at desktop level by making Progman their owner (ADR-011): they stay just above the
/// desktop through Win+D and Win+M while apps still cover them. Ownership must be re-applied after Explorer
/// restarts (a new Progman window); <see cref="IsAttached"/> verifies it.
/// </summary>
public static class DesktopHost
{
    /// <summary>Registered message Explorer broadcasts when the taskbar (and desktop) is recreated.</summary>
    public static uint TaskbarCreatedMessage { get; } = PInvoke.RegisterWindowMessage("TaskbarCreated");

    public static unsafe nint FindProgman() => (nint)PInvoke.FindWindow("Progman", null).Value;

    /// <returns>False when Progman does not exist yet (Explorer still starting); retry later.</returns>
    public static unsafe bool AttachToDesktop(nint fenceHandle)
    {
        var progman = FindProgman();
        if (progman == 0) return false;
        // ponytail: GWLP_HWNDPARENT changes the owner after creation, which is undocumented (ADR-011); checked via IsAttached.
        PInvoke.SetWindowLongPtr((HWND)fenceHandle, WINDOW_LONG_PTR_INDEX.GWLP_HWNDPARENT, progman);
        return IsAttached(fenceHandle);
    }

    public static unsafe bool IsAttached(nint fenceHandle)
    {
        var progman = FindProgman();
        return progman != 0 && (nint)PInvoke.GetWindow((HWND)fenceHandle, GET_WINDOW_CMD.GW_OWNER).Value == progman;
    }
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
    /// <summary>~25% dark tint (AABBGGRR) used with accent blur (ADR-011; level to be confirmed in M2a).</summary>
    public const uint DefaultTint = 0x40201A16;

    /// <summary>Not in the taskbar or Alt+Tab, and not activated just by being shown.</summary>
    public static void ApplyToolWindowStyles(nint handle)
    {
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

    public static PixelRect GetPixelRect(nint handle)
    {
        PInvoke.GetWindowRect((HWND)handle, out var rect);
        return new PixelRect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
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

`src/NeoFences.Shell/DesktopIcons.cs`:
```csharp
using Windows.Win32;
using Windows.Win32.UI.Shell;
using ComServiceProvider = Windows.Win32.System.Com.IServiceProvider;

namespace NeoFences.Shell;

/// <summary>Hides/shows the native desktop icons via the documented folder-view flag (ADR-002, no injection).</summary>
public static class DesktopIcons
{
    /// <returns>False when the desktop folder view is unavailable (e.g. Explorer restarting); retry later.</returns>
    public static bool TrySetHidden(bool hidden)
    {
        try
        {
            GetDesktopFolderView().SetCurrentFolderFlags((uint)FOLDERFLAGS.FWF_NOICONS, hidden ? (uint)FOLDERFLAGS.FWF_NOICONS : 0u);
            return TryIsHidden() == hidden;
        }
        catch (Exception failure) when (failure is System.Runtime.InteropServices.COMException or InvalidCastException or NullReferenceException)
        {
            return false;
        }
    }

    /// <returns>Null when the desktop folder view is unavailable.</returns>
    public static bool? TryIsHidden()
    {
        try
        {
            GetDesktopFolderView().GetCurrentFolderFlags(out uint flags);
            return (flags & (uint)FOLDERFLAGS.FWF_NOICONS) != 0;
        }
        catch (Exception failure) when (failure is System.Runtime.InteropServices.COMException or InvalidCastException or NullReferenceException)
        {
            return null;
        }
    }

    // Raymond Chen, "Manipulating the positions of desktop icons": ShellWindows -> desktop -> top-level browser -> active view.
    private static IFolderView2 GetDesktopFolderView()
    {
        var shellWindows = (IShellWindows)new ShellWindows();
        object location = 0;   // CSIDL_DESKTOP as VT_I4
        object root = null!;   // VT_EMPTY
        var dispatch = shellWindows.FindWindowSW(location, root, ShellWindowTypeConstants.SWC_DESKTOP, out _,
            ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH);
        var serviceProvider = (ComServiceProvider)dispatch;
        serviceProvider.QueryService(PInvoke.SID_STopLevelBrowser, out IShellBrowser browser);
        browser.QueryActiveShellView(out IShellView view);
        return (IFolderView2)view;
    }
}
```

`src/NeoFences.Shell/Watchdog.cs`:
```csharp
using System.Diagnostics;
using System.Globalization;
using NeoFences.Core.Lifecycle;

namespace NeoFences.Shell;

/// <summary>
/// Restores desktop icons if NeoFences dies without a clean shutdown (ADR-005, ADR-011). The watchdog is the
/// same exe in <c>--watchdog</c> mode, started through a short-lived <c>--watchdog-launch</c> process so it is
/// not a child of the main process ("End process tree" in Task Manager cannot take both down).
/// </summary>
public sealed class Watchdog(string dataDirectory, Action<string> log)
{
    public const string LaunchArgument = "--watchdog-launch";
    public const string RunArgument = "--watchdog";

    private const int RestoreAttempts = 10;
    private static readonly TimeSpan RestoreRetryDelay = TimeSpan.FromMilliseconds(500);

    private string TakeoverActivePath => Path.Combine(dataDirectory, "takeover-active");
    private string RestartLogPath => Path.Combine(dataDirectory, "watchdog-restarts.txt");
    private string MarkerPath(int processId) => Path.Combine(dataDirectory, $"clean-shutdown-{processId}");

    /// <summary>Main process, at startup: drops a stale marker for this PID, then launches the watchdog detached.</summary>
    public void LaunchDetached(int mainProcessId)
    {
        Directory.CreateDirectory(dataDirectory);
        File.Delete(MarkerPath(mainProcessId));
        StartSelf($"{LaunchArgument} {mainProcessId}");
    }

    /// <summary><c>--watchdog-launch</c> mode: start the real watchdog and exit, orphaning it from the main process tree.</summary>
    public static void RunLauncher(int mainProcessId) => StartSelf($"{RunArgument} {mainProcessId}");

    /// <summary>Main process: the icons are hidden by NeoFences right now (the watchdog only restores in that case).</summary>
    public void SetTakeoverActive(bool active)
    {
        Directory.CreateDirectory(dataDirectory);
        if (active) File.WriteAllText(TakeoverActivePath, DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
        else File.Delete(TakeoverActivePath);
    }

    /// <summary>Main process, on orderly exit (or WM_ENDSESSION TRUE).</summary>
    public void MarkCleanShutdown(int mainProcessId)
    {
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(MarkerPath(mainProcessId), DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
    }

    /// <summary><c>--watchdog</c> mode. Returns when the main process has exited and any recovery is done.</summary>
    public void Run(int mainProcessId)
    {
        log($"watching {mainProcessId}");
        try
        {
            using var mainProcess = Process.GetProcessById(mainProcessId);
            mainProcess.WaitForExit();
        }
        catch (ArgumentException)
        {
            log("main process already gone");
        }

        var marker = MarkerPath(mainProcessId);
        if (File.Exists(marker))
        {
            File.Delete(marker);
            log("clean shutdown");
            return;
        }

        log("UNCLEAN exit");
        if (File.Exists(TakeoverActivePath)) RestoreIconsWithRetry();

        var now = DateTimeOffset.Now;
        if (!RestartThrottle.ShouldRestart(recentRestarts: ReadRestarts(), now: now))
        {
            log("restart limit reached (3 per 10 min), staying down");
            return;
        }
        File.AppendAllText(RestartLogPath, now.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine);
        StartSelf(arguments: "");
        log("main restarted");
    }

    private void RestoreIconsWithRetry()
    {
        // Explorer may be down at the same moment (e.g. it crashed together with us): keep trying for ~5 s.
        for (var attempt = 1; attempt <= RestoreAttempts; attempt++)
        {
            if (DesktopIcons.TrySetHidden(false))
            {
                File.Delete(TakeoverActivePath);
                log($"desktop icons restored (attempt {attempt})");
                return;
            }
            Thread.Sleep(RestoreRetryDelay);
        }
        log("FAILED to restore desktop icons");
    }

    private List<DateTimeOffset> ReadRestarts() =>
        File.Exists(RestartLogPath)
            ? File.ReadAllLines(RestartLogPath)
                .Select(line => DateTimeOffset.TryParse(line, CultureInfo.InvariantCulture, DateTimeStyles.None, out var restartTime) ? restartTime : (DateTimeOffset?)null)
                .OfType<DateTimeOffset>()
                .ToList()
            : [];

    private static void StartSelf(string arguments) =>
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, arguments) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
}
```

- [ ] **Step 3: Build**

Run: `dotnet build NeoFences.slnx`
Expected: `0 Warning(s)`, `0 Error(s)`. Then run `dotnet test NeoFences.slnx`; expected `Passed: 87`, unchanged.

- [ ] **Step 4: Commit**

```powershell
git add NeoFences.slnx src/NeoFences.Shell
git commit -m "feat: added shell layer for monitors, desktop ownership, blur, icons and watchdog"
```

---

### Task 4: NeoFences.App

**Files:**
- Create: `src/NeoFences.App/NeoFences.App.csproj`, `app.manifest`, `App.xaml`, `App.xaml.cs`, `AppPaths.cs`, `SystemMessageWindow.cs`, `FenceWindow.xaml`, `FenceWindow.xaml.cs`, `FenceHost.cs` (and the template's `AssemblyInfo.cs`)

**Interfaces:**
- Consumes:
  - Core: `ConfigStore`, `LayoutEngine` (`Resolve`, `WithFenceRect`), `FencePlacement`, `FenceMembership.CreateFence`, `SessionEndHandler`.
  - Everything from Task 3.
- Produces: `NeoFences.exe` (assembly name), with these modes:
  - no arguments: the app;
  - `--exit`: signal the running instance to quit cleanly;
  - `--watchdog-launch <pid>`;
  - `--watchdog <pid>`.

  Single instance via the mutex `Local\NeoFences.Main`; the exit signal is the event `Local\NeoFences.Exit`. Logs: `logs\neofences-<date>.log`, `logs\watchdog-<date>.log` (7 days). The fence body's context menu has New fence, Hide desktop icons (preview), and Exit NeoFences, plus `Debug: freeze this UI thread for 10 s (B11)` in DEBUG builds.

- [ ] **Step 1: Create the project**

```powershell
dotnet new wpf -n NeoFences.App -o src/NeoFences.App
Remove-Item src/NeoFences.App/MainWindow.xaml, src/NeoFences.App/MainWindow.xaml.cs
dotnet sln NeoFences.slnx add src/NeoFences.App/NeoFences.App.csproj
```
Replace `src/NeoFences.App/NeoFences.App.csproj` with:
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
Create `src/NeoFences.App/app.manifest`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <!-- Fence positions are stored in DIPs per monitor and placed in physical pixels: needs per-monitor DPI awareness. -->
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```
Replace `src/NeoFences.App/App.xaml` with:
```xml
<Application x:Class="NeoFences.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
</Application>
```

- [ ] **Step 2: Create the source files**

`src/NeoFences.App/AppPaths.cs`:
```csharp
using System.IO;

namespace NeoFences.App;

/// <summary>Runtime data lives in %LOCALAPPDATA%\NeoFences (config.json, backups\, logs\, watchdog state).</summary>
public static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoFences");

    public static string LogsDirectory { get; } = Path.Combine(DataDirectory, "logs");
}
```

`src/NeoFences.App/SystemMessageWindow.cs`:
```csharp
using System.Windows.Interop;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// Hidden top-level window that receives system broadcasts: Explorer restarts (TaskbarCreated), sign-out /
/// shutdown (WM_QUERYENDSESSION, WM_ENDSESSION) and display or work-area changes. Message-only windows do not
/// get broadcasts, so this is an invisible normal window.
/// </summary>
public sealed class SystemMessageWindow : IDisposable
{
    private const int WmQueryEndSession = 0x0011;
    private const int WmEndSession = 0x0016;
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmDpiChanged = 0x02E0;
    private const int SpiSetWorkArea = 0x002F;

    private readonly HwndSource _source;

    public event Action? ExplorerRestarted;
    public event Action? QueryEndSession;
    public event Action<bool>? EndSession;
    public event Action? DisplayChanged;

    public SystemMessageWindow()
    {
        _source = new HwndSource(new HwndSourceParameters("NeoFences.SystemMessages") { WindowStyle = 0, Width = 0, Height = 0 });
        _source.AddHook(OnMessage);
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case WmQueryEndSession:
                QueryEndSession?.Invoke();
                handled = true;
                return 1; // never block sign-out or shutdown
            case WmEndSession:
                EndSession?.Invoke(wParam != 0);
                handled = true;
                return 0;
            case WmDisplayChange:
            case WmDpiChanged:
            case WmSettingChange when wParam == SpiSetWorkArea:
                DisplayChanged?.Invoke();
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
         The 1/255-alpha background keeps the empty area hit-testable (fully transparent pixels click through). -->
    <WindowChrome.WindowChrome>
        <WindowChrome GlassFrameThickness="0" CaptionHeight="30" ResizeBorderThickness="6"
                      CornerRadius="0" UseAeroCaptionButtons="False" />
    </WindowChrome.WindowChrome>
    <Border CornerRadius="8" BorderBrush="#40FFFFFF" BorderThickness="1">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="30" />
                <RowDefinition />
            </Grid.RowDefinitions>
            <TextBlock x:Name="TitleText" Foreground="White" FontWeight="SemiBold" Margin="12,0"
                       VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
            <Border Grid.Row="1" BorderBrush="#26FFFFFF" BorderThickness="0,1,0,0" Background="#01000000">
                <Border.ContextMenu>
                    <ContextMenu x:Name="BodyContextMenu">
                        <MenuItem x:Name="NewFenceItem" Header="New fence" />
                        <MenuItem x:Name="TakeoverItem" Header="Hide desktop icons (preview)" IsCheckable="True" />
                        <Separator />
                        <MenuItem x:Name="ExitItem" Header="Exit NeoFences" />
                    </ContextMenu>
                </Border.ContextMenu>
                <TextBlock Text="Icons arrive in M2b. Right-click for options." Foreground="#99FFFFFF"
                           Margin="12" TextWrapping="Wrap" />
            </Border>
        </Grid>
    </Border>
</Window>
```

`src/NeoFences.App/FenceWindow.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Interop;
using NeoFences.Core.Layouts;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>One fence on the desktop. Placement and persistence are the host's job; this window only reports moves.</summary>
public partial class FenceWindow : Window
{
    private const int WmExitSizeMove = 0x0232;
    private const double CornerRadiusDips = 8;

    public string FenceId { get; }

    public nint Handle { get; private set; }

    /// <summary>Raised after the user finishes moving or resizing, with the new physical-pixel rect.</summary>
    public event Action<FenceWindow, PixelRect>? MovedByUser;

    public event Action? NewFenceRequested;
    public event Action<bool>? TakeoverToggled;
    public event Action? ExitRequested;

    public FenceWindow(string fenceId, string title, bool takeoverActive)
    {
        FenceId = fenceId;
        InitializeComponent();
        TitleText.Text = title;
        TakeoverItem.IsChecked = takeoverActive;
        NewFenceItem.Click += (_, _) => NewFenceRequested?.Invoke();
        TakeoverItem.Click += (_, _) => TakeoverToggled?.Invoke(TakeoverItem.IsChecked);
        ExitItem.Click += (_, _) => ExitRequested?.Invoke();
#if DEBUG
        // Checklist B11: a hung fence UI thread must not freeze the desktop or taskbar (owner input-queue attachment, ADR-011).
        var freezeItem = new System.Windows.Controls.MenuItem { Header = "Debug: freeze this UI thread for 10 s (B11)" };
        freezeItem.Click += (_, _) => Thread.Sleep(TimeSpan.FromSeconds(10));
        BodyContextMenu.Items.Add(freezeItem);
#endif
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => ApplyRoundedCorners();
    }

    public void SetTakeoverChecked(bool active) => TakeoverItem.IsChecked = active;

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
        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        FenceWindowChrome.ApplyRoundedCorners(Handle, pixels.Width, pixels.Height, (int)Math.Round(CornerRadiusDips * scale));
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmExitSizeMove) MovedByUser?.Invoke(this, FenceWindowChrome.GetPixelRect(Handle));
        return 0;
    }
}
```

`src/NeoFences.App/FenceHost.cs`:
```csharp
using System.IO;
using System.Windows.Interop;
using System.Windows.Threading;
using NeoFences.Core.Config;
using NeoFences.Core.Layouts;
using NeoFences.Core.Lifecycle;
using NeoFences.Core.Membership;
using NeoFences.Core.Model;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Owns the fences on the desktop: loads the config, places one <see cref="FenceWindow"/> per fence on the current
/// monitors, saves moves (debounced 500 ms, ADR-006), and keeps everything attached through display changes,
/// Explorer restarts and sign-out (ADR-011). Every Win32/COM call goes through NeoFences.Shell.
/// </summary>
public sealed class FenceHost
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReattachInterval = TimeSpan.FromMilliseconds(500);
    private const int ReattachAttempts = 10;

    private readonly ConfigStore _store = new(AppPaths.DataDirectory);
    private readonly Watchdog _watchdog = new(AppPaths.DataDirectory, message => Log.Information("watchdog: {Message}", message));
    private readonly SystemMessageWindow _messages = new();
    private readonly Dictionary<string, FenceWindow> _windows = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _saveTimer;
    private readonly SessionEndHandler _sessionEnd;
    private NeoFencesConfig _config = NeoFencesConfig.CreateDefault();
    private IReadOnlyList<MonitorPlacement> _monitors = [];
    private bool _takeoverActive;

    public event Action? ExitRequested;

    public FenceHost()
    {
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();
        _sessionEnd = new SessionEndHandler(
            restoreIcons: () => SetIconsHidden(false),
            reapplyTakeover: () => SetIconsHidden(true),
            markCleanShutdown: () =>
            {
                SaveNow();
                _watchdog.MarkCleanShutdown(Environment.ProcessId);
            },
            isTakeoverActive: () => _takeoverActive);
        _messages.ExplorerRestarted += OnExplorerRestarted;
        _messages.DisplayChanged += OnDisplayChanged;
        _messages.QueryEndSession += () =>
        {
            Log.Information("session ending (query)");
            _sessionEnd.OnQueryEndSession();
        };
        _messages.EndSession += sessionIsEnding =>
        {
            Log.Information("session end, ending: {SessionIsEnding}", sessionIsEnding);
            _sessionEnd.OnEndSession(sessionIsEnding);
        };
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
        ApplyLayout();
        if (_config.Settings.Takeover) SetTakeover(true);
        ScheduleSave();
    }

    /// <summary>Orderly exit: save, bring icons back, tell the watchdog all is well.</summary>
    public void Shutdown()
    {
        SaveNow();
        if (_takeoverActive) SetIconsHidden(false);
        _watchdog.MarkCleanShutdown(Environment.ProcessId);
        foreach (var window in _windows.Values) window.Close();
        _messages.Dispose();
    }

    /// <summary>Best effort from the crash handler; the watchdog restores too.</summary>
    public void EmergencyRestoreIcons()
    {
        if (_takeoverActive) DesktopIcons.TrySetHidden(false);
    }

    private void OpenWindow(Fence fence)
    {
        var window = new FenceWindow(fence.Id, fence.Title, _takeoverActive);
        window.MovedByUser += OnFenceMoved;
        window.NewFenceRequested += CreateFence;
        window.TakeoverToggled += SetTakeover;
        window.ExitRequested += () => ExitRequested?.Invoke();
        new WindowInteropHelper(window).EnsureHandle(); // HWND exists (styles, blur) before the first Show
        if (!DesktopHost.AttachToDesktop(window.Handle)) Log.Warning("fence {FenceId}: not attached to the desktop yet (no Progman)", fence.Id);
        _windows[fence.Id] = window;
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
                if (!window.IsVisible) window.Show();
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
        ApplyLayout();
        ScheduleSave();
    }

    private void SetTakeover(bool active)
    {
        _takeoverActive = active;
        _config = _config with { Settings = _config.Settings with { Takeover = active } };
        SetIconsHidden(active);
        foreach (var window in _windows.Values) window.SetTakeoverChecked(active);
        ScheduleSave();
    }

    private void SetIconsHidden(bool hidden)
    {
        // The watchdog must know whenever icons may be hidden: mark before hiding, unmark only after a confirmed show.
        if (hidden) _watchdog.SetTakeoverActive(true);
        var applied = DesktopIcons.TrySetHidden(hidden);
        if (applied && !hidden) _watchdog.SetTakeoverActive(false);
        if (applied) Log.Information("desktop icons hidden: {Hidden}", hidden);
        else Log.Warning("could not set desktop icons hidden: {Hidden}", hidden);
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
            var iconsOk = !_takeoverActive || DesktopIcons.TrySetHidden(true);
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

Replace `src/NeoFences.App/App.xaml.cs` with:
```csharp
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

public partial class App : Application
{
    /// <summary>`NeoFences.exe --exit` asks the running instance to shut down cleanly (scripts, installer).</summary>
    public const string ExitArgument = "--exit";
    private const string ExitSignalName = @"Local\NeoFences.Exit";

    private Mutex? _singleInstance;
    private EventWaitHandle? _exitSignal;
    private RegisteredWaitHandle? _exitWait;
    private bool _ownsSingleInstance;
    private FenceHost? _host;

    protected override void OnStartup(StartupEventArgs startupArgs)
    {
        base.OnStartup(startupArgs);
        Directory.CreateDirectory(AppPaths.LogsDirectory);

        switch (startupArgs.Args)
        {
            case [ExitArgument]:
                if (EventWaitHandle.TryOpenExisting(ExitSignalName, out var runningInstanceExit)) runningInstanceExit.Set();
                Shutdown();
                return;
            case [Watchdog.LaunchArgument, var launchProcessIdText]:
                Watchdog.RunLauncher(int.Parse(launchProcessIdText, CultureInfo.InvariantCulture));
                Shutdown();
                return;
            case [Watchdog.RunArgument, var mainProcessIdText]:
                ConfigureLogging(fileName: "watchdog-.log");
                new Watchdog(AppPaths.DataDirectory, message => Log.Information("{Message}", message))
                    .Run(int.Parse(mainProcessIdText, CultureInfo.InvariantCulture));
                Shutdown();
                return;
        }

        _singleInstance = new Mutex(initiallyOwned: true, name: @"Local\NeoFences.Main", out _ownsSingleInstance);
        if (!_ownsSingleInstance)
        {
            Shutdown();
            return;
        }

        ConfigureLogging(fileName: "neofences-.log");
        Log.Information("NeoFences starting, pid {ProcessId}, OS {OsVersion}", Environment.ProcessId, Environment.OSVersion.Version);
        DispatcherUnhandledException += OnUnhandledException;

        _exitSignal = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ExitSignalName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitSignal, (_, _) => Dispatcher.BeginInvoke(Shutdown), null, Timeout.Infinite, executeOnlyOnce: true);

        _host = new FenceHost();
        _host.ExitRequested += Shutdown;
        _host.Start();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs unhandled)
    {
        // Not handled on purpose: the process dies, the watchdog restores icons and restarts us (ADR-005).
        Log.Fatal(unhandled.Exception, "unhandled exception");
        _host?.EmergencyRestoreIcons();
        Log.CloseAndFlush();
    }

    protected override void OnExit(ExitEventArgs exitArgs)
    {
        if (_host is not null)
        {
            _host.Shutdown();
            Log.Information("NeoFences exited");
        }
        Log.CloseAndFlush();
        _exitWait?.Unregister(null);
        _exitSignal?.Dispose();
        if (_ownsSingleInstance) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(exitArgs);
    }

    private static void ConfigureLogging(string fileName) =>
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(AppPaths.LogsDirectory, fileName), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();
}
```

- [ ] **Step 3: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 87`.

- [ ] **Step 4: Smoke run (agent, non-disruptive)**

Save as `<scratchpad>\smoke.ps1` and run it with `& "<scratchpad>\smoke.ps1"`:
```powershell
param([string]$Exe = "F:\projects\neo_fences\src\NeoFences.App\bin\Debug\net10.0-windows\NeoFences.exe")
$data = Join-Path $env:LOCALAPPDATA 'NeoFences'
$logs = Join-Path $data 'logs'
function LogTail($pattern) { Get-ChildItem $logs -Filter $pattern -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1 | ForEach-Object { Get-Content $_.FullName -Tail 20 } }
function Wait-Until([scriptblock]$condition, $seconds = 10) { $deadline = (Get-Date).AddSeconds($seconds); while ((Get-Date) -lt $deadline) { if (& $condition) { return $true }; [System.Threading.Thread]::Sleep(200) }; return $false }

$main = Start-Process -FilePath $Exe -PassThru
Write-Output ("started main {0}" -f $main.Id)
Write-Output ("watchdog appeared: {0}" -f (Wait-Until { @(Get-CimInstance Win32_Process -Filter "Name='NeoFences.exe'" | Where-Object { $_.CommandLine -match '--watchdog \d' }).Count -ge 1 }))
[System.Threading.Thread]::Sleep(1500)
Get-CimInstance Win32_Process -Filter "Name='NeoFences.exe'" | ForEach-Object {
  $parentAlive = [bool](Get-Process -Id $_.ParentProcessId -ErrorAction SilentlyContinue)
  "  pid {0} parent {1} (alive: {2}) args: {3}" -f $_.ProcessId, $_.ParentProcessId, $parentAlive, ($_.CommandLine -replace '^.*NeoFences.exe"?\s*', '')
}
Write-Output "--- main log:"; LogTail 'neofences-*.log'
Write-Output ("config exists: {0}" -f (Test-Path (Join-Path $data 'config.json')))

$exitProcess = Start-Process -FilePath $Exe -ArgumentList '--exit' -PassThru; $exitProcess.WaitForExit(10000) | Out-Null
Write-Output ("main exited after --exit: {0}" -f (Wait-Until { $main.HasExited } 10))
[System.Threading.Thread]::Sleep(1000)
Write-Output "--- main log tail:"; LogTail 'neofences-*.log' | Select-Object -Last 3
Write-Output "--- watchdog log:"; LogTail 'watchdog-*.log'
Write-Output ("NeoFences processes left: {0}" -f @(Get-Process NeoFences -ErrorAction SilentlyContinue).Count)
Write-Output ("data dir: {0}" -f ((Get-ChildItem $data).Name -join ', '))
```
Expected:
- `watchdog appeared: True`;
- the watchdog's parent is **not alive** (detached);
- the main log shows `config loaded from …` and a `monitors: \\?\DISPLAY#…` line;
- `main exited after --exit: True`;
- the watchdog log ends with `clean shutdown`;
- `NeoFences processes left: 0`.

- [ ] **Step 5: Commit**

```powershell
git add NeoFences.slnx src/NeoFences.App
git commit -m "feat: added fence host app with layered desktop fences, saves and session handling"
```

---

### Task 5: Verification on the real desktop (with the user)

**Files:**
- Create: `docs/research/m2a-fence-host.md`
- Modify: `docs/TEST-CHECKLIST.md` (add section G)

**Interfaces:**
- Consumes: the built app (`src/NeoFences.App/bin/Debug/net10.0-windows/NeoFences.exe`).
- Produces: results that decide whether M2b can build on this host, plus any fixes (each as its own `fix:` commit with a test where the logic is in Core).

- [ ] **Step 1: Add section G to `docs/TEST-CHECKLIST.md`** (append at the end):

```markdown
## G — Fence host (M2a+)
| ID | Steps | Expected |
|---|---|---|
| G1 | First start with no config | one "Inbox" fence at (24,24) DIPs on the primary monitor; blurred wallpaper behind; rounded corners; not in taskbar/Alt+Tab |
| G2 | Drag and resize the fence, exit (`NeoFences.exe --exit`), start again | same position and size |
| G3 | Right-click body → New fence | second fence cascaded at (56,56); persists after restart |
| G4 | Change taskbar size/position or display scaling while running | fences stay on screen; returning to the old setting restores exact positions |
| G5 | `NeoFences.exe --exit` | clean exit; watchdog log "clean shutdown"; no processes left |
| G6 | Preview "Hide desktop icons" on, then kill NeoFences (`Stop-Process -Force`) | watchdog restores icons and restarts NeoFences, which hides them again |
| G7 | Preview off, kill NeoFences | watchdog restarts it but does NOT touch desktop icons |
| G8 | Start a second NeoFences.exe while one runs | second exits immediately; still one set of fences |
```

- [ ] **Step 2: Run the app** — `Start-Process src/NeoFences.App/bin/Debug/net10.0-windows/NeoFences.exe`. Screenshot the fence region (use the M0 plan's helper commands) and check **G1** together with the user. Look closely at the corners. If the rounded region does not clip the blur on this layered window, record it and apply the fallback ruling: square window, rounded WPF border, and a note in the research file.

- [ ] **Step 3: Re-verification block (ADR-011, research note "M2 re-verification")** — run each of these on the layered + owned fence and record the results:
  - **A2 [USER]:** Wallpaper Engine animation visibly moving and blurred through the fence; no stutter.
  - **A4 [USER]:** drag and resize are smooth.
  - **Tint [USER]:** tell the user the tint can change (e.g. `0x20…` lighter, `0x66…` darker) and ask whether the default looks right. If not, change `FenceWindowChrome.DefaultTint` and record the chosen value.
  - **B2:** clicking the fence never raises it above an app window.
  - **B3/B4/B6 [USER]:** Win+D, the Show-desktop corner, and Win+M keep the fence visible.
  - **B7 (forced):** `Stop-Process -Name explorer -Force`. The log shows `re-attach … unattached 0`; then Win+D keeps the fence.
  - **B10 (graceful) [USER]:** Task Manager → Windows Explorer → Restart. Same expectations as B7.
  - **B11 [USER]:** right-click the body → Debug: freeze 10 s. During the freeze, right-click the desktop and click the taskbar; both must respond.
  - **B8 [USER]:** with an elevated Task Manager focused, Win+D keeps the fence.
  - **G2–G8.**

- [ ] **Step 4: Sign-out and shutdown [USER]** — with the preview "Hide desktop icons" **on**:
  - **C8:** sign out and back in. The icons must be visible. `logs\neofences-*.log` must show `session ending (query)`, then `desktop icons hidden: False`, then `session end, ending: True`. Also note whether `FWF_NOICONS` would have persisted (were icons hidden right after sign-in before NeoFences started?).
  - **Cancelled shutdown:** open an unsaved Notepad document, choose Shut down, then click **Cancel** on the "apps are blocking" screen. Icons must be hidden again, and the log must show `session end, ending: False` with no `clean-shutdown-<pid>` file in the data directory.

  **Safety:** if icons are ever left hidden, use right-click desktop → View → Show desktop icons, and mark the row FAIL.

- [ ] **Step 5: Write `docs/research/m2a-fence-host.md`**

```markdown
# M2a fence host verification

Build: <commit>. Machine: see `desktop-layer.md`. Date: <today>.

| ID | Result | Notes |
|---|---|---|
| G1 | | corners: rounded/square |
| A2 | | |
| A4 | | |
| Tint | | chosen value |
| B2 | | |
| B3 | | |
| B4 | | |
| B6 | | |
| B7 | | log line |
| B8 | | |
| B10 | | log line |
| B11 | | |
| C8 | | log lines; FWF_NOICONS persisted? |
| Cancel | | |
| G2–G8 | | one row each |

## Conclusions
<GO for M2b, or which fixes were made>
```
Fill every cell from what was observed.

- [ ] **Step 6: Commit**

```powershell
git add docs/research/m2a-fence-host.md docs/TEST-CHECKLIST.md
git commit -m "docs: recorded M2a fence host verification results"
```

---

### Task 6: Docs sync

**Files:** `docs/DECISIONS.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/FEATURES.md`, `docs/SETUP.md`, `docs/SESSION-LOG.md`, `docs/hub/neofences-hq.html`

- [ ] **Step 1: Append ADR-012 to `docs/DECISIONS.md`**

```markdown
## ADR-012 — M2 split and fence-host mechanics
**Date:** 2026-10-02 · **Status:** Accepted · **Refines:** ADR-005, ADR-011

**Decision.**
- M2 is split into M2a (fence host), M2b (desktop items, Takeover + first run) and M2c (fence
  interactions); each ends runnable.
- The watchdog starts detached through a short-lived `--watchdog-launch` process (no parent-PID
  spoofing, no WMI). It restores icons only while `takeover-active` exists, retrying ~5 s if
  Explorer is down.
- `NeoFences.exe --exit` signals the running instance (event `Local\NeoFences.Exit`) to quit
  cleanly; single instance via mutex `Local\NeoFences.Main`.
- Monitors are identified by device interface path; fences are placed in physical pixels from DIP
  rects (per-monitor-v2 manifest).
- Shell/App target `net10.0-windows` with CA1416 suppressed (Windows 10 1809+ minimum); a
  versioned TFM would add the WinRT projections.
- Stored layouts keep unclamped rects; only displayed rects are clamped.

**Consequences.** If NeoFences runs inside a job object that kills children (some launchers,
debuggers), the watchdog dies with it; normal launches (Explorer, Run key) are unaffected.
```

- [ ] **Step 2: Update `docs/ARCHITECTURE.md`**
  - Replace `## Status` with `M2a complete: NeoFences.Shell + NeoFences.App host layered, Progman-owned fences (no icons yet). Next: M2b.`
  - Add to the `## Data` list:
    - `takeover-active` (watchdog restores icons only while it exists)
    - `clean-shutdown-<pid>`
    - `watchdog-restarts.txt`
    - `logs\watchdog-<date>.log`
  - Add one row to the components table: `| App/FenceHost | orchestrates config, monitors, windows, debounced saves, display changes, Explorer restarts, session end | — |`.

- [ ] **Step 3: Update `docs/ROADMAP.md`**
  - Mark M2a `— done <today>`.
  - Tick the M2 carry-overs that Task 5 covered (layered window + blur + rounded, re-run A2/A4/B2/B7/B10, hang test, detached watchdog, session end, C8, B8). Mark the rest `[!]` with a short note if they failed.
  - Under "Now", add `- [ ] Merge \`m2a-fence-host\` (after final review)` and `- [ ] M2b implementation plan`.

- [ ] **Step 4: Update `docs/FEATURES.md`**
  - Set `| Watchdog: icons always restored |` and `| Live acrylic blur over Wallpaper Engine |` to `wip`, with notes `M2a: host done`.
  - Set `| Per-monitor layout, survives resolution changes |` to `wip`, with note `M2a: placement + persistence done`.

- [ ] **Step 5: Update `docs/SETUP.md`** — replace the line `` `dotnet run --project src/NeoFences.App` arrives in M2. `` with:
````
```
dotnet run --project src/NeoFences.App      # starts NeoFences (fences, no icons yet)
src\NeoFences.App\bin\Debug\net10.0-windows\NeoFences.exe --exit   # quit it cleanly
```
````

- [ ] **Step 6: Append to `docs/SESSION-LOG.md`** — an entry for M2a: done, decisions (ADR-012), next (M2b plan), and open questions (whatever Task 5 left open).

- [ ] **Step 7: Refresh the hub** — `HUB`:
  - `updated`, `commit`, `phase` → `"M2a done"`, `current` → `"M2b plan"`;
  - in `milestones`, rename M2 to `"M2a–c"`, keep it `"next"`, and update its goal to mention the split;
  - `tasks`: an M2a group with each verification item and its result;
  - `adrs` += ADR-012; `research` += the M2a results summary; `sessions` and `commits` appended.

  Run `node --check` on the extracted script, then publish with `url` = `<private hub link: see CLAUDE.local.md>`.

- [ ] **Step 8: Commit (two commits)**

```powershell
git add docs/DECISIONS.md docs/ARCHITECTURE.md docs/FEATURES.md docs/SETUP.md
git commit -m "docs: added ADR-012 and documented the M2a fence host"
git add docs/ROADMAP.md docs/SESSION-LOG.md docs/hub/neofences-hq.html
git commit -m "docs: updated roadmap, session log and hub after M2a"
```

- [ ] **Step 9: Finish the branch** — run `dotnet test NeoFences.slnx` (87) and `dotnet test spikes/M0.slnx` (18), then use superpowers:finishing-a-development-branch.
