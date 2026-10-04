# M1 — Core + Config Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `NeoFences.Core`, the pure C# heart of NeoFences, with xUnit tests. It covers the config model, JSON persistence with atomic saves, backups and corrupt-file recovery, per-display-configuration layouts, and fence membership (which desktop item lives in which fence).

**Architecture:** Immutable records plus pure static functions. Every operation takes a `NeoFencesConfig` and returns a new one, so later milestones can call it from the UI thread, diff it, and save it. The only I/O is `ConfigStore`, which works on a directory it is given. Nothing references Windows, WPF or COM (CLAUDE.md hard rule 4); M2's `NeoFences.Shell` feeds Core plain strings and monitor sizes.

**Tech Stack:** .NET 10 SDK 10.0.401, C# (net10.0 class library), System.Text.Json (in-box, reflection-based), xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-10-02-neofences-v1-design.md` §5 (data model and persistence) and §7 (reliability rules). Also read `docs/DECISIONS.md` ADR-002, ADR-006 and ADR-011, and `CLAUDE.md`.

**Pre-verified:** every code block below was compiled and run together on 2026-10-02 (0 errors, 0 warnings, **58/58 tests pass**). The test files were written first and the implementations made them pass. The plan keeps that order: in every task the test file comes first and must fail before the implementation is written.

## Global Constraints

- `NeoFences.Core` targets `net10.0` (not `-windows`) and references nothing Windows-specific: no WPF, no CsWin32, no `Microsoft.Win32` (hard rule 4).
- No new NuGet dependency (hard rule 6). The test project uses only what `dotnet new xunit` adds.
- Config lives in one JSON file `config.json`. Saves are atomic: write a `.tmp`, then `File.Replace` keeps a `.bak`. One backup per day goes in `backups/`, newest 10 kept. A corrupt file is kept as `config.corrupt-<yyyyMMdd-HHmmss>.json` (spec §5, ADR-006).
- Never lose or hide user files (hard rule 1). Deleting a fence moves its items to the Inbox, and exactly one Inbox always exists.
- `Settings.Takeover` defaults to **false** until the M2 sign-out test passes (ADR-011).
- Item refs are shell parsing names and compare **case-insensitively** (`ItemRef.Comparer`).
- Fence rects are DIPs relative to their monitor's work area. Layouts are keyed by display fingerprint (spec §5).
- Debouncing saves (500 ms) is **not** Core's job; M2's App does it with a dispatcher timer.
- Commits: single line, Conventional Commits, past tense, **no** `Co-Authored-By` trailer, one logical change each.
- Every test command: `dotnet test NeoFences.slnx` from the repo root. Never run `dotnet test` without the solution argument, because the root also contains `spikes/M0.slnx`.

## Review Focus

Five real-world conditions the spec implies but doesn't spell out, each with the test that pins it:

1. **Config written by a newer NeoFences** (`schemaVersion` 99, e.g. after a downgrade). Expected: the app starts with defaults and **never overwrites** that file. Test: `Load_ConfigFromNewerVersion_IsReadOnly_AndSaveNeverOverwritesIt` (Task 4).
2. **Hand-edited or damaged config:** no Inbox, two Inboxes, duplicate fence ids, explicit nulls, a missing `iconSize`, one item in two fences. Expected: repaired on load, no item lost. Tests: `ConfigNormalizerTests` (Task 3), `Deserialize_MissingPropertiesKeepTheirDefaults` (Task 2).
3. **Write fails mid-save** (disk full, AV lock). Expected: the existing `config.json` is byte-for-byte untouched. Test: `Save_WhenTempFileCannotBeWritten_Fails_AndExistingConfigIsUntouched` (Task 4).
4. **Windows path casing:** `Crysis 2.lnk` vs `CRYSIS 2.LNK`, and case-only renames. Expected: the same item, the fence is kept, the shell's spelling is adopted. Tests: `Reconcile_CaseDifferences_AreTheSameItem_AndAdoptShellSpelling`, `RenameItem_KeepsFenceAndPosition_IncludingCaseOnlyRename` (Task 6).
5. **Monitor unplugged or resolution changed.** Expected: fences move to the primary monitor, scaled and fully on-screen; the old layout comes back exactly when the setup returns. Tests: `MonitorUnplugged_FenceMovesToPrimary_ScaledAndFullyOnScreen`, `NewResolution_ScalesFromLastLayout_AndKeepsOldLayoutForLater` (Task 5).

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `NeoFences.slnx` | root solution: Core + Core.Tests (spikes stay in `spikes/M0.slnx`) | 1 |
| `src/NeoFences.Core/NeoFences.Core.csproj` | net10.0 class library | 1 |
| `src/NeoFences.Core/Model/Fence.cs` | `Fence`, `FenceSource`, `FenceSourceKind`, `FenceSort` | 1 |
| `src/NeoFences.Core/Model/Settings.cs` | `Settings`, `RollupExpand` | 1 |
| `src/NeoFences.Core/Model/Layout.cs` | `Layout`, `FenceRect`, `MonitorArea` | 1 |
| `src/NeoFences.Core/Model/NeoFencesConfig.cs` | root record, `CreateDefault`, `Inbox`, `WithFence` | 1 |
| `src/NeoFences.Core/Model/ItemRef.cs` | case-insensitive item-ref comparer | 1 |
| `src/NeoFences.Core/Config/ConfigJson.cs` | JSON shape (camelCase, enum strings) | 2 |
| `src/NeoFences.Core/Config/ConfigNormalizer.cs` | repair loaded configs | 3 |
| `src/NeoFences.Core/Config/ConfigStore.cs` | atomic save, backups, recovery chain, newer-schema lock | 4 |
| `src/NeoFences.Core/Layouts/DisplayMonitor.cs` | `DisplayMonitor`, `DisplayFingerprint` | 5 |
| `src/NeoFences.Core/Layouts/LayoutEngine.cs` | resolve/adapt/clamp/place layouts | 5 |
| `src/NeoFences.Core/Membership/FenceMembership.cs` | reconcile + add/remove/rename/move items, create/delete fences | 6 |
| `tests/NeoFences.Core.Tests/...` | one test file per source file, plus `TestSupport/TempDirectory.cs` | 1–6 |
| `docs/DECISIONS.md`, `docs/ARCHITECTURE.md`, `docs/SETUP.md`, `docs/ROADMAP.md`, `docs/SESSION-LOG.md`, `docs/hub/neofences-hq.html` | docs sync | 7 |

---

### Task 1: Solution, Core project and model

**Files:**
- Modify: `docs/ROADMAP.md` (claim M1)
- Create: `NeoFences.slnx`, `src/NeoFences.Core/NeoFences.Core.csproj`, `tests/NeoFences.Core.Tests/NeoFences.Core.Tests.csproj`
- Create: `src/NeoFences.Core/Model/{Fence,Settings,Layout,NeoFencesConfig,ItemRef}.cs`
- Test: `tests/NeoFences.Core.Tests/Model/NeoFencesConfigTests.cs`

