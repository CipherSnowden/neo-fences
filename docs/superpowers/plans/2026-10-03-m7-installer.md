# M7 — Installer (Velopack) → v1.0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- NeoFences 1.0 ships as a per-user Velopack installer (Setup.exe, no admin, self-contained).
- Its uninstall hook always restores the desktop icons, removes its own sign-in entry, and keeps the user's data.
- The sign-in entry follows the installed copy, so development builds never take it over.

**Architecture:**
- `NeoFences.Core`: `StartupPolicy` decides who owns the sign-in entry (installed copy > dev build; never temp builds).
- `NeoFences.Shell`: `StartupRegistration` uses it and removes the entry on uninstall.
- `NeoFences.App`:
  - `InstallHooks` handles Velopack's hooks first thing in `Main`;
  - Velopack 1.2.161 added;
  - version 1.0.0.
- Repo:
  - `dotnet-tools.json` (vpk 1.2.161);
  - `build\pack.ps1` (test → self-contained publish → `vpk pack`);
  - `artifacts\` git-ignored.

**Tech Stack:** .NET 10, WPF, CsWin32 0.3.335, Serilog, xUnit, **Velopack 1.2.161 (new; ADR-023)**.

**Spec:** `docs/superpowers/specs/2026-10-02-neofences-v1-design.md` §2 (installer: Velopack), §5 (autostart), §7 (icons always come back), §9 (M7). **Decisions:**
- ADR-001, ADR-005, ADR-019;
- **ADR-023** (new, Task 2: it comes with the dependency, hard rule 6).

User choices on 2026-10-03:
- installer only for now (no update source; GitHub auto-update later);
- uninstall keeps the data in `%LOCALAPPDATA%\NeoFences`.

**Pre-verified (2026-10-03):** every code block below was compiled together (0 warnings, 0 errors), and **248/248 tests pass**. `pack.ps1 -Version 1.0.0` built `NeoFences.App-win-Setup.exe` (71.7 MB). The install test ran on the real PC with the user's consent (no mouse or keyboard). It covered:
- **Silent install:** the exe in `%LOCALAPPDATA%\NeoFences.App\current`, an Apps-list entry and a Start menu shortcut. The installed NeoFences ran with the user's fences, owned the sign-in entry, and reported 1.0.0.
- **Uninstall through the Apps-list command:** NeoFences stopped and the program folder and Apps entry were removed. HideIcons went 1 → 0 (icons visible), the sign-in entry was gone, and the data folder was kept, with the config content unchanged.
- **Afterwards:** the dev build ran again and took the entry back.

**Found in the prototype:**
- Velopack installs into `%LOCALAPPDATA%\<packId>` and deletes it on uninstall. A packId of `NeoFences` would have deleted the user's data, so the packId is `NeoFences.App`.
- Velopack closes the app's processes before the uninstall hook runs, so the hook's own icon restore is what keeps hard rule 2.
- `dotnet vpk` resolves only from the repo root (the tool manifest), so `pack.ps1` changes into the root first.
- Windows PowerShell 5.1 reads BOM-less UTF-8 scripts as ANSI, so test scripts stay ASCII.

## Global Constraints

- **Hard rule 1:** uninstall never touches user files or the data folder. Velopack's uninstall deletes only `%LOCALAPPDATA%\NeoFences.App`.
- **Hard rule 2:** the uninstall hook always calls `DesktopIcons.TrySetHidden(false)`, whatever ran before.
- **Hard rule 6:** Velopack and vpk are added with ADR-023, in the same task.
- **Hard rule 7:** hooks never throw into Velopack (logged to `logs\install-*.log`), and they stay inside Velopack's 15–30 s limits.
- Commits: single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- **Installing on the user's PC changes their system.** Ask first, print "TEST RUNNING" / "TEST COMPLETE", and leave the dev NeoFences running afterwards. The final v1.0 install for real use is the user's call (Task 5).

## Review Focus

1. **Uninstall while NeoFences is hung, crashed (icons hidden), or mid-quick-hide / Pause.** Expected: icons visible afterwards, every time. By hand Q8, Q9.
2. **The sign-in entry across install, dev runs, uninstall, deleted install folders and the setting turned off.** Expected: it always starts something that exists, and the installed copy wins. Pinned by `StartupPolicyTests`; by hand Q4, Q5.
3. **Setup.exe over an existing install (upgrade).** Expected: a clean stop, the watchdog does not restart the old copy mid-update, and the fences are unchanged. By hand Q7.
4. **A watchdog started from the installed exe after an uninstall or update** (it restarts `current\NeoFences.exe`). Expected: no restart of a removed exe, no restart loop. By hand Q7, Q8.
5. **SmartScreen / unsigned Setup.exe, a non-ASCII user profile path, a very long LOCALAPPDATA.** Expected: installs; a clear message if Windows blocks it. By hand Q2.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Lifecycle/StartupPolicy.cs` | who owns the sign-in entry | 1 |
| `src/NeoFences.Shell/StartupRegistration.cs`, `src/NeoFences.App/InstallHooks.cs`, `Program.cs`, `NeoFences.App.csproj`, `docs/DECISIONS.md` (ADR-023) | entry, Velopack hooks, dependency | 2 |
| `dotnet-tools.json`, `build/pack.ps1`, `.gitignore` | packaging | 3 |
| `docs/TEST-CHECKLIST.md` (section Q), `docs/research/m7-installer.md` | verification | 4 |
| `docs/ARCHITECTURE.md`, `FEATURES.md`, `ROADMAP.md`, `SETUP.md`, `SESSION-LOG.md`, hub | docs sync, v1.0 | 5 |

