# M17 — Public Releases and Auto-Update Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** NeoFences updates itself from the public GitHub releases, and releases are built by GitHub Actions as drafts. Released as v1.8.0, the first public release.

**Architecture:**
- **Core:** `UpdatePolicy` (pure: when to check), `Settings.AutoUpdate`, schema 4.
- **App:** `FenceHost.Updates`: Velopack's `UpdateManager` with a `GithubSource` (or a local folder for rehearsals). It checks on a timer, downloads quietly, offers "Restart to update", and applies after the clean exit. Plus the Settings → Updates card and the tray item.
- **Build:**
  - `pack.ps1` gets `-OutputDir` and `-ReleaseNotes`;
  - `ci.yml` builds and tests;
  - `release.yml` turns a version tag into a draft release.

**Tech Stack:** .NET 10, WPF, Velopack 1.2.161 (already a dependency, ADR-023), GitHub Actions (`actions/checkout@v4`, `actions/setup-dotnet@v4`), xUnit.

**Spec:** `docs/superpowers/specs/2026-10-04-public-releases-design.md` (approved 2026-10-04). **Decision:** ADR-039 (new, Task 3).

**Already done (spec §2, with the user's explicit go):**
- the repository is public at `github.com/CipherSnowden/neo-fences`;
- the history is scrubbed (noreply address, no hub link; a scan of all commits found nothing);
- the README is in place.

**Pre-verified (2026-10-04):** every code block below was compiled together (0 warnings, 0 errors), and **438/438 tests pass** (7 new).
- **Local update rehearsal** (the user's go): 1.8.0 → Check now → "1.8.1 is ready" → Restart to update → 1.8.1 running 1.6 s later, icons restored then hidden, config intact, 0 warnings. Then 1.7.1 and the config were restored.

## Global Constraints

- **Secrets (the repo is public):**
  - never commit the personal email address, the hub link or ID, tokens or keys;
  - before any push, scan the new commits.
- **Outward-facing steps** are asked for every time: pushing to `main`, pushing a version tag, publishing a draft.
- **Hard rule 2:** an update is applied only after NeoFences' clean exit, never at sign-out or shutdown.
- **Hard rule 6:** no new NuGet. **Hard rule 7:** no update or network failure stops NeoFences.
- **Updates switched off** means no network calls at all; a developer build never updates.
- **Commits:** single line, Conventional Commits, past tense, no `Co-Authored-By` trailer, the repo's noreply email. Test command: `dotnet test NeoFences.slnx`.
- **Live checks:**
  - ask the user first; boxed TEST RUNNING / TEST COMPLETE banners;
  - restore the installed copy (silent Setup of the saved version) and `config.json`;
  - remove test-made `pre-schema-N` backups;
  - leave the Firefox PiP alone.

## Review Focus

1. **The update apply path versus hard rule 2.**
   - Scenarios: the watchdog during the apply; `ExitRequested` while Settings or a dialog is open; the session ending with an update waiting; Velopack failing to start Update.exe.
   - Expected: icons always restored; NeoFences never left closed by mistake after "Restart to update"; never an apply at sign-out.
2. **Network and timing.**
   - Scenarios: offline at start; GitHub rate limits (60 requests an hour per IP, unauthenticated); a slow download while a game starts; "Check now" while a check runs.
   - Expected: one check at a time, failures retried in 6 h, no notice storm.
3. **Switching off.**
   - Scenarios: off while downloading, or with an update already downloaded.
   - Expected: no new network calls; a downloaded update is still offered.
4. **Workflows.**
   - Scenarios: the first release (no previous release to diff); tags without a matching version; notes with no `feat:` or `fix:` commits; a failed test.
   - Expected: no published release from a red run; a draft only.
5. **The local test source.**
   - Scenarios: `NEOFENCES_UPDATE_SOURCE` set on a friend's PC by accident, pointing at a missing folder.
   - Expected: falls back to GitHub; never a crash.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Updates/UpdatePolicy.cs` (new), `Model/Settings.cs`, `Model/NeoFencesConfig.cs` + `tests/…/Updates/UpdatePolicyTests.cs` (new), `tests/…/Appearance/AppearanceTests.cs` | when to check; the setting; schema 4 | 1 |
| `src/NeoFences.App/FenceHost.Updates.cs` (new), `FenceHost.cs`, `SettingsWindow.xaml`, `SettingsWindow.xaml.cs`, `build/pack.ps1`, `.github/workflows/ci.yml`, `release.yml` (new) | the updater, Settings → Updates, tray item, packing options, workflows | 2 |
| `docs/…` (ADR-039, checklist AC, research, ROADMAP, FEATURES, ARCHITECTURE, SETUP, SESSION-LOG), hub | docs | 3 |

---

### Task 1: Core — update schedule and setting

**Files:**
- Modify: `docs/ROADMAP.md` (M17 section and claim).
- Create: `tests/NeoFences.Core.Tests/Updates/UpdatePolicyTests.cs`, `src/NeoFences.Core/Updates/UpdatePolicy.cs`.
- Replace: `src/NeoFences.Core/Model/Settings.cs`, `Model/NeoFencesConfig.cs`, `tests/NeoFences.Core.Tests/Appearance/AppearanceTests.cs`.

**Interfaces:**
- Produces:
  - `UpdateState(DateTimeOffset? LastCheck, bool LastFailed)`;
  - `UpdatePolicy.ShouldCheck(now, state, enabled, installed, gameMode)`, `UpdatePolicy.NextCheckIn(now, state)`;
  - `Settings.AutoUpdate`;
  - `NeoFencesConfig.CurrentSchemaVersion = 4`.

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m17-updates
```
At the end of `docs/ROADMAP.md` add:
```markdown

## M17 — Public releases and auto-update (v1.8, spec 2026-10-04-public-releases-design)
- [x] Repository public, history scrubbed, README (2026-10-04, with the user's go)
- [~] M17 — auto-update and release workflows (v1.8.0) — claimed by session 2026-10-04 m17
```
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M17"
```

- [ ] **Step 2: Write the failing tests**

`tests/NeoFences.Core.Tests/Updates/UpdatePolicyTests.cs`:
```csharp
using NeoFences.Core.Config;
using NeoFences.Core.Model;
using NeoFences.Core.Updates;

namespace NeoFences.Core.Tests.Updates;

/// <summary>M17 (v1.8, spec 2026-10-04-public-releases-design §4): when NeoFences looks for an update, and the setting.</summary>
public class UpdatePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 10, 0, 0, TimeSpan.FromHours(5.5));

    private static bool Check(UpdateState state, bool enabled = true, bool installed = true, bool gameMode = false) =>
        UpdatePolicy.ShouldCheck(Now, state, enabled: enabled, installed: installed, gameMode: gameMode);

    [Fact]
    public void FirstCheckAtStart_ThenOnceADay()
    {
        Assert.True(Check(new UpdateState(LastCheck: null, LastFailed: false)));
        Assert.False(Check(new UpdateState(Now.AddHours(-23), LastFailed: false)));
        Assert.True(Check(new UpdateState(Now.AddHours(-24), LastFailed: false)));
    }

    [Fact]
    public void AfterAFailure_AgainIn6Hours()
    {
        Assert.False(Check(new UpdateState(Now.AddHours(-5), LastFailed: true)));
        Assert.True(Check(new UpdateState(Now.AddHours(-6), LastFailed: true)));
    }

    [Theory]
    [InlineData(false, true, false)]  // switched off: no network at all
    [InlineData(true, false, false)]  // a developer build (not installed by Setup): never updates
    [InlineData(true, true, true)]    // a game in front: never
    public void Never_WhenOffNotInstalledOrGaming(bool enabled, bool installed, bool gameMode) =>
        Assert.False(Check(new UpdateState(LastCheck: null, LastFailed: false), enabled, installed, gameMode));

    [Fact]
    public void NextCheck_IsWhenTheScheduleSays_OrAtOnce()
    {
        Assert.Equal(TimeSpan.Zero, UpdatePolicy.NextCheckIn(Now, new UpdateState(null, false)));
        Assert.Equal(TimeSpan.FromHours(4), UpdatePolicy.NextCheckIn(Now, new UpdateState(Now.AddHours(-20), false)));
        Assert.Equal(TimeSpan.FromHours(1), UpdatePolicy.NextCheckIn(Now, new UpdateState(Now.AddHours(-5), true)));
        Assert.Equal(TimeSpan.Zero, UpdatePolicy.NextCheckIn(Now, new UpdateState(Now.AddDays(-3), false)));
    }

    [Fact]
    public void Config_AutoUpdateIsOnByDefault_SchemaFour_AndSurvivesARoundTrip()
    {
        Assert.Equal(4, NeoFencesConfig.CurrentSchemaVersion);
        Assert.True(new Settings().AutoUpdate);
        var old = ConfigNormalizer.Normalize(ConfigJson.Deserialize("""{ "schemaVersion": 3, "fences": [] }"""));
        Assert.True(old.Settings.AutoUpdate);
        Assert.Equal(4, old.SchemaVersion);
        var off = old with { Settings = old.Settings with { AutoUpdate = false } };
        Assert.False(ConfigNormalizer.Normalize(ConfigJson.Deserialize(ConfigJson.Serialize(off))).Settings.AutoUpdate);
    }
}
```

`tests/NeoFences.Core.Tests/Appearance/AppearanceTests.cs` (asserts the current schema, not 3):
```csharp
using NeoFences.Core.Appearance;
using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Appearance;

/// <summary>M14 (v1.7, spec 2026-10-04-appearance-design): the look resolver, the wallpaper accent and the config.</summary>
public class AppearanceTests
{
    private static readonly AppearanceSettings Defaults = new();
    private static readonly Fence Plain = Fence.Create("Games");
    private static readonly Argb Red = Argb.FromHex("#E84855")!.Value;

    // ---------- §3 the defaults are v1.6's look ----------

    [Fact]
    public void Defaults_GiveTheV16LookInBothTones()
    {
        var dark = FenceLook.Resolve(Defaults, Plain, light: false, wallpaperAccent: null);
        Assert.Equal(0, dark.Veil.A);                                   // Clear: no veil
        Assert.Equal(new Argb(0x40, 0xFF, 0xFF, 0xFF), dark.Border);
        Assert.Equal(new Argb(0xFF, 0xFF, 0xFF, 0xFF), dark.TitleText);
        Assert.Null(dark.TitleStrip);
        Assert.Null(dark.Bar);

        var light = FenceLook.Resolve(Defaults, Plain, light: true, wallpaperAccent: null);
        Assert.Equal(new Argb(0xB8, 0xF2, 0xF2, 0xF2), light.Veil);     // today's light veil
        Assert.Equal(new Argb(0x33, 0x00, 0x00, 0x00), light.Border);
        Assert.Equal(new Argb(0xE6, 0x00, 0x00, 0x00), light.TitleText);

        Assert.Equal(("Segoe UI", 14, TitleWeight.SemiBold, 30.0), (dark.Font.Family, dark.Font.Size, dark.Font.Weight, dark.TitleHeight));
    }

    [Theory]
    [InlineData(false, 40, 0x66)]
    [InlineData(true, 0, 0x00)]
    [InlineData(true, 85, 0xD9)]
    public void Strength_SetsTheVeilOfTheCurrentTone(bool light, int strength, byte alpha)
    {
        var appearance = light ? Defaults with { StrengthLight = strength } : Defaults with { StrengthDark = strength };
        Assert.Equal(alpha, FenceLook.Resolve(appearance, Plain, light, wallpaperAccent: null).Veil.A);
    }

    // ---------- §3.1 which colour ----------

    [Fact]
    public void Colour_CustomBeatsSwatchBeatsAccent_AndTheAccentNeedsItsSwitch()
    {
        var accent = new Argb(0xFF, 0x20, 0x90, 0x40);
        var swatch = Plain with { TabColor = TabColor.Blue };
        var custom = swatch with { CustomColor = "#E84855" };

        Assert.Equal(Red, FenceLook.Resolve(Defaults, custom, light: false, accent).Colour);
        Assert.Equal(FenceLook.Swatches[TabColor.Blue], FenceLook.Resolve(Defaults, swatch, light: false, accent).Colour);
        Assert.Null(FenceLook.Resolve(Defaults, Plain, light: false, accent).Colour);                                   // switch off
        Assert.Equal(accent, FenceLook.Resolve(Defaults with { WallpaperAccent = true }, Plain, light: false, accent).Colour);
    }

    // ---------- §3.2 the three styles ----------

    [Fact]
    public void AccentEdge_ColoursBorderBarAndTitle_KeepsTheVeil()
    {
        var look = FenceLook.Resolve(Defaults, Plain with { CustomColor = "#E84855" }, light: false, wallpaperAccent: null);
        Assert.Equal(0, look.Veil.A);
        Assert.Equal(Red, look.Border);
        Assert.Equal(Red, look.Bar);
        Assert.True(look.TitleText.R > Red.R || look.TitleText.G > Red.G); // lighter than the colour: readable on dark
        Assert.Null(look.TitleStrip);
    }

    [Fact]
    public void TintedGlass_ShowsAtClear_AndBorderTakesTheColour()
    {
        var look = FenceLook.Resolve(Defaults with { ColourStyle = ColourStyle.TintedGlass }, Plain with { CustomColor = "#E84855" }, light: false, wallpaperAccent: null);
        Assert.True(look.Veil.A >= 56);             // at least 22 %
        Assert.True(look.Veil.R > look.Veil.G);     // reddish
        Assert.Equal(Red with { A = 0xB3 }, look.Border);
        Assert.Null(look.Bar);
    }

    [Theory]
    [InlineData("#0078D4", 0xFF)] // dark blue strip: white title
    [InlineData("#FFB900", 0x00)] // yellow strip: dark title
    [InlineData("#16C60C", 0x00)] // green strip (luminance 0.41): dark reads better than white (final review I4)
    [InlineData("#00B7C3", 0x00)] // teal, same
    [InlineData("#E81123", 0xFF)] // deep red: white
    public void TitleStrip_ColoursOnlyTheTitleBar_WithReadableText(string colour, byte titleRed)
    {
        var look = FenceLook.Resolve(Defaults with { ColourStyle = ColourStyle.TitleStrip }, Plain with { CustomColor = colour }, light: false, wallpaperAccent: null);
        Assert.Equal(Argb.FromHex(colour)!.Value with { A = 0xBF }, look.TitleStrip);
        Assert.Equal(titleRed, look.TitleText.R);
        Assert.Equal(new Argb(0x40, 0xFF, 0xFF, 0xFF), look.Border);
    }

    // ---------- §3.4 fonts ----------

    [Fact]
    public void Font_OneForAllFences_AndTheTitleBarGrows() // per-fence fonts removed in v1.7.1 (ADR-038)
    {
        var appearance = Defaults with { TitleFont = new TitleFont("Bahnschrift", 20, TitleWeight.Bold) };
        var look = FenceLook.Resolve(appearance, Plain, light: false, wallpaperAccent: null);
        Assert.Equal(("Bahnschrift", 20, TitleWeight.Bold, 38.0), (look.Font.Family, look.Font.Size, look.Font.Weight, look.TitleHeight));
        Assert.Equal(26.0, FenceLook.TitleHeightFor(12));
    }

    [Fact]
    public void Font_AnOldPerFenceFontIsIgnored_AndDroppedOnSave()
    {
        var config = ConfigNormalizer.Normalize(ConfigJson.Deserialize(
            """{ "fences": [ { "id": "a", "title": "A", "isInbox": true, "titleFont": { "family": "Bahnschrift", "size": 20 } } ] }"""));
        var look = FenceLook.Resolve(config.Settings.Appearance, config.Fences[0], light: false, wallpaperAccent: null);
        Assert.Equal(("Segoe UI", 14), (look.Font.Family, look.Font.Size));
        Assert.DoesNotContain("Bahnschrift", ConfigJson.Serialize(config));
    }

    // ---------- §4 the wallpaper's colour ----------

    private static byte[] Image(params (byte R, byte G, byte B, int Count)[] runs) =>
        [.. runs.SelectMany(run => Enumerable.Repeat(new[] { run.B, run.G, run.R, (byte)255 }, run.Count).SelectMany(pixel => pixel))];

    [Fact]
    public void Accent_TheStrongestVividHueWins_DarkAndGreyPixelsAreIgnored()
    {
        // mostly near-black and grey, some vivid red, a little blue
        var accent = AccentColor.FromPixels(Image((5, 5, 8, 600), (128, 128, 128, 300), (220, 40, 50, 80), (30, 60, 220, 20)));
        Assert.NotNull(accent);
        Assert.True(accent.Value.R > 150 && accent.Value.G < 110 && accent.Value.B < 120);
        Assert.Equal(0xFF, accent.Value.A);
    }

