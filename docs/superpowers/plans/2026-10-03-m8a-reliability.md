# M8a — v1.1 Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The reliability batch of the open carry-overs (ROADMAP M8, user choice 2026-10-03: 4 batches, reliability first), plus the user's report that fence corners stick out of the rounded border.

**Architecture:**
- `NeoFences.Core`:
  - `FenceMembership.Reconcile` honours safe-save and drop memory and a moved Desktop folder;
  - `DisplayFingerprint.UniqueDeviceIds`;
  - `LayoutEngine` keeps fences made on another setup;
  - `ConfigStore` separates backup failures.
- `NeoFences.Shell`:
  - Windows 11 rounded corners (`FenceWindowChrome.UseRoundedCorners`);
  - `DesktopWatcher` edge cases;
  - unique monitor ids;
  - the session-notice constant.
- `NeoFences.App`:
  - monotonic memory clock;
  - watcher before reconcile;
  - RunState quick-hide that respects user-hidden icons;
  - instant Pause show/hide;
  - tray removed on session end;
  - capped game queue;
  - unlock-notice logging;
  - uninstall hook tweaks.
- `build/pack.ps1` robustness.

**Tech Stack:** .NET 10, WPF, CsWin32 0.3.335, Serilog, xUnit, Velopack 1.2.161. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-02-neofences-v1-design.md` §5 (reconcile, layouts, saving), §6 (look), §7 (reliability). **Decisions:** ADR-006, ADR-011, ADR-016, ADR-021, ADR-023; **ADR-024** (new, Task 5).

**Pre-verified (2026-10-03):** every code block below was compiled together (0 warnings, 0 errors), and **259/259 tests pass**. On the real desktop, with the user's consent:
- 8× corner captures of the prototype showed every corner rounded. In v1.0 a square of blur stuck out at each corner.
- Region probes showed v1.0's region *was* rounded, while the blur ignored it.
- An experiment with `DwmEnableBlurBehindWindow` plus the region did not help.
- The M5 gesture smoke and the M6a tray/Pause/game-mode smoke both passed on the prototype. The only miss was the known roll-up-after-Explorer carry-over, which is M8b.
- `pack.ps1 -Version 1.x` failed instantly with the SemVer message.

## Global Constraints

- **Hard rule 2:** quick-hide never shows icons it did not hide; Pause, exit and the hook paths are unchanged (RunState, marker).
- **Hard rules 4, 5 and 7:** the DWM call goes through CsWin32 in Shell and is refused harmlessly on Windows 10. A backup failure never fails a save.
- Reconcile never guesses an ambiguous name; a same-name move needs exactly one unfenced candidate.
- Commits: single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx`.
- **NeoFences 1.0 is installed on the user's PC.** Smokes must restore the installed copy afterwards: `-RestoreExe` / `-RepoExe "$env:LOCALAPPDATA\NeoFences.App\current\NeoFences.exe"`. Ask before mouse runs, print TEST RUNNING / TEST COMPLETE, and never type into Windows Terminal.

## Review Focus

1. **A moved Desktop with partly duplicate names** (user and Public Desktop, a name in two folders). Expected: unambiguous items keep their fence; ambiguous ones go the usual way (drop + Inbox); nothing is lost or duplicated. Pinned by `ReconcileRecoveryTests`; by hand R4.
2. **Corners at every size and DPI, during the roll-up animation, on Windows 10.** Expected: no square blur on Windows 11; harmless on Windows 10. By hand R1, R2.
3. **Watcher storms with locked files** (an antivirus scan, a download being written). Expected: reconciles instead of wrong deletions, with no reconcile loop. By hand R3, R10.
4. **The quick-hide ↔ Takeover ↔ Pause ↔ user-hidden-icons matrix.** Expected: icons end the way the user left them. By hand R6, R7.
5. **A clock jump and a long uptime** (the stopwatch). Expected: memories expire after their real windows. By hand R5.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Membership/FenceMembership.cs`, `Layouts/DisplayMonitor.cs`, `Layouts/LayoutEngine.cs`, `Config/ConfigStore.cs` + tests | reconcile recovery, unique ids, layouts, backups | 1 |
| `src/NeoFences.Shell/FenceWindowChrome.cs`, `DesktopWatcher.cs`, `Monitors.cs`, `TrayIcon.cs`, `NativeMethods.txt` | corners, watcher, monitors, constant | 2 |
| `src/NeoFences.App/FenceWindow.xaml.cs`, `FenceHost.cs`, `SystemMessageWindow.cs`, `InstallHooks.cs` | wiring | 3 |
| `build/pack.ps1` | packaging robustness | 4 |
| `docs/TEST-CHECKLIST.md` (R), `docs/research/m8a-reliability.md`, `DECISIONS.md` (ADR-024), `ROADMAP.md`, `ARCHITECTURE.md`, `SESSION-LOG.md`, hub | docs | 5 |

---

### Task 1: Core — reconcile recovery, unique monitor ids, layouts, backups

**Files:**
- Modify: `docs/ROADMAP.md` (claim M8a).
- Create: `tests/NeoFences.Core.Tests/Membership/ReconcileRecoveryTests.cs`.
- Replace: `tests/NeoFences.Core.Tests/Layouts/LayoutEngineTests.cs`, `tests/NeoFences.Core.Tests/Config/ConfigStoreTests.cs`, `src/NeoFences.Core/Membership/FenceMembership.cs`, `src/NeoFences.Core/Layouts/DisplayMonitor.cs`, `src/NeoFences.Core/Layouts/LayoutEngine.cs` and `src/NeoFences.Core/Config/ConfigStore.cs`.

**Interfaces:**
- Produces:
  - `FenceMembership.Reconcile(config, desktopItems, unavailableFolders = null, remembered = null, now = null)`;
  - `DisplayFingerprint.UniqueDeviceIds(IReadOnlyList<string>)`;
  - `ConfigStore.LastBackupFailure`.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m8a-reliability
```
In `docs/ROADMAP.md` under `## M8`, replace `- [ ] M8a implementation plan` with `- [x] M8a implementation plan` and add `- [~] M8a — claimed by session 2026-10-03 m8a`.
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M8a"
```

- [ ] **Step 2: Write the failing tests**

`tests/NeoFences.Core.Tests/Membership/ReconcileRecoveryTests.cs`:
```csharp
using NeoFences.Core.Membership;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Membership;

/// <summary>Reconcile keeps arrangements through lost watcher events and a moved Desktop folder (M8a).</summary>
public class ReconcileRecoveryTests
{
    private const string Desktop = @"C:\Users\cipher\Desktop\";
    private const string OneDriveDesktop = @"C:\Users\cipher\OneDrive\Desktop\";
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<string> ItemsOf(NeoFencesConfig config, string fenceId) =>
        config.Fences.Single(fence => fence.Id == fenceId).Items;

