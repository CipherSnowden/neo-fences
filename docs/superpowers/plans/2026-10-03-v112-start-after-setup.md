# v1.1.2 — Start After Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After any Setup run (install or upgrade, silent or not), the desktop icons are visible and NeoFences runs again by itself (T13 failed with v1.1.1; user chose "fix now as v1.1.2", 2026-10-03).

**Architecture:** `NeoFences.App/InstallHooks` gains Velopack's install hook. It shows the icons, then starts NeoFences ~4 s later through `cmd.exe` (outside the install folder, so Setup's force-stop after the hook cannot end it). No Shell or Core change.

**Tech Stack:** .NET 10, Velopack 1.2.161 (`OnAfterInstallFastCallback`). No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-02-neofences-v1-design.md` §7 (reliability, hard rule 2). **Decisions:** ADR-023, ADR-026; **ADR-028** (new, Task 2).

**Pre-verified (2026-10-03):** compiled (0 warnings, 0 errors); 283/283 tests. The prototype was packed as 1.1.2-beta.1 and installed over 1.1.1 with `--silent`:
- the hook showed the icons (attempt 1);
- NeoFences started by itself 4 s later and hid them again (Takeover);
- the config was kept.

## Global Constraints

- **Hard rule 2:** the hook shows the icons before anything else; a failure there is logged and never blocks Setup (`WithLog`).
- The hook must stay well inside Velopack's 30 s: icons ≤ 10 s, then a non-blocking process start.
- Commits: single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- Release testing: wait for the Setup process alone (not PowerShell `Start-Process -Wait`, which waits for the started NeoFences too).

## Review Focus

1. **Fresh install vs upgrade vs repair**, and an interactive (double-clicked) Setup that may start the app itself. Expected: exactly one NeoFences afterwards.
2. **The hook racing Setup's force-stop and end.** A slow machine where Setup takes longer than 4 s to finish. Expected: NeoFences still starts (the force-stop checks the install folder; the cmd runs from System32).
3. **The paths in the command line.** Spaces, or unusual characters in `%LOCALAPPDATA%` (a user name with `&` or `^`). Expected: the exe starts, and the quoting holds.

---

### Task 1: The install hook

**Files:**
- Modify: `docs/ROADMAP.md` (claim).
- Replace: `src/NeoFences.App/InstallHooks.cs`.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c v112-start-after-setup
```
In `docs/ROADMAP.md`, below the "Silent Setup upgrades leave NeoFences stopped" item, add `- [~] v1.1.2 start after Setup — claimed by session 2026-10-03 v112`.
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed the v1.1.2 setup restart fix"
```

- [ ] **Step 2: File**

`src/NeoFences.App/InstallHooks.cs`:
```csharp
using System.Diagnostics;
using System.IO;
using NeoFences.Shell;
using Serilog;
using Velopack;

namespace NeoFences.App;

/// <summary>
/// Velopack's install and uninstall hooks (M7, ADR-023). Velopack starts the installed exe with its own arguments;
/// <see cref="Run"/> handles them and exits the process before any WPF starts. Ordinary starts return at once. A hook has
/// a 30 s limit. A newer Setup.exe over an installed copy closes NeoFences, runs the install hook, force-stops anything
/// left in the install folder and ends without starting the app (seen with --silent, v1.1.1): the install hook therefore
/// shows the icons and starts NeoFences once Setup has ended (ADR-028).
/// </summary>
public static class InstallHooks
{
    // Within Velopack's 30 s: stop ≤ 5 s (Velopack has usually closed NeoFences already) + icons ≤ 20 s + the rest.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IconRetryLimit = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan InstallIconRetryLimit = TimeSpan.FromSeconds(10);
    /// <summary>Setup's force-stop of the install folder runs right after the hook, and Setup ends a moment later.</summary>
    private const int StartDelaySeconds = 4;

