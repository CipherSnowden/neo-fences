# M38 — Keyboard way in and the Windows 11 VM test pass (0.25.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** From any app, the Peek hotkey puts the keyboard into a fence with a visible focus (Tab / Shift+Tab to the next / previous fence, Esc gives the keyboard back), and NeoFences is proven on a fresh Windows 11 by a scripted test pass in a Hyper-V VM that can run again for every release candidate.

**Architecture:** Core (test-first) gains `KeyboardOrder` (the fences' reading order, where Peek starts, Tab wrapping) and `CheckReport` (reads the VM's `results.json` into a report). Shell gains `KeyboardFocus` (foreground window, give, give back). The App's `FenceHost.Keyboard.cs` gives the keyboard to a fence when Peek starts and back to the remembered app on Esc / the hotkey; `FenceWindow` gets a keyboard mode (outline, item focus ring, a rolled-up fence opens, Tab raises `FenceCycleRequested`). `tools/vm/` builds the VM from the ISO with an answer file (`New-TestVm.ps1`, admin, once) and runs the checklist inside it (`Invoke-VmChecks.ps1`, no admin) through a guest script that waits at the tester's sign-in.

**Tech Stack:** .NET 10, C#, WPF, xUnit, CsWin32; Hyper-V with PowerShell Direct; Windows PowerShell 5.1 inside the guest, PowerShell 7 on the host (it loads `NeoFences.Core.dll`).

**Spec:** `docs/superpowers/specs/2026-10-06-keyboard-and-older-windows-design.md` (approved 2026-10-06), as amended by ADR-059 (Windows 11 only, 2026-10-07: one VM, no Windows 10). The spec names the keyboard decision ADR-059; ADR-059 went to "Windows 11 only", so the keyboard and VM decision is **ADR-060** — added by Task 4.

## How this plan is written

Built in a scratch worktree (`neo_fences-m38-proto`, from `5862c3d`). The keyboard was probed on a copy of the owner's
data (owner's OK): Peek from Firefox and from a full-screen app put the keyboard in the fence under the mouse, Esc gave it
back — `SetForegroundWindow` right after the hotkey works, so fences did not need to become focusable (the spec's fallback).
The VM `NF-Win11` (Windows 11 Pro 26300, built by `New-TestVm.ps1` from the owner's ISO) ran the checklist five times; run 5
passed **12 of 12** on 0.24.0 → 0.25.0-proto.1:

| check | result (run 5) |
|---|---|
| install, welcome | 0.24.0 silent install; one welcome fence |
| behind-windows, show-desktop | fence behind Notepad; still shown after Win+D |
| icons-hidden, -after-exit, -after-kill, explorer-restart | hidden; back after exit; back after a kill (watchdog restarts NeoFences); fences re-attach, icons stay hidden |
| game-mode | on behind full-screen Edge (Windows reports "busy") |
| update | 0.24.0 → 0.25.0-proto.1 from a local feed, icons still hidden |
| peek-keyboard | keyboard in a fence; Esc gives it back to Notepad |
| uninstall | app gone, icons shown |

Runs 1–4 failed only on the test tooling, fixed in the prototype: a restored checkpoint resumed to a lock screen (now a
fresh boot), two waiters (a mutex), PowerShell 5.1's `Start-Process -Wait` waits for the whole process tree (Setup starts
NeoFences), checks ran before the first fence and `config.json` existed, Windows 11's Notepad is a Store app (found by
its title), the keyboard check ran on the previous release (moved after the update), and Windows' "quiet time" — the first
hour after an account first signs in — hides full-screen apps (game mode cannot pass then; the README says to wait an
hour after building the VM). VMConnect's enhanced session shows a password box over the signed-in desktop (README: untick
it to watch).

Replay-verified on a fresh worktree of `main` at `f1a3c3f`: `dotnet test` → 824 passed; patch 1a alone fails to build
(`error CS0246: … 'FenceSpot' could not be found` — the RED); 1b → 834 passed; patch 2 builds with 0 warnings, 834 pass;
patch 3's four scripts parse in Windows PowerShell 5.1; `src/`, `tests/` and `tools/` match the prototype (the two
`.csproj` files differ only by main's ADR-059 comments). Tasks 1–3 are **patches to apply**
(`git apply --whitespace=nowarn <file>`; if one does not apply, stop).

**Calls made while prototyping (ledger them as rulings at the task named):**
- Task 1: rows are fences whose tops lie within half the shortest height of the row's first fence; the main screen first,
  the others left to right; a box (tabs) counts once; `Next` from a fence that is gone gives the first; a damaged or
  missing `results.json` is one failed row "no results (the test pass did not finish)".
- Task 2: Peek gives the keyboard with `SetForegroundWindow` (the probe proved it; fences stay `WS_EX_NOACTIVATE`); the
  app to give back is not remembered when NeoFences' own window (tray menu, Settings) had the keyboard; the keyboard goes
  back only on Esc or the hotkey (a click elsewhere or an open leaves it where Windows puts it); the item focus ring shows
  only in keyboard mode (a `ItemFocusVisibility` resource), so mouse use looks as in 0.24.0; the keyboard outline and
  ring use the selection accent (`ItemSelectedEdge`); a rolled-up fence counts the keyboard as "the mouse inside".
- Task 3: one VM (ADR-059); the VM is built with DISM and an answer file (no Setup clicks), auto-sign-in for "tester"; the
  guest checks run from a waiter started at sign-in (a Run key) — a desktop session is needed for keys and screenshots;
  each run boots fresh from the `clean` checkpoint; the spec's VM step 6 "Tab to another fence, Enter opens an item" is
  checked by hand in the live check (AX3) — the VM checks Peek → keyboard in a fence → Esc back; game mode is checked with
  Edge `--kiosk … --edge-kiosk-type=fullscreen`.

## Global Constraints

- Windows 11 x64 only (ADR-059); nothing checks the Windows version.
- Hard rules stand: desktop icons always come back; NeoFences never touches user files; no DLL injection or new global
  hooks (the keyboard uses `SetForegroundWindow` after a registered hotkey, nothing else); Win32 only in
  `NeoFences.Shell` via CsWin32; no new NuGet dependency; Core test-first; shell failures are logged, never a crash.
- The mouse is unchanged; Peek stays off in game mode and while paused; nothing visible changes for mouse users.
- `tools/vm/`: ASCII only; the guest scripts run in Windows PowerShell 5.1; no personal paths; the tester password lives
  only in the throwaway VM's answer file and the scripts.
- A change to a key or gesture updates `docs/GUIDE.md` (and the README quick start when it names it) in the same commit
  (ADR-050).
- Commits: single line, Conventional Commits, past tense, **no Co-Authored-By trailer**; secret scan
  (`gmail|claude\.ai/artifact`) before each commit.
- Version stays `0.24.0` until the release step; the release is 0.25.0.

## Review Focus

1. **The keyboard given back to a window that is gone or changed**: the app closed while peeking, a UAC prompt or an
   elevated window in front, Peek started from the tray menu or Settings, a full-screen game started while peeking —
   nothing crashes, no window is wrongly brought forward, Peek ends cleanly. (AX4.)
2. **Fences that change while peeking**: a fence deleted, rolled up, moved to another screen, a tab switched or detached,
   NeoFences paused, the screen layout changed — Tab still goes somewhere sensible, no stale outline stays drawn. (AX5.)
3. **Focus ring leaking into mouse use**: after Peek ends (any way) no item ring or fence outline remains; clicking items
   later looks exactly as in 0.24.0; a rename's text box and the item menus still take the keys. (AX2.)
4. **Keys inside a fence in keyboard mode**: Tab must not break Ctrl+Tab (tabs), F2 rename, Del, Ctrl+Z, the menu key,
   type-ahead, the size grid's arrows, a folder panel's Backspace / Alt+Up. (AX3.)
5. **The VM pass on a slow or busy host**: a VM that boots slower, a sign-in that takes minutes, a check that times out —
   the host reports the failed check or "no results", never hangs past its 25-minute limit, and leaves the VM off.
   (`CheckReport` covers the damaged file; the time limit is in `Invoke-VmChecks.ps1`.)

---

### Task 0: Worktree and baseline

- [ ] `git worktree add -b m38-keyboard-and-vm ..\neo_fences-m38 main` (main at `f1a3c3f` or later docs-only commits).
- [ ] `dotnet build` → 0 warnings; `dotnet test` → 824 passed.

### Task 1: Core — the keyboard's reading order and the VM report

**Files:**
- Create: `tests/NeoFences.Core.Tests/Layouts/KeyboardOrderTests.cs`, `tests/NeoFences.Core.Tests/Lifecycle/CheckReportTests.cs`,
  `src/NeoFences.Core/Layouts/KeyboardOrder.cs`, `src/NeoFences.Core/Lifecycle/CheckReport.cs`

**Interfaces:**
- Produces: `record FenceSpot(string Id, double X, double Y, double W, double H, bool OnPrimary, double MonitorLeft)`;
  `KeyboardOrder.ReadingOrder(IReadOnlyList<FenceSpot>) → IReadOnlyList<string>`, `.Start(order, underMouse, lastUsed) →
  string?`, `.Next(order, current, step) → string?`; `record CheckRow(string Id, bool Ok, string Note)`;
  `record CheckReport(Machine, Version, Checks, Failed, Summary)` with `static CheckReport Read(string json)`.

- [ ] **Step 1: Write the failing tests.** Write this patch to `m38-1a-tests.patch` and apply it:

```diff
diff --git a/tests/NeoFences.Core.Tests/Layouts/KeyboardOrderTests.cs b/tests/NeoFences.Core.Tests/Layouts/KeyboardOrderTests.cs
new file mode 100644
index 0000000..e4b821c
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Layouts/KeyboardOrderTests.cs
@@ -0,0 +1,61 @@
+using NeoFences.Core.Layouts;
+
+namespace NeoFences.Core.Tests.Layouts;
+
+/// <summary>M38 (spec 2026-10-06-keyboard-and-older-windows-design §1): which fence the keyboard goes to.</summary>
+public class KeyboardOrderTests
+{
+    private static FenceSpot Spot(string id, double x, double y, double w = 300, double h = 200, bool primary = true, double monitorLeft = 0) =>
+        new(id, x, y, w, h, primary, monitorLeft);
+
+    [Fact]
+    public void ReadingOrder_IsLeftToRightThenTopToBottom_FencesOfARowTogetherEvenIfNotLevel()
+    {
+        var order = KeyboardOrder.ReadingOrder(
+        [
+            Spot("bottom-left", 20, 600),
+            Spot("top-right", 700, 30),
+            Spot("top-left", 20, 24),
+            Spot("top-middle", 360, 60, h: 300), // a little lower, still the first row
+        ]);
+        Assert.Equal(["top-left", "top-middle", "top-right", "bottom-left"], order);
+    }
+
+    [Fact]
+    public void ReadingOrder_TakesTheMainScreenFirst_ThenTheOthersFromLeftToRight()
+    {
+        var order = KeyboardOrder.ReadingOrder(
+        [
+            Spot("right-screen", 1940, 20, primary: false, monitorLeft: 1920),
+            Spot("left-screen", -1900, 20, primary: false, monitorLeft: -1920),
+            Spot("main", 20, 20),
+        ]);
+        Assert.Equal(["main", "left-screen", "right-screen"], order);
+    }
+
+    [Fact]
+    public void ReadingOrder_OfNothing_IsEmpty() => Assert.Empty(KeyboardOrder.ReadingOrder([]));
+
+    [Fact]
+    public void Start_IsTheFenceUnderTheMouse_ElseTheLastUsed_ElseTheFirst()
+    {
+        string[] order = ["a", "b", "c"];
+        Assert.Equal("c", KeyboardOrder.Start(order, underMouse: "c", lastUsed: "b"));
+        Assert.Equal("b", KeyboardOrder.Start(order, underMouse: null, lastUsed: "b"));
+        Assert.Equal("a", KeyboardOrder.Start(order, underMouse: null, lastUsed: "gone")); // deleted since
+        Assert.Equal("a", KeyboardOrder.Start(order, underMouse: "not-a-fence", lastUsed: null));
+        Assert.Null(KeyboardOrder.Start([], underMouse: null, lastUsed: null));
+    }
+
+    [Fact]
+    public void Next_GoesForwardAndBack_AndWrapsAtTheEnds()
+    {
+        string[] order = ["a", "b", "c"];
+        Assert.Equal("b", KeyboardOrder.Next(order, current: "a", step: 1));
+        Assert.Equal("a", KeyboardOrder.Next(order, current: "c", step: 1));
+        Assert.Equal("c", KeyboardOrder.Next(order, current: "a", step: -1));
+        Assert.Equal("a", KeyboardOrder.Next(order, current: "gone", step: 1)); // a fence that went: the first
+        Assert.Equal("a", KeyboardOrder.Next(["a"], current: "a", step: 1));
+        Assert.Null(KeyboardOrder.Next([], current: "a", step: 1));
+    }
+}
diff --git a/tests/NeoFences.Core.Tests/Lifecycle/CheckReportTests.cs b/tests/NeoFences.Core.Tests/Lifecycle/CheckReportTests.cs
new file mode 100644
index 0000000..4ebfdbf
--- /dev/null
+++ b/tests/NeoFences.Core.Tests/Lifecycle/CheckReportTests.cs
@@ -0,0 +1,37 @@
+using NeoFences.Core.Lifecycle;
+
+namespace NeoFences.Core.Tests.Lifecycle;
+
+/// <summary>M38 (spec §2): the results file the test pass writes inside a VM, read into a report on the host.</summary>
+public class CheckReportTests
+{
+    [Fact]
+    public void Read_ListsEveryCheckAndCountsTheFailures()
+    {
+        var report = CheckReport.Read("""
+            { "machine": "Windows 10 22H2 (19045)", "version": "0.25.0",
+              "checks": [
+                { "id": "install", "ok": true, "note": "welcome shown" },
+                { "id": "icons-after-kill", "ok": false, "note": "icons stayed hidden" },
+                { "id": "update", "ok": true }
+              ] }
+            """);
+        Assert.Equal(("Windows 10 22H2 (19045)", "0.25.0", 3, 1), (report.Machine, report.Version, report.Checks.Count, report.Failed));
+        Assert.Equal(["install", "icons-after-kill", "update"], report.Checks.Select(check => check.Id));
+        Assert.Equal("icons stayed hidden", report.Checks[1].Note);
+        Assert.Equal("", report.Checks[2].Note);
+        Assert.Equal("Windows 10 22H2 (19045), 0.25.0: 2 of 3 passed; failed: icons-after-kill (icons stayed hidden)", report.Summary);
+    }
+
+    [Theory]
+    [InlineData("")]
+    [InlineData("{ not json")]
+    [InlineData("""{ "machine": "x" }""")]
+    [InlineData("""{ "checks": [ null, { "ok": true } ] }""")]
+    public void Read_ADamagedOrUnfinishedFile_IsAFailedReport_NeverAThrow(string text)
+    {
+        var report = CheckReport.Read(text);
+        Assert.True(report.Failed >= 1);
+        Assert.Contains("no results", report.Summary);
+    }
+}
```

- [ ] **Step 2: Run them.** `dotnet test tests/NeoFences.Core.Tests` → Expected: build fails with `error CS0246: The type or namespace name 'FenceSpot' could not be found`.
- [ ] **Step 3: Implement.** Write this patch to `m38-1b-core.patch` and apply it:

```diff
diff --git a/src/NeoFences.Core/Layouts/KeyboardOrder.cs b/src/NeoFences.Core/Layouts/KeyboardOrder.cs
new file mode 100644
index 0000000..20e14e9
--- /dev/null
+++ b/src/NeoFences.Core/Layouts/KeyboardOrder.cs
@@ -0,0 +1,53 @@
+namespace NeoFences.Core.Layouts;
+
+/// <summary>A fence (a box) on screen for the keyboard order (M38): its rect in screen pixels and its monitor.</summary>
+/// <param name="MonitorLeft">The left edge of its monitor: screens other than the main one are taken from left to right.</param>
+public sealed record FenceSpot(string Id, double X, double Y, double W, double H, bool OnPrimary, double MonitorLeft);
+
+/// <summary>
+/// Which fence the keyboard goes to (M38, spec §1, ADR-060): Peek puts it in the fence under the mouse, else the one used
+/// last, else the first in reading order; Tab / Shift+Tab walk the reading order and wrap. Pure.
+/// </summary>
+public static class KeyboardOrder
+{
+    /// <summary>
+    /// Left to right, top to bottom, the main screen first and the others from left to right. Fences whose tops are within
+    /// half the shortest height of a row's first fence count as that row (fences are rarely exactly level).
+    /// </summary>
+    public static IReadOnlyList<string> ReadingOrder(IReadOnlyList<FenceSpot> spots)
+    {
+        var ordered = new List<string>();
+        foreach (var screen in spots.GroupBy(spot => (spot.OnPrimary, spot.MonitorLeft))
+                     .OrderBy(group => group.Key.OnPrimary ? 0 : 1).ThenBy(group => group.Key.MonitorLeft))
+        {
+            var rows = new List<List<FenceSpot>>();
+            foreach (var spot in screen.OrderBy(spot => spot.Y).ThenBy(spot => spot.X))
+            {
+                var row = rows.Count > 0 ? rows[^1] : null;
+                if (row is not null && spot.Y < row[0].Y + row.Min(member => member.H) / 2) row.Add(spot);
+                else rows.Add([spot]);
+            }
+            foreach (var row in rows) ordered.AddRange(row.OrderBy(spot => spot.X).Select(spot => spot.Id));
+        }
+        return ordered;
+    }
+
+    /// <summary>The fence Peek gives the keyboard to: under the mouse, else the last used, else the first; null without fences.</summary>
+    public static string? Start(IReadOnlyList<string> order, string? underMouse, string? lastUsed) =>
+        underMouse is not null && order.Contains(underMouse) ? underMouse
+        : lastUsed is not null && order.Contains(lastUsed) ? lastUsed
+        : order.Count > 0 ? order[0] : null;
+
+    /// <summary>The next (<paramref name="step"/> 1) or previous (−1) fence, wrapping; a fence that is gone gives the first.</summary>
+    public static string? Next(IReadOnlyList<string> order, string current, int step)
+    {
+        if (order.Count == 0) return null;
+        var index = -1;
+        for (var at = 0; at < order.Count; at++)
+        {
+            if (order[at] == current) index = at;
+        }
+        if (index < 0) return order[0];
+        return order[((index + step) % order.Count + order.Count) % order.Count];
+    }
+}
diff --git a/src/NeoFences.Core/Lifecycle/CheckReport.cs b/src/NeoFences.Core/Lifecycle/CheckReport.cs
new file mode 100644
index 0000000..d641c22
--- /dev/null
+++ b/src/NeoFences.Core/Lifecycle/CheckReport.cs
@@ -0,0 +1,48 @@
+using System.Text.Json;
+
+namespace NeoFences.Core.Lifecycle;
+
+/// <summary>One check of the VM test pass (M38): its id, whether it passed, and what the guest script noted.</summary>
+public sealed record CheckRow(string Id, bool Ok, string Note);
+
+/// <summary>
+/// The results file the test pass writes inside a VM (<c>results.json</c>, M38 spec §2), read on the host into a report. A
+/// damaged or unfinished file is a failed report (the pass did not finish), never a throw.
+/// </summary>
+public sealed record CheckReport(string Machine, string Version, IReadOnlyList<CheckRow> Checks, int Failed, string Summary)
+{
+    public static CheckReport Read(string json)
+    {
+        string machine = "unknown machine", version = "?";
+        try
+        {
+            using var document = JsonDocument.Parse(json);
+            var root = document.RootElement;
+            if (root.ValueKind != JsonValueKind.Object) return NoResults(machine, version);
+            if (root.TryGetProperty("machine", out var machineValue) && machineValue.ValueKind == JsonValueKind.String) machine = machineValue.GetString()!;
+            if (root.TryGetProperty("version", out var versionValue) && versionValue.ValueKind == JsonValueKind.String) version = versionValue.GetString()!;
+            if (!root.TryGetProperty("checks", out var checks) || checks.ValueKind != JsonValueKind.Array) return NoResults(machine, version);
+            var rows = new List<CheckRow>();
+            foreach (var check in checks.EnumerateArray())
+            {
+                if (check.ValueKind != JsonValueKind.Object || !check.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
+                var ok = check.TryGetProperty("ok", out var okValue) && okValue.ValueKind == JsonValueKind.True;
+                var note = check.TryGetProperty("note", out var noteValue) && noteValue.ValueKind == JsonValueKind.String ? noteValue.GetString()! : "";
+                rows.Add(new CheckRow(id.GetString()!, ok, note));
+            }
+            if (rows.Count == 0) return NoResults(machine, version);
+            var failed = rows.Where(row => !row.Ok).ToList();
+            var summary = $"{machine}, {version}: {rows.Count - failed.Count} of {rows.Count} passed";
+            if (failed.Count > 0)
+                summary += "; failed: " + string.Join(", ", failed.Select(row => row.Note.Length > 0 ? $"{row.Id} ({row.Note})" : row.Id));
+            return new CheckReport(machine, version, rows, failed.Count, summary);
+        }
+        catch (JsonException)
+        {
+            return NoResults(machine, version);
+        }
+    }
+
+    private static CheckReport NoResults(string machine, string version) =>
+        new(machine, version, [], 1, $"{machine}, {version}: no results (the test pass did not finish)");
+}
```