---

### Task 1: Core — sign-in entry ownership

**Files:**
- Modify: `docs/ROADMAP.md` (claim M7)
- Modify (full new content): `src/NeoFences.Core/Lifecycle/StartupPolicy.cs`, `tests/NeoFences.Core.Tests/Lifecycle/StartupPolicyTests.cs`

**Interfaces:**
- Produces:
  - `StartupPolicy.ShouldRegister(bool startWithWindows, string exePath, string tempFolder, string? currentRunCommand, Func<string,bool> fileExists)`;
  - `StartupPolicy.IsInstalledExe(string, Func<string,bool>)`;
  - `StartupPolicy.ExePathOf(string?)`;
  - `StartupPolicy.RunCommand(string)`.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m7-installer
```
In `docs/ROADMAP.md`, under `## M7 — Package (Velopack) → v1.0`, replace `- [ ] M7 implementation plan` with `- [x] M7 implementation plan` and add `- [~] M7 — claimed by session 2026-10-03 m7`.
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M7"
```

- [ ] **Step 2: Write the failing tests**

Replace `tests/NeoFences.Core.Tests/Lifecycle/StartupPolicyTests.cs` with:
```csharp
using NeoFences.Core.Lifecycle;

namespace NeoFences.Core.Tests.Lifecycle;

/// <summary>Start with Windows (ADR-019, ADR-023): after a power loss NeoFences must come back by itself to restore the desktop.</summary>
public class StartupPolicyTests
{
    private const string Temp = @"C:\Users\cipher\AppData\Local\Temp\";
    private const string DevBuild = @"F:\projects\neo_fences\src\NeoFences.App\bin\Debug\net10.0-windows\NeoFences.exe";
    private const string InstallRoot = @"C:\Users\cipher\AppData\Local\NeoFences.App";
    private const string Installed = InstallRoot + @"\current\NeoFences.exe";

    /// <summary>A file system with the Velopack install in place (Update.exe and the installed exe exist).</summary>
    private static bool WithInstall(string path) =>
        path.Equals(InstallRoot + @"\Update.exe", StringComparison.OrdinalIgnoreCase) || path.Equals(Installed, StringComparison.OrdinalIgnoreCase)
        || path.Equals(DevBuild, StringComparison.OrdinalIgnoreCase);