    [Fact]
    public void ARememberedItem_ComingBackDuringAReconcile_ReturnsToItsFence()
    {
        // A safe-save (delete + create) whose events were lost: the reconcile sees the file again (M3a carry-over).
        var games = Fence.Create("Games") with { Items = [Desktop + "a.lnk"] };
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, games] };
        var report = Desktop + "report.docx";
        RememberedPlacement[] remembered = [new(report, games.Id, 0, Now + FenceMembership.SafeSaveWindow)];

        var (reconciled, result) = FenceMembership.Reconcile(config, [Desktop + "a.lnk", report], remembered: remembered, now: Now);

        Assert.Equal([report, Desktop + "a.lnk"], ItemsOf(reconciled, games.Id));
        Assert.Empty(reconciled.Inbox.Items);
        Assert.Empty(result.AddedToInbox);
    }

    [Fact]
    public void AnExpiredMemory_IsIgnored()
    {
        var games = Fence.Create("Games");
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, games] };
        RememberedPlacement[] remembered = [new(Desktop + "old.txt", games.Id, 0, Now - TimeSpan.FromSeconds(1))];

        var (reconciled, _) = FenceMembership.Reconcile(config, [Desktop + "old.txt"], remembered: remembered, now: Now);

        Assert.Equal([Desktop + "old.txt"], reconciled.Inbox.Items);
    }

    [Fact]
    public void AMovedDesktopFolder_KeepsEveryItemInItsFenceAndPlace()
    {
        // OneDrive's Known Folder Move: every path changes folder, names stay (M1 carry-over).
        var games = Fence.Create("Games") with { Items = [Desktop + "Crysis 2.lnk", Desktop + "AC Unity.lnk"] };
        var inbox = Fence.Create("Inbox") with { IsInbox = true, Items = [Desktop + "notes.txt"] };
        var config = new NeoFencesConfig { Fences = [inbox, games] };

        var (reconciled, report) = FenceMembership.Reconcile(config,
            [OneDriveDesktop + "notes.txt", OneDriveDesktop + "AC Unity.lnk", OneDriveDesktop + "Crysis 2.lnk", OneDriveDesktop + "new.txt"]);

        Assert.Equal([OneDriveDesktop + "Crysis 2.lnk", OneDriveDesktop + "AC Unity.lnk"], ItemsOf(reconciled, games.Id));
        Assert.Equal([OneDriveDesktop + "notes.txt", OneDriveDesktop + "new.txt"], reconciled.Inbox.Items);
        Assert.Empty(report.Removed);
        Assert.Equal([OneDriveDesktop + "new.txt"], report.AddedToInbox);
    }

    [Fact]
    public void ADeletedItem_WithAnUnrelatedNameElsewhere_IsStillRemoved()
    {
        var games = Fence.Create("Games") with { Items = [Desktop + "gone.lnk"] };
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, games] };

        var (reconciled, report) = FenceMembership.Reconcile(config, [Desktop + "other.lnk"]);

        Assert.Empty(ItemsOf(reconciled, games.Id));
        Assert.Equal([Desktop + "gone.lnk"], report.Removed);
    }

    [Fact]
    public void AnAmbiguousName_IsNotGuessed()
    {
        // Two candidates with the same name (user and Public Desktop): no silent guess, the usual drop + Inbox.
        var games = Fence.Create("Games") with { Items = [Desktop + "app.lnk"] };
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, games] };

        var (reconciled, _) = FenceMembership.Reconcile(config, [OneDriveDesktop + "app.lnk", @"C:\Users\Public\Desktop\app.lnk"]);

        Assert.Empty(ItemsOf(reconciled, games.Id));
        Assert.Equal(2, reconciled.Inbox.Items.Count);
    }
}
```
`tests/NeoFences.Core.Tests/Layouts/LayoutEngineTests.cs`:
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

    [Fact]
    public void AFenceMadeOnAnotherSetup_KeepsItsPlaceWhenAKnownSetupComesBack()
    {
        // Made on the laptop screen (1080p), back on the known 4K setup: adapt its last rect, do not re-place it (M1 carry-over).
        var (config, games) = ConfigWithGames();
        var fourK = DisplayFingerprint.Of([Dell4K]);
        var fullHd = DisplayFingerprint.Of([Dell1080]);
        config = config with
        {
            Layouts = new Dictionary<string, Layout>
            {
                [fourK] = new() { Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1400) }, Fences = new Dictionary<string, FenceRect> { [config.Inbox.Id] = new("DELL", 0, 0, 640, 350) } },
                [fullHd] = new()
                {
                    Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(1280, 700) },
                    Fences = new Dictionary<string, FenceRect> { [config.Inbox.Id] = new("DELL", 0, 0, 320, 175), [games.Id] = new("DELL", 640, 350, 256, 140) },
                },
            },
            LastLayoutFingerprint = fullHd,
        };

        var (resolved, layout) = LayoutEngine.Resolve(config, [Dell4K]);

        Assert.Equal(new FenceRect("DELL", 1280, 700, 512, 280), layout.Fences[games.Id]); // scaled ×2 from the 1080p layout
        Assert.Equal(new FenceRect("DELL", 0, 0, 640, 350), layout.Fences[config.Inbox.Id]); // the known rect stays exact
        Assert.Equal(new FenceRect("DELL", 1280, 700, 512, 280), resolved.Layouts[fourK].Fences[games.Id]);
    }

    [Fact]
    public void DuplicateDeviceIds_AreMadeUnique_InOrder()
    {
        // Cloned / mirrored outputs can report the same device path; a duplicate key would stop every layout (M1 carry-over).
        Assert.Equal(["DELL", "LG", "DELL#2", "DELL#3"], DisplayFingerprint.UniqueDeviceIds(["DELL", "LG", "DELL", "DELL"]));
        Assert.Equal(["A", "B"], DisplayFingerprint.UniqueDeviceIds(["A", "B"]));
    }
}
```
`tests/NeoFences.Core.Tests/Config/ConfigStoreTests.cs`:
```csharp
using NeoFences.Core.Config;
using NeoFences.Core.Model;
using NeoFences.Core.Tests.TestSupport;

namespace NeoFences.Core.Tests.Config;

public class ConfigStoreTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 30, 0, TimeSpan.Zero));

    public void Dispose() => _directory.Dispose();

    private ConfigStore NewStore() => new(_directory.Path, _clock);

    private static NeoFencesConfig ConfigTitled(string inboxTitle) =>
        NeoFencesConfig.CreateDefault() is var config ? config.WithFence(config.Inbox with { Title = inboxTitle }) : throw new InvalidOperationException();

    private string[] DailyBackupNames(ConfigStore store) =>
        Directory.GetFiles(store.BackupsDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    // ---------- Save ----------

    [Fact]
    public void Save_FirstTime_WritesConfigAndTodaysBackup_NoTempLeft()
    {
        var store = NewStore();

        Assert.True(store.Save(ConfigTitled("First")));

        Assert.Equal("First", ConfigJson.Deserialize(File.ReadAllText(store.ConfigPath)).Inbox.Title);
        Assert.Equal(["config-20261002.json"], DailyBackupNames(store));
        Assert.False(File.Exists(store.ConfigPath + ".tmp"));
        Assert.False(File.Exists(store.BackupPath));
    }

    [Fact]
    public void Save_SecondTime_KeepsPreviousVersionAsBak()
    {
        var store = NewStore();
        store.Save(ConfigTitled("First"));

        store.Save(ConfigTitled("Second"));

        Assert.Equal("Second", ConfigJson.Deserialize(File.ReadAllText(store.ConfigPath)).Inbox.Title);
        Assert.Equal("First", ConfigJson.Deserialize(File.ReadAllText(store.BackupPath)).Inbox.Title);
    }

    [Fact]
    public void Save_SameDay_DailyBackupKeepsFirstSaveOfTheDay()
    {
        var store = NewStore();
        store.Save(ConfigTitled("Morning"));
        _clock.Now = _clock.Now.AddHours(8);

        store.Save(ConfigTitled("Evening"));

        var daily = Path.Combine(store.BackupsDirectory, "config-20261002.json");
        Assert.Equal("Morning", ConfigJson.Deserialize(File.ReadAllText(daily)).Inbox.Title);
    }

    [Fact]
    public void Save_KeepsOnlyNewestTenDailyBackups()
    {
        var store = NewStore();
        for (var dayIdx = 0; dayIdx < 12; dayIdx++)
        {
            store.Save(ConfigTitled($"Day {dayIdx}"));
            _clock.Now = _clock.Now.AddDays(1);
        }

        var names = DailyBackupNames(store);
        Assert.Equal(ConfigStore.DailyBackupsKept, names.Length);
        Assert.Equal("config-20261004.json", names[0]); // days 0 and 1 pruned
        Assert.Equal("config-20261013.json", names[^1]);
    }

    [Fact]
    public void Save_WhenTempFileCannotBeWritten_Fails_AndExistingConfigIsUntouched()
    {
        var store = NewStore();
        store.Save(ConfigTitled("Good"));
        var before = File.ReadAllText(store.ConfigPath);
        Directory.CreateDirectory(store.ConfigPath + ".tmp"); // a directory where the temp file must go: the write fails

        var failure = Record.Exception(() => store.Save(ConfigTitled("Never written")));

        Assert.True(failure is IOException or UnauthorizedAccessException, $"unexpected {failure?.GetType().Name}");

        Assert.Equal(before, File.ReadAllText(store.ConfigPath));
    }

    // ---------- Load ----------

    [Fact]
    public void Load_NothingOnDisk_IsFreshDefault()
    {
        var result = NewStore().Load();

        Assert.Equal(ConfigLoadSource.Fresh, result.Source);
        Assert.Single(result.Config.Fences);
        Assert.Null(result.CorruptCopyPath);
        Assert.False(result.IsReadOnly);
    }

    [Fact]
    public void Load_ValidConfig_IsPrimary()
    {
        var store = NewStore();
        store.Save(ConfigTitled("Saved"));

        var result = NewStore().Load();

        Assert.Equal(ConfigLoadSource.Primary, result.Source);
        Assert.Equal("Saved", result.Config.Inbox.Title);
    }

    [Fact]
    public void Load_IsNormalized()
    {
        var store = NewStore();
        File.WriteAllText(store.ConfigPath, """{ "schemaVersion": 1, "fences": [] }""");

        var result = store.Load();

        Assert.Equal(ConfigLoadSource.Primary, result.Source);
        Assert.True(Assert.Single(result.Config.Fences).IsInbox);
    }

    [Fact]
    public void Load_CorruptConfig_KeepsCorruptCopy_AndFallsBackToBak()
    {
        var store = NewStore();
        store.Save(ConfigTitled("Older"));
        store.Save(ConfigTitled("Newer"));
        File.WriteAllText(store.ConfigPath, "{ broken");

        var result = NewStore().Load();

        Assert.Equal(ConfigLoadSource.Backup, result.Source);
        Assert.Equal("Older", result.Config.Inbox.Title);
        Assert.Equal(_directory.File("config.corrupt-20261002-093000.json"), result.CorruptCopyPath);
        Assert.Equal("{ broken", File.ReadAllText(result.CorruptCopyPath!));
    }

    [Fact]
    public void Load_CorruptConfigAndBak_FallsBackToNewestReadableDailyBackup()
    {
        var store = NewStore();
        store.Save(ConfigTitled("Day 1"));
        _clock.Now = _clock.Now.AddDays(1);
        store.Save(ConfigTitled("Day 2"));
        File.WriteAllText(Path.Combine(store.BackupsDirectory, "config-20261003.json"), "garbage");
        File.WriteAllText(store.ConfigPath, "garbage");
        File.WriteAllText(store.BackupPath, "garbage");

        var result = NewStore().Load();

        Assert.Equal(ConfigLoadSource.DailyBackup, result.Source);
        Assert.Equal("Day 1", result.Config.Inbox.Title);
    }

    [Fact]
    public void Load_EverythingCorrupt_IsFreshButCorruptFileIsKept()
    {
        var store = NewStore();
        File.WriteAllText(store.ConfigPath, "garbage");

        var result = store.Load();

        Assert.Equal(ConfigLoadSource.Fresh, result.Source);
        Assert.NotNull(result.CorruptCopyPath);
        Assert.True(File.Exists(result.CorruptCopyPath));
    }

    [Fact]
    public void Load_ConfigFromNewerVersion_IsReadOnly_AndSaveNeverOverwritesIt()
    {
        var store = NewStore();
        const string newerJson = """{ "schemaVersion": 99, "somethingNew": true }""";
        File.WriteAllText(store.ConfigPath, newerJson);

        var result = store.Load();
        var saved = store.Save(result.Config);

        Assert.True(result.IsReadOnly);
        Assert.Equal(ConfigLoadSource.Fresh, result.Source);
        Assert.False(saved);
        Assert.Equal(newerJson, File.ReadAllText(store.ConfigPath));
    }

    [Fact]
    public void Load_PrimaryLockedByAnotherProcess_IsReadOnly_UsesBackup_AndSaveDoesNotOverwrite()
    {
        var store = NewStore();
        store.Save(ConfigTitled("Precious"));
        var before = File.ReadAllText(store.ConfigPath);
        var lockedStore = NewStore();

        ConfigLoadResult result;
        using (new FileStream(store.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = lockedStore.Load();
        }
        var saved = lockedStore.Save(result.Config);

        Assert.True(result.IsReadOnly);
        Assert.Equal("Precious", result.Config.Inbox.Title); // from the daily backup
        Assert.False(saved);
        Assert.Equal(before, File.ReadAllText(store.ConfigPath));
    }

    [Fact]
    public void Save_FromStoreThatNeverLoaded_NeverOverwritesNewerConfig()
    {
        var store = NewStore();
        const string newerJson = """{ "schemaVersion": 99 }""";
        File.WriteAllText(store.ConfigPath, newerJson);

        Assert.False(store.Save(NeoFencesConfig.CreateDefault()));
        Assert.Equal(newerJson, File.ReadAllText(store.ConfigPath));
    }

    [Fact]
    public void Save_WhenTheDailyBackupCannotBeWritten_StillSaves_AndReportsTheBackupFailure()
    {
        // A file where the backups folder should be (or a full disk): the config itself must still save (M1 carry-over).
        var store = NewStore();
        File.WriteAllText(store.BackupsDirectory, "not a folder");

        Assert.True(store.Save(ConfigTitled("Saved")));

        Assert.Equal("Saved", ConfigJson.Deserialize(File.ReadAllText(store.ConfigPath)).Inbox.Title);
        Assert.NotNull(store.LastBackupFailure);
    }

    [Fact]
    public void Save_WithWorkingBackups_ReportsNoBackupFailure()
    {
        var store = NewStore();
        Assert.True(store.Save(ConfigTitled("Saved")));
        Assert.Null(store.LastBackupFailure);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/NeoFences.Core.Tests`