- [ ] **Step 4: Run.** `dotnet test tests/NeoFences.Core.Tests` → Expected: `Passed: 834`.
- [ ] **Step 5: Commit.** `git add -A && git commit -m "feat: added the fences' keyboard reading order and the VM check report in Core"` (ledger the Task 1 calls).

### Task 2: Shell + App — Peek takes the keyboard; the guide

**Files:**
- Create: `src/NeoFences.Shell/KeyboardFocus.cs`, `src/NeoFences.App/FenceHost.Keyboard.cs`
- Modify: `src/NeoFences.App/FenceHost.cs` (`OnHotkey` gives the keyboard back on Esc / the hotkey, `SetPeek` takes /
  releases it, `FenceCycleRequested` wired), `src/NeoFences.App/FenceWindow.xaml` (`ItemFocusVisibility`, `KeyboardRing`,
  `FocusRing`), `src/NeoFences.App/FenceWindow.xaml.cs` (`FenceCycleRequested`, `TakeKeyboard()`, `ReleaseKeyboard()`,
  the hover poll), `docs/GUIDE.md` (§8, §15), `README.md` (quick start 5)

**Interfaces:**
- Consumes: Task 1's `FenceSpot`, `KeyboardOrder`.
- Produces: `KeyboardFocus.Foreground() → nint`, `.IsOwn(nint) → bool`, `.TryGive(nint) → bool`, `.GiveBack(nint)`;
  `FenceWindow.FenceCycleRequested` (`Action<int>`), `.TakeKeyboard()`, `.ReleaseKeyboard()`; log line
  `peek: Windows kept the keyboard elsewhere; …` when Windows refuses.

- [ ] **Step 1: Implement.** Write this patch to `m38-2-app.patch` and apply it:

```diff
diff --git a/src/NeoFences.App/FenceHost.Keyboard.cs b/src/NeoFences.App/FenceHost.Keyboard.cs
new file mode 100644
index 0000000..130afb8
--- /dev/null
+++ b/src/NeoFences.App/FenceHost.Keyboard.cs
@@ -0,0 +1,76 @@
+using NeoFences.Core.Layouts;
+using NeoFences.Shell;
+using Serilog;
+
+namespace NeoFences.App;
+
+/// <summary>
+/// The keyboard way in (M38, spec 2026-10-06-keyboard-and-older-windows-design §1, ADR-060): Peek gives the keyboard to the
+/// fence under the mouse (else the one used last, else the first in reading order); Tab / Shift+Tab move to the next /
+/// previous fence; Esc or the hotkey ends Peek and gives the keyboard back to the app that had it.
+/// </summary>
+public sealed partial class FenceHost
+{
+    private nint _peekReturnWindow;
+    private FenceWindow? _keyboardWindow;
+    private string? _lastKeyboardFence;
+
+    /// <summary>Peek started: the app with the keyboard is remembered and a fence takes it.</summary>
+    private void TakeKeyboardForPeek()
+    {
+        var foreground = KeyboardFocus.Foreground();
+        _peekReturnWindow = KeyboardFocus.IsOwn(foreground) ? 0 : foreground; // the tray menu or Settings: nothing to give back
+        var spots = KeyboardSpots();
+        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
+        var underMouse = spots.FirstOrDefault(spot => cursorX >= spot.X && cursorX < spot.X + spot.W && cursorY >= spot.Y && cursorY < spot.Y + spot.H)?.Id;
+        var start = KeyboardOrder.Start(KeyboardOrder.ReadingOrder(spots), underMouse, _lastKeyboardFence);
+        if (start is not null && _windows.TryGetValue(start, out var window)) GiveKeyboard(window);
+    }
+
+    /// <summary>Tab / Shift+Tab in a fence while peeking: the next / previous fence in reading order.</summary>
+    private void CycleKeyboard(FenceWindow from, int step)
+    {
+        if (!_peeking) return;
+        var next = KeyboardOrder.Next(KeyboardOrder.ReadingOrder(KeyboardSpots()), from.BoxId, step);
+        if (next is not null && _windows.TryGetValue(next, out var window)) GiveKeyboard(window);
+    }
+
+    private void GiveKeyboard(FenceWindow window)
+    {
+        if (_keyboardWindow is { } previous && previous != window) previous.ReleaseKeyboard();
+        if (!KeyboardFocus.TryGive(window.Handle)) Log.Information("peek: Windows kept the keyboard elsewhere; the fence shows its ring for the mouse");
+        window.TakeKeyboard();
+        _keyboardWindow = window;
+        _lastKeyboardFence = window.BoxId;
+    }
+
+    /// <summary>Peek ended (any way): the fence lets the keyboard go.</summary>
+    private void ReleaseKeyboardForPeek()
+    {
+        _keyboardWindow?.ReleaseKeyboard();
+        _keyboardWindow = null;
+    }
+
+    /// <summary>Peek ended by Esc or its hotkey: the app that had the keyboard gets it back (not after a click elsewhere or an open).</summary>
+    private void ReturnKeyboard()
+    {
+        KeyboardFocus.GiveBack(_peekReturnWindow);
+        _peekReturnWindow = 0;
+    }
+
+    /// <summary>Every shown fence (a box once) with its rect and monitor, for the reading order.</summary>
+    private List<FenceSpot> KeyboardSpots()
+    {
+        var spots = new List<FenceSpot>();
+        foreach (var (boxId, window) in _windows)
+        {
+            if (!window.IsVisible || window.Handle == 0) continue;
+            var rect = FenceWindowChrome.GetPixelRect(window.Handle);
+            var (centerX, centerY) = (rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
+            var monitor = _monitors.FirstOrDefault(candidate => centerX >= candidate.WorkLeftPx && centerX < candidate.WorkLeftPx + candidate.WorkWidthPx
+                                                                && centerY >= candidate.WorkTopPx && centerY < candidate.WorkTopPx + candidate.WorkHeightPx);
+            spots.Add(new FenceSpot(boxId, rect.X, rect.Y, rect.Width, rect.Height, monitor?.IsPrimary ?? true, monitor?.WorkLeftPx ?? 0));
+        }
+        return spots;
+    }
+}
diff --git a/src/NeoFences.App/FenceHost.cs b/src/NeoFences.App/FenceHost.cs
index bc63720..29e416a 100644
--- a/src/NeoFences.App/FenceHost.cs
+++ b/src/NeoFences.App/FenceHost.cs
@@ -300,6 +300,7 @@ public sealed partial class FenceHost
         };
         window.DetachTabRequested += () => DetachTab(window, window.FenceId, dropPoint: null);
         window.TabCycleRequested += step => CycleTab(window, step);
+        window.FenceCycleRequested += step => CycleKeyboard(window, step); // M38
         window.SetTabs(FenceTabs.TabsOf(_config, box.Id), shown.Id);
         ApplyStyle(window); // M14
         window.SetHoverPaused(_gameMode || !Current.FencesVisible); // M37
@@ -1486,8 +1487,17 @@ public sealed partial class FenceHost
         CheckGameMode(); // fresh: a game may have gone full screen since the last check (M6a review I1)
         // Paused: the desktop belongs to Windows. Gaming: fences must not rise over the game, and the click-outside hook is off.
         if (_paused || _gameMode) return;
-        if (hotkeyId == PeekHotkeyId) SetPeek(!_peeking);
-        else if (hotkeyId == PeekEscapeHotkeyId) SetPeek(false);
+        if (hotkeyId == PeekHotkeyId)
+        {
+            var ending = _peeking;
+            SetPeek(!_peeking);
+            if (ending) ReturnKeyboard(); // M38: the app you were in gets the keyboard back
+        }
+        else if (hotkeyId == PeekEscapeHotkeyId)
+        {
+            SetPeek(false);
+            ReturnKeyboard(); // M38
+        }
     }
 
     /// <summary>Peek (M5): every fence above all windows until the hotkey again, Esc, a click outside, or an item opens.</summary>
@@ -1510,6 +1520,8 @@ public sealed partial class FenceHost
                 Log.Warning("Esc is taken by another app; Peek ends with its hotkey or a click outside");
             foreach (var window in _windows.Values) CheckFence(window.FenceId); // the shown fences are checked again (spec §4)
         }
+        if (peeking) TakeKeyboardForPeek(); // M38: the keyboard way in
+        else ReleaseKeyboardForPeek();
         Log.Information("peek: {Peeking}", peeking);
     }
 
diff --git a/src/NeoFences.App/FenceWindow.xaml b/src/NeoFences.App/FenceWindow.xaml
index dbd54e9..c795b37 100644
--- a/src/NeoFences.App/FenceWindow.xaml
+++ b/src/NeoFences.App/FenceWindow.xaml
@@ -31,6 +31,8 @@
         <sys:Double x:Key="GridCellHeight" xmlns:sys="clr-namespace:System;assembly=System.Runtime">96</sys:Double>
         <!-- M36: the room around each element, from the fence's spacing (Compact 0, Normal 2, Roomy 8). -->
         <Thickness x:Key="CellInset">2</Thickness>
+        <!-- M38: the ring around the item under the keyboard, shown only while Peek has given the fence the keyboard. -->
+        <Visibility x:Key="ItemFocusVisibility">Collapsed</Visibility>
         <!-- Thin scrollbar (spec §6): no arrows, a rounded thumb that brightens under the mouse. -->
         <Style TargetType="ScrollBar">
             <Setter Property="Width" Value="6" />
@@ -287,6 +289,9 @@
             <Rectangle x:Name="TitleColorBar" Height="3" Width="48" Margin="12,0,0,1" RadiusX="1.5" RadiusY="1.5"
                        VerticalAlignment="Bottom" HorizontalAlignment="Left" Visibility="Collapsed" IsHitTestVisible="False" />
             <!-- Rename: replaces the title while editing. Hit-testable inside the caption area. -->
+            <!-- M38: the outline of the fence that has the keyboard (Peek). -->
+            <Border x:Name="KeyboardRing" Grid.RowSpan="3" CornerRadius="7" BorderThickness="2" BorderBrush="{DynamicResource ItemSelectedEdge}"
+                    Visibility="Collapsed" IsHitTestVisible="False" Panel.ZIndex="10" />
             <TextBox x:Name="TitleBox" Visibility="Collapsed" Margin="8,4" Padding="3,1" FontWeight="SemiBold"
                      VerticalContentAlignment="Center" WindowChrome.IsHitTestVisibleInChrome="True"
                      Foreground="{DynamicResource FenceText}" Background="{DynamicResource FenceHover}"
@@ -372,12 +377,20 @@
                                             <!-- The outer border fills the gaps between cells, so the pointer is always over some cell and the
                                                  pop-under name does not blink between neighbours (final review I2). -->
                                             <Border Background="Transparent" Padding="{DynamicResource CellInset}">
+                                                <Grid>
                                                 <!-- M34 (spec §4, "Clean"): no slab; a faint rounded hover, the accent for the selection. -->
-                                                <Border x:Name="Chrome" Background="Transparent" BorderBrush="Transparent" BorderThickness="1" CornerRadius="6" Padding="1,3">
-                                                    <ContentPresenter HorizontalAlignment="Center" />
-                                                </Border>
+                                                    <Border x:Name="Chrome" Background="Transparent" BorderBrush="Transparent" BorderThickness="1" CornerRadius="6" Padding="1,3">
+                                                        <ContentPresenter HorizontalAlignment="Center" />
+                                                    </Border>
+                                                    <!-- M38: the keyboard's ring (only in keyboard mode, ItemFocusVisibility). -->
+                                                    <Border x:Name="FocusRing" Margin="-1" CornerRadius="7" BorderThickness="2" BorderBrush="{DynamicResource ItemSelectedEdge}"
+                                                            Visibility="Collapsed" IsHitTestVisible="False" />
+                                                </Grid>
                                             </Border>
                                             <ControlTemplate.Triggers>
+                                                <Trigger Property="IsKeyboardFocused" Value="True">
+                                                    <Setter TargetName="FocusRing" Property="Visibility" Value="{DynamicResource ItemFocusVisibility}" />
+                                                </Trigger>
                                                 <Trigger Property="IsMouseOver" Value="True">
                                                     <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource FenceHover}" />
                                                 </Trigger>
diff --git a/src/NeoFences.App/FenceWindow.xaml.cs b/src/NeoFences.App/FenceWindow.xaml.cs
index 0a25b22..ba04b96 100644
--- a/src/NeoFences.App/FenceWindow.xaml.cs
+++ b/src/NeoFences.App/FenceWindow.xaml.cs
@@ -86,6 +86,8 @@ public partial class FenceWindow : Window
     public event Action? DetachTabRequested;
     /// <summary>Ctrl+Tab (+1) / Ctrl+Shift+Tab (-1).</summary>
     public event Action<int>? TabCycleRequested;
+    /// <summary>Tab / Shift+Tab while Peek gave this fence the keyboard (M38): the next / previous fence (1 / -1).</summary>
+    public event Action<int>? FenceCycleRequested;
 
     /// <summary>The accent colours (M9), as Windows' own accent palette roughly offers them; defined in Core since M14.</summary>
     public static readonly IReadOnlyDictionary<TabColor, Color> TabColors =
@@ -595,6 +597,11 @@ public partial class FenceWindow : Window
             TabCycleRequested?.Invoke(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
             key.Handled = true;
         }
+        else if (key.Key == Key.Tab && _keyboardMode && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift) // M38
+        {
+            FenceCycleRequested?.Invoke(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1);
+            key.Handled = true;
+        }
     }
 
     private void EndTabGesture()
@@ -1177,7 +1184,7 @@ public partial class FenceWindow : Window
         var rect = FenceWindowChrome.GetPixelRect(Handle);
         // While the height animates, judge "inside" against where the fence is going, so it does not flicker shut.
         var height = _heightAnimation.IsEnabled ? Math.Max(rect.Height, _heightTo) : rect.Height;
-        var inside = cursorX >= rect.X && cursorX < rect.X + rect.Width && cursorY >= rect.Y && cursorY < rect.Y + height;
+        var inside = cursorX >= rect.X && cursorX < rect.X + rect.Width && cursorY >= rect.Y && cursorY < rect.Y + height || _keyboardMode;
         if (!_expansion.Tick(inside)) return;
         AnimateHeight(_expansion.Expanded ? _fullHeightPx : RolledUpHeightPx);
         ItemsShownChanged?.Invoke(); // M25
@@ -1296,6 +1303,40 @@ public partial class FenceWindow : Window
         }
     }
 
+    private bool _keyboardMode;
+
+    /// <summary>
+    /// M38 (ADR-060): Peek gave this fence the keyboard — its outline and the item ring show, a rolled-up fence opens, and the
+    /// item that was selected (else the first) takes the keyboard. The host has already brought the window forward.
+    /// </summary>
+    public void TakeKeyboard()
+    {
+        _keyboardMode = true;
+        KeyboardRing.Visibility = Visibility.Visible;
+        Resources["ItemFocusVisibility"] = Visibility.Visible;
+        Activate();
+        var item = ItemList.SelectedItem ?? (ItemList.Items.Count > 0 ? ItemList.Items[0] : null);
+        if (item is null)
+        {
+            ItemList.Focus();
+            return;
+        }
+        if (ItemList.SelectedItems.Count == 0) ItemList.SelectedItem = item;
+        ItemList.ScrollIntoView(item);
+        ItemList.UpdateLayout();
+        if (ItemList.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem container) container.Focus();
+        else ItemList.Focus();
+    }
+
+    /// <summary>M38: Peek ended or the keyboard went to another fence: the outline and the ring go; a rolled-up fence may close.</summary>
+    public void ReleaseKeyboard()
+    {
+        if (!_keyboardMode) return;
+        _keyboardMode = false;
+        KeyboardRing.Visibility = Visibility.Collapsed;
+        Resources["ItemFocusVisibility"] = Visibility.Collapsed;
+    }
+
     /// <summary>The hover poll runs while it is needed: rolled up (M5), or a title bar shown on hover (M36).</summary>
     private void UpdateHoverTimer()
     {
diff --git a/src/NeoFences.Shell/KeyboardFocus.cs b/src/NeoFences.Shell/KeyboardFocus.cs
new file mode 100644
index 0000000..3872e67
--- /dev/null
+++ b/src/NeoFences.Shell/KeyboardFocus.cs
@@ -0,0 +1,38 @@
+using Windows.Win32;
+using Windows.Win32.Foundation;
+
+namespace NeoFences.Shell;
+
+/// <summary>
+/// The keyboard for Peek (M38, ADR-060): which window has it, giving it to a fence, giving it back. A fence is
+/// WS_EX_NOACTIVATE and does not take the keyboard from a click while another app is in front (ADR-015); a process that
+/// just received its hotkey may bring a window forward, which is what Peek does.
+/// </summary>
+public static class KeyboardFocus
+{
+    /// <summary>The window that has the keyboard now (0: none).</summary>
+    public static nint Foreground() => PInvoke.GetForegroundWindow();
+
+    /// <summary>Whether a window belongs to NeoFences itself (a fence, a menu, Settings).</summary>
+    public static unsafe bool IsOwn(nint handle)
+    {
+        if (handle == 0) return false;
+        uint processId = 0;
+        PInvoke.GetWindowThreadProcessId((HWND)handle, &processId);
+        return processId == (uint)Environment.ProcessId;
+    }
+
+    /// <returns>True when the window has the keyboard afterwards.</returns>
+    public static bool TryGive(nint handle)
+    {
+        if (handle == 0 || !PInvoke.IsWindow((HWND)handle)) return false;
+        PInvoke.SetForegroundWindow((HWND)handle);
+        return PInvoke.GetForegroundWindow() == (HWND)handle;
+    }
+
+    /// <summary>The app that had the keyboard before Peek gets it back (when it still exists).</summary>
+    public static void GiveBack(nint handle)
+    {
+        if (handle != 0 && PInvoke.IsWindow((HWND)handle)) PInvoke.SetForegroundWindow((HWND)handle);
+    }
+}
```

- [ ] **Step 2: The guide (ADR-050).** In `docs/GUIDE.md` §8 replace the Peek bullet with:

```markdown
- **Peek:** **Ctrl+Alt+Space** lifts all fences above your open windows and puts the keyboard in a fence — the one under
  the mouse, else the one you used last, else the first (left to right, top to bottom). An accent outline shows which
  fence has the keyboard. **Arrow keys**, **Home** and **End** move between items, **Enter** opens, **Tab** /
  **Shift+Tab** go to the next / previous fence. Press the hotkey again or **Esc** to send the fences back — the app you
  were in gets the keyboard back; a click outside or opening something also ends Peek. Change the keys in
  **Settings → General → Peek hotkey**.
```

  In §15 replace the Peek row with these two rows:

```markdown
| Anywhere | **Ctrl+Alt+Space** (then **Esc**) | Peek: fences above windows, the keyboard in a fence (and back to your app) |
| Peek | **Tab** / **Shift+Tab** | Next / previous fence |
```

  In `README.md` quick start item 5 replace "to bring the fences above your open windows; press it again or **Esc** to
  send them back." with "to bring the fences above your open windows with the keyboard in one (arrows, **Enter**, **Tab**
  to the next fence); press it again or **Esc** to send them back."
- [ ] **Step 3: Build and test.** `dotnet build` → `0 Warning(s)`; `dotnet test` → `Passed: 834`.
- [ ] **Step 4: Commit.** `git add -A && git commit -m "feat: made Peek put the keyboard in a fence and give it back on Esc"` (ledger the Task 2 calls).