    private static bool WithoutInstall(string path) => path.Equals(DevBuild, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Enabled_DevBuildWithNoEntry_Registers() =>
        Assert.True(StartupPolicy.ShouldRegister(startWithWindows: true, exePath: DevBuild, tempFolder: Temp, currentRunCommand: null, fileExists: WithoutInstall));

    [Fact]
    public void Disabled_DoesNotRegister() =>
        Assert.False(StartupPolicy.ShouldRegister(startWithWindows: false, exePath: Installed, tempFolder: Temp, currentRunCommand: null, fileExists: WithInstall));

    [Fact]
    public void ABuildRunningFromTheTempFolder_NeverRegisters()
    {
        // A test build there is deleted later; sign-in would then start nothing and the icons would stay hidden.
        Assert.False(StartupPolicy.ShouldRegister(startWithWindows: true, exePath: Temp + @"scratch\m4\NeoFences.exe", tempFolder: Temp, currentRunCommand: null, fileExists: WithoutInstall));
        Assert.False(StartupPolicy.ShouldRegister(startWithWindows: true, exePath: Temp.ToUpperInvariant() + @"x\NeoFences.exe", tempFolder: Temp, currentRunCommand: null, fileExists: WithoutInstall));
    }

    [Fact]
    public void TheInstalledCopy_AlwaysTakesTheEntry() =>
        Assert.True(StartupPolicy.ShouldRegister(startWithWindows: true, exePath: Installed, tempFolder: Temp,
            currentRunCommand: StartupPolicy.RunCommand(DevBuild), fileExists: WithInstall));

    [Fact]
    public void ADevBuild_NeverTakesTheEntryFromAnInstalledCopy() =>
        // Smoke runs start the repo build; sign-in must keep starting the installed NeoFences (M7).
        Assert.False(StartupPolicy.ShouldRegister(startWithWindows: true, exePath: DevBuild, tempFolder: Temp,
            currentRunCommand: StartupPolicy.RunCommand(Installed), fileExists: WithInstall));

    [Fact]
    public void ADevBuild_TakesTheEntryBackWhenTheInstallIsGone() =>
        // Uninstalled without its hook (files deleted by hand): the stale entry would start nothing after a power cut.
        Assert.True(StartupPolicy.ShouldRegister(startWithWindows: true, exePath: DevBuild, tempFolder: Temp,
            currentRunCommand: StartupPolicy.RunCommand(Installed), fileExists: WithoutInstall));

    [Fact]
    public void IsInstalledExe_NeedsCurrentFolderAndUpdateExe()
    {
        Assert.True(StartupPolicy.IsInstalledExe(Installed, WithInstall));
        Assert.False(StartupPolicy.IsInstalledExe(Installed, WithoutInstall));
        Assert.False(StartupPolicy.IsInstalledExe(DevBuild, WithInstall));
    }

    [Theory]
    [InlineData("\"C:\\A B\\NeoFences.exe\"", "C:\\A B\\NeoFences.exe")]
    [InlineData("\"C:\\A B\\NeoFences.exe\" --flag", "C:\\A B\\NeoFences.exe")]
    [InlineData("C:\\X\\NeoFences.exe", "C:\\X\\NeoFences.exe")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExePathOf_ReadsQuotedAndPlainCommands(string? command, string? expected) =>
        Assert.Equal(expected, StartupPolicy.ExePathOf(command));

    [Fact]
    public void RunCommand_QuotesThePath() =>
        Assert.Equal("\"C:\\Program Files\\NeoFences\\NeoFences.exe\"", StartupPolicy.RunCommand(@"C:\Program Files\NeoFences\NeoFences.exe"));
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS: `ShouldRegister` has no overload with `currentRunCommand`; `IsInstalledExe` and `ExePathOf` do not exist.

- [ ] **Step 4: Implement**

Replace `src/NeoFences.Core/Lifecycle/StartupPolicy.cs` with:
```csharp
namespace NeoFences.Core.Lifecycle;

/// <summary>
/// Start with Windows (ADR-019, refined by ADR-023). Takeover's hidden desktop icons survive a hard power loss (Explorer
/// persists them), so NeoFences has to come back at sign-in by itself to show fences, or the desktop is empty.
/// </summary>
public static class StartupPolicy
{
    /// <summary>
    /// Whether this exe should own the sign-in entry:
    /// <list type="bullet">
    /// <item>only while the setting is on;</item>
    /// <item>never for a build running from the temp folder: that copy is deleted later, and sign-in would start nothing;</item>
    /// <item>an installed copy always does;</item>
    /// <item>a development build does unless the entry already points to an installed copy that still exists, so test runs
    /// never take sign-in away from the installed NeoFences (M7).</item>
    /// </list>
    /// </summary>
    /// <param name="currentRunCommand">The entry as it is now (null when there is none).</param>
    public static bool ShouldRegister(bool startWithWindows, string exePath, string tempFolder, string? currentRunCommand, Func<string, bool> fileExists)
    {
        if (!startWithWindows || exePath.StartsWith(tempFolder, StringComparison.OrdinalIgnoreCase)) return false;
        if (IsInstalledExe(exePath, fileExists)) return true;
        var registered = ExePathOf(currentRunCommand);
        return registered is null
            || string.Equals(registered, exePath, StringComparison.OrdinalIgnoreCase)
            || !IsInstalledExe(registered, fileExists)
            || !fileExists(registered);
    }

    /// <summary>
    /// An exe installed by Velopack: <c>&lt;root&gt;\current\NeoFences.exe</c> next to <c>&lt;root&gt;\Update.exe</c>.
    /// </summary>
    public static bool IsInstalledExe(string exePath, Func<string, bool> fileExists)
    {
        var folder = Path.GetDirectoryName(exePath);
        if (folder is null || !string.Equals(Path.GetFileName(folder), "current", StringComparison.OrdinalIgnoreCase)) return false;
        var root = Path.GetDirectoryName(folder);
        return root is not null && fileExists(Path.Combine(root, "Update.exe"));
    }

    /// <summary>The command line stored for sign-in: the quoted exe path.</summary>
    public static string RunCommand(string exePath) => $"\"{exePath}\"";

    /// <summary>The exe path inside a stored command (quoted or not); null for none.</summary>
    public static string? ExePathOf(string? runCommand)
    {
        if (string.IsNullOrWhiteSpace(runCommand)) return null;
        var command = runCommand.Trim();
        if (command[0] != '"') return command;
        var closing = command.IndexOf('"', 1);
        return closing > 1 ? command[1..closing] : null;
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/NeoFences.Core.Tests`
Expected: `Passed! - Failed: 0, Passed: 248`. The whole solution builds only after Task 2, because Shell calls the old signature.

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core tests/NeoFences.Core.Tests
git commit -m "feat: gave the installed copy ownership of the start with windows entry in core"
```

---

### Task 2: Shell + App — Velopack hooks, sign-in entry, version

**Files:**
- Create: `src/NeoFences.App/InstallHooks.cs`.
- Replace: `src/NeoFences.Shell/StartupRegistration.cs`, `src/NeoFences.App/Program.cs` and `src/NeoFences.App/NeoFences.App.csproj`.
- Append ADR-023 to `docs/DECISIONS.md` (hard rule 6: the ADR comes with the dependency).

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `StartupRegistration.RemoveIfUnder(string installRoot, Action<string> log)`;
  - `InstallHooks.Run()`, called first in `Main`.

- [ ] **Step 1: Files** (stop a running NeoFences first: `& <exe> --exit`)

`src/NeoFences.Shell/StartupRegistration.cs`:
```csharp
using Microsoft.Win32;
using NeoFences.Core.Lifecycle;

namespace NeoFences.Shell;

/// <summary>
/// The per-user "run at sign-in" entry (HKCU\…\Run, no admin rights), ADR-019. If the user disables NeoFences under
/// Task Manager → Startup apps, Windows records that separately (StartupApproved) and keeps honouring it; this entry
/// does not override it.
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NeoFences";

    /// <summary>Writes or removes the entry to match the setting (and the exe's current path). Never throws.</summary>
    /// <summary>Uninstall (M7): removes the entry only if it starts the copy being uninstalled. Never throws.</summary>
    public static void RemoveIfUnder(string installRoot, Action<string> log)
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            var registered = StartupPolicy.ExePathOf(run?.GetValue(ValueName) as string);
            if (run is null || registered is null) return;
            var root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(registered).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;
            run.DeleteValue(ValueName, throwOnMissingValue: false);
            log("start with Windows: removed for uninstall");
        }
        catch (Exception failure) when (failure is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException)
        {
            log($"start with Windows: could not remove the sign-in entry ({failure.Message})");
        }
    }