**Interfaces:**
- Produces: `Fence` (record: `required string Id`, `required string Title`, `FenceSource Source`, `IReadOnlyList<string> Items`, `bool IsInbox`, `FenceSort Sort`, `int IconSize` = 48, `bool RolledUp`, `bool Locked`; static `Create(string title, FenceSource? source = null)`, `NewId()`); `FenceSource(FenceSourceKind Kind, string? Path = null)` with `FenceSource.Desktop` and `FenceSource.Portal(string folderPath)`; `enum FenceSourceKind { Desktop, Portal }`; `enum FenceSort { Manual, Name, Type, Date }`; `Settings` (`Takeover` = false, `PeekHotkey` = "Ctrl+Alt+Space", `StartWithWindows` = true, `GameMode` = true, `RollupExpand` = Hover); `enum RollupExpand { Hover, Click }`; `FenceRect(string Monitor, double X, double Y, double W, double H)`; `MonitorArea(double WorkWidth, double WorkHeight)`; `Layout` (`Monitors`, `Fences` dictionaries); `NeoFencesConfig` (`SchemaVersion`, `Settings`, `Fences`, `Layouts`, `LastLayoutFingerprint`, `[JsonIgnore] Inbox`, `CreateDefault()`, `WithFence(Fence updated)`, const `CurrentSchemaVersion` = 1); `ItemRef.Comparer` (OrdinalIgnoreCase).

- [ ] **Step 1: Branch and claim**

```powershell
git switch -c m1-core-config
```
In `docs/ROADMAP.md`, change the M1 heading line `## M1 — Core + config` to:
```
## M1 — Core + config — [~] claimed by session 2026-10-02 m1-core
```
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M1 core and config"
```

- [ ] **Step 2: Create projects from templates**

```powershell
dotnet new classlib -n NeoFences.Core -o src/NeoFences.Core
dotnet new xunit -n NeoFences.Core.Tests -o tests/NeoFences.Core.Tests
Remove-Item src/NeoFences.Core/Class1.cs, tests/NeoFences.Core.Tests/UnitTest1.cs
dotnet new sln -n NeoFences --format slnx
dotnet sln NeoFences.slnx add src/NeoFences.Core/NeoFences.Core.csproj tests/NeoFences.Core.Tests/NeoFences.Core.Tests.csproj
dotnet add tests/NeoFences.Core.Tests reference src/NeoFences.Core
```
`src/NeoFences.Core/NeoFences.Core.csproj` stays exactly as the template made it:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```
`tests/NeoFences.Core.Tests/NeoFences.Core.Tests.csproj` should now read:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\NeoFences.Core\NeoFences.Core.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Write the failing test** — `tests/NeoFences.Core.Tests/Model/NeoFencesConfigTests.cs`

```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Model;

public class NeoFencesConfigTests
{
    [Fact]
    public void Default_HasExactlyOneEmptyInbox()
    {
        var config = NeoFencesConfig.CreateDefault();

        var inbox = Assert.Single(config.Fences);
        Assert.True(inbox.IsInbox);
        Assert.Equal("Inbox", inbox.Title);
        Assert.Empty(inbox.Items);
        Assert.Same(inbox, config.Inbox);
    }

    [Fact]
    public void Default_TakeoverIsOff_UntilSignOutTestPasses()
    {
        // ADR-011: Takeover must not ship enabled by default until C8 passes.
        Assert.False(NeoFencesConfig.CreateDefault().Settings.Takeover);
    }

    [Fact]
    public void Default_UsesSpecSettings()
    {
        var settings = NeoFencesConfig.CreateDefault().Settings;

        Assert.Equal("Ctrl+Alt+Space", settings.PeekHotkey);
        Assert.True(settings.StartWithWindows);
        Assert.True(settings.GameMode);
        Assert.Equal(RollupExpand.Hover, settings.RollupExpand);
    }

    [Fact]
    public void NewFences_GetUniqueIds()
    {
        Assert.NotEqual(Fence.Create("A").Id, Fence.Create("B").Id);
    }

    [Fact]
    public void WithFence_ReplacesByIdAndKeepsOrder()
    {
        var games = Fence.Create("Games");
        var config = NeoFencesConfig.CreateDefault() with { Fences = [NeoFencesConfig.CreateDefault().Inbox, games] };

        var updated = config.WithFence(games with { Title = "Games 2" });

        Assert.Equal(["Inbox", "Games 2"], updated.Fences.Select(fence => fence.Title));
        Assert.Equal("Games", config.Fences[1].Title); // original untouched
    }
}
```

- [ ] **Step 4: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS with `CS0234: The type or namespace name 'Model' does not exist in the namespace 'NeoFences.Core'`.

- [ ] **Step 5: Implement the model**

`src/NeoFences.Core/Model/Fence.cs`:
```csharp
namespace NeoFences.Core.Model;

public enum FenceSourceKind { Desktop, Portal }

/// <summary>Where a fence's items come from: the desktop (Takeover) or a folder (Portal).</summary>
public sealed record FenceSource(FenceSourceKind Kind, string? Path = null)
{
    public static FenceSource Desktop { get; } = new(FenceSourceKind.Desktop);

    public static FenceSource Portal(string folderPath) => new(FenceSourceKind.Portal, folderPath);
}

public enum FenceSort { Manual, Name, Type, Date }

/// <summary>
/// One fence. Desktop fences keep an ordered list of item refs (shell parsing names: file paths or
/// "::{GUID}" for virtual items). Portal fences keep no items; they mirror <see cref="FenceSource.Path"/>.
/// </summary>
public sealed record Fence
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public FenceSource Source { get; init; } = FenceSource.Desktop;
    public IReadOnlyList<string> Items { get; init; } = [];
    public bool IsInbox { get; init; }
    public FenceSort Sort { get; init; } = FenceSort.Manual;
    public int IconSize { get; init; } = 48;
    public bool RolledUp { get; init; }
    public bool Locked { get; init; }

    public static Fence Create(string title, FenceSource? source = null) =>
        new() { Id = NewId(), Title = title, Source = source ?? FenceSource.Desktop };

    public static string NewId() => Guid.NewGuid().ToString("N");
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
    public string PeekHotkey { get; init; } = "Ctrl+Alt+Space";
    public bool StartWithWindows { get; init; } = true;
    public bool GameMode { get; init; } = true;
    public RollupExpand RollupExpand { get; init; } = RollupExpand.Hover;
}
```

`src/NeoFences.Core/Model/Layout.cs`:
```csharp
namespace NeoFences.Core.Model;

/// <summary>A fence's rectangle in DIPs, relative to the top-left of its monitor's work area.</summary>
public sealed record FenceRect(string Monitor, double X, double Y, double W, double H);

/// <summary>Work-area size (DIPs) a layout was saved for, so it can be scaled to another monitor.</summary>
public sealed record MonitorArea(double WorkWidth, double WorkHeight);

/// <summary>Fence positions for one display configuration (see <c>DisplayFingerprint</c>).</summary>
public sealed record Layout
{
    public IReadOnlyDictionary<string, MonitorArea> Monitors { get; init; } = new Dictionary<string, MonitorArea>();
    public IReadOnlyDictionary<string, FenceRect> Fences { get; init; } = new Dictionary<string, FenceRect>();
}
```