    public static void Run() =>
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => OnAfterInstall())
            .OnBeforeUninstallFastCallback(_ => OnBeforeUninstall())
            .Run();

    /// <summary>
    /// Hard rule 2 on install and upgrade: Setup closed the old copy, perhaps with the icons hidden, and will not start
    /// the new one. The icons come back now; NeoFences starts a few seconds after Setup has ended, through cmd.exe (outside
    /// the install folder, so Setup's force-stop does not end it). If Setup or the user starts it too, the second copy
    /// waits for the first and exits (single instance).
    /// </summary>
    private static void OnAfterInstall() => WithLog(() =>
    {
        Log.Information("install: showing desktop icons and starting NeoFences after Setup");
        DesktopIcons.ShowWithRetry(giveUpAfter: InstallIconRetryLimit, log: message => Log.Information("install: {Message}", message));
        if (Environment.ProcessPath is not { } exePath) return;
        // ping waits without a window or input (timeout needs a console); "start" then runs NeoFences as a normal app.
        var command = $"/d /c ping -n {StartDelaySeconds + 1} 127.0.0.1 >nul & start \"\" \"{exePath}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", command) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
    });

    /// <summary>
    /// Hard rule 2: after uninstall the desktop icons are always visible, even if NeoFences was not running or had
    /// crashed (Explorer keeps "hide icons" across restarts). The sign-in entry goes if it starts this copy. The data
    /// in %LOCALAPPDATA%\NeoFences stays (user choice 2026-10-03), so a reinstall brings the fences back.
    /// </summary>
    private static void OnBeforeUninstall() => WithLog(() =>
    {
        Log.Information("uninstall: stopping NeoFences, restoring icons, removing the sign-in entry");
        StopRunningInstance();
        // The last code that can restore the icons (Velopack has already closed NeoFences and its watchdog): retry, then
        // fall back to Explorer's persisted setting (M7 review I2).
        DesktopIcons.ShowWithRetry(giveUpAfter: IconRetryLimit, log: message => Log.Information("uninstall: {Message}", message));
        if (InstallRoot() is { } installRoot) StartupRegistration.RemoveIfUnder(installRoot, log: message => Log.Information("{Message}", message));
    });

    /// <summary>
    /// Asks the running NeoFences to exit cleanly (it restores icons and tells its watchdog all is well), then waits for
    /// every other NeoFences process (main and watchdog) to end. Velopack ends whatever is left after that.
    /// </summary>
    private static void StopRunningInstance()
    {
        if (EventWaitHandle.TryOpenExisting(Program.ExitSignalName, out var exit))
        {
            using (exit) exit.Set();
        }
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < StopTimeout && OtherNeoFencesProcesses() > 0) Thread.Sleep(200);
        Log.Information("stop for install: {Remaining} NeoFences process(es) left after {Seconds:0.0} s", OtherNeoFencesProcesses(), deadline.Elapsed.TotalSeconds);
    }

    private static int OtherNeoFencesProcesses()
    {
        // Only this session: the exit signal is per session, so another signed-in user's NeoFences would only make this
        // wait the full timeout (M8a).
        var sessionId = Process.GetCurrentProcess().SessionId;
        var processes = Process.GetProcessesByName("NeoFences");
        var others = processes.Count(process => process.Id != Environment.ProcessId && process.SessionId == sessionId);
        foreach (var process in processes) process.Dispose();
        return others;
    }

    /// <summary><c>&lt;root&gt;\current\NeoFences.exe</c> → <c>&lt;root&gt;</c> (Velopack's layout); null when not installed.</summary>
    private static string? InstallRoot() =>
        Environment.ProcessPath is { } exePath && NeoFences.Core.Lifecycle.StartupPolicy.IsInstalledExe(exePath, File.Exists)
            ? Path.GetDirectoryName(Path.GetDirectoryName(exePath))
            : null;

    private static void WithLog(Action hook)
    {
        try
        {
            // Logging is set up inside the try: a broken logs folder must never stop the icon restore (M7 review I2).
            try
            {
                Directory.CreateDirectory(AppPaths.LogsDirectory);
                App.ConfigureLogging(fileName: "install-.log");
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // run the hook without a log file
            }
            hook();
        }
        catch (Exception failure)
        {
            Log.Error(failure, "install hook failed"); // never block an uninstall or update
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
```

- [ ] **Step 3: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 283`.

- [ ] **Step 4: Commit**

```powershell
git add src/NeoFences.App/InstallHooks.cs
git commit -m "fix: showed the desktop icons and started neofences again after setup"
```

---

### Task 2: Docs

- [ ] **Step 1: Append ADR-028 to `docs/DECISIONS.md`**

```markdown

## ADR-028 — NeoFences starts itself after Setup (v1.1.2)
**Date:** 2026-10-03 · **Status:** Accepted · **Refines:** ADR-023 (installer), ADR-026 (single-instance wait)

**Context.** Upgrading 1.1.0 → 1.1.1 with `Setup.exe --silent` left NeoFences stopped: no fences, and the desktop icons
hidden until the next sign-in (hard rule 2). A verbose Setup log showed the order: Setup closes the app, runs the
installed exe's `--veloapp-install` hook, force-stops anything still running from the install folder, writes the
uninstall key and ends. It never starts the app. The 5 s single-instance wait (ADR-026) assumed a start that never came.

**Decision.** The install hook (`InstallHooks.OnAfterInstall`, Velopack's `OnAfterInstallFastCallback`):
1. shows the desktop icons (`DesktopIcons.ShowWithRetry`, up to 10 s), so they can never stay hidden after Setup;
2. starts NeoFences ~4 s later through `cmd.exe /c ping … & start "" <exe>`: outside the install folder, so Setup's
   force-stop (right after the hook) does not end it, and late enough that Setup has finished. A second start (Setup,
   the user, or sign-in) waits for the first and exits (single instance).

Live (2026-10-03): 1.1.1 → 1.1.2-beta.1 with `--silent`: hook logged "showing desktop icons and starting NeoFences after
Setup", icons shown on attempt 1, NeoFences started by itself 4 s later.

**Consequences.**
- A fresh install also starts NeoFences this way; if Setup starts it too, one copy exits after its 5 s wait.
- Testing note: PowerShell's `Start-Process -Wait` on Setup now waits forever (it waits for the whole process tree,
  which includes the started NeoFences); wait for the Setup process alone instead.
```

- [ ] **Step 2: Update the docs**
  - `ROADMAP.md`: tick the "Silent Setup upgrades" item with " — v1.1.2 (ADR-028; checked with 1.1.2-beta.1)"; the claim line becomes `- [x] v1.1.2 start after Setup — done <date>`.
  - `TEST-CHECKLIST.md` section Q: add the row `| Q11 | Upgrade with a newer Setup.exe (silent and double-clicked) | icons visible during Setup; NeoFences runs again ~4 s after it ends; one copy only |`.
  - `SESSION-LOG.md`: add an entry.
- [ ] **Step 3: Refresh the hub** (`node --check`, publish with `url`).

- [ ] **Step 4: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-028 and the setup restart check"
```