    [Fact]
    public void Accent_AGreyImageHasNone_AndColoursAreClampedToReadableLightness()
    {
        Assert.Null(AccentColor.FromPixels(Image((10, 10, 10, 500), (200, 200, 200, 500))));
        var deep = AccentColor.FromPixels(Image((60, 0, 0, 1000)))!.Value;   // very dark red
        var lightness = (Math.Max(deep.R, Math.Max(deep.G, deep.B)) + Math.Min(deep.R, Math.Min(deep.G, deep.B))) / 2.0 / 255;
        Assert.InRange(lightness, 0.39, 0.66);

        // a muted slate (the user's WE wallpaper gave #566376): the same hue, vivid enough to read as an accent
        var muted = AccentColor.FromPixels(Image((86, 99, 118, 1000)))!.Value;
        double max = Math.Max(muted.R, Math.Max(muted.G, muted.B)) / 255.0, min = Math.Min(muted.R, Math.Min(muted.G, muted.B)) / 255.0;
        Assert.True((max - min) / (1 - Math.Abs(max + min - 1)) >= 0.44, $"{muted.ToHex()} is too grey");
        Assert.True(muted.B > muted.G && muted.G > muted.R); // still blue
    }

    // ---------- §4 Wallpaper Engine's files ----------

    [Fact]
    public void WallpaperEngine_SelectedWallpapersAndPreviewAreRead()
    {
        const string config = """
            { "cipher": { "general": { "wallpaperconfig": { "selectedwallpapers": {
                "Monitor0": { "file": "C:/Steam/steamapps/workshop/content/431960/1405736695/scene.pkg" },
                "Monitor1": { "file": "D:/WE/projects/myproject/index.html" } } } } },
              "?installdirectory": "C:/Steam" }
            """;
        var selected = WallpaperEngineFiles.SelectedWallpapers(config);
        Assert.Equal(@"C:\Steam\steamapps\workshop\content\431960\1405736695\scene.pkg", selected[0]);
        Assert.Equal(@"D:\WE\projects\myproject\index.html", selected[1]);
        Assert.Equal("preview.gif", WallpaperEngineFiles.PreviewName("""{ "title": "Kara", "preview": "preview.gif", "type": "scene" }"""));
        Assert.Equal("Kara", WallpaperEngineFiles.TitleOf("""{ "title": " Kara ", "preview": "preview.gif" }"""));
        Assert.Empty(WallpaperEngineFiles.SelectedWallpapers("{ not json"));
        Assert.Empty(WallpaperEngineFiles.SelectedWallpapers("""{ "cipher": { "general": {} } }"""));
        Assert.Null(WallpaperEngineFiles.PreviewName("""{ "preview": "../../escape.jpg" }""")); // stays inside the wallpaper's folder
    }

    // ---------- §2 config ----------

    [Fact]
    public void Config_SchemaThree_AndBrokenAppearanceIsRepaired()
    {
        const string json = """
            { "schemaVersion": 2,
              "settings": { "appearance": { "strengthDark": 300, "strengthLight": -5, "colourStyle": "neon", "titleFont": { "family": " ", "size": 13, "weight": "heavy" } } },
              "fences": [ { "id": "a", "title": "A", "isInbox": true, "customColor": "red", "titleFont": { "size": 99, "family": "Bahnschrift" } },
                          { "id": "b", "title": "B", "customColor": "#e84855" } ] }
            """;
        var config = ConfigNormalizer.Normalize(ConfigJson.Deserialize(json));

        Assert.Equal(NeoFencesConfig.CurrentSchemaVersion, config.SchemaVersion); // 3 in v1.7, 4 since v1.8 (M17)
        var appearance = config.Settings.Appearance;
        Assert.Equal((85, 0, ColourStyle.AccentEdge), (appearance.StrengthDark, appearance.StrengthLight, appearance.ColourStyle));
        Assert.Equal(new TitleFont("Segoe UI", 14, TitleWeight.SemiBold), appearance.TitleFont);
        Assert.Null(config.Fences[0].CustomColor);
        Assert.Equal("#E84855", config.Fences[1].CustomColor);
    }

    [Fact]
    public void Config_AnOldFileGetsTheDefaults_AndAppearanceRoundTrips()
    {
        var old = ConfigNormalizer.Normalize(ConfigJson.Deserialize("""{ "schemaVersion": 2, "fences": [ { "id": "a", "title": "A", "isInbox": true } ] }"""));
        Assert.Equal(new AppearanceSettings(), old.Settings.Appearance);

        var styled = old with { Settings = old.Settings with { Appearance = new AppearanceSettings { ColourStyle = ColourStyle.TitleStrip, WallpaperAccent = true, StrengthDark = 40 } } };
        var again = ConfigNormalizer.Normalize(ConfigJson.Deserialize(ConfigJson.Serialize(styled)));
        Assert.Equal(styled.Settings.Appearance, again.Settings.Appearance);
        Assert.Contains("\"colourStyle\": \"titleStrip\"", ConfigJson.Serialize(styled));
    }

    // ---------- fence menu edits ----------

    [Fact]
    public void Edits_ASwatchClearsTheCustomColour()
    {
        var config = new NeoFencesConfig { Fences = [Fence.Create("Inbox") with { IsInbox = true }, Plain] };

        var custom = FenceEdits.SetCustomColor(config, Plain.Id, "#e84855");
        Assert.Equal("#E84855", custom.Fences[1].CustomColor);
        var swatch = FenceTabs.SetColor(custom, Plain.Id, TabColor.Teal);
        Assert.Null(swatch.Fences[1].CustomColor);
        Assert.Equal(TabColor.Teal, swatch.Fences[1].TabColor);
        Assert.Same(swatch, FenceTabs.SetColor(swatch, Plain.Id, TabColor.Teal));

    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build errors: the namespace `NeoFences.Core.Updates` does not exist; `UpdateState` not found.

- [ ] **Step 4: Implement**

`src/NeoFences.Core/Updates/UpdatePolicy.cs`:
```csharp
namespace NeoFences.Core.Updates;

/// <param name="LastCheck">When NeoFences last asked for an update (this run), or null.</param>
/// <param name="LastFailed">That check failed (offline, GitHub down): the next one comes sooner.</param>
public sealed record UpdateState(DateTimeOffset? LastCheck, bool LastFailed);

/// <summary>
/// When NeoFences looks for an update (M17, spec 2026-10-04-public-releases-design §4): at start, then once a day, 6 h after
/// a failure; never while a game is in front, never when switched off (no network at all), never in a developer build.
/// </summary>
public static class UpdatePolicy
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(6);

    public static bool ShouldCheck(DateTimeOffset now, UpdateState state, bool enabled, bool installed, bool gameMode) =>
        enabled && installed && !gameMode && NextCheckIn(now, state) == TimeSpan.Zero;

    /// <summary>How long until the schedule wants the next check (zero: now).</summary>
    public static TimeSpan NextCheckIn(DateTimeOffset now, UpdateState state)
    {
        if (state.LastCheck is not { } last) return TimeSpan.Zero;
        var due = last + (state.LastFailed ? RetryAfterFailure : Interval);
        return due <= now ? TimeSpan.Zero : due - now;
    }
}
```

`src/NeoFences.Core/Model/Settings.cs`:
```csharp
namespace NeoFences.Core.Model;

public enum RollupExpand { Hover, Click }

public sealed record Settings
{
    /// <summary>Hide native desktop icons and show them in fences. Off by default until the M2 sign-out test passes (ADR-011).</summary>
    public bool Takeover { get; init; }
    /// <summary>The user answered the one-time "hide desktop icons?" banner in the Inbox (M2b first run).</summary>
    public bool TakeoverPromptAnswered { get; init; }
    public string PeekHotkey { get; init; } = "Ctrl+Alt+Space";
    public bool StartWithWindows { get; init; } = true;
    public bool GameMode { get; init; } = true;
    public RollupExpand RollupExpand { get; init; } = RollupExpand.Hover;
    /// <summary>Labels for new fences (M8b; Settings → Fences).</summary>
    public LabelMode DefaultLabels { get; init; } = LabelMode.Always;
    /// <summary>The small arrow Windows draws on shortcut icons (M8b, user choice: a setting, off by default).</summary>
    public bool ShowShortcutArrows { get; init; }
    /// <summary>Settings → Appearance (M14): background strength, colour style, wallpaper accent, title font.</summary>
    public AppearanceSettings Appearance { get; init; } = new();
    /// <summary>Download updates from GitHub by themselves (M17); off: NeoFences makes no network calls at all.</summary>
    public bool AutoUpdate { get; init; } = true;
}
```

`src/NeoFences.Core/Model/NeoFencesConfig.cs`:
```csharp
using System.Text.Json.Serialization;

namespace NeoFences.Core.Model;

/// <summary>Everything NeoFences persists, stored as <c>config.json</c> (ADR-006).</summary>
public sealed record NeoFencesConfig
{
    /// <summary>
    /// 4 since v1.8 (M17): the auto-update switch. 3 since v1.7 (M14): appearance settings and per-fence colours. 2 since v1.6 (M13a): tabs, rules and the
    /// library. An older NeoFences reads a newer number as read-only and never saves over it (it would drop the fields).
    /// </summary>
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Settings Settings { get; init; } = new();
    public IReadOnlyList<Fence> Fences { get; init; } = [];
    public IReadOnlyDictionary<string, Layout> Layouts { get; init; } = new Dictionary<string, Layout>();

    /// <summary>Rules auto-sort (M11), in order: the first matching enabled rule decides a new item's fence.</summary>
    public IReadOnlyList<Rule> Rules { get; init; } = [];

    /// <summary>Game Library settings (M12): game folders, sources, hidden games.</summary>
    public LibrarySettings Library { get; init; } = new();

    /// <summary>Fingerprint of the display configuration seen last; new configurations are derived from it.</summary>
    public string? LastLayoutFingerprint { get; init; }

    /// <summary>The single Inbox fence. Guaranteed to exist after <c>ConfigNormalizer.Normalize</c>.</summary>
    [JsonIgnore]
    public Fence Inbox => Fences.First(fence => fence.IsInbox);

    public static NeoFencesConfig CreateDefault() =>
        new() { Fences = [Fence.Create("Inbox") with { IsInbox = true }] };

    public NeoFencesConfig WithFence(Fence updated) =>
        this with { Fences = Fences.Select(fence => fence.Id == updated.Id ? updated : fence).ToList() };
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed: 438`, 0 failed.

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core tests
git commit -m "feat: added the update schedule and the auto-update setting"
```

---

### Task 2: App and build — updater, Settings → Updates, workflows

**Files:**
- Create: `src/NeoFences.App/FenceHost.Updates.cs`, `.github/workflows/ci.yml`, `.github/workflows/release.yml`.
- Replace: `src/NeoFences.App/FenceHost.cs`, `SettingsWindow.xaml`, `SettingsWindow.xaml.cs`, `build/pack.ps1`.

**Interfaces:**
- Consumes: Task 1's `UpdatePolicy`, `UpdateState` and `Settings.AutoUpdate`.
- Produces:
  - `UpdatesView(Available, AutoUpdate, Status, ReadyVersion)`;
  - `SettingsWindow.AutoUpdateChanged`, `CheckForUpdatesRequested`, `RestartToUpdateRequested`;
  - `pack.ps1 -OutputDir -ReleaseNotes`.

- [ ] **Step 1: Files**

`src/NeoFences.App/FenceHost.Updates.cs`:
```csharp
using System.IO;
using System.Windows.Threading;
using NeoFences.Core.Updates;
using NeoFences.Shell;
using Serilog;
using Velopack;
using Velopack.Sources;

namespace NeoFences.App;

/// <summary>What Settings → Updates shows (M17).</summary>
/// <param name="Available">An installed copy (not a developer build): updates can work at all.</param>
/// <param name="ReadyVersion">A downloaded update waiting for a restart, or null.</param>
public sealed record UpdatesView(bool Available, bool AutoUpdate, string Status, string? ReadyVersion);

/// <summary>
/// Auto-update (M17, spec 2026-10-04-public-releases-design §4): Velopack checks the public GitHub releases at start and once
/// a day (never during a game, never when switched off), downloads quietly, then offers "Restart to update"; an ignored
/// update is applied after the next normal exit. The update always runs after NeoFences' clean exit (hard rule 2).
/// </summary>
public sealed partial class FenceHost
{
    public const string ReleasesRepository = "https://github.com/CipherSnowden/neo-fences";
    private const int TrayRestartToUpdate = 13; // M17
    private static readonly TimeSpan UpdateTick = TimeSpan.FromHours(1);
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1); // never slows the start

    private UpdateManager? _updates;      // null: a developer build, or Velopack could not start (logged)
    private VelopackAsset? _readyUpdate;  // downloaded, waiting for a restart
    private UpdateState _updateState = new(LastCheck: null, LastFailed: false);
    private DispatcherTimer? _updateTimer;
    private bool _updateBusy, _restartAfterUpdate;
    private string _updateStatus = "";
    private readonly HashSet<string> _loggedUpdateFailures = new(StringComparer.Ordinal);

    /// <summary>
    /// The GitHub releases, or a local folder of releases from <c>NEOFENCES_UPDATE_SOURCE</c> (a rehearsal on this PC without
    /// GitHub). A copy not installed by Setup never updates.
    /// </summary>
    private void StartUpdates()
    {
        try
        {
            var local = Environment.GetEnvironmentVariable("NEOFENCES_UPDATE_SOURCE");
            IUpdateSource source = local is { Length: > 0 } && Directory.Exists(local)
                ? new SimpleFileSource(new DirectoryInfo(local))
                : new GithubSource(ReleasesRepository, accessToken: null, prerelease: false);
            var manager = new UpdateManager(source);
            if (!manager.IsInstalled)
            {
                _updateStatus = "Updates are off in a developer build.";
                return;
            }
            _updates = manager;
            _updateStatus = $"Version {manager.CurrentVersion}.";
            if (local is { Length: > 0 }) Log.Information("updates from the local folder {Folder} (rehearsal)", local);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Warning(failure, "updates unavailable"); // hard rule 7: NeoFences runs on without them
            _updateStatus = "Updates are unavailable (see the log).";
            return;
        }
        _updateTimer = new DispatcherTimer { Interval = FirstCheckDelay };
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = UpdateTick;
            CheckForUpdate(manual: false);
        };
        _updateTimer.Start();
    }

    /// <summary>The schedule (or "Check now"): look, download quietly, then offer the restart.</summary>
    private async void CheckForUpdate(bool manual)
    {
        if (_updates is not { } updates || _updateBusy || _readyUpdate is not null) return;
        if (!manual && !UpdatePolicy.ShouldCheck(DateTimeOffset.Now, _updateState, enabled: _config.Settings.AutoUpdate, installed: true, gameMode: _gameMode))
            return;
        _updateBusy = true;
        _updateStatus = "Checking for updates…";
        RefreshSettings();
        try
        {
            var found = await updates.CheckForUpdatesAsync();
            _updateState = new UpdateState(DateTimeOffset.Now, LastFailed: false);
            if (found is null)
            {
                _updateStatus = $"Up to date (version {updates.CurrentVersion}) · checked {DateTime.Now:HH:mm}.";
                return;
            }
            var version = found.TargetFullRelease.Version.ToString();
            _updateStatus = $"Downloading version {version}…";
            RefreshSettings();
            await updates.DownloadUpdatesAsync(found);
            _readyUpdate = found.TargetFullRelease;
            _updateStatus = $"Version {version} is ready. It installs when you restart NeoFences.";
            Log.Information("update {Version} downloaded; waiting for a restart", version);
            _trayIcon?.ShowBalloon("NeoFences update ready", $"Version {version} is ready. Restart to update (tray menu), or it installs when you exit NeoFences.");
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            _updateState = new UpdateState(DateTimeOffset.Now, LastFailed: true); // again in 6 h
            _updateStatus = $"Couldn't check for updates ({DateTime.Now:HH:mm}): {Reason(failure)}";
            if (_loggedUpdateFailures.Add(failure.GetType().Name)) Log.Warning(failure, "update check failed; trying again later");
        }
        finally
        {
            _updateBusy = false;
            RefreshSettings();
        }
    }

    private static string Reason(Exception failure) => failure switch
    {
        System.Net.Http.HttpRequestException => "offline, or GitHub cannot be reached.",
        TaskCanceledException or TimeoutException => "the connection timed out.",
        _ => "see the log.",
    };

    /// <summary>Tray or Settings → "Restart to update": NeoFences' normal clean exit, then Velopack installs and restarts it.</summary>
    private void RestartToUpdate()
    {
        if (_readyUpdate is null) return;
        Log.Information("restarting to update to {Version}", _readyUpdate.Version);
        _restartAfterUpdate = true;
        ExitRequested?.Invoke();
    }

    /// <summary>
    /// The last step of a clean exit (never at sign-out or shutdown): a downloaded update is installed once NeoFences has
    /// ended; after "Restart to update" the new version starts, after a normal Exit it stays closed.
    /// </summary>
    private void ApplyUpdateOnExit()
    {
        if (_updates is not { } updates || _readyUpdate is not { } ready) return;
        try
        {
            updates.WaitExitThenApplyUpdates(ready, silent: true, restart: _restartAfterUpdate, restartArgs: []);
            Log.Information("update {Version} will install now that NeoFences exits (restart: {Restart})", ready.Version, _restartAfterUpdate);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Warning(failure, "update {Version} could not be started; it is offered again next time", ready.Version);
        }
    }