### Task 3: The Windows 11 test VM tooling

**Files:**
- Create: `tools/vm/README.md`, `tools/vm/New-TestVm.ps1`, `tools/vm/Invoke-VmChecks.ps1`, `tools/vm/guest/unattend.xml`,
  `tools/vm/guest/wait-and-run.ps1`, `tools/vm/guest/guest-checks.ps1`

**Interfaces:**
- Consumes: Task 1's `CheckReport.Read` (loaded from `src/NeoFences.Core/bin/Debug/net10.0/NeoFences.Core.dll` by
  default, `-CoreDll` to point elsewhere).
- Produces: `New-TestVm.ps1 -Name <vm> -Iso <path>` (admin Windows PowerShell; ends with checkpoint `clean`);
  `Invoke-VmChecks.ps1 -Name -PreviousSetup -Feed -Candidate [-Out] [-CoreDll]` (pwsh, a member of Hyper-V
  Administrators) printing one `PASS`/`FAIL` line per check and a summary; guest check ids `install`, `welcome`,
  `behind-windows`, `show-desktop`, `icons-hidden`, `icons-after-exit`, `icons-after-kill`, `explorer-restart`,
  `game-mode`, `update`, `peek-keyboard`, `uninstall`.

- [ ] **Step 1: Implement.** Write this patch to `m38-3-vm.patch` and apply it:

```diff
diff --git a/tools/vm/Invoke-VmChecks.ps1 b/tools/vm/Invoke-VmChecks.ps1
new file mode 100644
index 0000000..7d46d2d
--- /dev/null
+++ b/tools/vm/Invoke-VmChecks.ps1
@@ -0,0 +1,55 @@
+# Runs the NeoFences checklist in a test VM (M38, ADR-060): back to the 'clean' checkpoint (the tester's desktop), the
+# previous release's Setup.exe and the release candidate's update files in, then the guest script runs on the desktop and
+# its results and screenshots come back out. No admin needed (a member of "Hyper-V Administrators"). ASCII only.
+#   tools\vm\Invoke-VmChecks.ps1 -Name NF-Win11 -PreviousSetup <Setup.exe of the last release> -Feed <folder with the candidate's releases.win.json and .nupkg> -Candidate 0.25.0
+param(
+  [Parameter(Mandatory)][string]$Name,
+  [Parameter(Mandatory)][string]$PreviousSetup,
+  [Parameter(Mandatory)][string]$Feed,
+  [Parameter(Mandatory)][string]$Candidate,
+  [string]$Out = (Join-Path ([IO.Path]::GetTempPath()) "neofences-vm-$Name"),
+  [string]$CoreDll = (Join-Path $PSScriptRoot '..\..\src\NeoFences.Core\bin\Debug\net10.0\NeoFences.Core.dll')
+)
+$ErrorActionPreference = 'Stop'
+$credential = New-Object PSCredential('tester', (ConvertTo-SecureString 'NeoFences-Test-1' -AsPlainText -Force))
+if (Test-Path -LiteralPath $Out) { Remove-Item -LiteralPath $Out -Recurse -Force }
+New-Item -ItemType Directory -Force $Out | Out-Null
+
+"${Name}: back to the clean checkpoint"
+Stop-VM -Name $Name -TurnOff -Force -ErrorAction SilentlyContinue
+Restore-VMSnapshot -VMName $Name -Name 'clean' -Confirm:$false
+# The checkpoint holds the running desktop; resumed, Windows would ask for a sign-in (a locked desktop takes no keys or
+# screenshots). Without its saved memory the VM boots fresh and the tester signs in by itself.
+Remove-VMSavedState -VMName $Name -ErrorAction SilentlyContinue
+Start-VM -Name $Name
+$session = $null
+$deadline = (Get-Date).AddMinutes(5)
+while (-not $session -and (Get-Date) -lt $deadline) {
+  try { $session = New-PSSession -VMName $Name -Credential $credential -ErrorAction Stop } catch { Start-Sleep -Seconds 5 }
+}
+if (-not $session) { throw "$Name does not answer PowerShell Direct" }
+try {
+  $machine = Invoke-Command -Session $session -ScriptBlock { $os = Get-CimInstance Win32_OperatingSystem; "$($os.Caption) ($($os.BuildNumber))" }
+  "${Name}: $machine; copying the run in"
+  Invoke-Command -Session $session -ScriptBlock { Remove-Item 'C:\NeoFencesTest\run' -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory 'C:\NeoFencesTest\run\feed' -Force | Out-Null }
+  Copy-Item -ToSession $session -LiteralPath $PreviousSetup -Destination 'C:\NeoFencesTest\run\previous-Setup.exe'
+  Copy-Item -ToSession $session -Path (Join-Path $Feed '*') -Destination 'C:\NeoFencesTest\run\feed' -Recurse
+  Copy-Item -ToSession $session -Path (Join-Path $PSScriptRoot 'guest\*.ps1') -Destination 'C:\NeoFencesTest' # the newest checks
+  $runJson = [ordered]@{ machine = $machine; candidate = $Candidate } | ConvertTo-Json
+  Invoke-Command -Session $session -ScriptBlock { param($json) Set-Content 'C:\NeoFencesTest\run\run.json' $json; Set-Content 'C:\NeoFencesTest\go.txt' 'go' } -ArgumentList $runJson
+
+  "${Name}: the checks run on the VM's desktop (up to 25 minutes)"
+  $deadline = (Get-Date).AddMinutes(25)
+  while ((Get-Date) -lt $deadline -and -not (Invoke-Command -Session $session -ScriptBlock { Test-Path 'C:\NeoFencesTest\out\finished.txt' })) { Start-Sleep -Seconds 15 }
+  Copy-Item -FromSession $session -Path 'C:\NeoFencesTest\out\*' -Destination $Out -Recurse -ErrorAction SilentlyContinue
+  Copy-Item -FromSession $session -Path 'C:\NeoFencesTest\*.log' -Destination $Out -ErrorAction SilentlyContinue
+} finally {
+  Remove-PSSession $session
+  Stop-VM -Name $Name -TurnOff -Force -ErrorAction SilentlyContinue
+}
+if (-not ('NeoFences.Core.Lifecycle.CheckReport' -as [type])) { Add-Type -Path $CoreDll }
+$results = Join-Path $Out 'results.json'
+$report = [NeoFences.Core.Lifecycle.CheckReport]::Read($(if (Test-Path -LiteralPath $results) { Get-Content -LiteralPath $results -Raw } else { '' }))
+foreach ($check in $report.Checks) { '  {0} {1} {2}' -f $(if ($check.Ok) { 'PASS' } else { 'FAIL' }), $check.Id, $check.Note }
+$report.Summary
+"screenshots and logs: $Out"
diff --git a/tools/vm/New-TestVm.ps1 b/tools/vm/New-TestVm.ps1
new file mode 100644
index 0000000..ed5e291
--- /dev/null
+++ b/tools/vm/New-TestVm.ps1
@@ -0,0 +1,86 @@
+# Builds a NeoFences test VM (M38, ADR-060) from a Windows ISO, without clicking through Windows Setup: Windows is written
+# straight onto the VM's disk (DISM) with an answer file (local "tester" account, signs in by itself, the test waiter at
+# sign-in), then the VM is started once and a "clean" checkpoint is taken at the tester's desktop.
+# Run in an ADMIN Windows PowerShell (powershell.exe; mounting disks and DISM need it), once per VM. Needs Hyper-V. ASCII only.
+#   tools\vm\New-TestVm.ps1 -Name NF-Win11 -Iso D:\NeoFences-VMs\iso\Win11.iso
+param(
+  [Parameter(Mandatory)][string]$Name,
+  [Parameter(Mandatory)][string]$Iso,
+  [string]$Edition = 'Pro',
+  [string]$Folder = 'D:\NeoFences-VMs'
+)
+$ErrorActionPreference = 'Stop'
+if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
+  throw 'Run this in an admin PowerShell (it mounts a disk and writes Windows onto it).' }
+if (-not (Get-Command New-VM -ErrorAction SilentlyContinue)) { throw 'Hyper-V is not turned on (see tools\vm\README.md).' }
+if (Get-VM -Name $Name -ErrorAction SilentlyContinue) { throw "A VM named $Name already exists (Remove-VM it and its disk first)." }
+$guest = Join-Path $PSScriptRoot 'guest'
+New-Item -ItemType Directory -Force $Folder | Out-Null
+$vhd = Join-Path $Folder "$Name.vhdx"
+if (Test-Path -LiteralPath $vhd) { throw "$vhd already exists." }
+
+"mounting $Iso"
+$image = Mount-DiskImage -ImagePath $Iso -PassThru
+try {
+  $isoDrive = ($image | Get-Volume).DriveLetter
+  $wim = @("${isoDrive}:\sources\install.wim", "${isoDrive}:\sources\install.esd") | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
+  if (-not $wim) { throw 'No install.wim / install.esd in the ISO.' }
+  $index = (Get-WindowsImage -ImagePath $wim | Where-Object { $_.ImageName -match "^Windows 1[01] $Edition$" } | Select-Object -First 1).ImageIndex
+  if (-not $index) { throw "No 'Windows 10/11 $Edition' edition in the ISO: $((Get-WindowsImage -ImagePath $wim).ImageName -join ', ')" }
+  "edition index $index of $wim"
+
+  "creating $vhd (64 GB, grows as needed)"
+  $disk = New-VHD -Path $vhd -SizeBytes 64GB -Dynamic | Mount-VHD -PassThru | Get-Disk
+  try {
+    Initialize-Disk -Number $disk.Number -PartitionStyle GPT
+    $efi = New-Partition -DiskNumber $disk.Number -Size 260MB -GptType '{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}'
+    Format-Volume -Partition $efi -FileSystem FAT32 -NewFileSystemLabel System -Confirm:$false | Out-Null
+    New-Partition -DiskNumber $disk.Number -Size 16MB -GptType '{e3c9e316-0b5c-4db8-817d-f92df00215ae}' | Out-Null
+    $windows = New-Partition -DiskNumber $disk.Number -UseMaximumSize
+    Format-Volume -Partition $windows -FileSystem NTFS -NewFileSystemLabel Windows -Confirm:$false | Out-Null
+    $efi | Add-PartitionAccessPath -AssignDriveLetter; $windows | Add-PartitionAccessPath -AssignDriveLetter
+    $efiDrive = (Get-Partition -DiskNumber $disk.Number -PartitionNumber $efi.PartitionNumber).DriveLetter
+    $winDrive = (Get-Partition -DiskNumber $disk.Number -PartitionNumber $windows.PartitionNumber).DriveLetter
+
+    "writing Windows to ${winDrive}: (a few minutes)"
+    Expand-WindowsImage -ImagePath $wim -Index $index -ApplyPath "${winDrive}:\" | Out-Null
+    & "$env:WINDIR\System32\bcdboot.exe" "${winDrive}:\Windows" /s "${efiDrive}:" /f UEFI | Out-Null
+    if ($LASTEXITCODE -ne 0) { throw 'bcdboot failed' }
+
+    "answer file and test files"
+    New-Item -ItemType Directory -Force "${winDrive}:\Windows\Panther" | Out-Null
+    (Get-Content -LiteralPath (Join-Path $guest 'unattend.xml') -Raw).Replace('__COMPUTERNAME__', $Name) |
+      Set-Content -LiteralPath "${winDrive}:\Windows\Panther\unattend.xml" -Encoding UTF8
+    New-Item -ItemType Directory -Force "${winDrive}:\NeoFencesTest" | Out-Null
+    Copy-Item -Path (Join-Path $guest '*.ps1') -Destination "${winDrive}:\NeoFencesTest"
+  } finally {
+    Dismount-VHD -Path $vhd
+  }
+} finally {
+  Dismount-DiskImage -ImagePath $Iso | Out-Null
+}
+
+"creating the VM"
+$vm = New-VM -Name $Name -Generation 2 -MemoryStartupBytes 4GB -VHDPath $vhd -SwitchName 'Default Switch' -Path $Folder
+Set-VMProcessor -VM $vm -Count 2
+Set-VMMemory -VM $vm -DynamicMemoryEnabled $true -MinimumBytes 2GB -MaximumBytes 6GB
+Set-VMFirmware -VM $vm -SecureBootTemplate MicrosoftWindows
+Set-VMKeyProtector -VM $vm -NewLocalKeyProtector
+Enable-VMTPM -VM $vm
+Enable-VMIntegrationService -VM $vm -Name 'Guest Service Interface'
+Set-VM -VM $vm -CheckpointType Standard -AutomaticCheckpointsEnabled $false
+
+"first start: Windows sets itself up and signs in (5-15 minutes)"
+Start-VM -VM $vm
+$credential = New-Object PSCredential('tester', (ConvertTo-SecureString 'NeoFences-Test-1' -AsPlainText -Force))
+$deadline = (Get-Date).AddMinutes(30)
+$ready = $false
+while (-not $ready -and (Get-Date) -lt $deadline) {
+  Start-Sleep -Seconds 20
+  try { $ready = Invoke-Command -VMName $Name -Credential $credential -ScriptBlock { Test-Path 'C:\NeoFencesTest\waiter.log' } -ErrorAction Stop } catch { }
+}
+if (-not $ready) { throw "$Name did not reach the tester's desktop in 30 minutes; look at it in Hyper-V Manager." }
+Start-Sleep -Seconds 60 # first-sign-in work settles
+Checkpoint-VM -VM $vm -SnapshotName 'clean'
+Stop-VM -VM $vm -TurnOff
+"done: $Name is ready (checkpoint 'clean'); tools\vm\Invoke-VmChecks.ps1 runs the checks without admin"
diff --git a/tools/vm/README.md b/tools/vm/README.md
new file mode 100644
index 0000000..2bd6b17
--- /dev/null
+++ b/tools/vm/README.md
@@ -0,0 +1,55 @@
+# NeoFences test VM (M38, ADR-060)
+
+A Hyper-V virtual machine with the current Windows 11 — the only target (ADR-059) — where NeoFences' checklist runs by
+itself: install the previous release, the first-run welcome, fences behind windows and after Win+D, desktop icons hidden
+and back after an exit, a kill and an Explorer restart, game mode, Peek and the keyboard, updating to the release
+candidate, and uninstall. Run it for every release candidate.
+
+## Once (admin)
+
+1. **Hyper-V** — in an admin PowerShell, then restart:
+
+   ```powershell
+   dism.exe /Online /Enable-Feature /FeatureName:Microsoft-Hyper-V /All /NoRestart
+   Add-LocalGroupMember -Group 'Hyper-V Administrators' -Member $env:USERNAME   # or: net localgroup "Hyper-V Administrators" $env:USERNAME /add
+   Restart-Computer
+   ```
+
+   (In PowerShell 7, `Enable-WindowsOptionalFeature` fails with "Class not registered"; `dism.exe` works everywhere.)
+
+2. **The ISO** (English (United States), 64-bit) into `D:\NeoFences-VMs\iso\`: https://www.microsoft.com/software-download/windows11
+   → Windows 11 (multi-edition ISO) → `Win11.iso`.
+
+3. **The VM** — in an admin **Windows PowerShell** (powershell.exe), from the repo folder (5–20 minutes; Windows sets
+   itself up without questions and signs in a local "tester" account):
+
+   ```powershell
+   powershell -ExecutionPolicy Bypass -File tools\vm\New-TestVm.ps1 -Name NF-Win11 -Iso D:\NeoFences-VMs\iso\Win11.iso
+   ```
+
+   It ends with a checkpoint named `clean` at the tester's desktop. The VM is unactivated (fine for testing) and uses
+   about 20–25 GB on `D:\NeoFences-VMs`. Wait an hour before the first run: for the first hour after an account first
+   signs in, Windows reports "quiet time" instead of a full-screen app, so the game-mode check cannot pass.
+
+## Every release candidate (no admin)
+
+```powershell
+pwsh -File tools\vm\Invoke-VmChecks.ps1 -Name NF-Win11 -PreviousSetup <the last release's NeoFences.App-win-Setup.exe> -Feed <folder with the candidate's releases.win.json and .nupkg files> -Candidate 0.25.0
+```
+
+It goes back to `clean`, copies the run in, lets the guest script work on the VM's desktop (watch it in Hyper-V Manager if
+you like; up to ~25 minutes), copies the results, screenshots and logs out to `%TEMP%\neofences-vm-<name>` and prints the
+report (one line per check, PASS / FAIL). The VM is turned off afterwards.
+
+To watch, untick **View → Enhanced session** in the VM window: enhanced session is a remote sign-in that shows a
+password box over the tester's already signed-in desktop. Do not sign in there — it takes the desktop away from the
+running checks.
+
+## Removing it
+
+```powershell
+Remove-VM NF-Win11 -Force; Remove-Item D:\NeoFences-VMs -Recurse -Force
+```
+
+The tester account's password (`NeoFences-Test-1`) is in `guest\unattend.xml`: a throwaway VM on the PC's own NAT network
+with nothing to protect.
diff --git a/tools/vm/guest/guest-checks.ps1 b/tools/vm/guest/guest-checks.ps1
new file mode 100644
index 0000000..1d67c5c
--- /dev/null
+++ b/tools/vm/guest/guest-checks.ps1
@@ -0,0 +1,133 @@
+# Inside a NeoFences test VM (M38, spec 2026-10-06-keyboard-and-older-windows-design section 2): the checklist, on the tester's
+# desktop. Reads C:\NeoFencesTest\run (previous-Setup.exe, feed\ with the release candidate, run.json), writes
+# C:\NeoFencesTest\out (results.json after every check, screenshots, NeoFences' logs). Windows PowerShell 5.1. ASCII only.
+$ErrorActionPreference = 'Stop'
+$root = 'C:\NeoFencesTest'
+$run = Join-Path $root 'run'
+$out = Join-Path $root 'out'
+if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
+New-Item -ItemType Directory -Force $out | Out-Null
+$settings = Get-Content -LiteralPath (Join-Path $run 'run.json') -Raw | ConvertFrom-Json
+$appDir = Join-Path $env:LOCALAPPDATA 'NeoFences.App'
+$exe = Join-Path $appDir 'current\NeoFences.exe'
+$data = Join-Path $env:LOCALAPPDATA 'NeoFences'
+$advanced = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
+Add-Type -AssemblyName System.Drawing
+Add-Type -Namespace NfVm -Name W -MemberDefinition @'
+[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
+[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
+[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
+[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
+[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
+[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder text, int max);
+[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr data);
+[DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
+public delegate bool EnumProc(IntPtr h, IntPtr data);
+public static System.Collections.Generic.List<IntPtr> TopLevel() {
+  var all = new System.Collections.Generic.List<IntPtr>();
+  EnumWindows(delegate (IntPtr h, IntPtr d) { all.Add(h); return true; }, IntPtr.Zero);
+  return all;
+}
+public static string Title(IntPtr h) { var t = new System.Text.StringBuilder(256); GetWindowText(h, t, 256); return t.ToString(); }
+'@
+
+$checks = New-Object System.Collections.ArrayList
+function Save { [ordered]@{ machine = $settings.machine; version = $settings.candidate; checks = $checks } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'results.json') -Encoding UTF8 }
+function Check([string]$id, [scriptblock]$test) {
+  $note = ''
+  try { $result = & $test; $ok = [bool]($result | Select-Object -Last 1); $note = [string](($result | Select-Object -SkipLast 1) -join '; ') }
+  catch { $ok = $false; $note = $_.Exception.Message }
+  [void]$checks.Add([ordered]@{ id = $id; ok = $ok; note = $note })
+  Save
+  Shot $id
+}
+function Shot([string]$name) { $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
+  $b = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height; $g = [System.Drawing.Graphics]::FromImage($b); $g.CopyFromScreen(0, 0, 0, 0, $b.Size)
+  $b.Save((Join-Path $out "$name.png")); $g.Dispose(); $b.Dispose() }
+Add-Type -AssemblyName System.Windows.Forms
+function Wait([scriptblock]$condition, [int]$seconds) { $deadline = (Get-Date).AddSeconds($seconds); while (-not (& $condition) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }; [bool](& $condition) }
+function Main { @(Get-CimInstance Win32_Process -Filter "Name = 'NeoFences.exe'" | Where-Object { $_.CommandLine -notlike '*--watchdog*' }) }
+function Running { @(Get-Process NeoFences -ErrorAction SilentlyContinue).Count -gt 0 }
+function Fences { @([NfVm.W]::TopLevel() | Where-Object { [NfVm.W]::Title($_) -eq 'NeoFences fence' -and [NfVm.W]::IsWindowVisible($_) }) }
+function IconsHidden { (Get-ItemProperty $advanced -ErrorAction SilentlyContinue).HideIcons -eq 1 }
+function LogText { Get-ChildItem (Join-Path $data 'logs') -Filter 'neofences-*.log' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 2 | ForEach-Object { Get-Content -LiteralPath $_.FullName } }
+function Keys([byte[]]$vks) { foreach ($vk in $vks) { [NfVm.W]::keybd_event($vk, 0, 0, [UIntPtr]::Zero) }; [array]::Reverse($vks); foreach ($vk in $vks) { [NfVm.W]::keybd_event($vk, 0, 2, [UIntPtr]::Zero) }; Start-Sleep -Milliseconds 800 }
+function StopNeoFences { if (Test-Path -LiteralPath $exe) { & $exe --exit }; [void](Wait { -not (Running) } 30) }
+function StartNeoFences { Start-Process $exe; [void](Wait { @(Fences).Count -gt 0 } 60); Start-Sleep -Seconds 4 }
+function SetConfig([string]$name, $value) {
+  $path = Join-Path $data 'config.json'; $config = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
+  $config.settings | Add-Member -NotePropertyName $name -NotePropertyValue $value -Force
+  $config | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $path -Encoding UTF8 }
+# Windows 11's Notepad is a Store app: notepad.exe hands over and exits, and a second start may open a tab in the first
+# window, so the window is found by its title.
+function NotepadWindow { @([NfVm.W]::TopLevel() | Where-Object { [NfVm.W]::IsWindowVisible($_) -and [NfVm.W]::Title($_) -like '*Notepad' }) | Select-Object -First 1 }
+function Notepad { if (-not (NotepadWindow)) { Start-Process notepad; [void](Wait { [bool](NotepadWindow) } 30) }
+  $window = NotepadWindow; if (-not $window) { throw 'Notepad did not open' }
+  Keys @(0x12); [void][NfVm.W]::SetForegroundWindow($window); Start-Sleep -Milliseconds 800; $window }
+function Notifications { $state = 0; [void][NfVm.W]::SHQueryUserNotificationState([ref]$state)
+  @{ 1 = 'not present'; 2 = 'busy'; 3 = 'D3D full screen'; 4 = 'presentation'; 5 = 'accepts notifications'; 6 = 'quiet time (the first hour after the account first signed in)'; 7 = 'app' }[$state] }
+
+$os = Get-CimInstance Win32_OperatingSystem
+"machine: $($os.Caption) $($os.Version)"
+Check 'install' {
+  (Start-Process (Join-Path $run 'previous-Setup.exe') -ArgumentList '--silent' -PassThru).WaitForExit() # -Wait would wait for NeoFences too (5.1 waits for the whole tree)
+  if (-not (Wait { (Running) -and @(Fences).Count -gt 0 } 90)) { StartNeoFences }
+  "version $((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion)"; (Running) -and @(Fences).Count -gt 0
+}
+Check 'welcome' { [void](Wait { Test-Path -LiteralPath (Join-Path $data 'config.json') } 30); $config = Get-Content -LiteralPath (Join-Path $data 'config.json') -Raw | ConvertFrom-Json; "fences: $(@($config.fences).Count)"; @($config.fences | Where-Object { $_.welcome }).Count -eq 1 }
+Check 'behind-windows' {
+  $notepad = Notepad; $order = [NfVm.W]::TopLevel(); $fence = @(Fences)[0]
+  "notepad at $($order.IndexOf($notepad)), fence at $($order.IndexOf($fence))"; $order.IndexOf($notepad) -lt $order.IndexOf($fence)
+}
+Check 'show-desktop' {
+  Keys @(0x5B, 0x44); Start-Sleep -Seconds 1; $fence = @(Fences)[0]
+  $shown = $fence -and [NfVm.W]::IsWindowVisible($fence) -and -not [NfVm.W]::IsIconic($fence)
+  Shot 'show-desktop-on'; Keys @(0x5B, 0x44); "fence visible after Win+D: $shown"; $shown
+}
+Check 'icons-hidden' { StopNeoFences; SetConfig 'hideDesktopIcons' $true; StartNeoFences; "HideIcons: $((Get-ItemProperty $advanced).HideIcons)"; IconsHidden }
+Check 'icons-after-exit' { StopNeoFences; $back = Wait { -not (IconsHidden) } 15; "icons back after exit: $back"; $back }
+Check 'icons-after-kill' {
+  StartNeoFences; $hiddenBefore = IconsHidden
+  $main = Main | Select-Object -First 1; Stop-Process -Id $main.ProcessId -Force
+  $seen = Wait { -not (IconsHidden) } 15; $restarted = Wait { @(Main).Count -gt 0 } 30; Start-Sleep -Seconds 6
+  "hidden before: $hiddenBefore; icons shown after the kill: $seen; NeoFences back: $restarted"; $hiddenBefore -and $seen
+}
+Check 'explorer-restart' {
+  if (-not (Running)) { StartNeoFences }
+  Stop-Process -Name explorer -Force; Start-Sleep -Seconds 4
+  if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer }
+  Start-Sleep -Seconds 12
+  "fences visible: $(@(Fences).Count); NeoFences running: $(Running); icons hidden: $(IconsHidden)"; (Running) -and @(Fences).Count -gt 0 -and (IconsHidden)
+}
+Check 'game-mode' {
+  $edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
+  if (-not (Test-Path -LiteralPath $edge)) { 'no Edge'; $false; return }
+  $before = @(LogText | Select-String -SimpleMatch 'game mode: True').Count
+  $since = Get-Date
+  Start-Process $edge -ArgumentList '--kiosk', 'about:blank', '--edge-kiosk-type=fullscreen', '--no-first-run', "--user-data-dir=$env:TEMP\nf-edge"
+  $on = Wait { @(LogText | Select-String -SimpleMatch 'game mode: True').Count -gt $before } 25
+  $windows = Notifications; Shot 'game-mode-on'
+  Get-Process msedge -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -ge $since } | Stop-Process -Force -ErrorAction SilentlyContinue
+  "game mode on: $on; Windows reports: $windows"; $on
+}
+Check 'update' {
+  StopNeoFences
+  $env:NEOFENCES_UPDATE_SOURCE = Join-Path $run 'feed'; Start-Process $exe; Remove-Item Env:\NEOFENCES_UPDATE_SOURCE
+  $downloaded = Wait { @(LogText | Select-String -Pattern "update $([regex]::Escape($settings.candidate)) downloaded").Count -gt 0 } 240
+  StopNeoFences; Start-Sleep -Seconds 20; StartNeoFences
+  $version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
+  "downloaded: $downloaded; version now: $version; icons hidden: $(IconsHidden)"; $version -like "$($settings.candidate)*" -and (IconsHidden)
+}
+# After the update: the keyboard way in is new in the candidate.
+Check 'peek-keyboard' {
+  $notepad = Notepad; Keys @(0x11, 0x12, 0x20); Start-Sleep -Seconds 1
+  $inFence = [NfVm.W]::Title([NfVm.W]::GetForegroundWindow()) -eq 'NeoFences fence'; Shot 'peek-keyboard-on'
+  Keys @(0x1B); Start-Sleep -Seconds 1; $back = [NfVm.W]::GetForegroundWindow() -eq $notepad
+  "keyboard in a fence: $inFence; back to Notepad after Esc: $back"; $inFence -and $back
+}
+Check 'uninstall' {
+  StopNeoFences; (Start-Process (Join-Path $appDir 'Update.exe') -ArgumentList '--uninstall' -PassThru).WaitForExit(); Start-Sleep -Seconds 5
+  $gone = -not (Test-Path -LiteralPath $exe); "app removed: $gone; icons hidden: $(IconsHidden)"; $gone -and -not (IconsHidden)
+}
+Copy-Item -LiteralPath (Join-Path $data 'logs') -Destination (Join-Path $out 'logs') -Recurse -Force -ErrorAction SilentlyContinue
+Set-Content -LiteralPath (Join-Path $out 'finished.txt') -Value (Get-Date -Format s)
diff --git a/tools/vm/guest/unattend.xml b/tools/vm/guest/unattend.xml
new file mode 100644
index 0000000..694c44e
--- /dev/null
+++ b/tools/vm/guest/unattend.xml
@@ -0,0 +1,62 @@
+<?xml version="1.0" encoding="utf-8"?>
+<!-- NeoFences test VM (M38, ADR-060): skips Windows' first-run questions, makes a local "tester" account that signs in by
+     itself, and starts the test waiter at sign-in. A throwaway VM with no network services: the password is not a secret.
+     __COMPUTERNAME__ is filled in by New-TestVm.ps1. -->
+<unattend xmlns="urn:schemas-microsoft-com:unattend" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
+  <settings pass="specialize">
+    <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
+      <ComputerName>__COMPUTERNAME__</ComputerName>
+      <TimeZone>India Standard Time</TimeZone>
+    </component>
+  </settings>
+  <settings pass="oobeSystem">
+    <component name="Microsoft-Windows-International-Core" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
+      <InputLocale>en-US</InputLocale>
+      <SystemLocale>en-US</SystemLocale>
+      <UILanguage>en-US</UILanguage>
+      <UserLocale>en-US</UserLocale>
+    </component>
+    <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
+      <OOBE>
+        <HideEULAPage>true</HideEULAPage>
+        <HideOEMRegistrationScreen>true</HideOEMRegistrationScreen>
+        <HideOnlineAccountScreens>true</HideOnlineAccountScreens>
+        <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
+        <ProtectYourPC>3</ProtectYourPC>
+        <SkipMachineOOBE>true</SkipMachineOOBE>
+        <SkipUserOOBE>true</SkipUserOOBE>
+      </OOBE>
+      <UserAccounts>
+        <LocalAccounts>
+          <LocalAccount wcm:action="add">
+            <Name>tester</Name>
+            <Group>Administrators</Group>
+            <Password>
+              <Value>NeoFences-Test-1</Value>
+              <PlainText>true</PlainText>
+            </Password>
+          </LocalAccount>
+        </LocalAccounts>
+      </UserAccounts>
+      <AutoLogon>
+        <Enabled>true</Enabled>
+        <Username>tester</Username>
+        <Password>
+          <Value>NeoFences-Test-1</Value>
+          <PlainText>true</PlainText>
+        </Password>
+        <LogonCount>9999</LogonCount>
+      </AutoLogon>
+      <FirstLogonCommands>
+        <SynchronousCommand wcm:action="add">
+          <Order>1</Order>
+          <CommandLine>reg add HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v NeoFencesTestWaiter /t REG_SZ /d "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File C:\NeoFencesTest\wait-and-run.ps1" /f</CommandLine>
+        </SynchronousCommand>
+        <SynchronousCommand wcm:action="add">
+          <Order>2</Order>
+          <CommandLine>cmd /c start "" powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File C:\NeoFencesTest\wait-and-run.ps1</CommandLine>
+        </SynchronousCommand>
+      </FirstLogonCommands>
+    </component>
+  </settings>
+</unattend>
diff --git a/tools/vm/guest/wait-and-run.ps1 b/tools/vm/guest/wait-and-run.ps1
new file mode 100644
index 0000000..df3315f
--- /dev/null
+++ b/tools/vm/guest/wait-and-run.ps1
@@ -0,0 +1,23 @@
+# Inside a NeoFences test VM (M38): runs at the tester's sign-in and waits for the host to drop a run in C:\NeoFencesTest
+# (go.txt, written last by Invoke-VmChecks.ps1); then runs the checks on this desktop. Windows PowerShell 5.1. ASCII only.
+$folder = 'C:\NeoFencesTest'
+$go = Join-Path $folder 'go.txt'
+$log = Join-Path $folder 'waiter.log'
+# One waiter at a time (the first sign-in starts one, and the Run key another).
+$created = $false
+$mutex = New-Object System.Threading.Mutex($true, 'Local\NeoFencesTestWaiter', [ref]$created)
+if (-not $created) { exit }
+Add-Content -LiteralPath $log -Value "$(Get-Date -Format s) waiting"
+while ($true) {
+  if (Test-Path -LiteralPath $go) {
+    Remove-Item -LiteralPath $go -Force
+    Add-Content -LiteralPath $log -Value "$(Get-Date -Format s) run"
+    try {
+      & (Join-Path $folder 'guest-checks.ps1') *>> (Join-Path $folder 'checks.log')
+    } catch {
+      Add-Content -LiteralPath $log -Value "$(Get-Date -Format s) checks failed: $($_.Exception.Message)"
+    }
+    Add-Content -LiteralPath $log -Value "$(Get-Date -Format s) done"
+  }
+  Start-Sleep -Seconds 2
+}
```