`src/NeoFences.Core/Model/NeoFencesConfig.cs`:
```csharp
using System.Text.Json.Serialization;

namespace NeoFences.Core.Model;

/// <summary>Everything NeoFences persists, stored as <c>config.json</c> (ADR-006).</summary>
public sealed record NeoFencesConfig
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Settings Settings { get; init; } = new();
    public IReadOnlyList<Fence> Fences { get; init; } = [];
    public IReadOnlyDictionary<string, Layout> Layouts { get; init; } = new Dictionary<string, Layout>();

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

`src/NeoFences.Core/Model/ItemRef.cs`:
```csharp
namespace NeoFences.Core.Model;

/// <summary>Item refs are Windows shell parsing names: paths compare case-insensitively.</summary>
public static class ItemRef
{
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;
}
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 5`

- [ ] **Step 7: Commit**

```powershell
git add NeoFences.slnx src tests
git commit -m "feat: added core config model with inbox and default settings"
```

---

### Task 2: JSON shape

**Files:**
- Test: `tests/NeoFences.Core.Tests/Config/ConfigJsonTests.cs`
- Create: `src/NeoFences.Core/Config/ConfigJson.cs`

**Interfaces:**
- Consumes: the Task 1 model.
- Produces: `ConfigJson.Serialize(NeoFencesConfig config) -> string`; `ConfigJson.Deserialize(string json) -> NeoFencesConfig` (throws `JsonException` on invalid text, `null`, or a fence without `id`). The raw result may contain explicit nulls; Task 3 normalizes them.

Why reflection-based instead of ADR-006's "source-generated": the System.Text.Json source generator assigns **every** init-only property in its object initializer. A property missing from a hand-edited file would get `default` (e.g. `iconSize: 0`) instead of the record's initializer value. `Deserialize_MissingPropertiesKeepTheirDefaults` pins this; Task 7 amends ADR-006.

- [ ] **Step 1: Write the failing test** — `tests/NeoFences.Core.Tests/Config/ConfigJsonTests.cs`

```csharp
using System.Text.Json;
using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Config;