    public static void Apply(bool startWithWindows, string exePath, Action<string> log)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            var current = run.GetValue(ValueName) as string;
            if (StartupPolicy.ShouldRegister(startWithWindows, exePath, Path.GetTempPath(), currentRunCommand: current, fileExists: File.Exists))
            {
                var command = StartupPolicy.RunCommand(exePath);
                if (current == command) return;
                run.SetValue(ValueName, command);
                log($"start with Windows: registered {command}");
            }
            else if (!startWithWindows && run.GetValue(ValueName) is not null)
            {
                run.DeleteValue(ValueName, throwOnMissingValue: false);
                log("start with Windows: removed");
            }
            else if (startWithWindows)
            {
                log($"start with Windows: left to {current ?? "(none)"} (this build is a temp or development copy)");
            }
        }
        catch (Exception failure) when (failure is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            log($"start with Windows: could not update the sign-in entry ({failure.Message})");
        }
    }
}
```
`src/NeoFences.App/InstallHooks.cs`:
```csharp
using System.Diagnostics;
using System.IO;
using NeoFences.Shell;
using Serilog;
using Velopack;

namespace NeoFences.App;

/// <summary>
/// Velopack's install, update and uninstall hooks (M7, ADR-023). Velopack starts the installed exe with its own
/// arguments; <see cref="Run"/> handles them and exits the process before any WPF starts. Ordinary starts return at once.
/// Each hook has a time limit (15–30 s), so everything here is short and never waits forever.
/// </summary>
public static class InstallHooks
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(12);

    public static void Run() =>
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => OnBeforeUninstall())
            .OnBeforeUpdateFastCallback(_ => WithLog(() => StopRunningInstance()))
            .Run();

    /// <summary>
    /// Hard rule 2: after uninstall the desktop icons are always visible, even if NeoFences was not running or had
    /// crashed (Explorer keeps "hide icons" across restarts). The sign-in entry goes if it starts this copy. The data
    /// in %LOCALAPPDATA%\NeoFences stays (user choice 2026-10-03), so a reinstall brings the fences back.
    /// </summary>
    private static void OnBeforeUninstall() => WithLog(() =>
    {
        Log.Information("uninstall: stopping NeoFences, restoring icons, removing the sign-in entry");
        StopRunningInstance();
        Log.Information("uninstall: desktop icons shown {Shown}", DesktopIcons.TrySetHidden(false));
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
        var processes = Process.GetProcessesByName("NeoFences");
        var others = processes.Count(process => process.Id != Environment.ProcessId);
        foreach (var process in processes) process.Dispose();
        return others;
    }

    /// <summary><c>&lt;root&gt;\current\NeoFences.exe</c> → <c>&lt;root&gt;</c> (Velopack's layout); null when not installed.</summary>
    private static string? InstallRoot() =>
        Environment.ProcessPath is { } exePath && Path.GetDirectoryName(exePath) is { } folder && Path.GetFileName(folder) == "current"
            ? Path.GetDirectoryName(folder)
            : null;

    private static void WithLog(Action hook)
    {
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        App.ConfigureLogging(fileName: "install-.log");
        try
        {
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
`src/NeoFences.App/Program.cs`:
```csharp
using System.Globalization;
using System.IO;
using NeoFences.Shell;
using Serilog;

namespace NeoFences.App;

/// <summary>
/// Entry point. The helper modes (<c>--exit</c>, <c>--watchdog-launch</c>, <c>--watchdog</c>) run without WPF, so the
/// watchdog owns no window: it never answers (or blocks) WM_QUERYENDSESSION and never shows on the "apps are
/// blocking shutdown" screen (M2a review C1/M2). Only the normal mode starts the WPF <see cref="App"/>.
/// </summary>
public static class Program
{
    public const string ExitArgument = "--exit";
    public const string ExitSignalName = @"Local\NeoFences.Exit";

    [STAThread]
    public static int Main(string[] args)
    {
        InstallHooks.Run(); // Velopack: install / update / uninstall hooks exit here; ordinary starts continue (M7)
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        switch (args)
        {
            case [ExitArgument]:
                if (EventWaitHandle.TryOpenExisting(ExitSignalName, out var runningInstanceExit)) runningInstanceExit.Set();
                return 0;
            case [Watchdog.LaunchArgument, var launchProcessIdText]:
                Watchdog.RunLauncher(int.Parse(launchProcessIdText, CultureInfo.InvariantCulture));
                return 0;
            case [Watchdog.RunArgument, var mainProcessIdText]:
                return RunWatchdog(int.Parse(mainProcessIdText, CultureInfo.InvariantCulture));
        }

        var app = new App();
        return app.Run();
    }

    private static int RunWatchdog(int mainProcessId)
    {
        App.ConfigureLogging(fileName: "watchdog-.log");
        try
        {
            new Watchdog(AppPaths.DataDirectory, message => Log.Information("{Message}", message)).Run(mainProcessId);
            return 0;
        }
        catch (Exception failure)
        {
            Log.Fatal(failure, "watchdog failed");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
```
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
    <!-- Shown in Settings → About. build\pack.ps1 -Version sets the released one (M7, ADR-023). -->
    <Version>1.0.0</Version>
    <!-- Resource 32512: the tray icon loads it from the exe (M6a). -->
    <ApplicationIcon>NeoFences.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Serilog" Version="4.4.0" />
    <PackageReference Include="Serilog.Sinks.File" Version="7.0.0" />
    <!-- Installer, updates and the uninstall hook (M7, ADR-023). -->
    <PackageReference Include="Velopack" Version="1.2.161" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\NeoFences.Core\NeoFences.Core.csproj" />
    <ProjectReference Include="..\NeoFences.Shell\NeoFences.Shell.csproj" />
  </ItemGroup>

</Project>
```
Append to `docs/DECISIONS.md`:
```markdown
## ADR-023 — Installer: Velopack, self-contained, uninstall restores icons; sign-in follows the installed copy
**Date:** 2026-10-03 · **Status:** Accepted · **Builds on:** ADR-001 (Velopack named for M7), ADR-005, ADR-019

**Context.** M7 ships v1.0 as an installer. The user chose on 2026-10-03:
- **Installer only for now.** No update source; GitHub auto-update can come later.
- **Uninstall keeps the data.** The data is `%LOCALAPPDATA%\NeoFences`: layout, backups, logs.

Hard rule 2 requires icons back when NeoFences is gone for good: the watchdog and session handling never cover "no
longer installed" (ROADMAP M7).

**Decision.**
- **New NuGet dependency: Velopack 1.2.161** (App), plus the `vpk` 1.2.161 CLI as a repo-local dotnet tool
  (`dotnet-tools.json`). This is the ADR that hard rule 6 requires. Why Velopack:
  - per-user install with no admin;
  - delta updates when an update source is added;
  - install/update/uninstall hooks in our own exe;
  - MIT licensed;
  - actively maintained.
- **Package.** `build\pack.ps1 -Version x.y.z` runs the tests, publishes self-contained win-x64 (no separate .NET
  runtime, nothing a runtime update can break: robustness first), and packs with Velopack into
  `artifacts\releases\NeoFences.App-win-Setup.exe` (about 72 MB), the full package and RELEASES. `artifacts\` is
  git-ignored. Unsigned for now: Windows SmartScreen may warn once.
- **Install location.** Velopack installs into `%LOCALAPPDATA%\<packId>` and deletes that folder on uninstall, so the
  packId is `NeoFences.App`, never `NeoFences`, which is the data folder. Program in `%LOCALAPPDATA%\NeoFences.App`,
  data in `%LOCALAPPDATA%\NeoFences`, as before.
- **Hooks** (`InstallHooks`, first line of `Main`, before any WPF):
  - **Before uninstall** (30 s limit): ask a running NeoFences to exit cleanly and wait up to 12 s. Then always show
    the desktop icons (`DesktopIcons.TrySetHidden(false)`, even if NeoFences was not running or had crashed). Then
    remove the sign-in entry if it starts this copy. Keep the data. Velopack closes the app's own processes before
    this hook runs (live test), so the icon restore here is what guarantees rule 2.
  - **Before update** (15 s): the same clean stop.
  - A failing hook is logged (`logs\install-*.log`) and never blocks Velopack.
- **Sign-in entry** (refines ADR-019, `StartupPolicy`, tested):
  - an installed copy (`<root>\current\NeoFences.exe` next to `<root>\Update.exe`) always owns the entry;
  - a development build takes it only if there is none, or it already points at that build, or it points at an
    installed copy that no longer exists;
  - temp-folder builds never do.

  So smoke runs from the repo never take sign-in away from the installed NeoFences, and power-outage recovery keeps
  working before and after installing.
- **Version.** The App's `<Version>` is 1.0.0. `pack.ps1 -Version` sets the released one, which Settings → About shows.

**Consequences.**
- Upgrading means running a newer Setup.exe (or, later, an update source). The data stays put, so the fences survive.
- Uninstalling by deleting the folder by hand skips the hook: the icons are not restored. Recovery: Explorer → View →
  Show desktop icons (SETUP.md). A development build would take the sign-in entry back.
- Code signing and an update source (GitHub Releases) are left for after v1.0.
```

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 248`. Start the dev build: it runs normally, and `VelopackApp.Run()` returns at once when not installed.

- [ ] **Step 3: Commit**

```powershell
git add src/NeoFences.Shell src/NeoFences.App docs/DECISIONS.md
git commit -m "feat: added velopack install hooks that restore desktop icons on uninstall, with ADR-023"
```

---

### Task 3: Packaging and the install test

**Files:** create `dotnet-tools.json` and `build/pack.ps1`; replace `.gitignore`.

- [ ] **Step 1: Files**

`dotnet-tools.json`:
```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "vpk": {
      "version": "1.2.161",
      "commands": [
        "vpk"
      ],
      "rollForward": false
    }
  }
}
```
`build/pack.ps1`:
```powershell
# Builds the NeoFences installer (M7, ADR-023): a self-contained win-x64 publish packed by Velopack into
# artifacts\releases (NeoFences.App-win-Setup.exe, the full package, and RELEASES). Run from the repo root:
#   powershell -NoProfile -ExecutionPolicy Bypass -File build\pack.ps1 -Version 1.0.0
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'artifacts\publish'
$releases = Join-Path $root 'artifacts\releases'
if (Test-Path $publish) { [System.IO.Directory]::Delete($publish, $true) }
Set-Location $root # dotnet finds the vpk tool through the repo's dotnet-tools.json

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed' }
dotnet test (Join-Path $root 'NeoFences.slnx') -v q
if ($LASTEXITCODE -ne 0) { throw 'tests failed: not packing' }
# Self-contained: no separate .NET runtime install, nothing a runtime update can break (robust first, ADR-023).
dotnet publish (Join-Path $root 'src\NeoFences.App') -c Release -r win-x64 --self-contained -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }
# packId NeoFences.App: Velopack installs into %LOCALAPPDATA%\<packId> and deletes that folder on uninstall, so it must not
# be NeoFences (the data folder: config, backups, logs).
dotnet vpk pack --packId NeoFences.App --packVersion $Version --packTitle NeoFences --packAuthors NeoFences `
  --packDir $publish --mainExe NeoFences.exe --icon (Join-Path $root 'src\NeoFences.App\NeoFences.ico') --outputDir $releases
if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }
Get-ChildItem $releases | Sort-Object LastWriteTime | Select-Object -Last 4 Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table | Out-String
```
`.gitignore`:
```
bin/
obj/
.vs/
*.user
TestResults/
artifacts/
```