    private void SetAutoUpdate(bool enabled)
    {
        _config = _config with { Settings = _config.Settings with { AutoUpdate = enabled } };
        ScheduleSave();
        if (enabled) CheckForUpdate(manual: false); // switched on: the schedule decides at once
        RefreshSettings();
    }

    /// <summary>The tray's first item while an update waits (M17).</summary>
    private IEnumerable<TrayMenuItem> UpdateTrayItems() => _readyUpdate is { } ready
        ? [new TrayMenuItem(TrayRestartToUpdate, $"Restart to update to v{ready.Version}"), TrayMenuItem.Separator]
        : [];

    private UpdatesView UpdatesView() =>
        new(_updates is not null, _config.Settings.AutoUpdate, _updateStatus, _readyUpdate?.Version.ToString());
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
public sealed partial class FenceHost
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
    private readonly ShellWorker _shellWorker = new(); // open, recycle, rename: off the UI thread, on STA (M3a review)
    private SpecialIconNotifications? _specialIcons;
    private bool _specialIconsDeferred;
    private readonly SnapshotStore _snapshots = new(Path.Combine(AppPaths.DataDirectory, "snapshots")); // M10
    private readonly HashSet<string> _loggedSnapshotProblems = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _specialIconsTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    // Rules wait until arrivals are quiet: a shortcut or file still being written reads wrong (M11 smoke X4, review M3).
    private readonly DispatcherTimer _filingTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly List<string> _pendingArrivals = [];
    // Watcher trouble arrives in bursts: one re-arm and one reconcile per burst; a watcher that fails again at once waits longer (M8a review).
    private readonly DispatcherTimer _watcherRecoveryTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _rearmWatcher;
    private DateTime _lastWatcherRearm = DateTime.MinValue;
    private TimeSpan _watcherRearmDelay = WatcherBackoff.First;
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
    private bool _iconsHiddenByUser;        // RunState.IconsHiddenByUser: set when quick-hide begins, cleared when it ends
    private DrawFenceOverlay? _drawOverlay; // right-drag on the desktop: the fence being drawn
    private DispatcherTimer? _drawTimer;
    private (int X, int Y) _drawStart;
    private GlobalHotkey? _peekHotkey;
    private GlobalHotkey? _peekEscapeHotkey; // Esc ends Peek; registered only while peeking
    private bool _peeking;
    private bool _recordingHotkey;          // Settings' hotkey box has the keyboard: the Peek hotkey is released
    private string? _peekHotkeyProblem;     // why the Peek hotkey is not registered (Settings shows it), null when it is
    private bool _cornersLogged;
    private FenceWindow? _movingWindow; // the box being moved by its title (not resized): it may merge where it is dropped (M9)
    // M6a
    private bool _paused;                    // tray: Pause NeoFences (not saved)
    private bool _gameMode;                  // a full-screen app is in front: idle (spec §4.7, ADR-021)
    private ForegroundWatcher? _foregroundWatcher;
    private TrayIcon? _trayIcon;
    private readonly List<DesktopChange> _deferredDesktopChanges = [];
    private bool _reconcileDeferred;
    private const int MaxDeferredDesktopChanges = 500;
    private const int TrayNewFence = 1, TrayQuickHide = 2, TrayPeek = 3, TrayPause = 4, TrayExit = 5, TraySettings = 6;
    // Snapshots (M10): "Restore snapshot" lists the newest few by id TrayRestoreFirst + index.
    private const int TrayTakeSnapshot = 7, TrayRestoreMenu = 8, TrayRestoreBefore = 9, TrayRestoreFirst = 100, TrayRestoreCount = 10;
    private const int TrayNewLibrary = 11; // M12
    private const int TraySnapshotsSettings = 12; // M13c: "More in Settings…" opens the Snapshots card
    private SettingsWindow? _settingsWindow; // M6b: one at a time

    public event Action? ExitRequested;

    public FenceHost()
    {
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();
        _messages.ExplorerRestarted += OnExplorerRestarted;
        _messages.DisplayChanged += OnDisplayChanged;
        _messages.ThemeChanged += OnThemeChanged;
        _messages.WallpaperChanged += () => { if (Appearance.WallpaperAccent) UpdateAccents(); }; // M14
        _messages.HotkeyPressed += OnHotkey;
        _messages.TrayMenuRequested += ShowTrayMenu;
        _messages.SessionUnlocked += OnSessionUnlocked;
        _messages.SpecialIconsChanged += ScheduleSpecialIconRefresh;
        _messages.DeviceRemovalRequested += handle =>
        {
            foreach (var (fenceId, portal) in _portals)
            {
                if (portal.ReleaseForRemoval(handle)) Log.Information("drive of Portal {FenceId} is being removed: released it", fenceId);
            }
            if (ReleaseLibraryForRemoval(handle)) Log.Information("a drive the game library watches is being removed: released it");
        };
        _specialIconsTimer.Tick += (_, _) => RefreshSpecialIcons();
        _filingTimer.Tick += (_, _) => FilePendingArrivals();
        _watcherRecoveryTimer.Tick += (_, _) => RecoverDesktopWatcher();
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
        foreach (var box in FenceTabs.Boxes(_config)) OpenWindow(box); // one window per box (M9)
        EnsurePortals();
        StartDesktopWatcher(); // first: an item created while the startup reconcile lists the desktop is not missed (M8a)
        ReconcileDesktop();
        StartSpecialIconNotifications();
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
        StartUpdates(); // M17: the first check a minute after start
        if (Appearance.WallpaperAccent) UpdateAccents(); // M14: the accent is read once at start, then on wallpaper changes
        ScheduleSave();
    }

    /// <summary>
    /// The clock for safe-save and drop memory: wall time at start plus a monotonic stopwatch, so a clock change (time
    /// sync, daylight saving, the user) cannot expire or extend a memory early (M8a).
    /// </summary>
    private static readonly DateTimeOffset ClockStart = DateTimeOffset.Now;
    private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private static DateTimeOffset Now => ClockStart + Clock.Elapsed;