- [ ] **Step 2: Check.** Each script parses in Windows PowerShell 5.1:
  `foreach ($f in Get-ChildItem tools\vm -Recurse -Filter *.ps1) { $e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$null, [ref]$e); "$($f.Name) $($e.Count)" }`
  run with `powershell.exe -NoProfile -Command` → Expected: `0` for all four; no non-ASCII byte in `tools/vm`
  (`grep -rP '[^\x00-\x7F]' tools/vm/guest tools/vm/*.ps1` → nothing).
- [ ] **Step 3: Commit.** `git add -A && git commit -m "feat: added the Windows 11 test VM and its scripted checklist"` (ledger the Task 3 calls).

### Task 4: Docs

**Files:**
- Modify: `docs/DECISIONS.md` (ADR-060), `docs/ARCHITECTURE.md` (0.25.0 paragraph after 0.24.0), `docs/FEATURES.md` (an
  extras row), `docs/TEST-CHECKLIST.md` (section AX)
- Create: `docs/research/m38-keyboard-and-windows-11-vm.md`

- [ ] **Step 1: ADR-060** appended to `docs/DECISIONS.md`:

```markdown
## ADR-060 — Peek takes the keyboard; a scripted Windows 11 test pass in a Hyper-V VM
**Date:** 2026-10-07 · **Status:** Accepted · **Supersedes:** ADR-015's "keyboard focus from an app is a ROADMAP follow-up"

**Context.** The readiness review found no keyboard way into the fences (a `WS_EX_NOACTIVATE` fence cannot take the
keyboard while another app is in front, ADR-015), no visible keyboard focus, and NeoFences never run on any PC but the
owner's. The owner chose the keyboard through Peek (no second hotkey) and a scripted test pass; Windows 10 was dropped
(ADR-059).

**Decision.**
- **Peek takes the keyboard**: after its hotkey NeoFences may bring a window forward, so Peek gives the keyboard
  (`SetForegroundWindow`) to the fence under the mouse, else the last used, else the first in reading order (`KeyboardOrder`:
  the main screen first, rows left to right, top to bottom). Tab / Shift+Tab go to the next / previous fence, wrapping. Esc
  or the hotkey ends Peek and gives the keyboard back to the app that had it. Fences stay non-activating otherwise.
- **Visible focus only from the keyboard**: an accent outline on the fence and a ring on the item, shown only while Peek
  gave the fence the keyboard; a rolled-up fence opens meanwhile.
- **A Windows 11 VM checks every release candidate**: `tools/vm/New-TestVm.ps1` builds a Hyper-V VM from Microsoft's ISO
  (DISM and an answer file, an auto-signed-in "tester", a `clean` checkpoint); `Invoke-VmChecks.ps1` boots it fresh, copies
  in the previous release and the candidate's update files, and a guest script works through install, welcome, behind
  windows, Win+D, desktop icons after exit / kill / Explorer restart, game mode, update, Peek and the keyboard, uninstall;
  `CheckReport` reads its results.

**Consequences.** The keyboard reaches every fence from any app without a new hotkey or a hook. The first pass on Windows 11
26300 found no NeoFences problem (12 of 12); it found Windows' first-hour "quiet time", which hides full-screen apps from
game mode on a brand-new account. Tab and Enter inside the fences are checked by hand (AX), not in the VM.
```