- [ ] **Step 2: Build the installer**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build\pack.ps1 -Version 1.0.0`
Expected: `Passed: 248`, then Velopack's `Setup bundle created 'NeoFences.App-win-Setup.exe'`, and the table shows the Setup at about 72 MB.

- [ ] **Step 3: Install test (ask first: it installs and uninstalls NeoFences on the user's PC, ~2 min, no mouse or keyboard; print TEST RUNNING / TEST COMPLETE)**

Build the dev build first (`dotnet build NeoFences.slnx`), so it has the new sign-in rule. Save as `<scratchpad>\m7-install-test.ps1` and run `powershell -NoProfile -ExecutionPolicy Bypass -File "<scratchpad>\m7-install-test.ps1"`:
```powershell
# M7 install test (no mouse or keyboard): silent install of the Velopack Setup.exe, checks, the dev-build sign-in rule,
# uninstall through the Apps-list command, checks (icons visible, data kept, sign-in entry gone), then the dev build again.
param(
  [string]$Setup = "F:\projects\neo_fences\artifacts\releases\NeoFences.App-win-Setup.exe",
  [string]$DevExe = "F:\projects\neo_fences\src\NeoFences.App\bin\Debug\net10.0-windows\NeoFences.exe")
function Pause-Ms([int]$ms) { [System.Threading.Thread]::Sleep($ms) }
function Wait-Until([scriptblock]$condition, [int]$seconds = 20) { $deadline = (Get-Date).AddSeconds($seconds); while (-not (& $condition) -and (Get-Date) -lt $deadline) { Pause-Ms 300 }; [bool](& $condition) }
function MainProcesses { @(Get-CimInstance Win32_Process -Filter "Name='NeoFences.exe'" | Where-Object { $_.CommandLine -notmatch '--watchdog' }) }
function NoNeoFences { @(Get-Process NeoFences -ErrorAction SilentlyContinue).Count -eq 0 }
function RunEntry { (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name NeoFences -ErrorAction SilentlyContinue).NeoFences }
function HideIcons { (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name HideIcons -ErrorAction SilentlyContinue).HideIcons }
function UninstallKey { Get-ChildItem 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall' | ForEach-Object { Get-ItemProperty $_.PSPath } | Where-Object { $_.DisplayName -like 'NeoFences*' } | Select-Object -First 1 }
$installRoot = Join-Path $env:LOCALAPPDATA 'NeoFences.App'
$installedExe = Join-Path $installRoot 'current\NeoFences.exe'
$dataDir = Join-Path $env:LOCALAPPDATA 'NeoFences'
$configPath = Join-Path $dataDir 'config.json'

# Arrange: the dev build stops cleanly (icons back, watchdog told); keep a copy of the config and the sign-in entry.
"before: sign-in entry $(RunEntry), HideIcons $(HideIcons)"
& $DevExe --exit; [void](Wait-Until { NoNeoFences } 15)
Copy-Item $configPath "$PSScriptRoot\config-before-m7.json" -Force
$configHash = (Get-FileHash $configPath).Hash
if (Test-Path $installRoot) { "an install already exists at $installRoot; aborting"; Start-Process $DevExe; return }

# 1. Silent install; Velopack starts the installed NeoFences.
Start-Process $Setup -ArgumentList '--silent' -Wait
"install: exe in place $(Test-Path $installedExe), Apps-list entry $([bool](UninstallKey)), Start menu shortcut $(Test-Path "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\NeoFences.lnk")"
if (-not (Wait-Until { @(MainProcesses | Where-Object ExecutablePath -eq $installedExe).Count -gt 0 } 15)) { Start-Process $installedExe; [void](Wait-Until { @(MainProcesses | Where-Object ExecutablePath -eq $installedExe).Count -gt 0 } 15) }
Pause-Ms 4000
"installed NeoFences running: $(@(MainProcesses | Where-Object ExecutablePath -eq $installedExe).Count -eq 1), sign-in entry -> installed copy $((RunEntry) -eq ('"' + $installedExe + '"')), HideIcons $(HideIcons) (Takeover on -> 1)"
"same data folder: config.json still at $configPath $(Test-Path $configPath)"
$version = (Get-Item $installedExe).VersionInfo.ProductVersion
"installed version: $version"

# 2. A dev build started while installed must not take the sign-in entry.
if ($env:NF_TEST_DEV_RULE -ne "0") {
  & $installedExe --exit; [void](Wait-Until { NoNeoFences } 15)
  Start-Process $DevExe; Pause-Ms 5000
  "dev build keeps its hands off: entry still -> installed copy $((RunEntry) -eq ('"' + $installedExe + '"'))"
  & $DevExe --exit; [void](Wait-Until { NoNeoFences } 15)
  Start-Process $installedExe; Pause-Ms 5000
} else { "(dev-build sign-in rule: skipped by NF_TEST_DEV_RULE=0)" }

# 3. Uninstall with the Apps-list command, as Windows would run it.
$uninstall = (UninstallKey).UninstallString
"uninstall command: $uninstall"
$quoted = [regex]::Match($uninstall, '^"([^"]+)"\s*(.*)$')
if ($quoted.Success) { Start-Process $quoted.Groups[1].Value -ArgumentList ($quoted.Groups[2].Value + ' --silent').Trim() -Wait } else { Start-Process cmd -ArgumentList "/c $uninstall" -Wait }
[void](Wait-Until { -not (Test-Path $installedExe) } 30)
[void](Wait-Until { NoNeoFences } 15)
"uninstall: NeoFences stopped $(NoNeoFences), program removed $(-not (Test-Path $installedExe)), Apps-list entry gone $(-not (UninstallKey))"
"icons visible after uninstall: HideIcons $(HideIcons) -> $((HideIcons) -eq 0)"
"sign-in entry removed: $(-not (RunEntry))"
"data kept: config.json $(Test-Path $configPath), unchanged $((Get-FileHash $configPath).Hash -eq $configHash), logs $(Test-Path (Join-Path $dataDir 'logs'))"
$installLog = Get-ChildItem (Join-Path $dataDir 'logs') -Filter 'install-*' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
if ($installLog) { "install log:"; Get-Content $installLog.FullName | Select-Object -Last 6 | ForEach-Object { "  $_" } }

# Restore: the dev build again (it takes the sign-in entry back, since no installed copy remains).
Start-Process $DevExe; Pause-Ms 5000
"dev NeoFences running again: $(@(MainProcesses | Where-Object ExecutablePath -eq $DevExe).Count -eq 1), sign-in entry -> dev build $((RunEntry) -eq ('"' + $DevExe + '"')), HideIcons $(HideIcons)"
```
Expected:
- every check ends `True`, including "dev build keeps its hands off" and "icons visible after uninstall: HideIcons 0 -> True";
- the install log shows the uninstall lines;
- at the end, the dev NeoFences runs again and owns the entry.

Regression: `<scratchpad>\m6a-smoke.ps1` (tray, Pause, game mode: startup through the new `Main`). Expected: as in M6a.

- [ ] **Step 4: Commit**

```powershell
git add dotnet-tools.json build/pack.ps1 .gitignore
git commit -m "build: added the velopack packaging script and the vpk tool manifest"
```

---

### Task 4: Verification

**Files:** `docs/TEST-CHECKLIST.md` (append section Q), `docs/research/m7-installer.md` (create)

- [ ] **Step 1: Append section Q**

```markdown
## Q — Installer (M7, ADR-023)
| ID | Steps | Expected |
|---|---|---|
| Q1 | `build\pack.ps1 -Version 1.0.0` | tests pass; `artifacts\releases\NeoFences.App-win-Setup.exe` (~72 MB) |
| Q2 | Run Setup.exe (double-click; SmartScreen: More info → Run anyway) | installs without admin; NeoFences starts with your fences; Start menu "NeoFences"; Apps list entry |
| Q3 | Settings → About | NeoFences 1.0.0 |
| Q4 | Check `HKCU\…\Run\NeoFences` | the installed copy (`%LOCALAPPDATA%\NeoFences.App\current\NeoFences.exe`) |
| Q5 | With the installed copy set up: run a development build, exit it | the sign-in entry still points at the installed copy |
| Q6 | **[USER]** Restart Windows (or cut the power with Takeover on) | the installed NeoFences starts by itself; fences and hidden icons as before |
| Q7 | Run a newer Setup.exe over the installed one | NeoFences stops cleanly, updates, starts again; fences unchanged |
| Q8 | Apps → NeoFences → Uninstall | NeoFences stops; desktop icons visible; Start menu entry and program folder gone; sign-in entry gone; `%LOCALAPPDATA%\NeoFences` (layout, backups, logs) kept |
| Q9 | Uninstall while NeoFences had crashed (icons hidden, nothing running) | icons visible after uninstall |
| Q10 | Reinstall after Q8 | the same fences come back (data kept) |
```

- [ ] **Step 2: Run Q1–Q10.**
  - The pack and the install test cover Q1, Q2 (silent), Q4, Q5, Q8 and Q10 (data kept).
  - **[USER]**: Q2 by double-click (SmartScreen), Q3, Q6, Q7, Q9.

- [ ] **Step 3: Write `docs/research/m7-installer.md`** with:
  - the results;
  - the packId/data-folder finding;
  - "Velopack closes the app before the hook";
  - the `dotnet vpk` root finding;
  - the PowerShell 5.1 ANSI finding.

- [ ] **Step 4: Commit**

```powershell
git add docs/TEST-CHECKLIST.md docs/research/m7-installer.md
git commit -m "docs: added M7 installer checks and results"
```

---

### Task 5: Docs sync, v1.0

- [ ] **Step 1: Update docs**
  - `ARCHITECTURE.md`:
    - add `App/InstallHooks` + `build/pack.ps1`;
    - extend the startup row (`StartupPolicy` ownership rule);
    - replace "Next: M7" with "**v1.0 complete** (M7) … next: v2 backlog".
  - `FEATURES.md`: installer/uninstall rows → done (M7).
  - `ROADMAP.md`:
    - tick M7 and the "Uninstall hook restores desktop icons" line;
    - add "v1.0 released 2026-10-03 (local installer)".
  - `SETUP.md`: a "Release / install" section (`build\pack.ps1 -Version x.y.z`, `artifacts\releases`, Setup.exe, uninstall, SmartScreen) and the recovery note for a hand-deleted install.
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 2: Refresh the hub** (`node --check`, publish with `url`).

- [ ] **Step 3: Commit**

```powershell
git add docs
git commit -m "docs: synced docs for M7 and v1.0"
```

- [ ] **Step 4: After the final review and merge: ask the user**
  - whether to install v1.0 for real now (run Setup.exe from `artifacts\releases`, then stop the dev build: the installed copy takes over the sign-in entry);
  - whether to tag `v1.0.0` locally (`git tag -a v1.0.0 -m "NeoFences 1.0"`; no remote exists).