    /// <summary>All of NeoFences' run-time modes together (Core rules: which fences, icons and hooks they imply).</summary>
    private RunState Current => new(Takeover: _takeoverActive, QuickHidden: _quickHidden, Paused: _paused, GameMode: _gameMode,
        IconsHiddenByUser: _iconsHiddenByUser);

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
        WatchWallpaperEngine(watch: false); // M14
        _libraryStopped = true; // also on session end: a library scan finishing now writes and re-arms nothing (M13a review)
        ApplyDeferredShellWork(); // Desktop changes queued during a game must be saved too (M6a review M2)
        SaveNow();
        _trayIcon?.Dispose(); // also on session end: a cancelled shutdown restarts us, and the old icon would linger (M8a)
        _trayIcon = null;
        if (_sessionEnding) return; // OnSessionEnding already restored and marked; no clean marker, so a cancel restarts us
        // The marker also catches a show that failed earlier (quick-hide ending while Explorer was busy; M5 review M6).
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) SetIconsHidden(false);
        TryMarker(() => _watchdog.MarkCleanShutdown(Environment.ProcessId), what: "clean-shutdown marker");
        _foregroundWatcher?.Dispose();
        _mouseHook?.Dispose();
        _peekHotkey?.Dispose();
        _peekEscapeHotkey?.Dispose();
        _desktopWatcher?.Dispose();
        _desktopWatcher = null;
        _specialIcons?.Dispose();
        StopLibraryWatchers();
        _libraryTimer?.Stop();
        _shellWorker.Dispose();
        _iconLoader.Dispose();
        _messages.Dispose();
        _updateTimer?.Stop();
        ApplyUpdateOnExit(); // M17: last, after the icons are back and the watchdog was told (never at session end: returned above)
    }

    /// <summary>Best effort from the crash handler; the watchdog restores too.</summary>
    public void EmergencyRestoreIcons()
    {
        if (Current.IconsHidden || _watchdog.IsTakeoverActiveMarked) DesktopIcons.TrySetHidden(false);
    }

    /// <summary>One window per box (M9): it shows the box's active tab; roll-up and lock are the box's.</summary>
    private void OpenWindow(Fence box)
    {
        var shown = FenceTabs.ActiveOf(_config, box.Id);
        var window = new FenceWindow(shown with { RolledUp = box.RolledUp, Locked = box.Locked }, takeoverActive: _takeoverActive,
            lightTheme: _lightTheme, iconLoader: _iconLoader, rollupExpand: _config.Settings.RollupExpand)
        {
            // Spec §6: no animations when Windows' "Animation effects" are off; spec §4.7: none while gaming.
            AnimationsAllowed = () => !_gameMode && SystemParameters.ClientAreaAnimation,
            BoxId = box.Id,
        };
        window.TabSelected += fenceId => SwitchTab(window, fenceId);
        window.TabDropped += (fenceId, screenX, screenY) => OnTabDropped(window, fenceId, screenX, screenY);
        window.TabDragMoved += (screenX, screenY) =>
        {
            foreach (var other in _windows.Values) other.SetMergeHighlight(other != window && other.TitleRowContains(screenX, screenY));
        };
        window.TabColorRequested += color =>
        {
            _config = FenceTabs.SetColor(_config, window.FenceId, color);
            RefreshTabs(window);
            ScheduleSave();
        };
        window.CustomColorRequested += () => PickCustomColour(window); // M14
        window.DetachTabRequested += () => DetachTab(window, window.FenceId, dropPoint: null);
        window.TabCycleRequested += step => CycleTab(window, step);
        window.SetTabs(FenceTabs.TabsOf(_config, box.Id), shown.Id);
        ApplyStyle(window); // M14
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
        window.OpenManyRequested += itemRefs =>
        {
            foreach (var itemRef in itemRefs) OpenItem(itemRef, ownerHandle: window.Handle); // folders open in Explorer
            SetPeek(false);
        };
        window.ItemMenuRequested += (itemRefs, screenX, screenY, fromKeyboard) => ShowItemMenu(window, itemRefs, screenX, screenY, fromKeyboard);
        window.RecycleRequested += itemRefs => RecycleItems(window, itemRefs);
        window.ItemRenameRequested += (itemRef, newName) => RenameItem(window, itemRef, newName);
        window.BackRequested += () => BrowsePortal(window, back: true);
        window.NewPortalRequested += () => CreatePortal(window);
        window.NewLibraryRequested += CreateLibraryFence;
        window.RefreshLibraryRequested += () => ScanLibrary(full: true);
        window.StartupToggled += SetStartWithWindows;
        window.SettingsRequested += OpenSettings;
        window.RulesRequested += () => OpenRulesFor(window.FenceId);
        window.LabelModeRequested += labels => SetFenceLabels(window, labels);
        window.SetShortcutArrows(_config.Settings.ShowShortcutArrows);
        window.SetStartupChecked(_config.Settings.StartWithWindows);
        window.SortRequested += sort => SortFence(window, sort);
        window.OpenFolderRequested += () => { if (_portals.TryGetValue(window.FenceId, out var portal)) OpenItem(portal.Current, ownerHandle: window.Handle); };
        window.DragRequested += itemRefs =>
            ShellDragDrop.TryDrag(window.Handle, itemRefs, logFailure: failure => Log.Warning(failure, "could not start dragging {ItemRefs}", itemRefs),
                copyOnly: window.IsLibrary); // a game dragged out of the library is copied, never moved (M12)
        window.TakeoverPromptAnswered += AnswerTakeoverPrompt;
        window.RollUpToggled += () => ToggleRollUp(window);
        new WindowInteropHelper(window).EnsureHandle(); // HWND exists (styles, blur) before the first Show
        if (window.CornersUnavailable && !_cornersLogged)
        {
            _cornersLogged = true;
            Log.Information("Windows refused rounded corners (Windows 10?): fences keep square blur corners"); // M8a review
        }
        RegisterDrops(window); // needs the HWND
        if (!DesktopHost.AttachToDesktop(window.Handle)) Log.Warning("fence {FenceId}: not attached to the desktop yet (no Progman)", box.Id);
        _windows[box.Id] = window;
        // A Portal tab that gets a window of its own (detached, or its host deleted) lists its folder now (final review I1).
        if (_portals.TryGetValue(shown.Id, out var shownPortal)) shownPortal.Refresh();
    }

    /// <summary>Full pass over the Desktop folders: at start and whenever watcher events were lost.</summary>
    private void ReconcileDesktop()
    {
        var listing = DesktopItems.Enumerate();
        var (reconciled, report) = FenceMembership.Reconcile(_config, listing.ItemRefs, listing.UnavailableFolders,
            remembered: _rememberedPlacements, now: Now);
        _config = reconciled;
        _rememberedPlacements = [.. _rememberedPlacements.Except(report.UsedMemories)]; // each memory places one item once (M8a review)
        if (listing.UnavailableFolders.Count > 0) Log.Warning("desktop folders not readable: {Folders}", listing.UnavailableFolders);
        if (report.Suspicious) Log.Warning("kept fenced items from an unreadable or empty desktop listing until a later reconcile");
        Log.Information("desktop reconciled: {AddedCount} added to the Inbox, {RemovedCount} removed", report.AddedToInbox.Count, report.Removed.Count);
        RefreshWindows();
        ScheduleSave();
        FileNewItems(report.AddedToInbox);
        OnDesktopShortcutsChanged([.. report.Removed, .. report.AddedToInbox]); // a game-mode flood, lost watcher events, start (M16)
    }

    private void StartDesktopWatcher()
    {
        // A folder that cannot be watched degrades to "its changes show after a restart" (hard rule 7).
        _desktopWatcher = new DesktopWatcher((folder, failure) => Log.Error(failure, "cannot watch {Folder}; its changes show after a restart", folder));
        var dispatcher = Dispatcher.CurrentDispatcher;
        _desktopWatcher.Changed += change => dispatcher.BeginInvoke(() => OnDesktopChanged(change));
        _desktopWatcher.Overflowed += () => dispatcher.BeginInvoke(() => OnDesktopWatcherTrouble(rearm: true));
        _desktopWatcher.ReconcileNeeded += () => dispatcher.BeginInvoke(() => OnDesktopWatcherTrouble(rearm: false));
    }

    /// <summary>
    /// Events were lost or the watcher stopped (re-arm: .NET disables it after a non-overflow error), or one event could
    /// not be read (reconcile only). Bursts are gathered into one recovery (M8a review).
    /// </summary>
    private void OnDesktopWatcherTrouble(bool rearm)
    {
        if (_desktopWatcher is null) return; // shut down meanwhile
        if (rearm && !_rearmWatcher)
        {
            _rearmWatcher = true;
            // Measured when the failure arrives: the wait itself is not quiet time (Core WatcherBackoff, M8c review I3).
            _watcherRearmDelay = WatcherBackoff.Next(_watcherRearmDelay, lastRearm: _lastWatcherRearm, failureAt: DateTime.UtcNow);
            _watcherRecoveryTimer.Stop(); // a reconcile-only recovery already waiting now waits for the re-arm delay
            _watcherRecoveryTimer.Interval = _watcherRearmDelay;
            _watcherRecoveryTimer.Start();
            return;
        }
        if (_watcherRecoveryTimer.IsEnabled) return;
        _watcherRecoveryTimer.Interval = WatcherBackoff.First;
        _watcherRecoveryTimer.Start();
    }

    private void RecoverDesktopWatcher()
    {
        _watcherRecoveryTimer.Stop();
        if (_desktopWatcher is null) return;
        if (_rearmWatcher)
        {
            _rearmWatcher = false;
            _lastWatcherRearm = DateTime.UtcNow;
            Log.Warning("desktop watcher lost events or stopped; re-armed after {Delay} and reconciling", _watcherRearmDelay);
            _desktopWatcher.Dispose();
            StartDesktopWatcher();
        }
        else
        {
            Log.Information("a desktop change could not be read; reconciling");
        }
        if (Current.ShellWorkDeferred) _reconcileDeferred = true; // after the game
        else ReconcileDesktop();
    }

    /// <summary>Also after an Explorer restart: Explorer brokers the shell's change notices and forgets them (M8c review I6).</summary>
    private void StartSpecialIconNotifications()
    {
        _specialIcons?.Dispose();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _specialIcons = new SpecialIconNotifications(_messages.Handle,
            settingsChanged: () => dispatcher.BeginInvoke(ScheduleSpecialIconRefresh),
            log: (what, failure) => Log.Warning(failure, "{What} unavailable: special icons change after a restart", what));
    }

    private void ScheduleSpecialIconRefresh()
    {
        _specialIconsTimer.Stop(); // a burst (several icons, a Recycle Bin emptying) is one refresh
        _specialIconsTimer.Start();
    }

    /// <summary>"Desktop icon settings" changed (reconcile), or the Recycle Bin turned full or empty (new icon) (M8c).</summary>
    private void RefreshSpecialIcons()
    {
        _specialIconsTimer.Stop();
        if (Current.ShellWorkDeferred)
        {
            _specialIconsDeferred = true; // games get every bit of the machine: after the game (M8c review)
            return;
        }
        var shown = DesktopItems.SpecialIconRefs().ToHashSet(ItemRef.Comparer);
        var fenced = _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).SelectMany(fence => fence.Items)
            .Where(itemRef => itemRef.StartsWith("::", StringComparison.Ordinal)).ToHashSet(ItemRef.Comparer);
        if (!shown.SetEquals(fenced))
        {
            Log.Information("desktop icon settings changed; reconciling");
            ReconcileDesktop(); // game mode returned early above: no deferral needed here (M13c, dead branch removed)
        }
        foreach (var window in _windows.Values) window.ReloadSpecialIcons();
        Log.Information("special icons refreshed");
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
        var arrival = ArrivalOf(change); // before Apply: was the item fenced already?
        (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
        RefreshWindows();
        ScheduleSave();
        if (arrival is not null) FileNewItems([arrival]);
        OnDesktopShortcutChange(change);
    }

    /// <summary>
    /// The item a change brings that rules may file (M11): a created item no fence holds yet (an attribute change on an
    /// existing item also arrives as "created", final review I1), or a download that just got its final name (I2).
    /// </summary>
    private string? ArrivalOf(DesktopChange change) => change switch
    {
        DesktopChange.Created created when !_config.Fences.Any(fence => fence.Items.Contains(created.ItemRef, ItemRef.Comparer)) => created.ItemRef,
        DesktopChange.Renamed renamed when Rules.IsDownloadRename(renamed.OldRef) => renamed.NewRef,
        _ => null,
    };

    /// <summary>
    /// Rules auto-sort (M11, spec §3): new items that landed in the Inbox go to the fence of the first rule they match.
    /// Their facts are read on the shell worker (a shortcut to an offline share can be slow); an item moved or deleted
    /// meanwhile stays put. Membership only: no file is touched.
    /// </summary>
    private void FileNewItems(IReadOnlyList<string> itemRefs)
    {
        if (itemRefs.Count == 0 || !_config.Rules.Any(rule => rule.Enabled)) return;
        _pendingArrivals.AddRange(itemRefs);
        _filingTimer.Stop(); // a burst (an unpack, an installer) is one read, 1.5 s after the last arrival
        _filingTimer.Start();
    }

    private void FilePendingArrivals()
    {
        _filingTimer.Stop();
        var inbox = _config.Inbox.Items.ToHashSet(ItemRef.Comparer);
        var arrivals = _pendingArrivals.Distinct(ItemRef.Comparer).Where(inbox.Contains).ToList(); // a safe-save memory or a drop placed it already
        _pendingArrivals.Clear();
        if (arrivals.Count == 0) return;
        ReadFactsThen(arrivals, facts =>
        {
            var stillInInbox = _config.Inbox.Items.ToHashSet(ItemRef.Comparer);
            var filed = Rules.File(_config, [.. facts.Where(fact => stillInInbox.Contains(fact.ItemRef))], DateTimeOffset.Now);
            if (ReferenceEquals(filed, _config)) return;
            var moved = _config.Inbox.Items.Except(filed.Inbox.Items, ItemRef.Comparer).ToList();
            Log.Information("rules filed {Count} new item(s): {ItemRefs}", moved.Count, moved);
            _config = filed;
            RefreshWindows();
            ScheduleSave();
        });
    }

    /// <summary>Reads item facts on the shell worker, then continues on the UI thread.</summary>
    private void ReadFactsThen(IReadOnlyList<string> itemRefs, Action<IReadOnlyList<ItemFacts>> then)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        _shellWorker.Run(() =>
        {
            var facts = ItemFactsReader.Read(itemRefs, logFailure: (itemRef, failure) => Log.Warning(failure, "rules: {ItemRef} could not be read", itemRef));
            dispatcher.BeginInvoke(() => then(facts));
        });
    }

    /// <summary>
    /// Settings → "Apply rules now" (M11, spec §3): every desktop item, hand-placed ones too. "Before restore" is written
    /// first, so tray → "Undo the last restore" puts everything back; nothing moves without it.
    /// </summary>
    private void ApplyRulesNow()
    {
        var itemRefs = _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).SelectMany(fence => fence.Items).ToList();
        ReadFactsThen(itemRefs, facts =>
        {
            var now = DateTimeOffset.Now;
            var moves = Rules.CountMoves(_config, facts, now);
            if (moves == 0)
            {
                _settingsWindow?.ShowRulesResult("Nothing to move: every item is where the rules want it.");
                return;
            }
            if (_snapshots.Save(Snapshots.Take(_config, name: $"Before applying rules ({now:d MMM HH:mm})", now: now), SnapshotStore.BeforeRestoreFileName) is null)
            {
                Log.Warning(_snapshots.LastFailure, "rules not applied: 'Before restore' could not be saved");
                _settingsWindow?.ShowRulesResult("Nothing moved: NeoFences could not save 'Before restore' first (see the log).");
                return;
            }
            _config = Rules.File(_config, facts, now);
            Log.Information("rules applied: {Count} item(s) moved", moves);
            SaveNow();
            RefreshWindows();
            RefreshSettings();
            var message = $"{moves} item{(moves == 1 ? "" : "s")} moved. Tray → Restore snapshot → Undo the last restore puts them back.";
            _settingsWindow?.ShowRulesResult(message);
            _trayIcon?.ShowBalloon("Rules applied", message);
        });
    }

    private void RefreshWindows()
    {
        var showPrompt = !_config.Settings.TakeoverPromptAnswered && !_takeoverActive;
        foreach (var window in _windows.Values)
        {
            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } shown) continue;
            if (!_portals.ContainsKey(shown.Id)) window.SetItems(shown.Items); // Portals refresh on their own (M4 review I1)
            window.ShowTakeoverPrompt(showPrompt && shown.IsInbox);
        }
    }

    private void OpenItem(string itemRef, nint ownerHandle)
    {
        // Off the UI thread: ShellExecute can block on a network timeout or a UAC prompt, freezing every fence (M2b review I6);
        // on an STA thread, as shell handlers and Windows' error dialog expect (M3a review).
        // Each open on its own thread: one waiting on an offline share (and its error dialog) holds up nothing else (M8c review I4).
        ShellWorker.RunAlone(() =>
        {
            if (!ShellItems.TryOpen(itemRef, ownerHandle)) Log.Warning("could not open {ItemRef}", itemRef);
        }, name: "NeoFences open");
    }

    /// <summary>Makes the fence accept drops (M3b): fence items and Desktop files move membership; other files go to Windows.</summary>
    private void RegisterDrops(FenceWindow window)
    {
        try
        {
            _dropRegistrations[window.BoxId] = ShellDragDrop.RegisterFence(window.Handle, new FenceDropHandlers(
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
                LogFailure: failure => Log.Warning(failure, "drop on fence {FenceId} failed", window.FenceId),
                AcceptsDrops: () => !window.IsLibrary), // the library shows NeoFences' own shortcuts only (M12)
                portalFolder: () => _portals.TryGetValue(window.FenceId, out var portal) ? portal.Current : null);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            Log.Error(failure, "fence {FenceId} cannot accept drops", window.FenceId); // degrade: everything else still works
        }
    }

    private void ShowItemMenu(FenceWindow window, IReadOnlyList<string> itemRefs, int screenX, int screenY, bool fromKeyboard)
    {
        // Shift+right-click is the extended menu; Shift+F10 is just the keyboard's normal menu (M3a review).
        var extended = !fromKeyboard && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        if (window.IsLibrary)
        {
            ShowLibraryItemMenu(window, itemRefs, screenX, screenY, extended);
            return;
        }
        var choice = ShellItemMenu.Show(window.Handle, itemRefs, screenX, screenY, extended,
            logFailure: failure => Log.Warning(failure, "item menu or its command failed for {ItemRefs}", itemRefs));
        switch (choice)
        {
            case ItemMenuChoice.Rename:
                window.BeginItemRename(itemRefs[0]); // the right-clicked item (it comes first)
                break;
            case ItemMenuChoice.Delete:
                RecycleItems(window, itemRefs); // always the Recycle Bin, even with Shift held (hard rule 1)
                break;
        }
    }

    /// <summary>
    /// Windows moves them to the Recycle Bin (with its own dialogs) on the shell worker, so a long recycle never freezes
    /// the fences (M3a review); the watcher then removes them from the fence.
    /// </summary>
    private void RecycleItems(FenceWindow window, IReadOnlyList<string> itemRefs)
    {
        if (window.IsLibrary)
        {
            HideGames(itemRefs); // Delete in the library hides the game; its shortcut is NeoFences' own (M12)
            return;
        }
        Log.Information("recycling {Count} item(s)", itemRefs.Count);
        var ownerHandle = window.Handle;
        var dispatcher = Dispatcher.CurrentDispatcher;
        _shellWorker.Run(() =>
        {
            var (started, refused, missing) = ShellFileOps.TryRecycle(LiveOwner(ownerHandle), itemRefs);
            if (!started) Log.Warning("recycle did not run or was cancelled: {ItemRefs}", itemRefs);
            if (missing.Count > 0) Log.Information("skipped {Count} item(s) that no longer exist: {ItemRefs}", missing.Count, missing);
            if (refused.Count > 0) dispatcher.BeginInvoke(() => ExplainRefusedRecycle(window, refused));
        });
    }

    /// <summary>No owner when the fence was deleted while the operation waited: Windows' dialogs then stand alone (M8c review).</summary>
    private static nint LiveOwner(nint ownerHandle) => FenceWindowChrome.IsLiveWindow(ownerHandle) ? ownerHandle : 0;

    private static void ExplainRefusedRecycle(FenceWindow window, IReadOnlyList<string> refused)
    {
        Log.Information("not deleting {Count} item(s) on drives without a Recycle Bin", refused.Count);
        System.Windows.MessageBox.Show(window,
            (refused.Count == 1 ? $"\"{Path.GetFileName(refused[0])}\" is" : $"{refused.Count} items are") +
            " on a drive without a Recycle Bin (a USB stick or network drive), so NeoFences won't delete it." +
            " Deleting there would be permanent; if you really mean it, delete it in Explorer.",
            "NeoFences", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    /// <summary>Windows renames the file (on the shell worker: its conflict dialogs); the watcher keeps it in its fence and position.</summary>
    private void RenameItem(FenceWindow window, string itemRef, string newName)
    {
        var ownerHandle = window.Handle;
        _shellWorker.Run(() =>
        {
            if (!ShellFileOps.TryRename(LiveOwner(ownerHandle), itemRef, newName)) Log.Warning("rename did not run or was cancelled: {ItemRef}", itemRef);
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
        var wasMove = ReferenceEquals(_movingWindow, window);
        _movingWindow = null;
        foreach (var other in _windows.Values) other.SetMergeHighlight(false);
        // Dropped by its title onto another fence's title row: the whole box joins that box as tabs (M9).
        var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
        if (wasMove && _windows.Values.FirstOrDefault(other => other != window && other.TitleRowContains(cursorX, cursorY)) is { } target)
        {
            Log.Information("fence {FenceId} merged into the box of {TargetId}", window.BoxId, target.BoxId);
            _config = FenceTabs.Merge(_config, movingFenceId: window.BoxId, targetFenceId: target.BoxId);
            SyncBoxes();
            return;
        }
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint) return;
        var monitor = FencePlacement.ContainingMonitor(pixels, _monitors);
        _config = LayoutEngine.WithFenceRect(_config, fingerprint: fingerprint, fenceId: window.BoxId, rect: FencePlacement.FromPixels(pixels, monitor));
        if (Appearance.WallpaperAccent) ApplyStyle(window); // M14: moved to another monitor, another wallpaper
        ScheduleSave();
    }

    private PixelRect SnapFence(FenceWindow window, PixelRect rect, SnapEdges edges)
    {
        // While a box moves, the title row it would merge into lights up; a resize never merges (M9).
        _movingWindow = edges == SnapEdges.Move ? window : null;
        if (_movingWindow is not null)
        {
            var (cursorX, cursorY) = FenceWindowChrome.GetCursorPosition();
            foreach (var other in _windows.Values) other.SetMergeHighlight(other != window && other.TitleRowContains(cursorX, cursorY));
        }
        if (_monitors.Count == 0) return rect;
        var monitor = FencePlacement.ContainingMonitor(rect, _monitors);
        var others = _windows.Values.Where(other => other != window && other.IsVisible).Select(other => FenceWindowChrome.GetPixelRect(other.Handle)).ToList();
        return Snapping.Snap(rect, edges,
            workArea: new PixelRect(monitor.WorkLeftPx, monitor.WorkTopPx, monitor.WorkWidthPx, monitor.WorkHeightPx),
            others: others,
            gapPx: (int)Math.Round(SnapGapDips * monitor.Scale),
            thresholdPx: (int)Math.Round(SnapThresholdDips * monitor.Scale),
            // A resize snap never makes the fence smaller than its minimum (M2c review carry-over).
            minWidthPx: (int)Math.Round(LayoutEngine.MinWidth * monitor.Scale),
            minHeightPx: (int)Math.Round(LayoutEngine.MinHeight * monitor.Scale));
    }

    private void RenameFence(FenceWindow window, string title)
    {
        _config = FenceEdits.Rename(_config, window.FenceId, title);
        window.SetTitle(_config.Fences.First(fence => fence.Id == window.FenceId).Title);
        RefreshTabs(window);
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
        _config = FenceEdits.SetLocked(_config, window.BoxId, locked); // the box's (M9)
        window.SetLocked(locked);
        ScheduleSave();
    }

    /// <summary>Removes the fence; its items go to the Inbox. Files are never touched (hard rule 1).</summary>
    private void DeleteFence(FenceWindow window)
    {
        _config = FenceMembership.DeleteFence(_config, window.FenceId); // the shown tab; the rest of its box stays (M9)
        SyncBoxes(); // closes the window when its box is gone; a deleted Portal's watcher ends (the folder is never touched)
    }

    private void OnThemeChanged()
    {
        var light = SystemTheme.AppsUseLightTheme();
        if (light == _lightTheme) return;
        _lightTheme = light;
        Log.Information("Windows app mode changed; light: {Light}", light);
        foreach (var window in _windows.Values) window.ApplyTheme(light);
        RestyleAll(); // M14: the tone's strength and ink
        RefreshSettings();
    }

    private void OnDisplayChanged()
    {
        RefreshMonitors();
        ApplyLayout();
        if (Appearance.WallpaperAccent) UpdateAccents(); // M14: monitors and their wallpapers may have changed
        ScheduleSave();
    }

    /// <summary>"New Portal fence…": Windows' folder dialog, then a fence that mirrors the folder (M4).</summary>
    private void CreatePortal(FenceWindow owner)
    {
        var folder = FolderPicker.TryPick(owner.Handle, "Choose the folder for the new Portal fence",
            logFailure: failure => Log.Warning(failure, "folder dialog failed"));
        if (folder is null) return;
        var title = Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } name ? name : folder;
        (_config, _) = FenceMembership.CreatePortal(_config, title: title, folderPath: folder);
        Log.Information("Portal fence created for {Folder}", folder);
        SyncBoxes();
    }

    /// <summary>
    /// Brings the windows in line with the boxes after any change to them (M9): a window per box, showing the box's
    /// active tab, with its tab strip, lock and roll-up. A box that changed hands (its host left) keeps its window.
    /// </summary>
    private void SyncBoxes()
    {
        var boxes = FenceTabs.Boxes(_config);
        var boxIds = boxes.Select(box => box.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (boxId, window) in _windows.ToList())
        {
            if (boxIds.Contains(boxId)) continue;
            _windows.Remove(boxId);
            if (_config.Fences.Any(fence => fence.Id == window.FenceId) && FenceTabs.HostOf(_config, window.FenceId) is { } heir
                && boxIds.Contains(heir.Id) && !_windows.ContainsKey(heir.Id))
            {
                window.BoxId = heir.Id;
                _windows[heir.Id] = window;
                if (_dropRegistrations.Remove(boxId, out var moved)) _dropRegistrations[heir.Id] = moved;
                continue;
            }
            if (_dropRegistrations.Remove(boxId, out var registration)) registration.Dispose();
            window.Close();
        }
        foreach (var box in boxes.Where(box => !_windows.ContainsKey(box.Id))) OpenWindow(box);
        EnsurePortals();
        foreach (var box in boxes)
        {
            var window = _windows[box.Id];
            var active = FenceTabs.ActiveOf(_config, box.Id);
            if (window.FenceId != active.Id)
            {
                window.ShowTab(active);
                if (_portals.TryGetValue(active.Id, out var portal)) portal.Refresh();
            }
            window.SetTabs(FenceTabs.TabsOf(_config, box.Id), active.Id);
            window.SetLocked(box.Locked);
            window.SetRolledUp(box.RolledUp);
        }
        RefreshWindows();
        ApplyLayout();
        RestyleAll(); // M14: after placing, so each fence takes its own monitor's accent
        ScheduleSave();
    }

    /// <summary>A watcher per Portal fence, shown or not (a hidden Portal tab keeps watching, M9); gone ones end.</summary>
    private void EnsurePortals()
    {
        var portalFences = _config.Fences.Where(fence => fence.Source is { Kind: FenceSourceKind.Portal, Path: not null } or { Kind: FenceSourceKind.Library }).ToList();
        foreach (var goneId in _portals.Keys.Where(fenceId => portalFences.All(fence => fence.Id != fenceId)).ToList())
        {
            _portals[goneId].Dispose(); // the folder itself is never touched
            _portals.Remove(goneId);
        }
        foreach (var fence in portalFences.Where(fence => !_portals.ContainsKey(fence.Id)))
        {
            var fenceId = fence.Id;
            var root = fence.Source.Kind == FenceSourceKind.Library ? AppPaths.LibraryDirectory : fence.Source.Path!;
            if (fence.Source.Kind == FenceSourceKind.Library) TryCreateFolder(root);
            _portals[fenceId] = new PortalState(root, noticeOwner: _messages.Handle, show: items => ShowPortalTab(fenceId, items),
                logFailure: failure => Log.Warning(failure, "cannot watch Portal folder of {FenceId}", fenceId));
            if (_gameMode) _portals[fenceId].SetPaused(true);
        }
        UpdateLibrary(); // M12: the library scans while its fence exists
    }

    /// <summary>A Portal's listing goes to the window showing it; a hidden Portal tab re-lists when shown.</summary>
    private void ShowPortalTab(string fenceId, IReadOnlyList<ItemInfo>? items)
    {
        if (_windows.Values.FirstOrDefault(window => window.FenceId == fenceId) is { } window) ShowPortal(window, items);
    }

    private void RefreshTabs(FenceWindow window)
    {
        window.SetTabs(FenceTabs.TabsOf(_config, window.BoxId), window.FenceId);
        ApplyStyle(window); // the shown tab's colour and font (M14)
    }

    /// <summary>A header clicked (or hovered during a drop): that tab shows, now and after a restart.</summary>
    private void SwitchTab(FenceWindow window, string fenceId)
    {
        if (window.FenceId == fenceId || _config.Fences.FirstOrDefault(fence => fence.Id == fenceId) is not { } tab) return;
        _config = FenceTabs.SetActive(_config, fenceId);
        window.ShowTab(tab);
        RefreshTabs(window);
        if (_portals.TryGetValue(fenceId, out var portal)) portal.Refresh();
        RefreshWindows();
        ScheduleSave();
    }

    private void CycleTab(FenceWindow window, int step)
    {
        var tabs = FenceTabs.TabsOf(_config, window.BoxId);
        var index = tabs.ToList().FindIndex(tab => tab.Id == window.FenceId);
        SwitchTab(window, tabs[((index + step) % tabs.Count + tabs.Count) % tabs.Count].Id);
    }

    /// <summary>A tab header released after a drag: on its own strip = reorder; on another title row = merge; elsewhere = detach.</summary>
    private void OnTabDropped(FenceWindow window, string fenceId, int screenX, int screenY)
    {
        var target = _windows.Values.FirstOrDefault(candidate => candidate.TitleRowContains(screenX, screenY));
        var own = FenceWindowChrome.GetPixelRect(window.Handle);
        if (target is null && screenX >= own.X && screenX < own.X + own.Width && screenY >= own.Y && screenY < own.Y + own.Height)
        {
            return; // released over its own box: nothing happens (not a detach on top of itself; final review)
        }
        if (target == window)
        {
            var slot = window.TabIndexAt(screenX);
            var current = FenceTabs.TabsOf(_config, window.BoxId).ToList().FindIndex(tab => tab.Id == fenceId);
            _config = FenceTabs.Reorder(_config, fenceId, slot > current ? slot - 1 : slot);
        }
        else if (target is not null)
        {
            Log.Information("tab {FenceId} moved into the box of {TargetId}", fenceId, target.BoxId);
            // One tab moves, also when it is its box's host (final review C1).
            _config = FenceTabs.Merge(_config, movingFenceId: fenceId, targetFenceId: target.BoxId, insertAt: target.TabIndexAt(screenX), wholeBox: false);
        }
        else
        {
            DetachTab(window, fenceId, dropPoint: (screenX, screenY));
            return;
        }
        SyncBoxes();
    }

    /// <summary>A tab leaves its box: at the drop point (dragged out) or offset 40 DIP down-right of the box (menu), with the box's size.</summary>
    private void DetachTab(FenceWindow window, string fenceId, (int X, int Y)? dropPoint)
    {
        if (_monitors.Count == 0 || _config.LastLayoutFingerprint is not { } fingerprint
            || !_config.Layouts.TryGetValue(fingerprint, out var layout) || !layout.Fences.TryGetValue(window.BoxId, out var boxRect))
        {
            Log.Warning("tab {FenceId} not detached: its box has no place in the current layout", fenceId);
            return;
        }
        var boxMonitor = _monitors.FirstOrDefault(monitor => monitor.DeviceId == boxRect.Monitor) ?? _monitors[0];
        var box = FencePlacement.ToPixels(boxRect, boxMonitor);
        var offset = (int)Math.Round(40 * boxMonitor.Scale);
        var placed = dropPoint is { } point
            ? box with { X = point.X - offset, Y = point.Y - offset / 3 } // the pointer near its title
            : box with { X = box.X + offset, Y = box.Y + offset };
        var monitor = FencePlacement.ContainingMonitor(placed, _monitors);
        Log.Information("tab {FenceId} detached from the box of {BoxId}", fenceId, window.BoxId);
        _config = FenceTabs.Detach(_config, fenceId, fingerprint, FencePlacement.FromPixels(placed, monitor));
        SyncBoxes();
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
        if (fence.Source.Kind == FenceSourceKind.Library)
        {
            window.ShowPortalMessage(null);
            window.SetLibraryArt(LibraryArt());
            window.SetItems(items is null ? [] : LibraryOrder(items)); // A–Z by game, only NeoFences' own shortcuts (M12)
            return;
        }
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
        (_config, _) = FenceMembership.CreateFence(_config, "New fence");
        SyncBoxes();
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
        StartSpecialIconNotifications();
        ScheduleSpecialIconRefresh(); // the Recycle Bin may have changed meanwhile
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
        // Released while Settings records a new one, so pressing the current combination is recorded, not Peek (M6b review).
        if ((Current.PeekHotkeyWanted && !_recordingHotkey) == _peekHotkey is not null) return;
        if (_peekHotkey is not null)
        {
            _peekHotkey.Dispose();
            _peekHotkey = null;
            return;
        }
        _peekHotkey = TryRegisterPeekHotkey(_config.Settings.PeekHotkey, out var problem);
        _peekHotkeyProblem = _peekHotkey is null ? problem : null;
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
            problem = $"{hotkey.DisplayTextWith(LayoutKeyCap)} has no key Windows can watch for. Pick another key."; // would say "Saved" and never fire (M6b review M4)
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
        problem = $"{hotkey.DisplayTextWith(LayoutKeyCap)} is already taken by Windows or another app. Pick another combination.";
        return null;
    }

    /// <summary>Settings: a new Peek hotkey. It is kept only if Windows accepts it; otherwise the old one stays.</summary>
    private (bool Saved, string Message) SetPeekHotkey(string text)
    {
        if (!Hotkey.TryParse(text, out var parsed)) return (false, $"\"{text}\" is not a hotkey: it needs Ctrl, Alt, Shift or Win plus one key.");
        // A global hotkey swallows its keys in every app (M6b review I1): only safe combinations are recorded.
        if (!parsed.IsSafeToRecord) return (false, $"{parsed.DisplayTextWith(LayoutKeyCap)} would stop working everywhere else (typing, Tab, closing windows). Use Alt or Win with a key, or an F-key.");
        var normalized = parsed.ToString();
        // The same combination again is a retry when it is not registered (Settings showed it as not active, M6b review).
        if (normalized == _config.Settings.PeekHotkey && _peekHotkeyProblem is null) return (true, $"Peek: {parsed.DisplayTextWith(LayoutKeyCap)}");
        _peekHotkey?.Dispose();
        _peekHotkey = null;
        var registration = TryRegisterPeekHotkey(normalized, out var problem);
        if (registration is null)
        {
            UpdatePeekHotkey(); // the old one again
            return (false, problem);
        }
        registration.Dispose(); // proven free; UpdatePeekHotkey registers it whenever it is wanted
        _peekHotkeyProblem = null;
        _config = _config with { Settings = _config.Settings with { PeekHotkey = normalized } };
        UpdatePeekHotkey();
        SaveNow();
        return (true, $"Saved. Peek: {parsed.DisplayTextWith(LayoutKeyCap)}");
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
        window.TakeSnapshotRequested += () => TakeSnapshot();
        window.RestoreSnapshotRequested += RestoreSnapshot;
        window.RenameSnapshotRequested += (path, name) =>
        {
            if (!_snapshots.Rename(path, name))
            {
                Log.Warning(_snapshots.LastFailure, "snapshot {Path} could not be renamed", path);
                SnapshotFailure("Snapshot not renamed", "The snapshot file could not be changed (see the log)."); // final review M1
            }
            RefreshSettings();
        };
        window.DeleteSnapshotRequested += DeleteSnapshot;
        window.RulesChanged += rules =>
        {
            _config = _config with { Rules = rules };
            Log.Information("rules changed: {Count} rule(s)", rules.Count);
            SaveNow();
            RefreshSettings();
        };
        window.ApplyRulesRequested += ApplyRulesNow;
        WireLibrarySettings(window);
        window.OpenSnapshotsRequested += () =>
        {
            try
            {
                Directory.CreateDirectory(_snapshots.Directory);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                Log.Warning(failure, "snapshots folder {Folder} could not be created", _snapshots.Directory); // hard rule 7 (final review I3)
                return;
            }
            OpenItem(_snapshots.Directory, ownerHandle: 0);
        };
        window.HotkeyRecording += recording =>
        {
            _recordingHotkey = recording;
            UpdatePeekHotkey();
            // Leaving the box re-registers: Settings must show if that failed (final review I5).
            if (!recording) RefreshSettings();
        };
        window.DefaultLabelsChanged += labels =>
        {
            _config = _config with { Settings = _config.Settings with { DefaultLabels = labels } };
            SaveNow();
        };
        window.LabelsAppliedToAll += labels =>
        {
            _config = FenceEdits.SetLabelsEverywhere(_config, labels);
            foreach (var fenceWindow in _windows.Values) fenceWindow.SetLabelMode(labels);
            Log.Information("labels for every fence: {Labels}", labels);
            SaveNow();
            RefreshSettings();
        };
        window.ShortcutArrowsChanged += show =>
        {
            _config = _config with { Settings = _config.Settings with { ShowShortcutArrows = show } };
            foreach (var fenceWindow in _windows.Values) fenceWindow.SetShortcutArrows(show);
            SaveNow();
        };
        window.GameModeChanged += SetGameModeEnabled;
        window.AppearanceChanged += SetAppearance; // M14
        window.AutoUpdateChanged += SetAutoUpdate; // M17
        window.CheckForUpdatesRequested += () => CheckForUpdate(manual: true);
        window.RestartToUpdateRequested += RestartToUpdate;
        window.KeyboardLayoutChanged += RefreshSettings; // M16: key caps follow the new layout
        window.OpenLogsRequested += () => OpenItem(AppPaths.LogsDirectory, ownerHandle: 0);
        window.OpenDataRequested += () => OpenItem(AppPaths.DataDirectory, ownerHandle: 0);
        window.Closed += (_, _) =>
        {
            _settingsWindow = null;
            _recordingHotkey = false;
            UpdatePeekHotkey();
        };
        _settingsWindow = window;
        RefreshSettings();
        window.Show();
        window.Activate();
    }

    private void RefreshSettings() => _settingsWindow?.Show(new SettingsView(
        StartWithWindows: _config.Settings.StartWithWindows,
        Takeover: _takeoverActive,
        PeekHotkey: PeekHotkeyDisplay,
        PeekHotkeyActive: _peekHotkeyProblem is null,
        RollupExpand: _config.Settings.RollupExpand,
        GameModeEnabled: _config.Settings.GameMode,
        GameModeActive: _gameMode,
        Version: typeof(FenceHost).Assembly.GetName().Version?.ToString(3) ?? "",
        DataFolder: AppPaths.DataDirectory,
        DefaultLabels: _config.Settings.DefaultLabels,
        ShowShortcutArrows: _config.Settings.ShowShortcutArrows,
        Snapshots: ListSnapshots(),
        Rules: _config.Rules,
        RuleLines: [.. _config.Rules.Select(rule => Rules.Describe(rule, _config))],
        RuleFences: [.. _config.Fences.Where(fence => fence.Source.Kind == FenceSourceKind.Desktop).Select(fence => new RuleFence(fence.Id, fence.Title))],
        Library: LibrarySettingsView(),
        Appearance: AppearanceView(),
        Updates: UpdatesView()));

    /// <summary>Fence menu → "Rules for this fence…" (M11): Settings at Rules, a new rule for that fence.</summary>
    private void OpenRulesFor(string fenceId)
    {
        OpenSettings();
        _settingsWindow?.BeginNewRule(fenceId);
    }

    /// <summary>The Peek hotkey as a person reads it ("Ctrl+Shift+=", not "Ctrl+Shift+OemPlus").</summary>
    private string PeekHotkeyDisplay =>
        Hotkey.TryParse(_config.Settings.PeekHotkey, out var hotkey) ? hotkey.DisplayTextWith(LayoutKeyCap) : _config.Settings.PeekHotkey;

    /// <summary>The character the user's keyboard layout prints on an OEM key ("+" for OemPlus on a German keyboard, where the US name is "="), or null (M13c).</summary>
    private static string? LayoutKeyCap(string key) =>
        key.StartsWith("Oem", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<System.Windows.Input.Key>(key, ignoreCase: true, out var wpfKey)
            ? KeyboardLayout.CharacterOf(System.Windows.Input.KeyInterop.VirtualKeyFromKey(wpfKey))
            : null;

    /// <summary>Saves the arrangement now, named by date and time (M10); renamed in Settings if wanted.</summary>
    private void TakeSnapshot()
    {
        var now = DateTimeOffset.Now;
        var snapshot = Snapshots.Take(_config, name: $"Snapshot {now:d MMM HH:mm}", now: now);
        if (_snapshots.Save(snapshot) is { } path)
        {
            Log.Information("snapshot saved: {Path}", path);
            _trayIcon?.ShowBalloon("Snapshot saved", snapshot.Name);
            _settingsWindow?.ShowSnapshotNotice($"Saved \"{snapshot.Name}\".", failed: false);
        }
        else
        {
            Log.Warning(_snapshots.LastFailure, "snapshot could not be saved");
            SnapshotFailure("Snapshot not saved", "NeoFences could not write the snapshot file (see the log).");
        }
        RefreshSettings();
    }

    /// <summary>
    /// Puts a snapshot's arrangement back (M10, spec §3): only with a complete desktop listing, and only after "Before
    /// restore" was written, so the restore itself can be undone. One config change, saved at once.
    /// </summary>
    private void RestoreSnapshot(string path)
    {
        if (_snapshots.Load(path) is not { } snapshot)
        {
            Log.Warning(_snapshots.LastFailure, "snapshot {Path} could not be read", path);
            SnapshotFailure("Snapshot not restored", "The snapshot file could not be read.");
            return;
        }
        var listing = DesktopItems.Enumerate();
        if (listing.UnavailableFolders.Count > 0)
        {
            Log.Warning("snapshot not restored: desktop folders not readable {Folders}", listing.UnavailableFolders);
            SnapshotFailure("Snapshot not restored", "A Desktop folder cannot be read right now. Try again in a moment.");
            return;
        }
        var now = DateTimeOffset.Now;
        if (_snapshots.Save(Snapshots.Take(_config, name: $"Before restore ({now:d MMM HH:mm})", now: now), SnapshotStore.BeforeRestoreFileName) is null)
        {
            Log.Warning(_snapshots.LastFailure, "snapshot not restored: 'Before restore' could not be saved");
            SnapshotFailure("Snapshot not restored", "NeoFences could not save 'Before restore' first (see the log).");
            return;
        }
        Log.Information("restoring snapshot {Name} from {Path}", snapshot.Name, path);
        _config = Snapshots.Restore(_config, snapshot, listing.ItemRefs);
        SaveNow();
        SyncBoxes();
        // Windows that kept their fence still show its old title, icon size and labels (final review I1).
        foreach (var window in _windows.Values)
        {
            if (_config.Fences.FirstOrDefault(fence => fence.Id == window.FenceId) is not { } shown) continue;
            window.Refresh(shown);
            RefreshPortal(window); // the Portal breadcrumb replaces the plain title again
        }
        _settingsWindow?.ShowSnapshotNotice($"Restored \"{snapshot.Name}\".", failed: false); // replaces an earlier failure line (final review M1)
        RefreshSettings();
    }

    /// <summary>The snapshot list; a damaged file or an unreadable folder is logged once, not on every tray open (final review I2).</summary>
    private IReadOnlyList<SnapshotEntry> ListSnapshots()
    {
        var snapshots = _snapshots.List();
        foreach (var (path, failure) in _snapshots.Problems.Where(problem => _loggedSnapshotProblems.Add(problem.Path)))
            Log.Warning(failure, "snapshot {Path} skipped: it cannot be read", path);
        return snapshots;
    }

    /// <summary>
    /// A snapshot failure, said where the user looks (M13c): Windows' warning notice, and the Settings card when it is open
    /// (a notice alone is easy to miss, and Do Not Disturb hides it).
    /// </summary>
    private void SnapshotFailure(string title, string reason)
    {
        _trayIcon?.ShowBalloon(title, reason, warning: true);
        _settingsWindow?.ShowSnapshotNotice($"{title}: {reason}", failed: true);
    }

    /// <summary>A snapshot file goes to the Recycle Bin, never deleted for good (hard rule 1).</summary>
    private void DeleteSnapshot(string path)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        // Windows' delete confirmation (when turned on) belongs to Settings, not to no window behind it (M13c).
        var owner = _settingsWindow is { } settings ? new WindowInteropHelper(settings).Handle : 0;
        _shellWorker.Run(() =>
        {
            var (started, refused, missing) = ShellFileOps.TryRecycle(LiveOwner(owner), [path]);
            if (!started || refused.Count > 0)
            {
                Log.Warning("snapshot {Path} could not be moved to the Recycle Bin", path);
                dispatcher.BeginInvoke(() => SnapshotFailure("Snapshot not deleted", "It was not moved to the Recycle Bin (see the log).")); // final review M1
            }
            if (missing.Count > 0) Log.Information("snapshot {Path} was already gone", path);
            dispatcher.BeginInvoke(RefreshSettings);
        });
    }

    /// <summary>Fence menu → Labels (M8b).</summary>
    private void SetFenceLabels(FenceWindow window, LabelMode labels)
    {
        _config = FenceEdits.SetLabels(_config, window.FenceId, labels);
        window.SetLabelMode(labels);
        ScheduleSave();
    }

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
        if (_libraryDeferred)
        {
            _libraryDeferred = false;
            ScanLibrary(); // a game installed while playing shows up now (M12)
        }
        if (_specialIconsDeferred)
        {
            _specialIconsDeferred = false;
            ScheduleSpecialIconRefresh();
        }
        if (_reconcileDeferred)
        {
            _reconcileDeferred = false;
            _deferredDesktopChanges.Clear();
            ReconcileDesktop();
            return;
        }
        if (_deferredDesktopChanges.Count == 0) return;
        Log.Information("applying {Count} desktop change(s) from game mode", _deferredDesktopChanges.Count);
        var arrivals = new List<string>();
        foreach (var change in _deferredDesktopChanges)
        {
            if (ArrivalOf(change) is { } arrival) arrivals.Add(arrival);
            (_config, _rememberedPlacements) = FenceMembership.Apply(_config, change, _rememberedPlacements, Now);
            OnDesktopShortcutChange(change);
        }
        _deferredDesktopChanges.Clear();
        RefreshWindows();
        ScheduleSave();
        FileNewItems(arrivals);
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
        if (paused)
        {
            _quickHidden = false; // resuming shows everything
            _iconsHiddenByUser = false;
        }
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
        RefreshSettings(); // a hotkey that cannot be registered on resume shows in an open Settings (final review I5)
        _trayIcon?.SetTooltip(TrayTooltip());
        Log.Information("paused: {Paused}", paused);
    }

    private string TrayTooltip() =>
        _paused ? "NeoFences — paused" : _gameMode ? "NeoFences — idle while a game runs" : "NeoFences";

    /// <summary>Tray menu (spec §6; user choice 2026-10-03: left-click opens it too).</summary>
    private void ShowTrayMenu(int screenX, int screenY)
    {
        var snapshots = ListSnapshots();
        var restorable = snapshots.Where(entry => !entry.IsBeforeRestore).Take(TrayRestoreCount).ToList();
        var beforeRestore = snapshots.FirstOrDefault(entry => entry.IsBeforeRestore);
        // One safe line per snapshot (M13c): no blank, multi-line, tabbed or 300-character entries.
        List<TrayMenuItem> restoreItems = [.. restorable.Select((entry, index) => new TrayMenuItem(TrayRestoreFirst + index, Snapshots.MenuLabel(entry, TimeZoneInfo.Local)))];
        if (beforeRestore is not null)
        {
            if (restoreItems.Count > 0) restoreItems.Add(TrayMenuItem.Separator); // never a separator first (M13c)
            restoreItems.Add(new TrayMenuItem(TrayRestoreBefore, "Undo the last restore"));
        }
        if (restoreItems.Count > 0) restoreItems.Add(TrayMenuItem.Separator);
        restoreItems.Add(new TrayMenuItem(TraySnapshotsSettings, "More in Settings…"));
        var chosen = TrayMenu.Show(_messages.Handle,
        [
            .. UpdateTrayItems(), // M17: "Restart to update to v…" first while an update waits
            new TrayMenuItem(TrayNewFence, "New fence", Enabled: !_paused),
            new TrayMenuItem(TrayNewLibrary, "New Game Library fence", Enabled: !_paused),
            new TrayMenuItem(TrayQuickHide, "Quick-hide", Checked: _quickHidden, Enabled: !_paused),
            new TrayMenuItem(TrayPeek, $"Peek\t{PeekHotkeyDisplay}", Checked: _peeking, Enabled: !_paused),
            TrayMenuItem.Separator,
            new TrayMenuItem(TrayTakeSnapshot, "Take snapshot"),
            new TrayMenuItem(TrayRestoreMenu, "Restore snapshot", Enabled: snapshots.Count > 0) { Children = restoreItems },
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
            case TrayNewLibrary: CreateLibraryFence(); break;
            case TrayQuickHide: SetQuickHidden(!_quickHidden); break;
            case TrayPeek: SetPeek(!_peeking); break;
            case TrayPause: SetPaused(!_paused); break;
            case TraySettings: OpenSettings(); break;
            case TrayRestartToUpdate: RestartToUpdate(); break; // M17
            case TraySnapshotsSettings:
                OpenSettings();
                _settingsWindow?.ShowSnapshotsCard();
                break;
            case TrayExit: ExitRequested?.Invoke(); break;
            case TrayTakeSnapshot: TakeSnapshot(); break;
            case TrayRestoreBefore when beforeRestore is not null: RestoreSnapshot(beforeRestore.Path); break;
            case >= TrayRestoreFirst and < TrayRestoreFirst + TrayRestoreCount when chosen - TrayRestoreFirst < restorable.Count:
                RestoreSnapshot(restorable[chosen - TrayRestoreFirst].Path);
                break;
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
        // Hidden now without the takeover-active marker: the user hid them in Explorer, so they stay the user's. With the marker
        // set, NeoFences hid them (an earlier show failed) and the end of this quick-hide retries the show (M8a review I1).
        if (hidden && !_takeoverActive) _iconsHiddenByUser = DesktopIcons.TryIsHidden() == true && !_watchdog.IsTakeoverActiveMarked;
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
        if (Current.IconsHidden != iconsWereHidden) SetIconsHidden(Current.IconsHidden); // RunState decides, user-hidden icons included
        if (!hidden) _iconsHiddenByUser = false;
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
        var rolledUp = !_config.Fences.First(fence => fence.Id == window.BoxId).RolledUp; // the box's (M9)
        _config = FenceEdits.SetRolledUp(_config, window.BoxId, rolledUp);
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
            else if (_store.LastBackupFailure is { } backupFailure) Log.Warning(backupFailure, "config saved, but the daily backups could not be written or pruned");
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Error(failure, "config save failed");
        }
    }

    private static void TryCreateFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Log.Warning(failure, "cannot create {Folder}", folder); // the fence then shows "not available" (hard rule 7)
        }
    }
}
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
                        <TextBlock Text="Start with Windows" TextWrapping="Wrap" />
                        <TextBlock x:Name="StartupDescription" Style="{StaticResource Description}" Text="Your fences come back by themselves after a restart or a power cut." />
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <CheckBox x:Name="TakeoverBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Hide desktop icons" />
                    <StackPanel>
                        <TextBlock Text="Hide desktop icons" TextWrapping="Wrap" />
                        <TextBlock x:Name="TakeoverDescription" Style="{StaticResource Description}" Text="Show your desktop items only in fences. They are back on the desktop whenever NeoFences is paused or closed." />
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <TextBox x:Name="HotkeyBox" DockPanel.Dock="Right" Width="190" IsReadOnly="True" IsReadOnlyCaretVisible="False"
                                 VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Peek hotkey" />
                        <StackPanel>
                            <TextBlock Text="Peek hotkey" TextWrapping="Wrap" />
                            <TextBlock x:Name="HotkeyDescription" Style="{StaticResource Description}" Text="Shows your fences above all windows. Click the box, then press the new combination (with Alt or Win, or an F-key)." />
                        </StackPanel>
                    </DockPanel>
                    <!-- Assertive live region: screen readers announce a refused combination at once (M6b review carry-over). -->
                    <TextBlock x:Name="HotkeyStatus" Style="{StaticResource Description}" Margin="0,8,0,0" Visibility="Collapsed"
                               AutomationProperties.LiveSetting="Assertive" />
                </StackPanel>
            </Border>

            <TextBlock Text="Fences" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <ComboBox x:Name="LabelsBox" DockPanel.Dock="Right" Width="230" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Labels for new fences">
                            <ComboBoxItem Content="Always" Tag="Always" />
                            <ComboBoxItem Content="On hover (icons only)" Tag="OnHover" />
                        </ComboBox>
                        <StackPanel>
                            <TextBlock Text="Labels for new fences" TextWrapping="Wrap" />
                            <TextBlock x:Name="LabelsDescription" Style="{StaticResource Description}" Text="Icons only shows an item's name when you point at it or select it. Each fence can also choose in its own menu." />
                        </StackPanel>
                    </DockPanel>
                    <Button x:Name="LabelsApplyAllButton" Content="Apply to all fences" HorizontalAlignment="Left" Margin="0,10,0,0" />
                </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <CheckBox x:Name="ArrowsBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Show shortcut arrows" />
                    <StackPanel>
                        <TextBlock Text="Show shortcut arrows" TextWrapping="Wrap" />
                        <TextBlock x:Name="ArrowsDescription" Style="{StaticResource Description}" Text="The small arrow Windows draws on shortcut icons." />
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <ComboBox x:Name="RollupBox" DockPanel.Dock="Right" Width="230" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Rolled-up fences open">
                        <ComboBoxItem Content="When the mouse rests on them" Tag="Hover" />
                        <ComboBoxItem Content="When you click their title" Tag="Click" />
                    </ComboBox>
                    <StackPanel>
                        <TextBlock Text="Rolled-up fences open" TextWrapping="Wrap" />
                        <TextBlock x:Name="RollupDescription" Style="{StaticResource Description}" Text="Double-click a fence's title to roll it up to its title bar." />
                    </StackPanel>
                </DockPanel>
            </Border>

            <TextBlock Text="Appearance" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock Text="Colour style" />
                    <TextBlock Style="{StaticResource Description}" Text="How a fence shows its colour (fence menu → Colour)." />
                    <UniformGrid Rows="1" Margin="0,10,0,0" AutomationProperties.Name="Colour style">
                        <RadioButton x:Name="AccentEdgeStyle" GroupName="ColourStyle" Margin="0,0,8,0" AutomationProperties.Name="Accent edge">
                            <StackPanel>
                                <Border Width="64" Height="36" CornerRadius="4" BorderThickness="1" BorderBrush="#E84855" Background="#30101018" HorizontalAlignment="Left">
                                    <Rectangle Width="18" Height="3" Fill="#E84855" HorizontalAlignment="Left" VerticalAlignment="Top" Margin="6,10,0,0" />
                                </Border>
                                <TextBlock Text="Accent edge" Margin="0,4,0,0" />
                            </StackPanel>
                        </RadioButton>
                        <RadioButton x:Name="TintedGlassStyle" GroupName="ColourStyle" Margin="0,0,8,0" AutomationProperties.Name="Tinted glass">
                            <StackPanel>
                                <Border Width="64" Height="36" CornerRadius="4" BorderThickness="1" BorderBrush="#B3E84855" Background="#59E84855" HorizontalAlignment="Left" />
                                <TextBlock Text="Tinted glass" Margin="0,4,0,0" />
                            </StackPanel>
                        </RadioButton>
                        <RadioButton x:Name="TitleStripStyle" GroupName="ColourStyle" AutomationProperties.Name="Title strip">
                            <StackPanel>
                                <Border Width="64" Height="36" CornerRadius="4" BorderThickness="1" BorderBrush="#40808080" Background="#30101018" HorizontalAlignment="Left">
                                    <Border Height="11" CornerRadius="3,3,0,0" Background="#BFE84855" VerticalAlignment="Top" />
                                </Border>
                                <TextBlock Text="Title strip" Margin="0,4,0,0" />
                            </StackPanel>
                        </RadioButton>
                    </UniformGrid>
                </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock Text="Background strength" />
                    <TextBlock x:Name="StrengthDescription" Style="{StaticResource Description}" />
                    <DockPanel Margin="0,8,0,0">
                        <TextBlock Text="Clear" DockPanel.Dock="Left" VerticalAlignment="Center" Margin="0,0,10,0" Style="{StaticResource Description}" />
                        <TextBlock Text="Solid" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="10,0,0,0" Style="{StaticResource Description}" />
                        <Slider x:Name="StrengthSlider" Minimum="0" Maximum="85" TickFrequency="5" IsSnapToTickEnabled="True" SmallChange="5" LargeChange="10"
                                VerticalAlignment="Center" AutomationProperties.Name="Background strength" />
                    </DockPanel>
                </StackPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <DockPanel>
                    <CheckBox x:Name="AccentBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Colour fences from the wallpaper" />
                    <StackPanel>
                        <TextBlock Text="Colour fences from the wallpaper" TextWrapping="Wrap" />
                        <TextBlock Style="{StaticResource Description}" Text="Fences without a colour of their own take the wallpaper's colour (also Wallpaper Engine's)." />
                        <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                            <Border x:Name="AccentSwatch" Width="16" Height="16" CornerRadius="3" Margin="0,0,6,0" Visibility="Collapsed" VerticalAlignment="Center" />
                            <TextBlock x:Name="AccentSource" Style="{StaticResource Description}" VerticalAlignment="Center" Margin="0" Visibility="Collapsed" />
                        </StackPanel>
                    </StackPanel>
                </DockPanel>
            </Border>
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock Text="Title font" />
                    <TextBlock Style="{StaticResource Description}" Text="Every fence's title uses it." />
                    <StackPanel Orientation="Horizontal" Margin="0,8,0,0">
                        <ComboBox x:Name="FontFamilyBox" Width="200" IsEditable="False" AutomationProperties.Name="Title font" VirtualizingPanel.IsVirtualizing="True">
                            <ComboBox.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <VirtualizingStackPanel />
                                </ItemsPanelTemplate>
                            </ComboBox.ItemsPanel>
                            <ComboBox.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding}" FontFamily="{Binding}" />
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>
                        <ComboBox x:Name="FontSizeBox" Width="110" Margin="8,0,0,0" AutomationProperties.Name="Title size">
                            <ComboBoxItem Content="Small" Tag="12" />
                            <ComboBoxItem Content="Normal" Tag="14" />
                            <ComboBoxItem Content="Large" Tag="17" />
                            <ComboBoxItem Content="Huge" Tag="20" />
                        </ComboBox>
                        <ComboBox x:Name="FontWeightBox" Width="110" Margin="8,0,0,0" AutomationProperties.Name="Title weight">
                            <ComboBoxItem Content="Regular" Tag="Regular" />
                            <ComboBoxItem Content="SemiBold" Tag="SemiBold" />
                            <ComboBoxItem Content="Bold" Tag="Bold" />
                        </ComboBox>
                    </StackPanel>
                </StackPanel>
            </Border>

            <TextBlock Text="Game mode" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <CheckBox x:Name="GameModeBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Go idle while a full-screen game runs" />
                        <StackPanel>
                            <TextBlock Text="Go idle while a full-screen game runs" TextWrapping="Wrap" />
                            <TextBlock x:Name="GameModeDescription" Style="{StaticResource Description}" Text="NeoFences removes its mouse hook and waits with background work, so games get every bit of input. Fences stay where they are." />
                        </StackPanel>
                    </DockPanel>
                    <TextBlock x:Name="GameModeStatus" Style="{StaticResource Description}" Margin="0,8,0,0" />
                </StackPanel>
            </Border>

            <TextBlock Text="Snapshots" Style="{StaticResource SectionHeader}" />
            <Border x:Name="SnapshotsCard" Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock x:Name="SnapshotsDescription" Style="{StaticResource Description}" Margin="0,0,0,8"
                               Text="Save how your fences are arranged and put it back later. Restoring first saves 'Before restore', so it can be undone. Your files and settings are never changed." />
                    <ListBox x:Name="SnapshotList" MaxHeight="220" AutomationProperties.Name="Snapshots">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <DockPanel>
                                    <TextBlock DockPanel.Dock="Right" Text="{Binding When}" Opacity="0.7" Margin="16,0,0,0" />
                                    <TextBlock Text="{Binding Name}" TextTrimming="CharacterEllipsis" />
                                </DockPanel>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                        <ListBox.ItemContainerStyle>
                            <Style TargetType="ListBoxItem" BasedOn="{StaticResource {x:Type ListBoxItem}}">
                                <Setter Property="AutomationProperties.Name" Value="{Binding Spoken}" />
                            </Style>
                        </ListBox.ItemContainerStyle>
                    </ListBox>
                    <TextBox x:Name="SnapshotNameBox" Visibility="Collapsed" Margin="0,8,0,0" AutomationProperties.Name="New name for the snapshot" />
                    <TextBlock x:Name="SnapshotsStatus" Style="{StaticResource Description}" Margin="0,8,0,0" Visibility="Collapsed" />
                    <WrapPanel Margin="0,10,0,0">
                        <Button x:Name="TakeSnapshotButton" Content="Take snapshot" Margin="0,0,8,6" />
                        <Button x:Name="RestoreSnapshotButton" Content="Restore" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="RenameSnapshotButton" Content="Rename" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="DeleteSnapshotButton" Content="Delete" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="OpenSnapshotsButton" Content="Open snapshots folder" Margin="0,0,0,6" />
                    </WrapPanel>
                </StackPanel>
            </Border>

            <TextBlock Text="Rules" Style="{StaticResource SectionHeader}" />
            <Border x:Name="RulesCard" Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock x:Name="RulesDescription" Style="{StaticResource Description}" Margin="0,0,0,8"
                               Text="New Desktop items go to the fence of the first rule they match; the rest go to the Inbox. Rules only choose fences: files are never moved or changed." />
                    <ListBox x:Name="RuleList" MaxHeight="220" AutomationProperties.Name="Rules">
                        <ListBox.ItemTemplate>
                            <DataTemplate>
                                <DockPanel>
                                    <CheckBox IsChecked="{Binding Enabled, Mode=OneWay}" Tag="{Binding Id}" Checked="OnRuleToggled" Unchecked="OnRuleToggled"
                                              VerticalAlignment="Center" Margin="0,0,8,0" AutomationProperties.Name="{Binding ToggleName}" />
                                    <TextBlock Text="{Binding Text}" Opacity="{Binding Opacity}" TextTrimming="CharacterEllipsis" VerticalAlignment="Center" />
                                </DockPanel>
                            </DataTemplate>
                        </ListBox.ItemTemplate>
                        <ListBox.ItemContainerStyle>
                            <Style TargetType="ListBoxItem" BasedOn="{StaticResource {x:Type ListBoxItem}}">
                                <Setter Property="AutomationProperties.Name" Value="{Binding Spoken}" />
                            </Style>
                        </ListBox.ItemContainerStyle>
                    </ListBox>
                    <StackPanel x:Name="RuleEditor" Visibility="Collapsed" Margin="0,10,0,0">
                        <WrapPanel>
                            <TextBlock Text="When" VerticalAlignment="Center" Margin="0,0,8,6" />
                            <ComboBox x:Name="RuleKindBox" Width="170" Margin="0,0,8,6" AutomationProperties.Name="Rule condition">
                                <ComboBoxItem Content="Type of file" Tag="Type" />
                                <ComboBoxItem Content="Game shortcut" Tag="Game" />
                                <ComboBoxItem Content="Name" Tag="Name" />
                                <ComboBoxItem Content="Date modified" Tag="Age" />
                                <ComboBoxItem Content="Size" Tag="Size" />
                            </ComboBox>
                            <ComboBox x:Name="RuleChoiceBox" Width="190" Margin="0,0,8,6" />
                            <TextBox x:Name="RuleTextBox" Width="150" Margin="0,0,8,6" />
                            <Button x:Name="RuleBrowseButton" Content="Choose…" Margin="0,0,8,6" Visibility="Collapsed" AutomationProperties.Name="Choose the game folder" />
                            <TextBlock x:Name="RuleUnitText" VerticalAlignment="Center" Margin="0,0,8,6" />
                        </WrapPanel>
                        <WrapPanel>
                            <TextBlock Text="Put in" VerticalAlignment="Center" Margin="0,0,8,6" />
                            <ComboBox x:Name="RuleFenceBox" Width="220" Margin="0,0,8,6" DisplayMemberPath="Title" AutomationProperties.Name="Put in fence" />
                            <Button x:Name="SaveRuleButton" Content="Save rule" Margin="0,0,8,6" IsDefault="False" />
                            <Button x:Name="CancelRuleButton" Content="Cancel" Margin="0,0,8,6" />
                        </WrapPanel>
                        <TextBlock x:Name="RuleHint" Style="{StaticResource Description}" />
                    </StackPanel>
                    <WrapPanel Margin="0,10,0,0">
                        <Button x:Name="AddRuleButton" Content="Add" Margin="0,0,8,6" />
                        <Button x:Name="EditRuleButton" Content="Edit" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="DeleteRuleButton" Content="Delete" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="MoveRuleUpButton" Content="Move up" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="MoveRuleDownButton" Content="Move down" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="ApplyRulesButton" Content="Apply rules now" Margin="0,0,0,6" />
                    </WrapPanel>
                    <TextBlock x:Name="RulesStatus" Style="{StaticResource Description}" Visibility="Collapsed" />
                </StackPanel>
            </Border>

            <TextBlock Text="Game Library" Style="{StaticResource SectionHeader}" />
            <Border x:Name="LibraryCard" Style="{StaticResource Card}">
                <StackPanel>
                    <TextBlock x:Name="LibraryDescription" Style="{StaticResource Description}" Margin="0,0,0,8"
                               Text="The Game Library fence lists every installed game it finds. Launcher games start through their launcher. Nothing is downloaded, and your games and files are never changed." />
                    <TextBlock Text="Game folders (each sub-folder is a game)" Margin="0,4,0,4" />
                    <ListBox x:Name="GameFolderList" MaxHeight="120" AutomationProperties.Name="Game folders" />
                    <WrapPanel Margin="0,8,0,0">
                        <Button x:Name="AddGameFolderButton" Content="Add folder…" Margin="0,0,8,6" />
                        <Button x:Name="RemoveGameFolderButton" Content="Remove" Margin="0,0,8,6" IsEnabled="False" />
                    </WrapPanel>
                    <TextBlock Text="Look for games in" Margin="0,8,0,4" />
                    <WrapPanel>
                        <CheckBox x:Name="SteamSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="EpicSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="GogSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="UbisoftSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="EaSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="BattleNetSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="XboxSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="FoldersSourceBox" Margin="0,0,16,6" />
                        <CheckBox x:Name="ShortcutsSourceBox" Margin="0,0,16,6" />
                    </WrapPanel>
                    <TextBlock Text="Hidden games" Margin="0,8,0,4" />
                    <TextBlock x:Name="HiddenGamesEmpty" Style="{StaticResource Description}" Text="None. Right-click a game → Hide from library." />
                    <ListBox x:Name="HiddenGameList" MaxHeight="120" AutomationProperties.Name="Hidden games" />
                    <WrapPanel Margin="0,8,0,0">
                        <Button x:Name="ShowGameAgainButton" Content="Show again" Margin="0,0,8,6" IsEnabled="False" />
                        <Button x:Name="RefreshLibraryButton" Content="Refresh library now" Margin="0,0,8,6" />
                    </WrapPanel>
                    <TextBlock x:Name="LibraryStatus" Style="{StaticResource Description}" />
                </StackPanel>
            </Border>

            <TextBlock Text="Updates" Style="{StaticResource SectionHeader}" />
            <Border Style="{StaticResource Card}">
                <StackPanel>
                    <DockPanel>
                        <CheckBox x:Name="AutoUpdateBox" DockPanel.Dock="Right" VerticalAlignment="Center" Margin="16,0,0,0" AutomationProperties.Name="Download updates automatically" />
                        <StackPanel>
                            <TextBlock Text="Download updates automatically" TextWrapping="Wrap" />
                            <TextBlock Style="{StaticResource Description}" Text="Checks GitHub at start and once a day, never during a game. Off: NeoFences makes no network calls." />
                        </StackPanel>
                    </DockPanel>
                    <TextBlock x:Name="UpdateStatus" Style="{StaticResource Description}" Margin="0,10,0,0" AutomationProperties.LiveSetting="Polite" />
                    <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
                        <Button x:Name="CheckUpdatesButton" Content="Check now" />
                        <Button x:Name="RestartToUpdateButton" Margin="8,0,0,0" Visibility="Collapsed" Style="{DynamicResource AccentButtonStyle}" />
                    </StackPanel>
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
/// <param name="PeekHotkey">As a person reads it (key caps).</param>
/// <param name="PeekHotkeyActive">False when Windows refused it (another app owns it): the window says so.</param>
public sealed record SettingsView(
    bool StartWithWindows, bool Takeover, string PeekHotkey, bool PeekHotkeyActive, RollupExpand RollupExpand,
    bool GameModeEnabled, bool GameModeActive, string Version, string DataFolder, LabelMode DefaultLabels, bool ShowShortcutArrows,
    IReadOnlyList<NeoFences.Core.Config.SnapshotEntry> Snapshots, IReadOnlyList<Rule> Rules, IReadOnlyList<string> RuleLines,
    IReadOnlyList<RuleFence> RuleFences, LibraryView Library, AppearanceView Appearance, UpdatesView Updates);