public class ConfigJsonTests
{
    private static NeoFencesConfig SampleConfig()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true, Items = [@"C:\Users\cipher\Desktop\notes.txt"] };
        var games = Fence.Create("Games") with
        {
            Items = [@"C:\Users\cipher\Desktop\Crysis 2.lnk", "::{645FF040-5081-101B-9F08-00AA002F954E}"],
            IconSize = 64,
            RolledUp = true,
        };
        var screenshots = Fence.Create("Screenshots", FenceSource.Portal(@"D:\Pictures\Screenshots")) with { Sort = FenceSort.Date };
        var layout = new Layout
        {
            Monitors = new Dictionary<string, MonitorArea> { ["DELL"] = new(2560, 1392) },
            Fences = new Dictionary<string, FenceRect> { [games.Id] = new("DELL", 40, 60, 420, 260) },
        };
        return new NeoFencesConfig
        {
            Fences = [inbox, games, screenshots],
            Layouts = new Dictionary<string, Layout> { ["1mon:DELL-3840x2160@150%"] = layout },
            LastLayoutFingerprint = "1mon:DELL-3840x2160@150%",
        };
    }

    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var original = SampleConfig();

        var restored = ConfigJson.Deserialize(ConfigJson.Serialize(original));

        Assert.Equal(original.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(original.Settings, restored.Settings);
        Assert.Equal(original.LastLayoutFingerprint, restored.LastLayoutFingerprint);
        Assert.Equal(original.Fences.Count, restored.Fences.Count);
        for (var fenceIdx = 0; fenceIdx < original.Fences.Count; fenceIdx++)
        {
            var expected = original.Fences[fenceIdx];
            var actual = restored.Fences[fenceIdx];
            Assert.Equal(expected with { Items = [] }, actual with { Items = [] });
            Assert.Equal(expected.Items, actual.Items);
        }
        var restoredLayout = restored.Layouts["1mon:DELL-3840x2160@150%"];
        Assert.Equal(new MonitorArea(2560, 1392), restoredLayout.Monitors["DELL"]);
        Assert.Equal(new FenceRect("DELL", 40, 60, 420, 260), restoredLayout.Fences[original.Fences[1].Id]);
    }

    [Fact]
    public void Json_UsesCamelCaseNamesAndEnumValues_AndOmitsComputedInbox()
    {
        var json = ConfigJson.Serialize(SampleConfig());
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.False(root.GetProperty("settings").GetProperty("takeover").GetBoolean());
        Assert.False(root.TryGetProperty("inbox", out _));
        var screenshots = root.GetProperty("fences")[2];
        Assert.Equal("portal", screenshots.GetProperty("source").GetProperty("kind").GetString());
        Assert.Equal(@"D:\Pictures\Screenshots", screenshots.GetProperty("source").GetProperty("path").GetString());
        Assert.Equal("date", screenshots.GetProperty("sort").GetString());
        var rect = root.GetProperty("layouts").GetProperty("1mon:DELL-3840x2160@150%").GetProperty("fences").EnumerateObject().Single().Value;
        Assert.Equal("DELL", rect.GetProperty("monitor").GetString());
        Assert.Equal(420, rect.GetProperty("w").GetDouble());
    }

    [Fact]
    public void Deserialize_AcceptsMinimalDocument()
    {
        var config = ConfigJson.Deserialize("""{ "schemaVersion": 1 }""");

        Assert.Empty(config.Fences);
        Assert.Equal(new Settings(), config.Settings);
    }

    [Fact]
    public void Deserialize_MissingPropertiesKeepTheirDefaults()
    {
        var config = ConfigJson.Deserialize("""
            { "schemaVersion": 1, "settings": { "takeover": true },
              "fences": [ { "id": "f1", "title": "Games" } ] }
            """);

        Assert.True(config.Settings.Takeover);
        Assert.Equal("Ctrl+Alt+Space", config.Settings.PeekHotkey);
        var fence = Assert.Single(config.Fences);
        Assert.Equal(48, fence.IconSize);
        Assert.Equal(FenceSource.Desktop, fence.Source);
        Assert.Empty(fence.Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{ "fences": [ { "title": "no id" } ] }""")]
    public void Deserialize_RejectsInvalidDocuments(string json)
    {
        Assert.ThrowsAny<JsonException>(() => ConfigJson.Deserialize(json));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS with `CS0234: The type or namespace name 'Config' does not exist in the namespace 'NeoFences.Core'`.

- [ ] **Step 3: Implement** — `src/NeoFences.Core/Config/ConfigJson.cs`

```csharp
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

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 13`

- [ ] **Step 5: Commit**

```powershell
git add src/NeoFences.Core/Config/ConfigJson.cs tests/NeoFences.Core.Tests/Config/ConfigJsonTests.cs
git commit -m "feat: added config json serialization with camelcase enums"
```

---

### Task 3: Normalizing loaded configs

**Files:**
- Test: `tests/NeoFences.Core.Tests/Config/ConfigNormalizerTests.cs`
- Create: `src/NeoFences.Core/Config/ConfigNormalizer.cs`

**Interfaces:**
- Consumes: the model, `ItemRef.Comparer`, `ConfigJson.Deserialize`.
- Produces: `ConfigNormalizer.Normalize(NeoFencesConfig config) -> NeoFencesConfig`, which guarantees:
  - exactly one Inbox (first `IsInbox` wins; inserted at index 0 if none);
  - unique non-blank fence ids;
  - non-null `Settings` / `Fences` / `Layouts` / `Items` / `Title` / `Source`, and `PeekHotkey` not blank;
  - `IconSize` in `ConfigNormalizer.IconSizes` (32/48/64/96), else 48;
  - each desktop item in only its first fence (case-insensitive); blank items dropped; portal fences hold no items.

- [ ] **Step 1: Write the failing test** — `tests/NeoFences.Core.Tests/Config/ConfigNormalizerTests.cs`

```csharp
using NeoFences.Core.Config;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Config;

public class ConfigNormalizerTests
{
    private static Fence DesktopFence(string title, params string[] items) => Fence.Create(title) with { Items = items };

    [Fact]
    public void MissingInbox_IsAddedFirst_OtherFencesKept()
    {
        var games = DesktopFence("Games", "a.lnk");

        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [games] });

        Assert.Equal(2, normalized.Fences.Count);
        Assert.True(normalized.Fences[0].IsInbox);
        Assert.Equal(games with { Items = [] }, normalized.Fences[1] with { Items = [] });
        Assert.Equal(["a.lnk"], normalized.Fences[1].Items);
    }

    [Fact]
    public void TwoInboxes_FirstStaysInbox_SecondBecomesNormalFenceWithItems()
    {
        var first = DesktopFence("Inbox", "a.txt") with { IsInbox = true };
        var second = DesktopFence("Inbox 2", "b.txt") with { IsInbox = true };

        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [first, second] });

        Assert.True(normalized.Fences[0].IsInbox);
        Assert.False(normalized.Fences[1].IsInbox);
        Assert.Equal(["b.txt"], normalized.Fences[1].Items);
    }

    [Fact]
    public void DuplicateFenceIds_LaterOneGetsNewId()
    {
        var inbox = DesktopFence("Inbox") with { IsInbox = true };
        var copy = DesktopFence("Copy") with { Id = inbox.Id };

        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [inbox, copy] });

        Assert.Equal(inbox.Id, normalized.Fences[0].Id);
        Assert.NotEqual(inbox.Id, normalized.Fences[1].Id);
        Assert.Equal("Copy", normalized.Fences[1].Title);
    }

    [Fact]
    public void ItemInTwoFences_StaysOnlyInFirst_ComparedCaseInsensitively()
    {
        var inbox = DesktopFence("Inbox", @"C:\Desktop\Crysis.lnk") with { IsInbox = true };
        var games = DesktopFence("Games", @"c:\desktop\crysis.LNK", @"C:\Desktop\AC.lnk");

        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [inbox, games] });

        Assert.Equal([@"C:\Desktop\Crysis.lnk"], normalized.Fences[0].Items);
        Assert.Equal([@"C:\Desktop\AC.lnk"], normalized.Fences[1].Items);
    }

    [Fact]
    public void PortalFence_ItemsAreCleared()
    {
        var portal = Fence.Create("Shots", FenceSource.Portal(@"D:\Shots")) with { Items = ["stray.png"] };

        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [portal] });

        Assert.Empty(normalized.Fences.Single(fence => fence.Id == portal.Id).Items);
    }

    [Theory]
    [InlineData(0, 48)]
    [InlineData(500, 48)]
    [InlineData(64, 64)]
    public void UnsupportedIconSize_FallsBackTo48(int loadedSize, int expectedSize)
    {
        var fence = Fence.Create("Games") with { IconSize = loadedSize };

        var normalized = ConfigNormalizer.Normalize(new NeoFencesConfig { Fences = [fence] });

        Assert.Equal(expectedSize, normalized.Fences.Single(candidate => candidate.Id == fence.Id).IconSize);
    }

    [Fact]
    public void NullsFromHandEditedJson_AreRepaired()
    {
        var config = ConfigJson.Deserialize("""
            { "schemaVersion": 1, "settings": { "peekHotkey": null }, "layouts": null,
              "fences": [ null, { "id": null, "title": null, "source": null, "items": [ "a.txt", null, "" ] } ] }
            """);

        var normalized = ConfigNormalizer.Normalize(config);

        Assert.Equal(new Settings(), normalized.Settings);
        Assert.Empty(normalized.Layouts);
        var repaired = normalized.Fences.Single(fence => !fence.IsInbox);
        Assert.False(string.IsNullOrWhiteSpace(repaired.Id));
        Assert.Equal("", repaired.Title);
        Assert.Equal(FenceSource.Desktop, repaired.Source);
        Assert.Equal(["a.txt"], repaired.Items);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS with `CS0103: The name 'ConfigNormalizer' does not exist in the current context`.

- [ ] **Step 3: Implement** — `src/NeoFences.Core/Config/ConfigNormalizer.cs`

```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

/// <summary>
/// Repairs a loaded (possibly hand-edited) config without losing items: exactly one Inbox, unique fence
/// ids, no nulls, supported icon sizes, each desktop item in at most one fence, no items on portal fences.
/// </summary>
public static class ConfigNormalizer
{
    public static IReadOnlyList<int> IconSizes { get; } = [32, 48, 64, 96];

    public static NeoFencesConfig Normalize(NeoFencesConfig config)
    {
        var defaults = new Settings();
        var settings = config.Settings ?? defaults;
        settings = settings with { PeekHotkey = string.IsNullOrWhiteSpace(settings.PeekHotkey) ? defaults.PeekHotkey : settings.PeekHotkey };

        var seenFenceIds = new HashSet<string>(StringComparer.Ordinal);
        var seenItems = new HashSet<string>(ItemRef.Comparer);
        var inboxFound = false;
        var fences = new List<Fence>();

        foreach (var loadedFence in config.Fences ?? [])
        {
            if (loadedFence is null) continue;
            var fence = loadedFence with
            {
                Id = string.IsNullOrWhiteSpace(loadedFence.Id) || !seenFenceIds.Add(loadedFence.Id) ? Fence.NewId() : loadedFence.Id,
                Title = loadedFence.Title ?? "",
                Source = loadedFence.Source ?? FenceSource.Desktop,
                IsInbox = loadedFence.IsInbox && !inboxFound,
                IconSize = IconSizes.Contains(loadedFence.IconSize) ? loadedFence.IconSize : 48,
            };
            seenFenceIds.Add(fence.Id);
            inboxFound |= fence.IsInbox;

            var items = fence.Source.Kind == FenceSourceKind.Desktop
                ? (loadedFence.Items ?? []).Where(itemRef => !string.IsNullOrWhiteSpace(itemRef) && seenItems.Add(itemRef)).ToList()
                : [];
            fences.Add(fence with { Items = items });
        }

        if (!inboxFound)
        {
            fences.Insert(0, Fence.Create("Inbox") with { IsInbox = true });
        }

        return config with
        {
            Settings = settings,
            Fences = fences,
            Layouts = config.Layouts ?? new Dictionary<string, Layout>(),
        };
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 22`

- [ ] **Step 5: Commit**

```powershell
git add src/NeoFences.Core/Config/ConfigNormalizer.cs tests/NeoFences.Core.Tests/Config/ConfigNormalizerTests.cs
git commit -m "feat: added config normalizer that repairs hand-edited configs"
```

---

### Task 4: ConfigStore (atomic save, backups, recovery)

**Files:**
- Test: `tests/NeoFences.Core.Tests/TestSupport/TempDirectory.cs`, `tests/NeoFences.Core.Tests/Config/ConfigStoreTests.cs`
- Create: `src/NeoFences.Core/Config/ConfigStore.cs`

**Interfaces:**
- Consumes: `ConfigJson`, `ConfigNormalizer.Normalize`, `NeoFencesConfig.CreateDefault`.
- Produces:
  - `ConfigStore(string directory, TimeProvider? timeProvider = null)` with:
    - `Load() -> ConfigLoadResult`;
    - `Save(NeoFencesConfig config) -> bool` (false = blocked by a newer schema);
    - `ConfigPath`, `BackupPath`, `BackupsDirectory`;
    - consts `FileName` = "config.json", `DailyBackupsKept` = 10.
  - `ConfigLoadResult(NeoFencesConfig Config, ConfigLoadSource Source, string? CorruptCopyPath, bool IsReadOnly)`.
  - `enum ConfigLoadSource { Primary, Backup, DailyBackup, Fresh }`.
  - Test support: `TempDirectory` (`Path`, `File(string)`), `FixedTimeProvider(DateTimeOffset now)` with a settable `Now` (local time = UTC).
- Recovery chain: `config.json`, then `.bak`, then the newest readable `backups/config-*.json`, then defaults. Load always normalizes. An unreadable `config.json` is copied to `config.corrupt-<yyyyMMdd-HHmmss>.json` first.

- [ ] **Step 1: Write the test support** — `tests/NeoFences.Core.Tests/TestSupport/TempDirectory.cs`

```csharp
namespace NeoFences.Core.Tests.TestSupport;

/// <summary>A unique real directory under %TEMP%, deleted on dispose. Config tests use the real file system.</summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "neofences-tests", Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public string File(string relativePath) => System.IO.Path.Combine(Path, relativePath);

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}

/// <summary>A clock the test controls. Local time is UTC so file names are deterministic.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
```

- [ ] **Step 2: Write the failing test** — `tests/NeoFences.Core.Tests/Config/ConfigStoreTests.cs`

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
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS with `CS0246: The type or namespace name 'ConfigStore' could not be found`.

- [ ] **Step 4: Implement** — `src/NeoFences.Core/Config/ConfigStore.cs`

```csharp
using System.Text.Json;
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

public enum ConfigLoadSource { Primary, Backup, DailyBackup, Fresh }

/// <param name="CorruptCopyPath">Where an unreadable config.json was preserved, if it was.</param>
/// <param name="IsReadOnly">True when config.json was written by a newer NeoFences: it is never overwritten.</param>
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
    private bool _blockedByNewerSchema;

    public string ConfigPath => Path.Combine(directory, FileName);
    public string BackupPath => ConfigPath + ".bak";
    public string BackupsDirectory => Path.Combine(directory, "backups");
    private string TempPath => ConfigPath + ".tmp";

    public ConfigLoadResult Load()
    {
        _blockedByNewerSchema = false;
        string? corruptCopyPath = null;

        switch (TryRead(ConfigPath, out var primary))
        {
            case ReadOutcome.Ok:
                return new(ConfigNormalizer.Normalize(primary!), ConfigLoadSource.Primary, null, IsReadOnly: false);
            case ReadOutcome.NewerSchema:
                _blockedByNewerSchema = true;
                return new(NeoFencesConfig.CreateDefault(), ConfigLoadSource.Fresh, null, IsReadOnly: true);
            case ReadOutcome.Corrupt:
                corruptCopyPath = Path.Combine(directory, $"config.corrupt-{_time.GetLocalNow():yyyyMMdd-HHmmss}.json");
                File.Copy(ConfigPath, corruptCopyPath, overwrite: true);
                break;
        }

        if (TryRead(BackupPath, out var backup) == ReadOutcome.Ok)
        {
            return new(ConfigNormalizer.Normalize(backup!), ConfigLoadSource.Backup, corruptCopyPath, IsReadOnly: false);
        }

        foreach (var dailyBackupPath in DailyBackupsNewestFirst())
        {
            if (TryRead(dailyBackupPath, out var daily) == ReadOutcome.Ok)
            {
                return new(ConfigNormalizer.Normalize(daily!), ConfigLoadSource.DailyBackup, corruptCopyPath, IsReadOnly: false);
            }
        }

        return new(NeoFencesConfig.CreateDefault(), ConfigLoadSource.Fresh, corruptCopyPath, IsReadOnly: false);
    }

    /// <returns>False when saving is blocked because config.json belongs to a newer NeoFences version.</returns>
    public bool Save(NeoFencesConfig config)
    {
        if (_blockedByNewerSchema) return false;

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

        WriteDailyBackup(json);
        return true;
    }

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

    private enum ReadOutcome { Missing, Ok, Corrupt, NewerSchema }

    private static ReadOutcome TryRead(string path, out NeoFencesConfig? config)
    {
        config = null;
        if (!File.Exists(path)) return ReadOutcome.Missing;
        try
        {
            config = ConfigJson.Deserialize(File.ReadAllText(path));
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
Expected: `Passed! - Failed: 0, Passed: 34`. On Windows a write into a path that is a directory surfaces as `UnauthorizedAccessException`, which is why the failure test accepts it as well as `IOException`.

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core/Config/ConfigStore.cs tests/NeoFences.Core.Tests/TestSupport tests/NeoFences.Core.Tests/Config/ConfigStoreTests.cs
git commit -m "feat: added config store with atomic saves, daily backups and recovery"
```

---

### Task 5: Display fingerprints and layouts

**Files:**
- Test: `tests/NeoFences.Core.Tests/Layouts/LayoutEngineTests.cs`
- Create: `src/NeoFences.Core/Layouts/DisplayMonitor.cs`, `src/NeoFences.Core/Layouts/LayoutEngine.cs`

**Interfaces:**
- Consumes: `NeoFencesConfig`, `Layout`, `FenceRect`, `MonitorArea`.
- Produces:
  - `DisplayMonitor(string DeviceId, int PixelWidth, int PixelHeight, int ScalePercent, double WorkWidth, double WorkHeight, bool IsPrimary)`. M2's Shell builds these from `EnumDisplayMonitors`/`GetDpiForMonitor`.
  - `DisplayFingerprint.Of(IReadOnlyCollection<DisplayMonitor>) -> string`, e.g. `"2mon:DELL-3840x2160@150%+LG-1920x1080@100%"`.
  - `LayoutEngine.Resolve(NeoFencesConfig config, IReadOnlyList<DisplayMonitor> monitors) -> (NeoFencesConfig Config, Layout Layout)`. The returned config stores the layout under the current fingerprint and sets `LastLayoutFingerprint`.
  - `LayoutEngine.Clamp(FenceRect rect, MonitorArea area) -> FenceRect`.
  - Consts: `DefaultWidth` 320, `DefaultHeight` 220, `MinWidth` 120, `MinHeight` 60, `NewFenceMargin` 24, `CascadeStep` 32.
- Rules:
  - a known fingerprint restores exactly (clamped);
  - an unknown one is derived from `LastLayoutFingerprint`, scaled per axis by new/old work area, moved to the primary monitor if its own is gone;
  - fences without a rect cascade on the primary from (24, 24) in steps of 32;
  - rects of deleted fences are dropped.

Schema note: spec §5's sample stored only fence rects per fingerprint. Scaling to an unknown setup needs the old work-area size, so each `Layout` also stores `monitors: { id: { workWidth, workHeight } }`. Task 7 records this in ARCHITECTURE.

- [ ] **Step 1: Write the failing test** — `tests/NeoFences.Core.Tests/Layouts/LayoutEngineTests.cs`

```csharp
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
    public void FirstRun_PlacesFencesCascadingOnPrimary_AndRemembersFingerprint()
    {
        var (config, games) = ConfigWithGames();

        var (resolved, layout) = LayoutEngine.Resolve(config, [Lg, Dell4K]);

        Assert.Equal(new FenceRect("DELL", 24, 24, 320, 220), layout.Fences[config.Inbox.Id]);
        Assert.Equal(new FenceRect("DELL", 56, 56, 320, 220), layout.Fences[games.Id]);
        Assert.Equal("2mon:DELL-3840x2160@150%+LG-1920x1080@100%", resolved.LastLayoutFingerprint);
        Assert.Same(layout, resolved.Layouts[resolved.LastLayoutFingerprint!]);
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
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS with `CS0234: The type or namespace name 'Layouts' does not exist in the namespace 'NeoFences.Core'`.

- [ ] **Step 3: Implement**

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

    public static (NeoFencesConfig Config, Layout Layout) Resolve(NeoFencesConfig config, IReadOnlyList<DisplayMonitor> monitors)
    {
        if (monitors.Count == 0) throw new ArgumentException("At least one monitor is required.", nameof(monitors));

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

        var cascadeIndex = 0;
        foreach (var fence in config.Fences)
        {
            if (rects.TryGetValue(fence.Id, out var rect) && areas.TryGetValue(rect.Monitor, out var area))
            {
                rects[fence.Id] = Clamp(rect, area);
                continue;
            }
            var offset = NewFenceMargin + CascadeStep * cascadeIndex++;
            rects[fence.Id] = Clamp(new FenceRect(primary.DeviceId, offset, offset, DefaultWidth, DefaultHeight), areas[primary.DeviceId]);
        }

        var layout = new Layout { Monitors = areas, Fences = rects };
        var layouts = new Dictionary<string, Layout>(config.Layouts) { [fingerprint] = layout };
        return (config with { Layouts = layouts, LastLayoutFingerprint = fingerprint }, layout);
    }

    /// <summary>Keeps each fence on its monitor if that monitor still exists (else the primary), scaled to the new work area.</summary>
    private static Dictionary<string, FenceRect> Adapt(Layout previous, IReadOnlyDictionary<string, MonitorArea> areas, string primaryId)
    {
        var adapted = new Dictionary<string, FenceRect>();
        foreach (var (fenceId, rect) in previous.Fences)
        {
            var targetId = areas.ContainsKey(rect.Monitor) ? rect.Monitor : primaryId;
            var target = areas[targetId];
            var source = previous.Monitors.TryGetValue(rect.Monitor, out var savedArea) ? savedArea : target;
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

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 45`

- [ ] **Step 5: Commit**

```powershell
git add src/NeoFences.Core/Layouts tests/NeoFences.Core.Tests/Layouts
git commit -m "feat: added display fingerprints and per-setup fence layouts"
```

---

### Task 6: Fence membership

**Files:**
- Test: `tests/NeoFences.Core.Tests/Membership/FenceMembershipTests.cs`
- Create: `src/NeoFences.Core/Membership/FenceMembership.cs`

**Interfaces:**
- Consumes: `NeoFencesConfig` (`Inbox`, `WithFence`), `Fence.Create`, `ItemRef.Comparer`.
- Produces (all on static `FenceMembership`; each returns a new config):
  - `Reconcile(NeoFencesConfig config, IEnumerable<string> desktopItems) -> (NeoFencesConfig Config, ReconcileReport Report)`, with `ReconcileReport(IReadOnlyList<string> Removed, IReadOnlyList<string> AddedToInbox)`.
  - `AddItem(config, string itemRef, string? targetFenceId = null)`.
  - `RemoveItem(config, string itemRef)`.
  - `RenameItem(config, string oldRef, string newRef)`.
  - `MoveItem(config, string itemRef, string toFenceId, int? index = null)`. Throws `ArgumentException` for a portal or unknown fence.
  - `CreateFence(config, string title, FenceSource? source = null) -> (NeoFencesConfig Config, Fence Fence)`.
  - `DeleteFence(config, string fenceId)`. Its items go to the Inbox; throws `InvalidOperationException` for the Inbox and `ArgumentException` for an unknown id.
- No-op cases return the **same** config instance, so callers can skip saving with `ReferenceEquals`.

- [ ] **Step 1: Write the failing test** — `tests/NeoFences.Core.Tests/Membership/FenceMembershipTests.cs`

```csharp
using NeoFences.Core.Membership;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Membership;

public class FenceMembershipTests
{
    private const string Crysis = @"C:\Users\cipher\Desktop\Crysis 2.lnk";
    private const string Unity = @"C:\Users\cipher\Desktop\AC Unity.lnk";
    private const string Notes = @"C:\Users\cipher\Desktop\notes.txt";
    private const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    private static (NeoFencesConfig Config, Fence Games, Fence Portal) Sample()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true, Items = [Notes] };
        var games = Fence.Create("Games") with { Items = [Crysis, Unity] };
        var portal = Fence.Create("Shots", FenceSource.Portal(@"D:\Shots"));
        return (new NeoFencesConfig { Fences = [inbox, games, portal] }, games, portal);
    }

    private static IReadOnlyList<string> ItemsOf(NeoFencesConfig config, string fenceId) =>
        config.Fences.Single(fence => fence.Id == fenceId).Items;

    // ---------- Reconcile ----------

    [Fact]
    public void Reconcile_FirstRun_PutsEverythingInInboxInDesktopOrder()
    {
        var config = NeoFencesConfig.CreateDefault();

        var (reconciled, report) = FenceMembership.Reconcile(config, [Crysis, RecycleBin, Notes]);

        Assert.Equal([Crysis, RecycleBin, Notes], reconciled.Inbox.Items);
        Assert.Equal([Crysis, RecycleBin, Notes], report.AddedToInbox);
        Assert.Empty(report.Removed);
    }

    [Fact]
    public void Reconcile_DropsMissingItems_AddsUnknownToInbox_KeepsFencedOnes()
    {
        var (config, games, _) = Sample();

        var (reconciled, report) = FenceMembership.Reconcile(config, [Crysis, Notes, RecycleBin]);

        Assert.Equal([Crysis], ItemsOf(reconciled, games.Id));
        Assert.Equal([Notes, RecycleBin], reconciled.Inbox.Items);
        Assert.Equal([Unity], report.Removed);
        Assert.Equal([RecycleBin], report.AddedToInbox);
    }

    [Fact]
    public void Reconcile_CaseDifferences_AreTheSameItem_AndAdoptShellSpelling()
    {
        var (config, games, _) = Sample();
        var shellSpelling = Crysis.ToUpperInvariant();

        var (reconciled, report) = FenceMembership.Reconcile(config, [shellSpelling, Unity, Notes]);

        Assert.Equal([shellSpelling, Unity], ItemsOf(reconciled, games.Id));
        Assert.Empty(report.AddedToInbox);
        Assert.Empty(report.Removed);
    }

    [Fact]
    public void Reconcile_DuplicateDesktopEntries_AreAddedOnce()
    {
        var (reconciled, _) = FenceMembership.Reconcile(NeoFencesConfig.CreateDefault(), [Notes, Notes.ToUpperInvariant()]);

        Assert.Equal([Notes], reconciled.Inbox.Items);
    }

    // ---------- Change events ----------

    [Fact]
    public void AddItem_GoesToInbox_OrToDropTarget_AndIgnoresDuplicates()
    {
        var (config, games, _) = Sample();
        const string newFile = @"C:\Users\cipher\Desktop\screenshot.png";

        var toInbox = FenceMembership.AddItem(config, newFile);
        var toGames = FenceMembership.AddItem(config, newFile, targetFenceId: games.Id);
        var duplicate = FenceMembership.AddItem(config, Crysis.ToLowerInvariant());

        Assert.Equal([Notes, newFile], toInbox.Inbox.Items);
        Assert.Equal([Crysis, Unity, newFile], ItemsOf(toGames, games.Id));
        Assert.Same(config, duplicate);
    }

    [Fact]
    public void RemoveItem_RemovesFromOwner_UnknownIsNoOp()
    {
        var (config, games, _) = Sample();

        Assert.Equal([Unity], ItemsOf(FenceMembership.RemoveItem(config, Crysis), games.Id));
        Assert.Same(config, FenceMembership.RemoveItem(config, @"C:\nope.txt"));
    }

    [Fact]
    public void RenameItem_KeepsFenceAndPosition_IncludingCaseOnlyRename()
    {
        var (config, games, _) = Sample();
        const string renamed = @"C:\Users\cipher\Desktop\Crysis 2 Remastered.lnk";

        var afterRename = FenceMembership.RenameItem(config, Crysis, renamed);
        var afterCaseRename = FenceMembership.RenameItem(config, Crysis, Crysis.ToUpperInvariant());

        Assert.Equal([renamed, Unity], ItemsOf(afterRename, games.Id));
        Assert.Equal([Crysis.ToUpperInvariant(), Unity], ItemsOf(afterCaseRename, games.Id));
    }

    [Fact]
    public void RenameItem_UnknownOldRef_TreatedAsNewItem()
    {
        var (config, _, _) = Sample();

        var updated = FenceMembership.RenameItem(config, @"C:\gone.txt", @"C:\Users\cipher\Desktop\new.txt");

        Assert.Equal([Notes, @"C:\Users\cipher\Desktop\new.txt"], updated.Inbox.Items);
    }

    [Fact]
    public void MoveItem_BetweenFencesAndWithinFence()
    {
        var (config, games, _) = Sample();

        var moved = FenceMembership.MoveItem(config, Notes, games.Id, index: 1);
        var reordered = FenceMembership.MoveItem(config, Unity, games.Id, index: 0);
        var appendedPastEnd = FenceMembership.MoveItem(config, Notes, games.Id, index: 99);

        Assert.Empty(moved.Inbox.Items);
        Assert.Equal([Crysis, Notes, Unity], ItemsOf(moved, games.Id));
        Assert.Equal([Unity, Crysis], ItemsOf(reordered, games.Id));
        Assert.Equal([Crysis, Unity, Notes], ItemsOf(appendedPastEnd, games.Id));
    }

    [Fact]
    public void MoveItem_IntoPortalOrUnknownFence_IsRejected()
    {
        var (config, _, portal) = Sample();

        Assert.Throws<ArgumentException>(() => FenceMembership.MoveItem(config, Notes, portal.Id));
        Assert.Throws<ArgumentException>(() => FenceMembership.MoveItem(config, Notes, "no-such-fence"));
    }

    [Fact]
    public void CreateFence_AppendsEmptyDesktopFence()
    {
        var (config, _, _) = Sample();

        var (updated, fence) = FenceMembership.CreateFence(config, "Work");

        Assert.Equal(fence, updated.Fences[^1]);
        Assert.Equal("Work", fence.Title);
        Assert.Empty(fence.Items);
        Assert.Equal(FenceSourceKind.Desktop, fence.Source.Kind);
    }

    [Fact]
    public void DeleteFence_ItemsGoToInbox_NeverLost()
    {
        var (config, games, _) = Sample();

        var updated = FenceMembership.DeleteFence(config, games.Id);

        Assert.DoesNotContain(updated.Fences, fence => fence.Id == games.Id);
        Assert.Equal([Notes, Crysis, Unity], updated.Inbox.Items);
    }

    [Fact]
    public void DeleteFence_InboxIsRefused()
    {
        var (config, _, _) = Sample();

        Assert.Throws<InvalidOperationException>(() => FenceMembership.DeleteFence(config, config.Inbox.Id));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS with `CS0234: The type or namespace name 'Membership' does not exist in the namespace 'NeoFences.Core'`.

- [ ] **Step 3: Implement** — `src/NeoFences.Core/Membership/FenceMembership.cs`

```csharp
using NeoFences.Core.Model;

namespace NeoFences.Core.Membership;

public sealed record ReconcileReport(IReadOnlyList<string> Removed, IReadOnlyList<string> AddedToInbox);

/// <summary>
/// Which desktop item lives in which fence (spec §5). Pure functions: each returns a new config.
/// Item refs compare case-insensitively (<see cref="ItemRef.Comparer"/>). Portal fences hold no items.
/// </summary>
public static class FenceMembership
{
    /// <summary>
    /// Startup reconcile: drops refs that no longer exist, adopts the shell's current spelling, keeps each
    /// item in its first fence only, and appends unknown items to the Inbox in the order given.
    /// </summary>
    public static (NeoFencesConfig Config, ReconcileReport Report) Reconcile(NeoFencesConfig config, IEnumerable<string> desktopItems)
    {
        var presentSpelling = new Dictionary<string, string>(ItemRef.Comparer);
        var presentOrder = new List<string>();
        foreach (var desktopItem in desktopItems)
        {
            if (presentSpelling.TryAdd(desktopItem, desktopItem)) presentOrder.Add(desktopItem);
        }

        var placed = new HashSet<string>(ItemRef.Comparer);
        var removed = new List<string>();
        var fences = config.Fences.Select(fence =>
        {
            if (fence.Source.Kind != FenceSourceKind.Desktop) return fence;
            var kept = new List<string>();
            foreach (var itemRef in fence.Items)
            {
                if (!presentSpelling.TryGetValue(itemRef, out var currentSpelling)) removed.Add(itemRef);
                else if (placed.Add(currentSpelling)) kept.Add(currentSpelling);
            }
            return fence with { Items = kept };
        }).ToList();

        var added = presentOrder.Where(itemRef => !placed.Contains(itemRef)).ToList();
        var reconciled = config with { Fences = fences };
        if (added.Count > 0)
        {
            reconciled = reconciled.WithFence(reconciled.Inbox with { Items = [.. reconciled.Inbox.Items, .. added] });
        }
        return (reconciled, new ReconcileReport(removed, added));
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
        var newRefOwner = FindOwner(config, newRef);
        if (newRefOwner is not null && newRefOwner.Id != owner.Id) return RemoveItem(config, oldRef);
        return config.WithFence(owner with
        {
            Items = owner.Items.Select(existing => ItemRef.Comparer.Equals(existing, oldRef) ? newRef : existing).ToList(),
        });
    }

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

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 58`

- [ ] **Step 5: Commit**

```powershell
git add src/NeoFences.Core/Membership tests/NeoFences.Core.Tests/Membership
git commit -m "feat: added fence membership with reconcile and item events"
```

---

### Task 7: Docs sync

**Files:**
- Modify: `docs/DECISIONS.md` (amend ADR-006), `docs/ARCHITECTURE.md`, `docs/SETUP.md`, `docs/ROADMAP.md`, `docs/SESSION-LOG.md`, `docs/hub/neofences-hq.html`

**Interfaces:**
- Consumes: everything above. Produces: docs that M2 sessions read first.

- [ ] **Step 1: Amend ADR-006** — in `docs/DECISIONS.md`, change ADR-006's status line to:
```
**Date:** 2026-10-02 · **Status:** Accepted, amended in M1 (reflection-based JSON; layouts store monitor work areas)
```
and append at the end of ADR-006:
```markdown
**Amendment (M1).** JSON is reflection-based System.Text.Json, not source-generated: the source
generator assigns every init-only property, so properties missing from a hand-edited file lose
their defaults (e.g. `iconSize` 0). WPF has no AOT need, so nothing is lost. Enum values are
camelCase strings. Each layout also stores `monitors: { id: { workWidth, workHeight } }`, which is
needed to scale a layout to a new display setup. Debouncing saves (500 ms) lives in the App (M2).
```

- [ ] **Step 2: Update `docs/ARCHITECTURE.md`**

Replace the `## Status` section body with:
```
M0 spike complete (ADR-011). M1 complete: `NeoFences.Core` (model, ConfigJson, ConfigNormalizer,
ConfigStore, DisplayFingerprint/LayoutEngine, FenceMembership), 58 xUnit tests. Next: M2.
```
Under the `## Data` list, add this line after the `config.json` bullet:
```
  - shape: `{ schemaVersion, settings, fences[], layouts: { <fingerprint>: { monitors: { <id>: { workWidth, workHeight } }, fences: { <fenceId>: { monitor, x, y, w, h } } } }, lastLayoutFingerprint }`
```

- [ ] **Step 3: Update `docs/SETUP.md`**

Replace the line `## Build / run / test (once \`src/\` exists — M1)` with `## Build / run / test`, and replace the code block under it with:
````
```
dotnet build NeoFences.slnx
dotnet test NeoFences.slnx
dotnet test spikes/M0.slnx          # throwaway M0 spike tests
```
`dotnet run --project src/NeoFences.App` arrives in M2.
````

- [ ] **Step 4: Update `docs/ROADMAP.md`**

Change the M1 heading to `## M1 — Core + config — done <today>`, and tick both M1 checkboxes. Under "Now", tick `M1 implementation plan`, then add `- [ ] Merge \`m1-core-config\` into \`main\` (after final review)` and `- [ ] M2 implementation plan`.

- [ ] **Step 5: Append to `docs/SESSION-LOG.md`**

```markdown
## <today> — M1 core + config

**Done:** `NeoFences.Core` + `NeoFences.Core.Tests` (58 tests): immutable config model, JSON,
normalizer, atomic ConfigStore with daily backups and corrupt recovery, display fingerprints and
layout scaling, fence membership (reconcile, add/remove/rename/move, create/delete fence).
**Decisions:** ADR-006 amended (reflection-based JSON, layouts store monitor work areas).
Takeover defaults to off (ADR-011).
**Next:** M2 plan (Shell + App: layered fence windows, takeover, watchdog, re-verification block).
**Open questions:** none.
```

- [ ] **Step 6: Refresh the hub** (`docs/hub/neofences-hq.html`, `HUB` object)

- `updated` → today; `commit` → `git rev-parse --short HEAD`.
- `phase` → `"M1 done"`; `current` → `"M2 plan"`; `waiting` → `[]`.
- `milestones`: M1 `s: "done"`, M2 `s: "next"`.
- `tasks`: replace the `"Next"` group with M1's items (all `"done"`) and add `{ t: "M2 implementation plan", s: "todo" }`.
- `adrs`: ADR-006 `s` → `"Accepted, amended in M1"`, and append the amendment to `d`.
- `features`: set `"Per-monitor layouts that survive resolution changes"` to `"wip"` with notes `"Core done in M1; windows in M2"`.
- `sessions`: append today's entry. `commits`: append the M1 commits.

Check and republish:
```powershell
$html = Get-Content docs/hub/neofences-hq.html -Raw
$script = [regex]::Match($html, '(?s)<script>(.*)</script>').Groups[1].Value
Set-Content -Path "<scratchpad>\hub.js" -Value $script
node --check "<scratchpad>\hub.js"
```
Expected: no output. Then publish with the Artifact tool: `file_path` = `docs/hub/neofences-hq.html`, `url` = `<private hub link: see CLAUDE.local.md>`.

- [ ] **Step 7: Commit (two commits)**

```powershell
git add docs/DECISIONS.md docs/ARCHITECTURE.md docs/SETUP.md
git commit -m "docs: amended ADR-006 and documented core config shape"
git add docs/ROADMAP.md docs/SESSION-LOG.md docs/hub/neofences-hq.html
git commit -m "docs: updated roadmap, session log and hub after M1"
```

- [ ] **Step 8: Finish the branch**

Run `dotnet test NeoFences.slnx` (expected `Passed: 58`) and `dotnet test spikes/M0.slnx` (expected `Passed: 18`), then use superpowers:finishing-a-development-branch.