Expected: build FAILS: `CS0117 UniqueDeviceIds`, `CS1061 LastBackupFailure`, and `CS1739` (no `remembered` parameter).

- [ ] **Step 4: Implement**

`src/NeoFences.Core/Membership/FenceMembership.cs`:
```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Membership;

/// <param name="Suspicious">True when part of the desktop could not be listed (an unreadable folder, or an empty listing): the affected memberships were kept, not pruned.</param>
public sealed record ReconcileReport(IReadOnlyList<string> Removed, IReadOnlyList<string> AddedToInbox, bool Suspicious = false);

/// <summary>
/// Which desktop item lives in which fence (spec §5). Pure functions: each returns a new config.
/// Item refs compare case-insensitively (<see cref="ItemRef.Comparer"/>). Portal fences hold no items.
/// </summary>
public static class FenceMembership
{
    /// <summary>
    /// Startup reconcile: drops refs that no longer exist, adopts the shell's current spelling, keeps each
    /// item in its first fence only, and appends unknown items to the Inbox in the order given.
    /// Refs under <paramref name="unavailableFolders"/> (Desktop folders the shell layer could not list: offline
    /// redirect, unmounted OneDrive) are kept, and so is everything if the listing is empty; the report is then
    /// flagged. Everything else missing from a readable folder is dropped (M2b review: no ghosts), except:
    /// <list type="bullet">
    /// <item>a missing item whose file name now exists exactly once elsewhere, unfenced, takes that new path in place: a
    /// moved Desktop folder (OneDrive Known Folder Move) keeps every arrangement (M8a);</item>
    /// <item>an unknown item with an unexpired <paramref name="remembered"/> placement returns there instead of the Inbox:
    /// a safe-save whose watcher events were lost (M8a).</item>
    /// </list>
    /// </summary>
    public static (NeoFencesConfig Config, ReconcileReport Report) Reconcile(
        NeoFencesConfig config, IEnumerable<string> desktopItems, IReadOnlyCollection<string>? unavailableFolders = null,
        IReadOnlyList<RememberedPlacement>? remembered = null, DateTimeOffset? now = null)
    {
        var presentSpelling = new Dictionary<string, string>(ItemRef.Comparer);
        var presentOrder = new List<string>();
        foreach (var desktopItem in desktopItems)
        {
            if (presentSpelling.TryAdd(desktopItem, desktopItem)) presentOrder.Add(desktopItem);
        }

        var unlistedPrefixes = (unavailableFolders ?? []).Select(folder => folder.TrimEnd('\\') + "\\").ToList();
        bool MayStillExist(string itemRef) =>
            presentOrder.Count == 0 || unlistedPrefixes.Any(prefix => itemRef.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        var suspicious = false;

        // Present but not yet fenced, by file name: candidates for an item whose folder moved.
        var fencedPresent = config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop)
            .SelectMany(fence => fence.Items).Where(presentSpelling.ContainsKey).ToHashSet(ItemRef.Comparer);
        var unfencedByName = presentOrder
            .Where(itemRef => !fencedPresent.Contains(itemRef) && !itemRef.StartsWith("::", StringComparison.Ordinal))
            .GroupBy(itemRef => Path.GetFileName(itemRef), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var placed = new HashSet<string>(ItemRef.Comparer);
        var removed = new List<string>();
        var fences = config.Fences.Select(fence =>
        {
            if (fence.Source.Kind != FenceSourceKind.Desktop) return fence;
            var kept = new List<string>();
            foreach (var itemRef in fence.Items)
            {
                if (presentSpelling.TryGetValue(itemRef, out var currentSpelling))
                {
                    if (placed.Add(currentSpelling)) kept.Add(currentSpelling);
                }
                else if (MayStillExist(itemRef))
                {
                    suspicious = true;
                    if (placed.Add(itemRef)) kept.Add(itemRef);
                }
                else if (!itemRef.StartsWith("::", StringComparison.Ordinal)
                         && unfencedByName.TryGetValue(Path.GetFileName(itemRef), out var candidates)
                         && candidates.Count == 1 && placed.Add(candidates[0]))
                {
                    kept.Add(candidates[0]); // same name, new folder: the same item moved (an ambiguous name is never guessed)
                }
                else
                {
                    removed.Add(itemRef);
                }
            }
            return fence with { Items = kept };
        }).ToList();

        var reconciled = config with { Fences = fences };
        var live = (remembered ?? []).Where(placement => now is null || placement.ExpiresAt >= now).ToList();
        var added = new List<string>();
        foreach (var itemRef in presentOrder.Where(itemRef => !placed.Contains(itemRef)))
        {
            var memory = live.LastOrDefault(placement => ItemRef.Comparer.Equals(placement.ItemRef, itemRef));
            if (memory is not null && reconciled.Fences.FirstOrDefault(fence => fence.Id == memory.FenceId) is { Source.Kind: FenceSourceKind.Desktop } home)
            {
                var items = home.Items.ToList();
                items.Insert(Math.Clamp(memory.Index, 0, items.Count), itemRef);
                reconciled = reconciled.WithFence(home with { Items = items });
            }
            else
            {
                added.Add(itemRef);
            }
        }
        if (added.Count > 0)
        {
            reconciled = reconciled.WithFence(reconciled.Inbox with { Items = [.. reconciled.Inbox.Items, .. added] });
        }
        return (reconciled, new ReconcileReport(removed, added, suspicious));
    }

    /// <summary>Applies one watcher event: created → Inbox, deleted → removed, renamed → same fence and position.</summary>
    public static NeoFencesConfig Apply(NeoFencesConfig config, DesktopChange change) => change switch
    {
        DesktopChange.Created created => AddItem(config, created.ItemRef),
        DesktopChange.Deleted deleted => RemoveItem(config, deleted.ItemRef),
        DesktopChange.Renamed renamed => RenameItem(config, renamed.OldRef, renamed.NewRef),
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown desktop change."),
    };

    /// <summary>How long a removed item's fence and position are remembered, so a replace-save puts it back (M3a).</summary>
    public static readonly TimeSpan SafeSaveWindow = TimeSpan.FromSeconds(5);

    /// <summary>How long files dropped from Explorer are expected: Windows creates each when it starts copying it (M3b review I1).</summary>
    public static readonly TimeSpan ArrivalWindow = TimeSpan.FromMinutes(3);

    /// <summary>
    /// <see cref="Apply(NeoFencesConfig, DesktopChange)"/> that also survives "safe saves": editors replace a file by
    /// deleting (or renaming away) the original and creating (or renaming a temp file onto) the same name. A fenced item
    /// that disappears is remembered for <see cref="SafeSaveWindow"/>; if the same ref comes back in that time it returns
    /// to its fence and position instead of the Inbox. Keep the returned list and pass it to the next call.
    /// </summary>
    public static (NeoFencesConfig Config, IReadOnlyList<RememberedPlacement> Recent) Apply(
        NeoFencesConfig config, DesktopChange change, IReadOnlyList<RememberedPlacement> recent, DateTimeOffset now)
    {
        var remembered = recent.Where(placement => placement.ExpiresAt >= now).ToList();
        switch (change)
        {
            case DesktopChange.Deleted deleted when FindOwner(config, deleted.ItemRef) is { } deletedOwner:
                var index = deletedOwner.Items.ToList().FindIndex(existing => ItemRef.Comparer.Equals(existing, deleted.ItemRef));
                remembered.Add(new RememberedPlacement(deleted.ItemRef, deletedOwner.Id, index, now + SafeSaveWindow));
                return (RemoveItem(config, deleted.ItemRef), remembered);
            // A temp file renamed onto a name that just disappeared (Excel, LibreOffice, atomic writers): the temp may
            // already sit in the Inbox from its Created event; drop it and put the returning name back (M3a review I1).
            case DesktopChange.Renamed renamed
                when FindOwner(config, renamed.NewRef) is null
                     && remembered.Any(removal => ItemRef.Comparer.Equals(removal.ItemRef, renamed.NewRef)):
                return (Return(RemoveItem(config, renamed.OldRef), renamed.NewRef, remembered), remembered);
            case DesktopChange.Renamed renamed when FindOwner(config, renamed.OldRef) is { } owner:
                // Word renames the original away before renaming its temp file onto the old name: remember the old name too.
                var oldIndex = owner.Items.ToList().FindIndex(existing => ItemRef.Comparer.Equals(existing, renamed.OldRef));
                remembered.Add(new RememberedPlacement(renamed.OldRef, owner.Id, oldIndex, now + SafeSaveWindow));
                return (RenameItem(config, renamed.OldRef, renamed.NewRef), remembered);
            case DesktopChange.Created created when FindOwner(config, created.ItemRef) is null:
                return (Return(config, created.ItemRef, remembered), remembered);
            case DesktopChange.Renamed renamed when FindOwner(config, renamed.OldRef) is null && FindOwner(config, renamed.NewRef) is null:
                return (Return(config, renamed.NewRef, remembered), remembered);
            default:
                return (Apply(config, change), remembered);
        }
    }

    /// <summary>Puts an appearing item back where it was removed from moments ago, or in the Inbox. Consumes the memory.</summary>
    private static NeoFencesConfig Return(NeoFencesConfig config, string itemRef, List<RememberedPlacement> remembered)
    {
        var removal = remembered.FindLast(candidate => ItemRef.Comparer.Equals(candidate.ItemRef, itemRef));
        if (removal is null) return AddItem(config, itemRef);
        remembered.Remove(removal);
        var fence = config.Fences.FirstOrDefault(candidate => candidate.Id == removal.FenceId);
        return fence is { Source.Kind: FenceSourceKind.Desktop }
            ? MoveItem(config, itemRef, fence.Id, removal.Index)
            : AddItem(config, itemRef);
    }

    /// <summary>A new desktop item appeared: goes to <paramref name="targetFenceId"/> (a drop target) or the Inbox. No-op if already fenced.</summary>
    public static NeoFencesConfig AddItem(NeoFencesConfig config, string itemRef, string? targetFenceId = null)
    {
        if (FindOwner(config, itemRef) is not null) return config;
        var target = targetFenceId is null ? config.Inbox : RequireDesktopFence(config, targetFenceId);
        return config.WithFence(target with { Items = [.. target.Items, itemRef] });
    }

    /// <summary>A desktop item was deleted.</summary>
    public static NeoFencesConfig RemoveItem(NeoFencesConfig config, string itemRef)
    {
        var owner = FindOwner(config, itemRef);
        return owner is null
            ? config
            : config.WithFence(owner with { Items = owner.Items.Where(existing => !ItemRef.Comparer.Equals(existing, itemRef)).ToList() });
    }

    /// <summary>A desktop item was renamed (including case-only renames). Unknown old ref: treated as a new item.</summary>
    public static NeoFencesConfig RenameItem(NeoFencesConfig config, string oldRef, string newRef)
    {
        var owner = FindOwner(config, oldRef);
        if (owner is null) return AddItem(config, newRef);
        // New name already fenced (an event storm can deliver its create before the rename): keep that entry only.
        if (!ItemRef.Comparer.Equals(oldRef, newRef) && FindOwner(config, newRef) is not null) return RemoveItem(config, oldRef);
        return config.WithFence(owner with
        {
            Items = owner.Items.Select(existing => ItemRef.Comparer.Equals(existing, oldRef) ? newRef : existing).ToList(),
        });
    }

    /// <summary>
    /// Drag-drop of fence items (M3b). Moves them, in the order they had in their fences (not the selection order), to
    /// <paramref name="toFenceId"/>, before the item shown at <paramref name="insertAt"/> in the target as displayed
    /// during the drag (still including the dragged items). Past the end appends; unknown refs are ignored.
    /// </summary>
    /// <exception cref="ArgumentException">The target is unknown or a Portal fence.</exception>
    public static NeoFencesConfig MoveItems(NeoFencesConfig config, IReadOnlyList<string> itemRefs, string toFenceId, int insertAt)
    {
        var target = RequireDesktopFence(config, toFenceId);
        var moving = new HashSet<string>(itemRefs, ItemRef.Comparer);
        var ordered = config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop)
            .SelectMany(fence => fence.Items).Where(moving.Contains).ToList();
        if (ordered.Count == 0) return config;

        var displayedIndex = Math.Clamp(insertAt, 0, target.Items.Count);
        var movingBeforeDrop = target.Items.Take(displayedIndex).Count(moving.Contains);
        var withoutMoving = config with
        {
            Fences = config.Fences.Select(fence => fence.Source.Kind == FenceSourceKind.Desktop
                ? fence with { Items = fence.Items.Where(itemRef => !moving.Contains(itemRef)).ToList() }
                : fence).ToList(),
        };
        var remainingTarget = withoutMoving.Fences.First(fence => fence.Id == toFenceId);
        var items = remainingTarget.Items.ToList();
        items.InsertRange(displayedIndex - movingBeforeDrop, ordered);
        return withoutMoving.WithFence(remainingTarget with { Items = items });
    }

    /// <summary>
    /// Files were dropped onto a fence from outside (Explorer) and Windows is copying or moving them to the Desktop:
    /// when each appears within <see cref="ArrivalWindow"/> it goes to that fence at the drop position, in order.
    /// </summary>
    public static IReadOnlyList<RememberedPlacement> ExpectArrivals(
        IReadOnlyList<RememberedPlacement> recent, IReadOnlyList<string> itemRefs, string fenceId, int insertAt, DateTimeOffset now) =>
        [.. recent.Where(placement => placement.ExpiresAt >= now),
         .. itemRefs.Select((itemRef, offset) => new RememberedPlacement(itemRef, fenceId, insertAt + offset, now + ArrivalWindow))];

    /// <summary>Moves an item to another desktop fence (or reorders within one). Index is clamped; null appends.</summary>
    public static NeoFencesConfig MoveItem(NeoFencesConfig config, string itemRef, string toFenceId, int? index = null)
    {
        var target = RequireDesktopFence(config, toFenceId);
        var withoutItem = RemoveItem(config, itemRef);
        target = withoutItem.Fences.First(fence => fence.Id == toFenceId);
        var items = target.Items.ToList();
        items.Insert(Math.Clamp(index ?? items.Count, 0, items.Count), itemRef);
        return withoutItem.WithFence(target with { Items = items });
    }

    public static (NeoFencesConfig Config, Fence Fence) CreateFence(NeoFencesConfig config, string title, FenceSource? source = null)
    {
        var fence = Fence.Create(title, source);
        return (config with { Fences = [.. config.Fences, fence] }, fence);
    }

    /// <summary>A Portal: a fence that shows a folder live (M4). New Portals sort newest first (user choice 2026-10-03).</summary>
    /// <exception cref="ArgumentException">No folder given.</exception>
    public static (NeoFencesConfig Config, Fence Fence) CreatePortal(NeoFencesConfig config, string title, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentException("A Portal needs a folder.", nameof(folderPath));
        var portal = Fence.Create(title, FenceSource.Portal(folderPath)) with { Sort = FenceSort.Date };
        return (config with { Fences = [.. config.Fences, portal] }, portal);
    }

    /// <summary>Deletes a fence; its items go to the Inbox. Files are never touched (hard rule 1).</summary>
    /// <exception cref="InvalidOperationException">The fence is the Inbox.</exception>
    public static NeoFencesConfig DeleteFence(NeoFencesConfig config, string fenceId)
    {
        var fence = config.Fences.FirstOrDefault(candidate => candidate.Id == fenceId)
                    ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));
        if (fence.IsInbox) throw new InvalidOperationException("The Inbox cannot be deleted.");

        var remaining = config with { Fences = config.Fences.Where(candidate => candidate.Id != fenceId).ToList() };
        return remaining.WithFence(remaining.Inbox with { Items = [.. remaining.Inbox.Items, .. fence.Items] });
    }

    private static Fence? FindOwner(NeoFencesConfig config, string itemRef) =>
        config.Fences.FirstOrDefault(fence => fence.Items.Contains(itemRef, ItemRef.Comparer));

    private static Fence RequireDesktopFence(NeoFencesConfig config, string fenceId)
    {
        var fence = config.Fences.FirstOrDefault(candidate => candidate.Id == fenceId)
                    ?? throw new ArgumentException($"No fence with id {fenceId}.", nameof(fenceId));
        return fence.Source.Kind == FenceSourceKind.Desktop
            ? fence
            : throw new ArgumentException("Portal fences hold no items; moving into them is a file operation.", nameof(fenceId));
    }
}
```
`src/NeoFences.Core/Layouts/DisplayMonitor.cs`:
```csharp
namespace NeoFences.Core.Layouts;

/// <summary>A monitor as NeoFences.Shell reports it. Work-area sizes are in DIPs.</summary>
/// <param name="DeviceId">Stable per physical monitor (Shell derives it from the device path).</param>
public sealed record DisplayMonitor(
    string DeviceId,
    int PixelWidth,
    int PixelHeight,
    int ScalePercent,
    double WorkWidth,
    double WorkHeight,
    bool IsPrimary);

/// <summary>Identifies a display configuration: which monitors, at which resolution and scale.</summary>
public static class DisplayFingerprint
{
    /// <example>"2mon:DELL-3840x2160@150%+LG-1920x1080@100%"</example>
    public static string Of(IReadOnlyCollection<DisplayMonitor> monitors) =>
        $"{monitors.Count}mon:" + string.Join("+", monitors
            .OrderBy(monitor => monitor.DeviceId, StringComparer.Ordinal)
            .Select(monitor => $"{monitor.DeviceId}-{monitor.PixelWidth}x{monitor.PixelHeight}@{monitor.ScalePercent}%"));

    /// <summary>
    /// Device ids must be unique (they key every layout), but cloned or mirrored outputs can report the same device
    /// path: the second and later copies get "#2", "#3"… in enumeration order (M8a).
    /// </summary>
    public static IReadOnlyList<string> UniqueDeviceIds(IReadOnlyList<string> deviceIds)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        return deviceIds.Select(deviceId =>
        {
            seen[deviceId] = seen.TryGetValue(deviceId, out var count) ? count + 1 : 1;
            return seen[deviceId] == 1 ? deviceId : $"{deviceId}#{seen[deviceId]}";
        }).ToList();
    }
}
```
`src/NeoFences.Core/Layouts/LayoutEngine.cs`:
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

        // A known setup that misses fences made on another setup since (laptop vs. dock): those keep their place from
        // the last layout, scaled, instead of being placed anew (M8a).
        if (known is not null && config.LastLayoutFingerprint is { } lastSeen && lastSeen != fingerprint
            && config.Layouts.TryGetValue(lastSeen, out var lastLayout))
        {
            foreach (var (fenceId, rect) in Adapt(previous: lastLayout, areas: areas, primaryId: primary.DeviceId))
            {
                rects.TryAdd(fenceId, rect);
            }
        }

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
`src/NeoFences.Core/Config/ConfigStore.cs`:
```csharp
using System.Text.Json;
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

public enum ConfigLoadSource { Primary, Backup, DailyBackup, Fresh }

/// <param name="CorruptCopyPath">Where an unreadable config.json was preserved, if it was.</param>
/// <param name="IsReadOnly">
/// True when config.json must not be overwritten this session: it was written by a newer NeoFences, or it
/// could not be read (locked by antivirus, OneDrive or an editor). The config returned is the best fallback.
/// </param>
public sealed record ConfigLoadResult(NeoFencesConfig Config, ConfigLoadSource Source, string? CorruptCopyPath, bool IsReadOnly);

/// <summary>
/// Loads and saves <c>config.json</c> (ADR-006): atomic replace with <c>.bak</c>, one backup per day
/// (newest 10 kept), and a recovery chain for corrupt files. Debouncing saves is the caller's job.
/// </summary>
public sealed class ConfigStore(string directory, TimeProvider? timeProvider = null)
{
    public const string FileName = "config.json";
    public const int DailyBackupsKept = 10;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private bool _saveBlocked;

    public string ConfigPath => Path.Combine(directory, FileName);
    public string BackupPath => ConfigPath + ".bak";
    public string BackupsDirectory => Path.Combine(directory, "backups");
    private string TempPath => ConfigPath + ".tmp";

    public ConfigLoadResult Load()
    {
        _saveBlocked = false;
        string? corruptCopyPath = null;

        switch (TryRead(ConfigPath, out var primary))
        {
            case ReadOutcome.Ok:
                return new(ConfigNormalizer.Normalize(primary!), ConfigLoadSource.Primary, null, IsReadOnly: false);
            case ReadOutcome.NewerSchema:
                _saveBlocked = true;
                return new(NeoFencesConfig.CreateDefault(), ConfigLoadSource.Fresh, null, IsReadOnly: true);
            case ReadOutcome.Unreadable:
                // Not corrupt, just inaccessible right now: show the best fallback, but never overwrite it.
                _saveBlocked = true;
                break;
            case ReadOutcome.Corrupt:
                corruptCopyPath = Path.Combine(directory, $"config.corrupt-{_time.GetLocalNow():yyyyMMdd-HHmmss}.json");
                try
                {
                    File.Copy(ConfigPath, corruptCopyPath, overwrite: true);
                }
                catch (Exception copyFailure) when (copyFailure is IOException or UnauthorizedAccessException)
                {
                    corruptCopyPath = null;
                    _saveBlocked = true; // could not preserve it, so do not overwrite it either
                }
                break;
        }

        if (TryRead(BackupPath, out var backup) == ReadOutcome.Ok)
        {
            return new(ConfigNormalizer.Normalize(backup!), ConfigLoadSource.Backup, corruptCopyPath, IsReadOnly: _saveBlocked);
        }

        foreach (var dailyBackupPath in DailyBackupsNewestFirst())
        {
            if (TryRead(dailyBackupPath, out var daily) == ReadOutcome.Ok)
            {
                return new(ConfigNormalizer.Normalize(daily!), ConfigLoadSource.DailyBackup, corruptCopyPath, IsReadOnly: _saveBlocked);
            }
        }

        return new(NeoFencesConfig.CreateDefault(), ConfigLoadSource.Fresh, corruptCopyPath, IsReadOnly: _saveBlocked);
    }

    /// <returns>
    /// False when saving is blocked: config.json belongs to a newer NeoFences version (checked on disk on every
    /// save, so a second store or a save without a prior Load cannot overwrite it either), or Load found it
    /// unreadable.
    /// </returns>
    public bool Save(NeoFencesConfig config)
    {
        if (_saveBlocked || TryRead(ConfigPath, out _) == ReadOutcome.NewerSchema) return false;

        Directory.CreateDirectory(directory);
        var json = ConfigJson.Serialize(config);
        using (var tempFile = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(tempFile))
        {
            writer.Write(json);
            writer.Flush();
            tempFile.Flush(flushToDisk: true);
        }

        if (File.Exists(ConfigPath))
        {
            File.Replace(TempPath, ConfigPath, BackupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(TempPath, ConfigPath);
        }

        // The daily backup is a convenience: its failure (a full disk, a file in the way) must not fail the save (M8a).
        try
        {
            WriteDailyBackup(json);
            LastBackupFailure = null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            LastBackupFailure = failure;
        }
        return true;
    }

    /// <summary>Why the last save could not write or prune the daily backup (null when it could). Reported apart from Save.</summary>
    public Exception? LastBackupFailure { get; private set; }

    private void WriteDailyBackup(string json)
    {
        Directory.CreateDirectory(BackupsDirectory);
        // First save of the day wins: a config that went bad later in the day cannot overwrite it.
        var todayPath = Path.Combine(BackupsDirectory, $"config-{_time.GetLocalNow():yyyyMMdd}.json");
        if (!File.Exists(todayPath)) File.WriteAllText(todayPath, json);

        foreach (var expiredPath in DailyBackupsNewestFirst().Skip(DailyBackupsKept))
        {
            File.Delete(expiredPath);
        }
    }

    private IEnumerable<string> DailyBackupsNewestFirst() =>
        Directory.Exists(BackupsDirectory)
            ? Directory.GetFiles(BackupsDirectory, "config-*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList()
            : [];

    private enum ReadOutcome { Missing, Ok, Corrupt, NewerSchema, Unreadable }

    private const int ReadAttempts = 3;
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(100);

    private static ReadOutcome TryRead(string path, out NeoFencesConfig? config)
    {
        config = null;
        if (!File.Exists(path)) return ReadOutcome.Missing;

        string? json = null;
        for (var attempt = 1; json is null; attempt++)
        {
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception readFailure) when (readFailure is IOException or UnauthorizedAccessException)
            {
                // ponytail: blocking retry (~200 ms worst case), fine for startup; make async if Load moves off-thread.
                if (attempt == ReadAttempts) return ReadOutcome.Unreadable;
                Thread.Sleep(ReadRetryDelay);
            }
        }

        try
        {
            config = ConfigJson.Deserialize(json);
        }
        catch (JsonException)
        {
            return ReadOutcome.Corrupt;
        }
        if (config.SchemaVersion > NeoFencesConfig.CurrentSchemaVersion) return ReadOutcome.NewerSchema;
        return config.SchemaVersion < 1 ? ReadOutcome.Corrupt : ReadOutcome.Ok;
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 259`

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core tests/NeoFences.Core.Tests
git commit -m "feat: kept fence arrangements through lost events and moved desktop folders, made monitor ids unique and reported backup failures apart"
```

---

### Task 2: Shell — rounded corners by Windows, watcher edge cases, monitors

**Files:** replace `src/NeoFences.Shell/FenceWindowChrome.cs`, `DesktopWatcher.cs`, `Monitors.cs`, `TrayIcon.cs` and `NativeMethods.txt`.

**Interfaces:**
- Consumes: Task 1 (`UniqueDeviceIds`).
- Produces:
  - `FenceWindowChrome.UseRoundedCorners(nint)` (replaces `ApplyRoundedCorners`, which is removed);
  - `DesktopWatcher` raises `Overflowed` for unreadable events.

- [ ] **Step 1: Files** (stop the running NeoFences first: `& "$env:LOCALAPPDATA\NeoFences.App\current\NeoFences.exe" --exit`)

`src/NeoFences.Shell/NativeMethods.txt`:
```
// Win32 APIs used by NeoFences.Shell. CsWin32 generates bindings for each name.
AllowSetForegroundWindow
AppendMenu
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
DestroyIcon
DestroyMenu
DIB_USAGE
DISPLAY_DEVICEW
DragQueryFile
DROPEFFECT
DVASPECT
DWM_WINDOW_CORNER_PREFERENCE
DwmSetWindowAttribute
DWMWINDOWATTRIBUTE
EnumDisplayDevices
EnumDisplayMonitors
EVENT_SYSTEM_FOREGROUND
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
GetDpiForSystem
GetForegroundWindow
GetMessage
GetModuleHandle
GetMonitorInfo
GetObject
GetSystemMetrics
GetSystemMetricsForDpi
GetWindow
GetWindowLongPtr
GetWindowRect
GetWindowText
GetWindowThreadProcessId
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
LoadImage
MODIFIERKEYS_FLAGS
MONITOR_DPI_TYPE
MONITORINFOEXW
MONITORINFOF_PRIMARY
MSLLHOOKSTRUCT
NIN_SELECT
NOTIFY_FOR_THIS_SESSION
NOTIFY_ICON_MESSAGE
NOTIFYICON_VERSION_4
NOTIFYICONDATAW
POINTL
PostMessage
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
SetWinEventHook
SFGAO_FLAGS
SHCreateItemFromParsingName
SHDoDragDrop
Shell_NotifyIcon
ShellWindows
SHGetDesktopFolder
SHOW_WINDOW_CMD
SHQueryUserNotificationState
SID_STopLevelBrowser
SIGDN
SIIGBF
STGMEDIUM
SYSTEM_METRICS_INDEX
TRACK_POPUP_MENU_FLAGS
TrackPopupMenuEx
TYMED
UIA_CONTROLTYPE_ID
UnhookWinEvent
UnregisterHotKey
WINDOW_EX_STYLE
WINDOW_LONG_PTR_INDEX
WINDOW_STYLE
WindowFromPoint
WINDOWPOS
WINDOWS_HOOK_ID
WINEVENT_OUTOFCONTEXT
WM_APP
WM_CONTEXTMENU
WM_LBUTTONDOWN
WM_MOUSEMOVE
WM_NULL
WM_QUIT
WM_RBUTTONDOWN
WM_RBUTTONUP
WM_WTSSESSION_CHANGE
WTS_SESSION_UNLOCK
WTSRegisterSessionNotification
WTSUnRegisterSessionNotification
```
`src/NeoFences.Shell/FenceWindowChrome.cs`:
```csharp
using System.Runtime.InteropServices;
using NeoFences.Core.Layouts;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
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

    /// <summary>
    /// Rounded corners drawn by Windows 11 itself (DWMWA_WINDOW_CORNER_PREFERENCE), which also round the accent blur.
    /// A window region does not: Windows draws the blur over the whole rectangle, so a square of blur showed outside
    /// the rounded border (user screenshot 2026-10-03, M8a). On Windows 10 the call is refused and the fence keeps
    /// square blur corners under its rounded border (the region WindowChrome keeps still shapes clicks).
    /// </summary>
    public static unsafe void UseRoundedCorners(nint handle)
    {
        var preference = DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        PInvoke.DwmSetWindowAttribute((HWND)handle, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, &preference, sizeof(DWM_WINDOW_CORNER_PREFERENCE));
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
`src/NeoFences.Shell/DesktopWatcher.cs`:
```csharp
using NeoFences.Core.Membership;

namespace NeoFences.Shell;

/// <summary>
/// Watches the user's and the Public Desktop folders. Events arrive on thread-pool threads; marshal them yourself.
/// <see cref="Overflowed"/> means events were lost (buffer overflow, folder gone): re-enumerate and reconcile, and
/// recreate the watcher (.NET stops raising events after any error other than an overflow). It is also raised when an
/// event cannot be read reliably (a rename split across buffers, an item that cannot be checked right now).
/// Special icons (Recycle Bin, ...) are not watched; they are picked up by the next reconcile.
/// </summary>
public sealed class DesktopWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private volatile bool _disposed; // events still in flight after Dispose are dropped (the next start reconciles)

    public event Action<DesktopChange>? Changed;
    public event Action? Overflowed;

    /// <param name="log">Called for a folder that cannot be watched; the other folder is still watched.</param>
    public DesktopWatcher(Action<string, Exception> log)
    {
        foreach (var directory in new[] { DesktopItems.UserDesktop, DesktopItems.PublicDesktop }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                _watchers.Add(Watch(directory));
            }
            catch (Exception failure) when (failure is IOException or ArgumentException or UnauthorizedAccessException)
            {
                log(directory, failure);
            }
        }
    }

    private FileSystemWatcher Watch(string directory)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes,
            IncludeSubdirectories = false,
            InternalBufferSize = 64 * 1024,
        };
        watcher.Created += (_, created) =>
        {
            switch (Visibility(created.FullPath))
            {
                case true: Raise(new DesktopChange.Created(created.FullPath)); break;
                case null: LostTrack(); break; // could not look: let a reconcile decide (M8a)
            }
        };
        watcher.Deleted += (_, deleted) => Raise(new DesktopChange.Deleted(deleted.FullPath));
        watcher.Renamed += (_, renamed) =>
        {
            // A rename split across two notification buffers arrives with one name empty: the pair is lost (M8a).
            if (string.IsNullOrEmpty(renamed.Name) || string.IsNullOrEmpty(renamed.OldName))
            {
                LostTrack();
                return;
            }
            switch (Visibility(renamed.FullPath))
            {
                case true: Raise(new DesktopChange.Renamed(renamed.OldFullPath, renamed.FullPath)); break;
                case false: Raise(new DesktopChange.Deleted(renamed.OldFullPath)); break;
                default: LostTrack(); break;
            }
        };
        // Attributes changed: a file the user (or an installer) hid or unhid. Created/Deleted are no-ops if nothing changed.
        watcher.Changed += (_, changed) =>
        {
            switch (Visibility(changed.FullPath))
            {
                case true: Raise(new DesktopChange.Created(changed.FullPath)); break;
                case false: Raise(new DesktopChange.Deleted(changed.FullPath)); break;
                default: LostTrack(); break;
            }
        };
        watcher.Error += (_, _) => LostTrack();
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void Raise(DesktopChange change)
    {
        if (!_disposed) Changed?.Invoke(change);
    }

    private void LostTrack()
    {
        if (!_disposed) Overflowed?.Invoke();
    }

    /// <summary>
    /// Whether the item shows on the desktop; null when it could not be checked (a file still being written, a sharing
    /// violation): such an item used to be treated as gone (M2b review), now a reconcile decides (M8a).
    /// </summary>
    private static bool? Visibility(string path)
    {
        try
        {
            FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return entry.Exists && DesktopItems.IsVisibleOnDesktop(entry);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var watcher in _watchers) watcher.Dispose();
    }
}
```
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
        // Cloned or mirrored outputs can report the same device path; ids key every layout, so they must be unique (M8a).
        var uniqueIds = DisplayFingerprint.UniqueDeviceIds(placements.Select(placement => placement.DeviceId).ToList());
        return placements.Select((placement, index) => placement with { DeviceId = uniqueIds[index] }).ToList();
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
`src/NeoFences.Shell/TrayIcon.cs`:
```csharp
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NeoFences.Shell;

/// <summary>
/// The NeoFences icon in the notification area (spec §6, M6a). Its clicks arrive at <c>windowHandle</c> as
/// <see cref="CallbackMessage"/>. Explorer forgets every tray icon when it restarts: call <see cref="Show"/> again on
/// TaskbarCreated.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    public static readonly uint CallbackMessage = PInvoke.WM_APP + 2;
    private const uint IconId = 1;
    private const int ApplicationIconResource = 32512; // .NET's ApplicationIcon resource id, also IDI_APPLICATION
    private const uint KeySelect = PInvoke.NIN_SELECT | 1; // NIN_KEYSELECT (NINF_KEY), not in the metadata

    private readonly nint _windowHandle;
    private readonly Action<string> _log;
    private readonly HICON _icon;
    private bool _iconShared; // Windows' own icon (LR_SHARED): never destroyed by us
    private string _tooltip;

    public TrayIcon(nint windowHandle, string tooltip, Action<string> log)
    {
        _windowHandle = windowHandle;
        _tooltip = tooltip;
        _log = log;
        _icon = LoadSmallIcon();
    }

    /// <summary>
    /// Adds the icon (again: after an Explorer restart). Never throws; a missing icon only loses the tray.
    /// A busy Explorer (sign-in autostart) can answer ERROR_TIMEOUT even when it did add the icon: then the modify
    /// succeeds instead, and the version must still be set, or clicks arrive in the old format (M6a review I2).
    /// </summary>
    /// <returns>False when Explorer took neither the add nor the modify: try again shortly.</returns>
    public bool Show()
    {
        var data = Data();
        if (!PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data) && !PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data))
        {
            _log("tray: icon could not be added yet");
            return false;
        }
        data.uVersion = PInvoke.NOTIFYICON_VERSION_4; // clicks as NIN_SELECT / WM_CONTEXTMENU with the point in wParam
        return PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, in data);
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        var data = Data();
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data);
    }

    /// <summary>
    /// A click on the icon (left, right or keyboard) asks for the menu (user choice 2026-10-03: left-click opens it too).
    /// Reads a <see cref="CallbackMessage"/>: the event in lParam's low word, the screen point in wParam.
    /// </summary>
    public static bool IsMenuRequest(nint wParam, nint lParam, out int screenX, out int screenY)
    {
        var trayEvent = (uint)(lParam & 0xFFFF);
        screenX = (short)(wParam & 0xFFFF);
        screenY = (short)((wParam >> 16) & 0xFFFF);
        return trayEvent is PInvoke.NIN_SELECT or KeySelect or PInvoke.WM_CONTEXTMENU;
    }

    private unsafe NOTIFYICONDATAW Data()
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = (HWND)_windowHandle,
            uID = IconId,
            uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        var tip = _tooltip.AsSpan(0, Math.Min(_tooltip.Length, 127));
        tip.CopyTo(data.szTip.AsSpan());
        data.szTip[tip.Length] = '\0';
        return data;
    }

    /// <summary>
    /// The exe's own icon at the tray's size for the system DPI; Windows' default app icon if it has none. LoadImage, not
    /// LoadIconMetric: that one lives in comctl32 v6, which a WPF app does not load (it crashed the M6a prototype).
    /// </summary>
    private unsafe HICON LoadSmallIcon()
    {
        var size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, PInvoke.GetDpiForSystem());
        var module = PInvoke.GetModuleHandle((PCWSTR)null);
        var icon = PInvoke.LoadImage((HINSTANCE)(nint)module, (PCWSTR)(char*)ApplicationIconResource, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, 0);
        if (!icon.IsNull) return (HICON)(nint)icon;
        _log("tray: the exe has no icon; using Windows' default");
        _iconShared = true;
        return (HICON)(nint)PInvoke.LoadImage(HINSTANCE.Null, (PCWSTR)(char*)ApplicationIconResource, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, IMAGE_FLAGS.LR_SHARED);
    }

    public void Dispose()
    {
        var data = Data();
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
        if (!_icon.IsNull && !_iconShared) PInvoke.DestroyIcon(_icon);
    }
}

/// <summary>One tray-menu entry; <see cref="Id"/> 0 is a separator.</summary>
public sealed record TrayMenuItem(int Id, string Text = "", bool Checked = false, bool Enabled = true)
{
    public static readonly TrayMenuItem Separator = new(0);
}

/// <summary>The tray menu: Windows' own popup menu (it closes reliably when the user clicks elsewhere).</summary>
public static class TrayMenu
{
    /// <returns>The chosen item's id, or 0 when the menu was dismissed.</returns>
    public static unsafe int Show(nint ownerHandle, IReadOnlyList<TrayMenuItem> items, int screenX, int screenY)
    {
        var menu = PInvoke.CreatePopupMenu();
        if (menu.IsNull) return 0;
        try
        {
            foreach (var item in items)
            {
                if (item.Id == 0)
                {
                    PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (PCWSTR)null);
                    continue;
                }
                var flags = MENU_ITEM_FLAGS.MF_STRING | (item.Checked ? MENU_ITEM_FLAGS.MF_CHECKED : 0) | (item.Enabled ? 0 : MENU_ITEM_FLAGS.MF_GRAYED);
                fixed (char* text = item.Text) PInvoke.AppendMenu(menu, flags, (nuint)item.Id, text);
            }
            // The documented tray-menu dance: foreground first, so a click elsewhere closes the menu; WM_NULL after.
            PInvoke.SetForegroundWindow((HWND)ownerHandle);
            var chosen = PInvoke.TrackPopupMenuEx(menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN),
                screenX, screenY, (HWND)ownerHandle, null);
            PInvoke.PostMessage((HWND)ownerHandle, PInvoke.WM_NULL, 0, 0);
            return chosen.Value;
        }
        finally
        {
            PInvoke.DestroyMenu(menu);
        }
    }
}

/// <summary>Session lock/unlock notices (WM_WTSSESSION_CHANGE) for a window: the mouse hook is re-installed on unlock.</summary>
public static class SessionNotifications
{
    public const int WmSessionChange = (int)PInvoke.WM_WTSSESSION_CHANGE;

    public static bool IsUnlock(nint wParam) => (uint)wParam == PInvoke.WTS_SESSION_UNLOCK;

    public static bool Register(nint windowHandle) => PInvoke.WTSRegisterSessionNotification((HWND)windowHandle, PInvoke.NOTIFY_FOR_THIS_SESSION);

    public static void Unregister(nint windowHandle) => PInvoke.WTSUnRegisterSessionNotification((HWND)windowHandle);
}
```

- [ ] **Step 2: Build** — `dotnet build src/NeoFences.Shell` → 0 errors. The App does not build until Task 3, because it still calls `ApplyRoundedCorners`.

- [ ] **Step 3: Commit**

```powershell
git add src/NeoFences.Shell
git commit -m "feat: rounded fence corners with windows 11 corner preference, read unreadable watcher events as a reconcile and made monitor ids unique"
```

---

### Task 3: App — wiring, clock, quick-hide, Pause, tray, game queue, uninstall hook

**Files:** replace `src/NeoFences.App/FenceWindow.xaml.cs`, `FenceHost.cs`, `SystemMessageWindow.cs` and `InstallHooks.cs`.

**Interfaces:**
- Consumes: Tasks 1 and 2.
- Produces:
  - `FenceWindow.ShowNow()` and `HideNow()`;
  - `SystemMessageWindow.SessionNotificationsActive`.

- [ ] **Step 1: Files**

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
    private int? _moveHeightPx; // a move that interrupted a roll-up animation keeps this height (M6b review M2)
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
            // WindowChrome owns the window region and re-applies it on every resize: a radius of 0 kept resetting it to
            // a square, so the blur showed outside the rounded border (user screenshot 2026-10-03). Same radius as the border.
            CornerRadius = new CornerRadius(CornerRadiusDips),
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

    /// <summary>Shows at once (Pause, layout): a quick-hide fade still running must not hide the fence afterwards (M8a).</summary>
    public void ShowNow()
    {
        ++_fadeGeneration;
        BeginAnimation(OpacityProperty, null);
        Show();
    }

    /// <summary>Hides at once (Pause).</summary>
    public void HideNow()
    {
        ++_fadeGeneration;
        BeginAnimation(OpacityProperty, null);
        Hide();
    }

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
        FenceWindowChrome.UseRoundedCorners(Handle);
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
                // A move that starts during a roll-up animation finishes it. Windows' move loop captured the partial rect
                // and proposes it on every step, so WM_MOVING keeps the finished height too (M6b review M2).
                _moveHeightPx = null;
                if (_heightAnimation.IsEnabled)
                {
                    _heightAnimation.Stop();
                    _moveHeightPx = _heightTo;
                    FenceWindowChrome.SetPixelRect(Handle, FenceWindowChrome.GetPixelRect(Handle) with { Height = _heightTo });
                }
                _drag = new DragTracker(FenceWindowChrome.GetPixelRect(Handle));
                break;
            case WmMoving when SnapRect is not null && _drag is not null:
                var proposed = FenceWindowChrome.ReadRect(lParam);
                if (_moveHeightPx is { } heightPx) proposed = proposed with { Height = heightPx };
                FenceWindowChrome.WriteRect(lParam, _drag.Step(proposed, snap: rect => SnapRect(rect, SnapEdges.Move)));
                handled = true;
                return 1;
            case WmSizing when SnapRect is not null && _drag is not null:
                var edges = SizingEdges((int)wParam);
                FenceWindowChrome.WriteRect(lParam, _drag.Step(FenceWindowChrome.ReadRect(lParam), snap: rect => SnapRect(rect, edges)));
                handled = true;
                return 1;
            case WmExitSizeMove:
                _drag = null;
                _moveHeightPx = null;
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
    private bool _quickHideLeavesIcons;     // Takeover off and the icons were already hidden by the user
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
    private const int MaxDeferredDesktopChanges = 500;
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
        StartDesktopWatcher(); // first: an item created while the startup reconcile lists the desktop is not missed (M8a)
        ReconcileDesktop();
        ApplyLayout();
        if (_config.Settings.Takeover) SetTakeover(true);
        else if (_watchdog.IsTakeoverActiveMarked)
        {
            // Icons may still be hidden from a run whose Takeover-off never got saved (both processes killed): show them.
            Log.Warning("takeover-active marker found while Takeover is off; showing desktop icons");
            SetIconsHidden(false);
        }
        StartGestures();
        if (!_messages.SessionNotificationsActive) Log.Warning("unlock notices unavailable: the mouse hook is re-installed only after an Explorer restart");
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
    /// <summary>
    /// The clock for safe-save and drop memory: wall time at start plus a monotonic stopwatch, so a clock change (time
    /// sync, daylight saving, the user) cannot expire or extend a memory early (M8a).
    /// </summary>
    private static readonly DateTimeOffset ClockStart = DateTimeOffset.Now;
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static DateTimeOffset Now => ClockStart + Clock.Elapsed;

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
        _trayIcon?.Dispose(); // also on session end: a cancelled shutdown restarts us, and the old icon would linger (M8a)
        _trayIcon = null;
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
        var (reconciled, report) = FenceMembership.Reconcile(_config, listing.ItemRefs, listing.UnavailableFolders,
            remembered: _rememberedPlacements, now: Now);
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
            // Applied in order when the game is left; a flood (a big download unpacking) becomes one reconcile instead (M8a).
            if (_deferredDesktopChanges.Count < MaxDeferredDesktopChanges) _deferredDesktopChanges.Add(change);
            else _reconcileDeferred = true;
            return;
        }
        (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
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
                    _rememberedPlacements = FenceMembership.ExpectArrivals(_rememberedPlacements, itemRefs, window.FenceId, insertAt, Now),
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
                    window.ShowNow();
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
        var virtualKey = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            problem = $"{hotkey} has no key Windows can watch for. Pick another key."; // would say "Saved" and never fire (M6b review M4)
            return null;
        }
        var registration = new GlobalHotkey(_messages.Handle, PeekHotkeyId);
        if (registration.TryRegister(hotkey, (uint)virtualKey))
        {
            problem = "";
            Log.Information("Peek hotkey {Hotkey} registered", hotkey);
            return registration;
        }
        registration.Dispose();
        problem = $"{hotkey} is already taken by Windows or another app. Pick another combination.";
        return null;
    }

    /// <summary>Settings: a new Peek hotkey. It is kept only if Windows accepts it; otherwise the old one stays.</summary>
    private (bool Saved, string Message) SetPeekHotkey(string text)
    {
        if (!Hotkey.TryParse(text, out var parsed)) return (false, $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.");
        // A global hotkey swallows its keys in every app (M6b review I1): only safe combinations are recorded.
        if (!parsed.IsSafeToRecord) return (false, $"{parsed} would stop working everywhere else (typing, Tab, closing windows). Use Alt or Win with a key, or an F-key.");
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
            (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
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
            if (!Current.FencesVisible) window.HideNow();
            else if (!window.IsVisible)
            {
                window.ShowNow();
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
            case DesktopGesture.DoubleClick when Current.IconsHidden
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
        // Icons the user had hidden through Explorer stay theirs: quick-hide neither hides nor later shows them (M8a).
        if (hidden && !_takeoverActive) _quickHideLeavesIcons = DesktopIcons.TryIsHidden() == true;
        var iconsWereHidden = Current.IconsHidden;
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
        if (Current.IconsHidden != iconsWereHidden && !_quickHideLeavesIcons) SetIconsHidden(Current.IconsHidden); // RunState decides
        if (!hidden) _quickHideLeavesIcons = false;
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
            else if (_store.LastBackupFailure is { } backupFailure) Log.Warning(backupFailure, "config saved, but today's backup could not be written");
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
/// It also receives global hotkeys (WM_HOTKEY), the tray icon's clicks and session lock/unlock notices.
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
    /// <summary>The tray icon was clicked: show the tray menu at this screen point (px).</summary>
    public event Action<int, int>? TrayMenuRequested;
    /// <summary>The user signed back in from the lock screen (WM_WTSSESSION_CHANGE).</summary>
    public event Action? SessionUnlocked;

    /// <summary>False when Windows refused unlock notices (very early at sign-in): the hook is then not re-installed on unlock.</summary>
    public bool SessionNotificationsActive { get; }

    /// <summary>Receives the global hotkeys (Peek, M5).</summary>
    public nint Handle => _source.Handle;

    public SystemMessageWindow()
    {
        _source = new HwndSource(new HwndSourceParameters("NeoFences.SystemMessages") { WindowStyle = 0, Width = 0, Height = 0 });
        _source.AddHook(OnMessage);
        SessionNotificationsActive = SessionNotifications.Register(_source.Handle);
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case GlobalHotkey.WmHotkey:
                HotkeyPressed?.Invoke((int)wParam);
                return 0;
            case SessionNotifications.WmSessionChange when SessionNotifications.IsUnlock(wParam):
                SessionUnlocked?.Invoke();
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
        if ((uint)message == TrayIcon.CallbackMessage && TrayIcon.IsMenuRequest(wParam, lParam, out var screenX, out var screenY))
        {
            TrayMenuRequested?.Invoke(screenX, screenY);
            handled = true;
        }
        return 0;
    }

    public void Dispose()
    {
        SessionNotifications.Unregister(_source.Handle);
        _source.Dispose();
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
/// Velopack's uninstall hook (M7, ADR-023). Velopack starts the installed exe with its own arguments; <see cref="Run"/>
/// handles them and exits the process before any WPF starts. Ordinary starts return at once. The hook has a 30 s limit.
/// There is no update hook: v1.0 has no update source, and running a newer Setup.exe closes NeoFences itself and starts
/// the new copy without calling any hook (M7 review I1).
/// </summary>
public static class InstallHooks
{
    // Within Velopack's 30 s: stop ≤ 5 s (Velopack has usually closed NeoFences already) + icons ≤ 20 s + the rest.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IconRetryLimit = TimeSpan.FromSeconds(20);

    public static void Run() =>
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => OnBeforeUninstall())
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

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 259`.

- [ ] **Step 3: Live checks (ask first; print TEST RUNNING / TEST COMPLETE; restore the installed copy)**

Corners: save as `<scratchpad>\corner-zoom.ps1`:
```powershell
# Captures the four corners of the first fence window, 28x28 px each, scaled 8x (nearest neighbour), into one image.
param([string]$Out = "$PSScriptRoot\corner-zoom.png", [int]$Index = 0)
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace NfCz -Name N -MemberDefinition @'
[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
public delegate bool EnumProc(IntPtr h, IntPtr l);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
'@
[void][NfCz.N]::SetProcessDPIAware()
$fences = New-Object System.Collections.Generic.List[IntPtr]
[void][NfCz.N]::EnumWindows({ param($h, $l) if ([NfCz.N]::IsWindowVisible($h)) { $s = New-Object System.Text.StringBuilder 64; [void][NfCz.N]::GetWindowText($h, $s, 64); if ("$s" -eq 'NeoFences fence') { $fences.Add($h) } }; $true }, [IntPtr]::Zero)
$shell = New-Object -ComObject Shell.Application; $shell.MinimizeAll(); Start-Sleep -Milliseconds 1500
$r = New-Object NfCz.N+RECT; [void][NfCz.N]::GetWindowRect($fences[$Index], [ref]$r)
"fence rect: $($r.Left),$($r.Top) $($r.Right - $r.Left)x$($r.Bottom - $r.Top)"
$size = 28; $zoom = 8
$sheet = New-Object System.Drawing.Bitmap ($size * $zoom * 2 + 8), ($size * $zoom * 2 + 8)
$g = [System.Drawing.Graphics]::FromImage($sheet); $g.Clear([System.Drawing.Color]::Magenta)
$g.InterpolationMode = 'NearestNeighbor'; $g.PixelOffsetMode = 'Half'
$m = 6; $right = $r.Right - $size + $m; $bottom = $r.Bottom - $size + $m
$corners = @((,@(($r.Left - $m), ($r.Top - $m), 0, 0)) + (,@($right, ($r.Top - $m), 1, 0)) + (,@(($r.Left - $m), $bottom, 0, 1)) + (,@($right, $bottom, 1, 1)))
foreach ($corner in $corners) {
  $crop = New-Object System.Drawing.Bitmap $size, $size
  $cg = [System.Drawing.Graphics]::FromImage($crop); $cg.CopyFromScreen($corner[0], $corner[1], 0, 0, $crop.Size); $cg.Dispose()
  $g.DrawImage($crop, (New-Object System.Drawing.Rectangle ($corner[2] * ($size * $zoom + 8)), ($corner[3] * ($size * $zoom + 8)), ($size * $zoom), ($size * $zoom)))
  $crop.Dispose() }
$g.Dispose(); $sheet.Save($Out); $sheet.Dispose()
$shell.UndoMinimizeALL(); Start-Sleep -Milliseconds 800
$terminal = Get-Process WindowsTerminal -ErrorAction SilentlyContinue | Select-Object -First 1
if ($terminal) { [void](New-Object -ComObject WScript.Shell).AppActivate($terminal.Id) }
"saved $Out"
```
With the installed copy stopped and the branch build running, run it for each fence (`-Index 0`, `-Index 1`, …). Look at the PNGs: no square of blur outside any rounded corner. Then stop the branch build and start the installed copy.

Regressions, both with the installed copy restored afterwards:
- `<scratchpad>\m5-smoke-branch.ps1 -Exe <branch exe> -RepoExe "$env:LOCALAPPDATA\NeoFences.App\current\NeoFences.exe"`;
- `powershell -File <scratchpad>\m6a-smoke.ps1 -Exe <branch exe> -RestoreExe <installed exe>`.

Expected: all True, except the known roll-up-after-Explorer line (M8b).

- [ ] **Step 4: Commit**

```powershell
git add src/NeoFences.App
git commit -m "fix: started the watcher before the startup reconcile, used a monotonic memory clock, kept user-hidden icons through quick-hide, paused fences instantly, removed the tray on session end and capped the game-time queue"
```

---

### Task 4: Packaging robustness

**Files:** replace `build/pack.ps1`.

- [ ] **Step 1: File**

`build/pack.ps1`:
```powershell
# Builds the NeoFences installer (M7, ADR-023): a self-contained win-x64 publish packed by Velopack into
# artifacts\releases (NeoFences.App-win-Setup.exe, the full package, and RELEASES). Run from the repo root:
#   powershell -NoProfile -ExecutionPolicy Bypass -File build\pack.ps1 -Version 1.0.0
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
# Checked first (M8a): vpk wants SemVer2, and would otherwise fail only after the tests and the publish.
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Version '$Version' is not SemVer (like 1.1.0 or 1.1.0-beta.1)." }
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'artifacts\publish'
$releases = Join-Path $root 'artifacts\releases'
# vpk stops and asks when this version is already packed: refuse up front instead of hanging (M8a).
if (Test-Path (Join-Path $releases "NeoFences.App-$Version-full.nupkg")) {
  throw "Version $Version is already in $releases. Use a newer version, or delete that folder to repack it."
}

Push-Location $root # dotnet finds the vpk tool through the repo's dotnet-tools.json
try {
  if (Test-Path $publish) { [System.IO.Directory]::Delete($publish, $true) }
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
}
finally {
  Pop-Location # the caller's folder is unchanged, even when a step fails
}
```

- [ ] **Step 2: Check**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File build\pack.ps1 -Version 1.x`
Expected: fails at once with `Version '1.x' is not SemVer`, and the current folder is unchanged.

- [ ] **Step 3: Commit**

```powershell
git add build/pack.ps1
git commit -m "build: validated the pack version first, refused an already packed version and kept the caller's folder"
```

---

### Task 5: Verification and docs

- [ ] **Step 1: Append section R to `docs/TEST-CHECKLIST.md`**

```markdown
## R — v1.1 reliability (M8a, ADR-024)
| ID | Steps | Expected |
|---|---|---|
| R1 | Zoom into each fence corner (screenshot at 4–8×), dark and light wallpaper | no square of blur outside the rounded border; corners smooth |
| R2 | Resize, roll up / unroll, move a fence to another monitor | corners stay rounded at every size |
| R3 | Save a fenced document in Word / Excel while NeoFences restarts its watcher (or with the folder briefly locked) | the document stays in its fence |
| R4 | **[USER]** Move the Desktop folder (OneDrive → Back up Desktop, or Desktop → Properties → Location) | fences keep their items and order; nothing piles into the Inbox |
| R5 | Change the system clock by an hour, then safe-save a fenced file | it stays in its fence |
| R6 | Takeover off, icons hidden in Explorer (View → Show desktop icons off); double-click empty desktop twice | fences hide and come back; Explorer's icons stay hidden |
| R7 | Pause / resume quickly after a quick-hide | fences end visible after resume |
| R8 | **[USER]** Sign out with "Restart apps" (or a cancelled shutdown) | no second tray icon after NeoFences restarts |
| R9 | `build\pack.ps1 -Version 1.x`; then a version already in `artifacts\releases` | instant "not SemVer" / "already in releases" messages; the shell's folder is unchanged |
| R10 | Unpack a big archive (1000+ files) to the Desktop during a game | after the game, one reconcile; every file appears |
```

- [ ] **Step 2: Append ADR-024 to `docs/DECISIONS.md`**

```markdown
## ADR-024 — v1.1 reliability batch (M8a): reconcile recovery, unique monitors, rounded corners by Windows
**Date:** 2026-10-03 · **Status:** Accepted · **Refines:** ADR-006, ADR-011 (fence look), ADR-016, ADR-021

**Context.** After v1.0 the user asked for all open carry-overs, in four batches with reliability first (M8a). During
M8a the user also reported fence corners "sticking out" of the rounded border (screenshot, 2026-10-03).

**Decision.**
- **Rounded corners by Windows 11.** `DWMWA_WINDOW_CORNER_PREFERENCE = ROUND`, set once per fence. A window region does
  *not* clip the accent blur: Windows draws it over the whole rectangle, which left a square of blur outside each
  rounded corner. 8× zoom captures confirmed this, and that the region itself was rounded.
  - `WindowChrome.CornerRadius` now matches the 8-DIP border, so WPF's own region (which it re-applies on every
    resize) stays rounded for hit-testing. A radius of 0 had kept resetting it to a square.
  - NeoFences' own `SetWindowRgn` code is removed.
  - On Windows 10 the DWM call is refused: square blur corners under the rounded border, as before.
- **Reconcile recovers instead of dropping** (`FenceMembership.Reconcile`, tested):
  - An item that comes back during a reconcile with an unexpired safe-save or drop memory returns to its fence and
    position. This covers a safe-save whose watcher events were lost.
  - A fenced item whose file name now exists **exactly once**, unfenced, under another folder takes that path in place.
    A moved Desktop (OneDrive Known Folder Move) keeps every arrangement. An ambiguous name is never guessed.
- **Memory clock.** Safe-save and drop memory use wall time at start plus a monotonic stopwatch, so a clock change
  cannot expire or extend a memory.
- **Watcher.**
  - It starts before the startup reconcile.
  - A rename split across buffers (an empty name) or an item that cannot be checked right now (sharing violation)
    asks for a reconcile, instead of being treated as gone.
  - Events after `Dispose` are dropped.
- **Layouts and monitors.**
  - Duplicate monitor device ids (cloned outputs) become `id#2`, `id#3`…; a duplicate key had stopped every layout.
  - A fence missing from a known setup's layout (made on another setup since) keeps its last place, scaled.
- **Saves.** A daily-backup failure no longer fails `Save`; it is reported (`ConfigStore.LastBackupFailure`) and
  logged on its own.
- **Run state.**
  - Quick-hide decides icons through `RunState`, and leaves alone icons the user hid in Explorer (Takeover off).
  - Pause and layout show or hide fences at once, ending any running quick-hide fade.
  - The tray icon is removed on session end too.
  - A failed unlock-notice registration is logged.
  - More than 500 Desktop changes during a game become one reconcile.
- **Installer and build.**
  - The uninstall stop-wait counts only this session's processes.
  - `InstallRoot` reuses `StartupPolicy.IsInstalledExe`.
  - `pack.ps1` validates the SemVer first, refuses an already-packed version instead of hanging on vpk's prompt, and
    restores the caller's folder.

**Consequences.**
- On Windows 10 the blur corners stay square.
- A same-name file that appears unfenced elsewhere while a fenced one disappears in the same reconcile is treated as a
  move. The rule keeps the fence and position, which is what a user expects from a moved Desktop.
```

- [ ] **Step 3: Write `docs/research/m8a-reliability.md`** with:
  - the corner investigation: the region was rounded, the blur ignored it, BlurBehind+region failed, DWM preference works;
  - the live check and regression results.

- [ ] **Step 4: Update the other docs**
  - `ROADMAP.md`:
    - tick these carry-overs: watcher before reconcile; watcher rename/IO/shutdown; DeviceId uniqueness; unclamped layouts + fences from another setup; backup failures + OneDrive KFM; safe-save monotonic clock + reconcile; quick-hide vs user-hidden icons; tray on session end; RunState in quick-hide; session-notice log + constant; deferred-queue cap; Pause/ApplyLayout Show/Hide; the three M7 minors;
    - tick M8a.
  - `ARCHITECTURE.md`: the fence row notes that corners come from DWM; add an "M8a complete" line.
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 5: Refresh the hub** (`node --check`, publish with `url`).

- [ ] **Step 6: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-024, M8a checks and results, and ticked the M8a carry-overs"
```