- [ ] **Step 2: ARCHITECTURE** — after the 0.24.0 paragraph:

```markdown
**0.25.0 (M38, keyboard way in and the Windows 11 VM)**: Peek takes the keyboard (ADR-060) — `KeyboardOrder` (Core) gives
the fences' reading order, the start fence and Tab wrapping; `KeyboardFocus` (Shell) reads, gives and gives back the
foreground; `FenceHost.Keyboard.cs` remembers the app, gives the keyboard to a fence and back on Esc / the hotkey;
`FenceWindow` has a keyboard mode (`KeyboardRing`, the `FocusRing` shown through `ItemFocusVisibility`, Tab raises
`FenceCycleRequested`). `tools/vm/` builds a Windows 11 Hyper-V VM and runs the checklist in it; `CheckReport` (Core)
reads the results. `research/m38-keyboard-and-windows-11-vm.md`.
```

- [ ] **Step 3: FEATURES** — after the M37 extras row:
  `| Keyboard way in: Peek puts the keyboard in a fence (Tab to the next, Esc back to your app), visible focus; a scripted Windows 11 VM test pass | 0.25 (M38) | done | ADR-060 |`
- [ ] **Step 4: Checklist AX** appended to `docs/TEST-CHECKLIST.md`:

```markdown
## AX — 0.25.0 keyboard way in and the Windows 11 VM (M38)

| ID | Steps | Expected |
|---|---|---|
| AX1 | `tools/vm/Invoke-VmChecks.ps1` for the candidate (VM built over an hour ago; View → Enhanced session off to watch) | 12 of 12 PASS; screenshots in the output folder |
| AX2 | In Firefox, Ctrl+Alt+Space with the mouse over a fence; then with the mouse over no fence | the fence under the mouse (else the last used, else the first) shows an accent outline and its item a ring; after Esc no ring or outline stays, Firefox has the keyboard; mouse clicks later look as in 0.24.0 |
| AX3 | While peeking: arrows, Home, End, Enter; Tab / Shift+Tab across fences and screens; Ctrl+Tab in a tabbed fence; F2, Del, Ctrl+Z, the menu key, type-ahead; a folder panel's Backspace | each works; Tab wraps; a rolled-up fence opens while it has the keyboard and closes after |
| AX4 | Peek, then close the app you came from; Peek from the tray menu / Settings; Peek with an elevated window in front; start a full-screen game while peeking | no crash, nothing wrong comes forward, Peek ends (game mode) |
| AX5 | While peeking: delete a fence, roll one up, switch / detach a tab, pause NeoFences | Tab still lands on a shown fence; no stale outline |
```

- [ ] **Step 5: Research note** `docs/research/m38-keyboard-and-windows-11-vm.md`: the probe (Firefox and full-screen app,
  `SetForegroundWindow` after the hotkey), the five VM runs and the run-5 table from "How this plan is written", every
  tooling lesson (lock screen after restore, enhanced session, `Start-Process -Wait` and the process tree, Store Notepad,
  quiet time, `dism.exe` vs `Enable-WindowsOptionalFeature` in PowerShell 7), the calls made while prototyping, and a
  "Live check" section filled by Task 6.
- [ ] **Step 6: Check** the texts against the code; secret scan of `git diff main`.
- [ ] **Step 7: Commit.** `git add -A && git commit -m "docs: described the keyboard way in and the Windows 11 VM in ADR-060, architecture, features, checklist AX and the M38 note"`

### Task 5: Final review and fix pass

- [ ] Whole-branch review on the most capable model (Review Focus above); findings re-graded by effect; Critical/Important
  fixed in one pass, each with a failing test first where Core-testable.

### Task 6: Live check and the VM pass (asked first)

- [ ] With the owner's OK: on a backed-up copy of the owner's data (`m38-switch.ps1`), the branch's build: AX2–AX5 as far as
  scriptable (screenshots sent as they are taken); restore the data and the Run value; start the installed copy; refocus
  Terminal.
- [ ] Pack the branch as `0.25.0-rc` (`build/pack.ps1 -Version 0.25.0-rc.1 -OutputDir <scratch>`) and run
  `tools/vm/Invoke-VmChecks.ps1 -Name NF-Win11 -PreviousSetup <0.24.0 Setup.exe> -Feed <scratch> -Candidate 0.25.0-rc.1`
  (AX1); send the screenshots; results into the research note; commit `docs: added the M38 live check and VM pass results`.