/// <summary>One row of the Snapshots list (M10).</summary>
public sealed record SnapshotRow(string Path, string Name, string When)
{
    public string Spoken => $"{Name}, {When}";
}

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
    /// <summary>Settings → Updates (M17).</summary>
    public event Action<bool>? AutoUpdateChanged;
    public event Action? CheckForUpdatesRequested;
    public event Action? RestartToUpdateRequested;
    /// <summary>The keyboard layout changed while Settings is open (M16): the hotkey label shows the new layout's characters.</summary>
    public event Action? KeyboardLayoutChanged;
    public event Action<RollupExpand>? RollupExpandChanged;
    public event Action<bool>? GameModeChanged;
    public event Action? OpenLogsRequested;
    public event Action? OpenDataRequested;
    public event Action? TakeSnapshotRequested;
    public event Action<string>? RestoreSnapshotRequested;
    public event Action<string, string>? RenameSnapshotRequested;
    public event Action<string>? DeleteSnapshotRequested;
    public event Action? OpenSnapshotsRequested;
    /// <summary>The hotkey box got (true) or lost (false) the keyboard: the host releases the Peek hotkey meanwhile (M6b review).</summary>
    public event Action<bool>? HotkeyRecording;
    public event Action<LabelMode>? DefaultLabelsChanged;
    public event Action<LabelMode>? LabelsAppliedToAll;
    public event Action<bool>? ShortcutArrowsChanged;

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
        HotkeyBox.GotKeyboardFocus += (_, _) =>
        {
            HotkeyRecording?.Invoke(true); // so pressing the current combination is recorded, not Peek
            ShowHotkeyHint("Press the new combination… (Esc keeps the current one)");
        };
        HotkeyBox.LostKeyboardFocus += (_, _) =>
        {
            HotkeyRecording?.Invoke(false);
            if (HotkeyStatus.Tag is null) HotkeyStatus.Visibility = Visibility.Collapsed;
        };
        OnToggled(ArrowsBox, isChecked => ShortcutArrowsChanged?.Invoke(isChecked));
        LabelsBox.SelectionChanged += (_, _) => { if (!_updating) DefaultLabelsChanged?.Invoke(SelectedLabels); };
        LabelsApplyAllButton.Click += (_, _) => LabelsAppliedToAll?.Invoke(SelectedLabels);
        // Screen readers read each setting's description with it (M6b review carry-over).
        foreach (var (control, description) in new (UIElement, TextBlock)[]
                 { (StartupBox, StartupDescription), (TakeoverBox, TakeoverDescription), (HotkeyBox, HotkeyDescription),
                   (LabelsBox, LabelsDescription), (ArrowsBox, ArrowsDescription), (RollupBox, RollupDescription), (GameModeBox, GameModeDescription) })
        {
            System.Windows.Automation.AutomationProperties.SetHelpText(control, description.Text);
        }
        OpenLogsButton.Click += (_, _) => OpenLogsRequested?.Invoke();
        OpenDataButton.Click += (_, _) => OpenDataRequested?.Invoke();
        TakeSnapshotButton.Click += (_, _) => TakeSnapshotRequested?.Invoke();
        OpenSnapshotsButton.Click += (_, _) => OpenSnapshotsRequested?.Invoke();
        RestoreSnapshotButton.Click += (_, _) => { if (SelectedSnapshot is { } row) RestoreSnapshotRequested?.Invoke(row.Path); };
        DeleteSnapshotButton.Click += (_, _) => { if (SelectedSnapshot is { } row) DeleteSnapshotRequested?.Invoke(row.Path); };
        RenameSnapshotButton.Click += (_, _) => BeginSnapshotRename();
        SnapshotList.SelectionChanged += (_, _) => UpdateSnapshotButtons();
        // Only a double-click on a row restores; the scrollbar or the empty space below the rows do nothing (M13c).
        SnapshotList.MouseDoubleClick += (_, click) =>
        {
            if (click.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(SnapshotList, source) is ListBoxItem
                && SelectedSnapshot is { } row) RestoreSnapshotRequested?.Invoke(row.Path);
        };
        SnapshotNameBox.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter) EndSnapshotRename(commit: true, backToRow: true);
            else if (key.Key == Key.Escape) EndSnapshotRename(commit: false, backToRow: true);
            else return;
            key.Handled = true;
        };
        SnapshotNameBox.LostKeyboardFocus += (_, _) => EndSnapshotRename(commit: true, backToRow: false); // the user went elsewhere: focus stays there
        System.Windows.Automation.AutomationProperties.SetHelpText(SnapshotList, SnapshotsDescription.Text);
        InitializeRules();
        InitializeLibrary();
        InitializeAppearance();
        OnToggled(AutoUpdateBox, isChecked => AutoUpdateChanged?.Invoke(isChecked)); // M17
        CheckUpdatesButton.Click += (_, _) => CheckForUpdatesRequested?.Invoke();
        RestartToUpdateButton.Click += (_, _) => RestartToUpdateRequested?.Invoke();
        // WM_INPUTLANGCHANGE (0x0051): every layout switch, also between two layouts of one language (US → US-International),
        // which WPF's InputLanguageChanged does not report (final review M2). The hook dies with the window.
        SourceInitialized += (_, _) => System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)
            .AddHook((nint _, int message, nint _, nint _, ref bool _) =>
            {
                if (message == 0x0051) KeyboardLayoutChanged?.Invoke();
                return 0;
            });
    }

    private void OnToggled(CheckBox box, Action<bool> report)
    {
        box.Checked += (_, _) => { if (!_updating) report(true); };
        box.Unchecked += (_, _) => { if (!_updating) report(false); };
    }

    private LabelMode SelectedLabels => LabelsBox.SelectedIndex == 1 ? LabelMode.OnHover : LabelMode.Always;

    private SnapshotRow? SelectedSnapshot => SnapshotList.SelectedItem as SnapshotRow;
    private string? _renamingPath;

    private void UpdateSnapshotButtons()
    {
        var selected = SelectedSnapshot is not null;
        RestoreSnapshotButton.IsEnabled = selected;
        RenameSnapshotButton.IsEnabled = selected;
        DeleteSnapshotButton.IsEnabled = selected;
    }

    private void BeginSnapshotRename()
    {
        if (SelectedSnapshot is not { } row) return;
        _renamingPath = row.Path;
        SnapshotNameBox.Text = row.Name;
        SnapshotNameBox.Visibility = Visibility.Visible;
        SnapshotNameBox.Focus();
        SnapshotNameBox.SelectAll();
    }

    /// <param name="backToRow">Enter or Esc: the keyboard goes back to the row, not nowhere (M13c). Never after a click elsewhere.</param>
    private void EndSnapshotRename(bool commit, bool backToRow)
    {
        if (_renamingPath is not { } path) return;
        _renamingPath = null;
        SnapshotNameBox.Visibility = Visibility.Collapsed;
        if (backToRow) _focusSnapshotPath = path;
        if (commit && SnapshotNameBox.Text.Trim() is { Length: > 0 } name) RenameSnapshotRequested?.Invoke(path, name);
        else FocusSnapshotRow();
    }

    private string? _focusSnapshotPath;

    /// <summary>Puts the keyboard on the row of <see cref="_focusSnapshotPath"/> once the list is laid out.</summary>
    private void FocusSnapshotRow()
    {
        if (_focusSnapshotPath is not { } path) return;
        _focusSnapshotPath = null;
        Dispatcher.BeginInvoke(() =>
        {
            if (SnapshotList.ItemsSource is IEnumerable<SnapshotRow> rows && rows.FirstOrDefault(row => row.Path == path) is { } row
                && SnapshotList.ItemContainerGenerator.ContainerFromItem(row) is ListBoxItem item) item.Focus();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>A snapshot action's result, in the card (M13c): Settings-started actions say what happened where the user looks.</summary>
    public void ShowSnapshotNotice(string message, bool failed)
    {
        SnapshotsStatus.Text = message;
        SnapshotsStatus.Foreground = failed ? System.Windows.Media.Brushes.IndianRed : SecondaryText;
        SnapshotsStatus.Visibility = Visibility.Visible;
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(SnapshotsStatus)
            .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    /// <summary>Tray → Restore snapshot → "More in Settings…": the Snapshots card in view (M13c).</summary>
    public void ShowSnapshotsCard() =>
        Dispatcher.BeginInvoke(() => SnapshotsCard.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);

    /// <summary>The snapshot list, keeping the selection where the same file is still listed.</summary>
    private void ShowSnapshots(IReadOnlyList<NeoFences.Core.Config.SnapshotEntry> snapshots)
    {
        var selectedPath = SelectedSnapshot?.Path;
        var rows = snapshots.Select(entry => new SnapshotRow(entry.Path, entry.Name, entry.TakenAt.ToLocalTime().ToString("d MMM yyyy, HH:mm"))).ToList();
        if (SnapshotList.ItemsSource is IEnumerable<SnapshotRow> shown && shown.SequenceEqual(rows))
        {
            FocusSnapshotRow(); // a refresh with nothing new keeps the list; a pending focus is used now, never left for later (final review I1)
            return;
        }
        var hadFocus = SnapshotList.IsKeyboardFocusWithin;
        SnapshotList.ItemsSource = rows;
        SnapshotList.SelectedItem = rows.FirstOrDefault(row => row.Path == selectedPath);
        UpdateSnapshotButtons();
        if (hadFocus && _focusSnapshotPath is null && SelectedSnapshot is { } selected) _focusSnapshotPath = selected.Path;
        FocusSnapshotRow();
    }

    public void Show(SettingsView view)
    {
        ShowSnapshots(view.Snapshots);
        ShowRules(view);
        _updating = true;
        LabelsBox.SelectedIndex = view.DefaultLabels == LabelMode.OnHover ? 1 : 0;
        ArrowsBox.IsChecked = view.ShowShortcutArrows;
        if (!view.PeekHotkeyActive && !HotkeyBox.IsKeyboardFocused)
        {
            ShowHotkeyResult(saved: false, message: $"{view.PeekHotkey} is not active: Windows or another app owns it. Record another combination.");
        }
        StartupBox.IsChecked = view.StartWithWindows;
        TakeoverBox.IsChecked = view.Takeover;
        HotkeyBox.Text = view.PeekHotkey;
        RollupBox.SelectedIndex = view.RollupExpand == RollupExpand.Click ? 1 : 0;
        GameModeBox.IsChecked = view.GameModeEnabled;
        ShowLibrary(view.Library);
        ShowAppearance(view.Appearance);
        AutoUpdateBox.IsChecked = view.Updates.AutoUpdate; // M17
        AutoUpdateBox.IsEnabled = view.Updates.Available;
        CheckUpdatesButton.IsEnabled = view.Updates.Available;
        UpdateStatus.Text = view.Updates.Status;
        RestartToUpdateButton.Visibility = view.Updates.ReadyVersion is null ? Visibility.Collapsed : Visibility.Visible;
        RestartToUpdateButton.Content = view.Updates.ReadyVersion is { } ready ? $"Restart to update to v{ready}" : "";
        GameModeStatus.Text = !view.GameModeEnabled ? "Off: NeoFences stays fully active during games."
            : view.GameModeActive ? "Right now: idle, a full-screen app is in front." : "Right now: active (no full-screen app in front).";
        VersionText.Text = $"NeoFences {view.Version}";
        DataFolderText.Text = $"Settings, backups and logs: {view.DataFolder}";
        _updating = false;
    }

    /// <summary>The host's answer to <see cref="PeekHotkeyChosen"/>: saved, or why not (invalid, or taken by another app).</summary>
    public void ShowHotkeyResult(bool saved, string message)
    {
        var changed = HotkeyStatus.Text != message || HotkeyStatus.Visibility != Visibility.Visible;
        HotkeyStatus.Tag = saved ? null : "error"; // an error stays visible after the box loses focus
        HotkeyStatus.Text = message;
        HotkeyStatus.Foreground = saved ? SecondaryText : System.Windows.Media.Brushes.IndianRed;
        HotkeyStatus.Visibility = Visibility.Visible;
        if (changed) AnnounceHotkeyStatus(); // Settings refreshes often (game mode): say a warning once
    }

    private void AnnounceHotkeyStatus() =>
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(HotkeyStatus)
            .RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);

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
        var key = pressed.Key == Key.System ? pressed.SystemKey : pressed.Key; // Alt combinations arrive as Key.System
        // Tab / Shift+Tab move on and Alt+F4 closes, as everywhere: never trap them in the box (M6b review I1).
        if (key == Key.Tab && (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None) return;
        if (key == Key.F4 && Keyboard.Modifiers == ModifierKeys.Alt) return;
        pressed.Handled = true;
        key = key switch { Key.ImeProcessed => pressed.ImeProcessedKey, Key.DeadCharProcessed => pressed.DeadCharProcessedKey, _ => key };
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

`build/pack.ps1`:
```powershell
# Builds the NeoFences installer (M7, ADR-023): a self-contained win-x64 publish packed by Velopack into
# artifacts\releases (NeoFences.App-win-Setup.exe, the full package, and RELEASES). Run from the repo root:
#   powershell -NoProfile -ExecutionPolicy Bypass -File build\pack.ps1 -Version 1.0.0
# -OutputDir (M17): another releases folder (a local update rehearsal, or CI); default artifacts\releases.
# -ReleaseNotes (M17): a markdown file shown with the release (CI writes it from the commits).
param([Parameter(Mandatory)][string]$Version, [string]$OutputDir, [string]$ReleaseNotes)
$ErrorActionPreference = 'Stop'
# Checked first (M8a): vpk wants SemVer2, and would otherwise fail only after the tests and the publish.
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(-[0-9A-Za-z.-]+)?$') { throw "Version '$Version' is not SemVer (like 1.1.0 or 1.1.0-beta.1)." }
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'artifacts\publish'
$releases = if ($OutputDir) { $OutputDir } else { Join-Path $root 'artifacts\releases' }
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
  $notes = if ($ReleaseNotes) { @('--releaseNotes', (Resolve-Path $ReleaseNotes).Path) } else { @() }
  dotnet vpk pack --packId NeoFences.App --packVersion $Version --packTitle NeoFences --packAuthors NeoFences `
    --packDir $publish --mainExe NeoFences.exe --icon (Join-Path $root 'src\NeoFences.App\NeoFences.ico') --outputDir $releases @notes
  if ($LASTEXITCODE -ne 0) { throw 'vpk pack failed' }
  Get-ChildItem $releases | Sort-Object LastWriteTime | Select-Object -Last 4 Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table | Out-String
}
finally {
  Pop-Location # the caller's folder is unchanged, even when a step fails
}
```

`.github/workflows/ci.yml`:
```yaml
# Every push to main and every pull request: build and run all tests on Windows (M17, ADR-039).
name: CI
on:
  push:
    branches: [main]
  pull_request:
permissions:
  contents: read
jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - run: dotnet build NeoFences.slnx -c Release
      - run: dotnet test NeoFences.slnx -c Release --no-build
```

`.github/workflows/release.yml`:
```yaml
# A pushed version tag (v1.8.0) builds, tests and packs NeoFences and uploads it as a DRAFT release (M17, ADR-039).
# The draft is installed and checked on the developer PC, then published; only then do installed copies update.
name: Release
on:
  push:
    tags: ['v*.*.*']
permissions:
  contents: write # the draft release; the workflow's own token, no stored secret
jobs:
  release:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0 # every tag: the release notes list the commits since the previous one
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - name: Version from the tag
        id: version
        shell: pwsh
        run: '"version=$("${{ github.ref_name }}".TrimStart("v"))" >> $env:GITHUB_OUTPUT'
      - name: Previous release (makes a small delta update)
        shell: pwsh
        continue-on-error: true # none before the first release
        run: |
          dotnet tool restore
          dotnet vpk download github --repoUrl "https://github.com/${{ github.repository }}" --token "${{ secrets.GITHUB_TOKEN }}" --outputDir artifacts/releases
      - name: Release notes (feat and fix commits since the previous tag)
        shell: pwsh
        run: |
          $previous = git describe --tags --abbrev=0 "${{ github.ref_name }}^" 2>$null
          $range = if ($previous) { "$previous..${{ github.ref_name }}" } else { "${{ github.ref_name }}" }
          $lines = git log $range --format='%s' | Where-Object { $_ -match '^(feat|fix):' } | ForEach-Object { '- ' + ($_ -replace '^(feat|fix):\s*', '') }
          New-Item -ItemType Directory -Force artifacts | Out-Null
          Set-Content artifacts/release-notes.md -Encoding utf8 -Value (@("## NeoFences ${{ github.ref_name }}", '') + $lines)
      - name: Build, test and pack
        shell: pwsh
        run: ./build/pack.ps1 -Version "${{ steps.version.outputs.version }}" -ReleaseNotes artifacts/release-notes.md
      # Stage 2 (ADR-039): code signing goes here (vpk pack --signParams / --azureTrustedSignFile) once a certificate exists.
      - name: Upload as a draft release
        shell: pwsh
        run: dotnet vpk upload github --repoUrl "https://github.com/${{ github.repository }}" --token "${{ secrets.GITHUB_TOKEN }}" --outputDir artifacts/releases --tag "${{ github.ref_name }}" --releaseName "NeoFences ${{ github.ref_name }}"
```

- [ ] **Step 2: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 438`.

- [ ] **Step 3: Commit**

```powershell
git add src build .github
git commit -m "feat: added auto-update from github releases, settings updates and the release workflows"
```

---

### Task 3: Verification and docs

- [ ] **Step 1: Add section AC to the end of `docs/TEST-CHECKLIST.md`**

```markdown

## AC — Public releases and auto-update (M17, ADR-039)
| ID | Steps | Expected |
|---|---|---|
| AC1 | A friend's PC: download Setup from the Releases page, run it | one SmartScreen "More info → Run anyway"; NeoFences installs for the user and starts |
| AC2 | A new release is published; wait (or Settings → Updates → Check now) | a notice "NeoFences x.y.z is ready"; the tray's first item "Restart to update to vx.y.z" |
| AC3 | Click "Restart to update" | icons come back, NeoFences closes, the new version starts within seconds; fences and settings as before |
| AC4 | Ignore the notice, then tray → Exit; start NeoFences again | the new version runs |
| AC5 | Sign out / shut down while an update waits | sign-out is as fast as before; the update installs on the next normal exit |
| AC6 | Settings → Updates → switch off; restart; watch the network (Resource Monitor) | no connection to github.com from NeoFences |
| AC7 | Offline: Check now | "Couldn't check for updates: offline…"; it tries again later; nothing else changes |
| AC8 | A full-screen game for over a day of uptime | no check and no notice while the game is in front |
| AC9 | A developer build (dotnet run) | Settings → Updates says updates are off in a developer build |
| AC10 | Push a version tag | GitHub Actions builds, tests and uploads a draft release with notes; installed copies see nothing until it is published |
```

- [ ] **Step 2: Append ADR-039 to `docs/DECISIONS.md`**

```markdown

## ADR-039 — Public repository, CI draft releases, Velopack auto-update; signing deferred (M17, v1.8.0)
**Date:** 2026-10-04 · **Status:** Accepted · **Spec:** `docs/superpowers/specs/2026-10-04-public-releases-design.md` · **Builds on:** ADR-023

**Context.** Friends should install NeoFences from GitHub and receive updates by themselves. ADR-023 shipped an installer
with no update source.

**Decision.**
- **One public repository** (`github.com/CipherSnowden/neo-fences`) with code, docs, history and releases; **not open
  source** (no license file: all rights reserved). Before the first push the history was rewritten: every commit uses the
  account's GitHub noreply address, and the private project-hub link was removed from every commit (it lives in the
  git-ignored `CLAUDE.local.md`). A scan of all commits found no personal address, hub link, token or key. The
  unscrubbed original is a local mirror only.
- **Releases from GitHub Actions:** `ci.yml` builds and tests every push; `release.yml` turns a pushed `v*.*.*` tag into a
  **draft** release (delta from the previous release, notes from the `feat:`/`fix:` commits), using the workflow's own
  token. A draft is installed and checked on the developer PC, then published; installed copies see only published releases.
- **Auto-update with Velopack** (already a dependency): `UpdatePolicy` (Core) checks at start and every 24 h, 6 h after a
  failure, never in game mode, never when switched off (`Settings.AutoUpdate`, schema 4: then no network calls) and never
  in a developer build. Updates download quietly; "Restart to update" or the next normal exit applies them, always after
  NeoFences' clean exit (hard rule 2); never at sign-out or shutdown. A local releases folder (`NEOFENCES_UPDATE_SOURCE`)
  allows a full rehearsal without GitHub.
- **Signing deferred:** not required. Friends see SmartScreen once at first install; updates never do. Smart App Control
  blocks unsigned apps; revisit then. Free OSS signing needs an open-source license; otherwise a paid certificate. The
  release workflow keeps a marked place for the signing step.

**Consequences.** Every release is two outward-facing steps (push the tag; publish the draft), each asked for. The parked
search palette, if resumed, takes schema 5.
```

- [ ] **Step 3: Write `docs/research/m17-public-releases.md`**

```markdown
# M17 — Public releases and auto-update: results

**Date:** 2026-10-04 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.7.1 installed.
**Checklist:** `docs/TEST-CHECKLIST.md` section AC · **Decision:** ADR-039.

## Making the repository public (done before the first push, with the user's explicit go)

| Step | Result |
|---|---|
| Mirror backup of every branch and tag | 21 refs; local only. |
| History rewrite (`git filter-branch`, 376 commits, ~3 min) | every author/committer now the GitHub noreply address; the private hub link replaced in every commit. |
| Final scan of all 377 commits (contents, messages, authors) for the personal address, name, hub link/id, Claude links, GitHub/Anthropic tokens, AWS keys, private keys | **0 hits**. |
| Push | `main`, the parked `m15-search-palette` branch and 14 tags to the public repo. |

## Local update rehearsal (the user's go; installed 1.7.1 and config restored afterwards)

Prototype packed as 1.8.0 and 1.8.1 (with a delta) into a local folder; 1.8.0 installed silently and restarted with
`NEOFENCES_UPDATE_SOURCE` pointing at the folder.

| Check | Result |
|---|---|
| Settings → Updates → Check now | **Pass**: "Version 1.8.1 is ready" after ~12 s (download); button "Restart to update to v1.8.1". |
| Restart to update | **Pass**: icons shown, clean exit, "update 1.8.1 will install now that NeoFences exits (restart: true)"; 1.8.1 running 1.6 s later, config loaded, icons hidden again. |
| Data | 2 fences before and after; 0 warnings or errors. |
| Restore | 1.7.1 reinstalled and running; config byte-identical; no test-made backup left. |

`pack.ps1 -ReleaseNotes` checked locally: the notes are embedded in the package.

## Core (test-first)

7 new tests (438 in all): first check at start, then daily; 6 h after a failure; never when off, in a developer build
or in game mode; when the next check is due; `AutoUpdate` on by default, schema 4, round trip.
```

- [ ] **Step 4: Update the other docs**
  - `ROADMAP.md`:
    - the claim line becomes `- [x] M17 — done <date> (ADR-039, research/m17-public-releases.md)`;
    - add `- [ ] v1.8.0: first public release (tag → draft → install check → publish)`;
    - tick the M7 line "Later: update source (GitHub Releases), code signing" as `update source — M17; signing deferred (ADR-039)`.
  - `FEATURES.md`: the installer row gets "auto-update from GitHub Releases (v1.8, ADR-039)".
  - `ARCHITECTURE.md`: add an "M17 complete" line.
  - `SETUP.md`: add "Releasing": tag push → draft → install check → publish; the local rehearsal with `NEOFENCES_UPDATE_SOURCE`.
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 5: Refresh the hub** (`node --check`, publish with `url` from `CLAUDE.local.md`).

- [ ] **Step 6: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-039, update checks and results, and recorded M17"
```
