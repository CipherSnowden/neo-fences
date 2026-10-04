# M2b — Desktop Items Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Put the real desktop into the fences. Every visible desktop item (files, folders, shortcuts, Recycle Bin and the other enabled special icons) shows in a fence with its Explorer name and icon or thumbnail, opens on double-click, and follows create/rename/delete on the Desktop live. On first run the Inbox asks once whether NeoFences should hide the native desktop icons.

**Architecture:**
- `NeoFences.Core` gains `DesktopChange` (created/deleted/renamed) and `FenceMembership.Apply`, plus `Settings.TakeoverPromptAnswered`.
- `NeoFences.Shell` gains:
  - `DesktopItems`: lists the user's and the Public Desktop plus the enabled special icons;
  - `DesktopWatcher`: `FileSystemWatcher` on both folders, raising `DesktopChange`, or `Overflowed` when events were lost;
  - `ShellItems`: display name, icon/thumbnail pixels and open, by item ref.
- `NeoFences.App` gains:
  - `IconLoader`: one background STA thread;
  - `FenceItemView`: the bindable item;
  - a `ListBox` (WrapPanel, 48-DIP icons) in `FenceWindow`, and the one-time banner in the Inbox.

  `FenceHost` reconciles at start, applies watcher events, reconciles again after an overflow, and opens items.

**Tech Stack:** .NET 10 SDK 10.0.401, WPF, `Microsoft.Windows.CsWin32` 0.3.335, Serilog 4.4.0, xUnit 2.9.3. No new dependencies.

**Spec:** `docs/superpowers/specs/2026-10-02-neofences-v1-design.md` §5 (membership), §6 (UI). **Decisions:** ADR-002 (Takeover, item refs), ADR-011 (Takeover off by default until C8), ADR-012 (M2 split), ADR-013, and **ADR-014** (new, Task 5). First-run behaviour is the user's choice of 2026-10-02: "Ask once in the Inbox".

**Pre-verified (2026-10-02):** every code block below was compiled together (0 warnings, 0 errors); **92/92 tests pass**. A smoke run on the real desktop (1920×1080, 100 %) succeeded:
- reconcile put 29 items in the Inbox: Recycle Bin, 14 from the user's Desktop, 14 from the Public Desktop;
- icons render with correct alpha;
- a created, renamed and deleted test file was tracked live and saved;
- double-clicking Recycle Bin opened it **in the foreground**;
- the banner showed with Takeover off, and "Hide them" hid the icons and saved `takeover: true, takeoverPromptAnswered: true`.

## Global Constraints

- Hard rules (CLAUDE.md):
  - never lose or hide user files: M2b only reads the Desktop folders and never moves, renames or deletes anything;
  - icons always come back;
  - all Win32/COM in `NeoFences.Shell`;
  - CsWin32 bindings;
  - no new NuGet dependency;
  - shell failures are logged and degrade one feature, never crash.
- Item ref = full file-system path, or `::{CLSID}` for special icons (ADR-002). Refs compare with `ItemRef.Comparer` (ordinal, ignore case).
- `Settings.Takeover` stays off by default (ADR-011, C8 pending). The banner only asks; nothing is hidden without a click.
- Fence windows stay `WS_EX_NOACTIVATE`. **Keyboard navigation (arrows, Enter, Delete, F2) moves to M2c**, because it needs an activation design that keeps fences at the bottom (ADR-014).
- Commits: single line, Conventional Commits, past tense, no `Co-Authored-By` trailer. Test command: `dotnet test NeoFences.slnx` (root).
- Steps marked **[USER]** need the human at the keyboard.

## Review Focus

1. **A burst of changes**, e.g. 500 files unzipped onto the Desktop and then deleted. Expected: every item appears and then disappears; if the watcher buffer overflows, the log shows "lost events; reconciling" and the result is still right. Manual H7 (Task 4); the reconcile logic is pinned by the existing `Reconcile*` tests.
2. **Desktop listing empty or incomplete at start** (OneDrive Desktop not mounted yet). Expected: fenced items are kept, not dropped. Pinned by `Reconcile_EmptyEnumeration_KeepsMemberships_AndFlagsReport` and `Reconcile_MostFencedRefsMissing_KeepsThem_ButStillAddsNewItems`; `FenceHost` logs the warning.
3. **A slow or broken shell handler**: a huge video thumbnail, or a shortcut to a disconnected network drive. Expected: fences stay responsive, the label shows, and the icon may stay blank. Loading runs off the UI thread; manual H10.
4. **An item that cannot be opened**: no associated app, UAC cancelled, or deleted meanwhile. Expected: a warning in the log, no crash. `ShellItems.TryOpen` returns false; manual H5.
5. **The same event reported twice, or a create before a rename.** Expected: no duplicate item. Pinned by `CreatedTwice_IsAddedOnce` (Task 1) and `RenameItem_OntoRefAlreadyInSameFence_LeavesOneEntry` and `RenameItem_UnknownOldRef_TreatedAsNewItem`.

---

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/NeoFences.Core/Membership/DesktopChange.cs`, `FenceMembership.cs` (`Apply`) | watcher events as values, applied purely | 1 |
| `src/NeoFences.Core/Model/Settings.cs` | `TakeoverPromptAnswered` | 1 |
| `src/NeoFences.Shell/DesktopItems.cs` | what Explorer shows on the desktop, as item refs | 2 |
| `src/NeoFences.Shell/DesktopWatcher.cs` | live changes on both Desktop folders | 2 |
| `src/NeoFences.Shell/ShellItems.cs` | display name, icon pixels, open | 2 |
| `src/NeoFences.App/FenceItemView.cs`, `IconLoader.cs` | bindable item, background STA loading | 3 |
| `src/NeoFences.App/FenceWindow.xaml(.cs)` | item grid, selection, double-click, first-run banner | 3 |
| `src/NeoFences.App/FenceHost.cs` | reconcile, watcher, open, banner answer | 3 |
| `docs/TEST-CHECKLIST.md` (section H), `docs/research/m2b-desktop-items.md` | verification | 4 |
| `docs/DECISIONS.md` (ADR-014), `ARCHITECTURE.md`, `FEATURES.md`, `ROADMAP.md`, `SESSION-LOG.md`, hub | docs sync | 5 |

---

### Task 1: Core — desktop changes and the first-run flag

**Files:**
- Modify: `docs/ROADMAP.md` (claim M2b)
- Create: `src/NeoFences.Core/Membership/DesktopChange.cs`, `tests/NeoFences.Core.Tests/Membership/DesktopChangeTests.cs`
- Modify: `src/NeoFences.Core/Membership/FenceMembership.cs`, `src/NeoFences.Core/Model/Settings.cs`, `tests/NeoFences.Core.Tests/Model/NeoFencesConfigTests.cs`

**Interfaces:**
- Consumes: `FenceMembership.AddItem/RemoveItem/RenameItem` (M1).
- Produces:
  - `abstract record DesktopChange` with `Created(string ItemRef)`, `Deleted(string ItemRef)`, `Renamed(string OldRef, string NewRef)`;
  - `FenceMembership.Apply(NeoFencesConfig config, DesktopChange change) -> NeoFencesConfig`;
  - `Settings.TakeoverPromptAnswered` (bool, default false, JSON `takeoverPromptAnswered`).

- [ ] **Step 1: Branch and claim M2b**

```powershell
git switch -c m2b-desktop-items
```
In `docs/ROADMAP.md`, replace the line `### M2b — Desktop items (icons, open/select/keyboard, change notifications, Takeover + first run)` with
```
### M2b — Desktop items (icons, open/select, change notifications, Takeover + first run) — [~] claimed by session 2026-10-02 m2b
```
and the line `### M2c — Fence interactions (snap, lock, scroll, icon size, rename, light/dark)` with
```
### M2c — Fence interactions (keyboard, snap, lock, scroll, icon size, rename, light/dark)
```
```powershell
git add docs/ROADMAP.md
git commit -m "docs: claimed M2b and moved fence keyboard navigation to M2c"
```

- [ ] **Step 2: Write the failing tests**

Create `tests/NeoFences.Core.Tests/Membership/DesktopChangeTests.cs`:
```csharp
using NeoFences.Core.Membership;
using NeoFences.Core.Model;

namespace NeoFences.Core.Tests.Membership;

public class DesktopChangeTests
{
    private const string Notes = @"C:\Users\cipher\Desktop\notes.txt";
    private const string Shot = @"C:\Users\cipher\Desktop\screenshot.png";

    private static (NeoFencesConfig Config, Fence Games) Sample()
    {
        var inbox = Fence.Create("Inbox") with { IsInbox = true };
        var games = Fence.Create("Games") with { Items = [Notes] };
        return (new NeoFencesConfig { Fences = [inbox, games] }, games);
    }

    [Fact]
    public void Created_GoesToInbox()
    {
        var (config, _) = Sample();

        var updated = FenceMembership.Apply(config, new DesktopChange.Created(Shot));

        Assert.Equal([Shot], updated.Inbox.Items);
    }

    [Fact]
    public void Deleted_LeavesItsFence()
    {
        var (config, games) = Sample();

        var updated = FenceMembership.Apply(config, new DesktopChange.Deleted(Notes));

        Assert.Empty(updated.Fences.Single(fence => fence.Id == games.Id).Items);
    }

    [Fact]
    public void Renamed_StaysInItsFence()
    {
        var (config, games) = Sample();
        const string renamed = @"C:\Users\cipher\Desktop\todo.txt";

        var updated = FenceMembership.Apply(config, new DesktopChange.Renamed(Notes, renamed));

        Assert.Equal([renamed], updated.Fences.Single(fence => fence.Id == games.Id).Items);
    }

    [Fact]
    public void CreatedTwice_IsAddedOnce()
    {
        // Watchers can report the same create twice (e.g. a download finishing); the second must be a no-op.
        var (config, _) = Sample();
        var once = FenceMembership.Apply(config, new DesktopChange.Created(Shot));

        Assert.Same(once, FenceMembership.Apply(once, new DesktopChange.Created(Shot)));
    }
}
```
In `tests/NeoFences.Core.Tests/Model/NeoFencesConfigTests.cs`, append before the class's closing `}`:
```csharp

    [Fact]
    public void Default_TakeoverPromptNotAnsweredYet()
    {
        // M2b first run: the Inbox asks once whether to hide desktop icons (user decision 2026-10-02).
        Assert.False(NeoFencesConfig.CreateDefault().Settings.TakeoverPromptAnswered);
    }
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test NeoFences.slnx`
Expected: build FAILS with `CS0246: The type or namespace name 'DesktopChange' could not be found` and `CS1061: 'Settings' does not contain a definition for 'TakeoverPromptAnswered'`.

- [ ] **Step 4: Implement**

Create `src/NeoFences.Core/Membership/DesktopChange.cs`:
```csharp
namespace NeoFences.Core.Membership;

/// <summary>A change to the desktop reported by NeoFences.Shell's watcher, applied with <see cref="FenceMembership.Apply"/>.</summary>
public abstract record DesktopChange
{
    public sealed record Created(string ItemRef) : DesktopChange;

    public sealed record Deleted(string ItemRef) : DesktopChange;

    public sealed record Renamed(string OldRef, string NewRef) : DesktopChange;
}
```
In `src/NeoFences.Core/Membership/FenceMembership.cs`, insert after `Reconcile` (before `AddItem`):
```csharp
    /// <summary>Applies one watcher event: created → Inbox, deleted → removed, renamed → same fence and position.</summary>
    public static NeoFencesConfig Apply(NeoFencesConfig config, DesktopChange change) => change switch
    {
        DesktopChange.Created created => AddItem(config, created.ItemRef),
        DesktopChange.Deleted deleted => RemoveItem(config, deleted.ItemRef),
        DesktopChange.Renamed renamed => RenameItem(config, renamed.OldRef, renamed.NewRef),
        _ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown desktop change."),
    };

```
In `src/NeoFences.Core/Model/Settings.cs`, after the `Takeover` property add:
```csharp
    /// <summary>The user answered the one-time "hide desktop icons?" banner in the Inbox (M2b first run).</summary>
    public bool TakeoverPromptAnswered { get; init; }
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test NeoFences.slnx`
Expected: `Passed! - Failed: 0, Passed: 92`

- [ ] **Step 6: Commit**

```powershell
git add src/NeoFences.Core tests/NeoFences.Core.Tests
git commit -m "feat: added desktop change events and the first-run prompt flag to core"
```

---

### Task 2: Shell — desktop listing, watcher, names, icons, open

**Files:**
- Create: `src/NeoFences.Shell/DesktopItems.cs`, `DesktopWatcher.cs`, `ShellItems.cs`
- Modify: `src/NeoFences.Shell/NativeMethods.txt` (full new content below)

**Interfaces:**
- Consumes: `DesktopChange` (Task 1), `ItemRef.Comparer` (M1).
- Produces:
  - `DesktopItems.Enumerate() -> IReadOnlyList<string>`: special icons first, then files and folders by name;
  - `DesktopItems.UserDesktop`, `DesktopItems.PublicDesktop`, `DesktopItems.IsVisibleOnDesktop(FileSystemInfo)`;
  - `DesktopWatcher : IDisposable` with `event Action<DesktopChange> Changed` and `event Action Overflowed` (both raised on thread-pool threads);
  - `ShellItems.TryGetDisplayName(string itemRef) -> string?`;
  - `ShellItems.TryGetImage(string itemRef, int sizePx) -> ShellImage?`, where `record ShellImage(int Width, int Height, byte[] Pixels)` holds premultiplied BGRA rows, top-down. Call it from an STA thread;
  - `ShellItems.TryOpen(string itemRef) -> bool`.

Notes for the implementer:
- Special icons come from `HKCU\…\HideDesktopIcons\NewStartPanel`, where a DWORD of 0 means shown. A missing value means the Windows default: only Recycle Bin is shown.
- `Environment.SpecialFolder.DesktopDirectory` already follows a OneDrive-redirected Desktop.
- CsWin32 generates the generic overload `SHCreateItemFromParsingName<T>(string, IBindCtx, out T)`; the `Guid*` overload does not accept a string.
- Thumbnails without alpha come back with every alpha byte 0; `ReadPixels` makes those opaque.
- Fences are never activated, so `TryOpen` first calls `AllowSetForegroundWindow(ASFW_ANY)`. Otherwise the opened window can appear behind the current one. Verified: the Recycle Bin window opened in front.

- [ ] **Step 1: Bindings**

Replace `src/NeoFences.Shell/NativeMethods.txt` with:
```
// Win32 APIs used by NeoFences.Shell. CsWin32 generates bindings for each name.
AllowSetForegroundWindow
BITMAP
BITMAPINFO
CreateRoundRectRgn
DeleteObject
DIB_USAGE
DISPLAY_DEVICEW
EnumDisplayDevices
EnumDisplayMonitors
FindWindow
FOLDERFLAGS
GET_WINDOW_CMD
GetDC
GetDIBits
GetDpiForMonitor
GetMonitorInfo
GetObject
GetSystemMetrics
GetWindow
GetWindowLongPtr
GetWindowRect
HWND_BOTTOM
IFolderView2
IServiceProvider
IShellBrowser
IShellItem
IShellItemImageFactory
IShellView
IShellWindows
MONITOR_DPI_TYPE
MONITORINFOEXW
MONITORINFOF_PRIMARY
RegisterWindowMessage
ReleaseDC
SET_WINDOW_POS_FLAGS
SetWindowLongPtr
SetWindowPos
SetWindowRgn
SHCreateItemFromParsingName
ShellWindows
SID_STopLevelBrowser
SIGDN
SIIGBF
SYSTEM_METRICS_INDEX
WINDOW_EX_STYLE
WINDOW_LONG_PTR_INDEX
WINDOW_STYLE
```

- [ ] **Step 2: Create the three files**

`src/NeoFences.Shell/DesktopItems.cs`:
```csharp
using Microsoft.Win32;
using NeoFences.Core.Model;

namespace NeoFences.Shell;

/// <summary>
/// What Explorer shows on the desktop, as item refs (ADR-002): the visible files and folders of the user's Desktop
/// (OneDrive-redirected or not) and the Public Desktop, plus the special icons the user enabled in "Desktop icon
/// settings" (Recycle Bin, This PC, ...) as "::{CLSID}".
/// </summary>
public static class DesktopItems
{
    private const string HideDesktopIconsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";

    /// <summary>Special desktop icons and whether Windows shows each one when the user never changed the setting.</summary>
    private static readonly (string Clsid, bool ShownByDefault)[] SpecialIcons =
    [
        ("{645FF040-5081-101B-9F08-00AA002F954E}", true),  // Recycle Bin
        ("{20D04FE0-3AEA-1069-A2D8-08002B30309D}", false), // This PC
        ("{59031a47-3f72-44a7-89c5-5595fe6b30ee}", false), // User's files
        ("{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", false), // Network
        ("{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", false), // Control Panel
    ];

    public static string UserDesktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string PublicDesktop => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    /// <summary>Special icons first (as Explorer arranges them), then files and folders by name.</summary>
    public static IReadOnlyList<string> Enumerate()
    {
        var itemRefs = new List<string>();
        using (var hideKey = Registry.CurrentUser.OpenSubKey(HideDesktopIconsKey))
        {
            foreach (var (clsid, shownByDefault) in SpecialIcons)
            {
                var hidden = hideKey?.GetValue(clsid) is int setting ? setting != 0 : !shownByDefault;
                if (!hidden) itemRefs.Add("::" + clsid);
            }
        }

        var files = new List<string>();
        foreach (var directory in new[] { UserDesktop, PublicDesktop }.Where(Directory.Exists).Distinct(ItemRef.Comparer))
        {
            try
            {
                files.AddRange(new DirectoryInfo(directory).EnumerateFileSystemInfos()
                    .Where(IsVisibleOnDesktop)
                    .Select(entry => entry.FullName));
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // An unreadable Public Desktop must not hide the user's own items; Reconcile's guard handles a short list.
            }
        }
        itemRefs.AddRange(files.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase));
        return itemRefs;
    }

    /// <summary>Hidden and system entries (desktop.ini, Office ~$ lock files) are not shown by Explorer either.</summary>
    public static bool IsVisibleOnDesktop(FileSystemInfo entry) =>
        (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;
}
```
`src/NeoFences.Shell/DesktopWatcher.cs`:
```csharp
using NeoFences.Core.Membership;

namespace NeoFences.Shell;

/// <summary>
/// Watches the user's and the Public Desktop folders. Events arrive on thread-pool threads; marshal them yourself.
/// <see cref="Overflowed"/> means events were lost (buffer overflow, folder gone): re-enumerate and reconcile.
/// Special icons (Recycle Bin, ...) are not watched; they are picked up by the next reconcile.
/// </summary>
public sealed class DesktopWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];

    public event Action<DesktopChange>? Changed;
    public event Action? Overflowed;

    public DesktopWatcher()
    {
        foreach (var directory in new[] { DesktopItems.UserDesktop, DesktopItems.PublicDesktop }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var watcher = new FileSystemWatcher(directory)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes,
                IncludeSubdirectories = false,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Created += (_, created) => { if (IsVisible(created.FullPath)) Changed?.Invoke(new DesktopChange.Created(created.FullPath)); };
            watcher.Deleted += (_, deleted) => Changed?.Invoke(new DesktopChange.Deleted(deleted.FullPath));
            watcher.Renamed += (_, renamed) => Changed?.Invoke(IsVisible(renamed.FullPath)
                ? new DesktopChange.Renamed(renamed.OldFullPath, renamed.FullPath)
                : new DesktopChange.Deleted(renamed.OldFullPath));
            // Hidden/shown via attributes (e.g. a file the user hid): the next reconcile sorts it out.
            watcher.Changed += (_, _) => { };
            watcher.Error += (_, _) => Overflowed?.Invoke();
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    private static bool IsVisible(string path)
    {
        try
        {
            FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return entry.Exists && DesktopItems.IsVisibleOnDesktop(entry);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
    }
}
```
`src/NeoFences.Shell/ShellItems.cs`:
```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;

namespace NeoFences.Shell;

/// <summary>32-bit premultiplied BGRA pixels, top-down rows (ready for WPF's Pbgra32 BitmapSource).</summary>
public sealed record ShellImage(int Width, int Height, byte[] Pixels);

/// <summary>Display names, icons/thumbnails and opening of desktop items, by item ref (parsing name).</summary>
public static class ShellItems
{
    /// <summary>The name Explorer shows ("Crysis 2", not "Crysis 2.lnk"; "Recycle Bin" in the user's language).</summary>
    public static unsafe string? TryGetDisplayName(string itemRef)
    {
        try
        {
            var item = Create(itemRef);
            item.GetDisplayName(SIGDN.SIGDN_NORMALDISPLAY, out var name);
            try { return name.ToString(); }
            finally { Marshal.FreeCoTaskMem((nint)name.Value); }
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// The thumbnail (images, videos) or icon at <paramref name="sizePx"/>. Call from an STA thread that is not the
    /// UI thread (slow for big files). Null when the shell has nothing (the item vanished, a broken handler).
    /// </summary>
    public static unsafe ShellImage? TryGetImage(string itemRef, int sizePx)
    {
        HBITMAP bitmap = default;
        try
        {
            var factory = (IShellItemImageFactory)Create(itemRef);
            factory.GetImage(new SIZE(sizePx, sizePx), SIIGBF.SIIGBF_RESIZETOFIT, &bitmap);
            return ReadPixels(bitmap);
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            if (!bitmap.IsNull) PInvoke.DeleteObject(bitmap);
        }
    }

    /// <summary>Opens with the default verb, like a double-click in Explorer.</summary>
    /// <returns>False when nothing could open it (no associated app, the user cancelled a UAC prompt, the item is gone).</returns>
    public static bool TryOpen(string itemRef)
    {
        try
        {
            var startInfo = itemRef.StartsWith("::", StringComparison.Ordinal)
                ? new ProcessStartInfo("explorer.exe", "shell:" + itemRef) { UseShellExecute = true }
                : new ProcessStartInfo(itemRef) { UseShellExecute = true };
            // The user just clicked our (never-activated) fence: let the opened window come to the front.
            PInvoke.AllowSetForegroundWindow(unchecked((uint)-1)); // ASFW_ANY
            Process.Start(startInfo)?.Dispose();
            return true;
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }

    private static IShellItem Create(string itemRef)
    {
        PInvoke.SHCreateItemFromParsingName(itemRef, null, out IShellItem item).ThrowOnFailure();
        return item;
    }

    private static unsafe ShellImage? ReadPixels(HBITMAP bitmap)
    {
        BITMAP header;
        if (PInvoke.GetObject(bitmap, sizeof(BITMAP), &header) == 0 || header.bmBitsPixel != 32) return null;

        var width = header.bmWidth;
        var height = Math.Abs(header.bmHeight);
        var pixels = new byte[width * height * 4];
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height, // negative: top-down rows
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,  // BI_RGB
            },
        };
        var screenDc = PInvoke.GetDC(HWND.Null);
        try
        {
            fixed (byte* pixelBuffer = pixels)
            {
                if (PInvoke.GetDIBits(screenDc, bitmap, 0, (uint)height, pixelBuffer, &info, DIB_USAGE.DIB_RGB_COLORS) == 0) return null;
            }
        }
        finally
        {
            PInvoke.ReleaseDC(HWND.Null, screenDc);
        }
        // Some thumbnail handlers return opaque images with every alpha byte 0: show them opaque, not invisible.
        var hasAlpha = false;
        for (var alphaIndex = 3; alphaIndex < pixels.Length && !hasAlpha; alphaIndex += 4) hasAlpha = pixels[alphaIndex] != 0;
        if (!hasAlpha) for (var alphaIndex = 3; alphaIndex < pixels.Length; alphaIndex += 4) pixels[alphaIndex] = 255;
        return new ShellImage(width, height, pixels);
    }
}
```

- [ ] **Step 3: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 92`. These classes are verified on the real desktop in Tasks 3 and 4: they are thin shell wrappers with no logic worth a unit test.

- [ ] **Step 4: Commit**

```powershell
git add src/NeoFences.Shell
git commit -m "feat: added desktop item listing, change watcher, shell names, icons and open"
```

---

### Task 3: App — items in fences and the first-run banner

**Files:**
- Create: `src/NeoFences.App/FenceItemView.cs`, `src/NeoFences.App/IconLoader.cs`
- Modify (full new content below): `src/NeoFences.App/FenceWindow.xaml`, `FenceWindow.xaml.cs`, `FenceHost.cs`

**Interfaces:**
- Consumes: Task 1 (`FenceMembership.Apply`, `Reconcile`, `Settings.TakeoverPromptAnswered`) and Task 2 (everything).
- Produces:
  - `FenceWindow.SetItems(IReadOnlyList<string> itemRefs, IconLoader iconLoader)`; items already shown keep their loaded name and icon;
  - `FenceWindow.ShowTakeoverPrompt(bool visible)`;
  - events `OpenRequested(string itemRef)` and `TakeoverPromptAnswered(bool hideIcons)`.

  Menu text changes from "Hide desktop icons (preview)" to "Hide desktop icons". Any explicit Takeover choice (banner or menu) sets `TakeoverPromptAnswered`.

- [ ] **Step 1: Create the item view and loader**

`src/NeoFences.App/FenceItemView.cs`:
```csharp
using System.ComponentModel;
using System.IO;
using System.Windows.Media;

namespace NeoFences.App;

/// <summary>One desktop item as a fence shows it. Label and icon start as placeholders and fill in from <see cref="IconLoader"/>.</summary>
public sealed class FenceItemView(string itemRef) : INotifyPropertyChanged
{
    public string ItemRef { get; } = itemRef;

    public string Label
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label))); }
    } = itemRef.StartsWith("::", StringComparison.Ordinal) ? "" : Path.GetFileNameWithoutExtension(itemRef);

    public ImageSource? Icon
    {
        get;
        set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
```
`src/NeoFences.App/IconLoader.cs`:
```csharp
using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>
/// Loads display names and icons on one background STA thread (shell extensions expect STA; thumbnails of big
/// files are slow) and hands them to the UI thread. A failed load leaves the placeholder, never throws.
/// </summary>
public sealed class IconLoader : IDisposable
{
    private readonly BlockingCollection<(FenceItemView View, int SizePx)> _requests = new();
    private readonly Dispatcher _uiDispatcher;

    public IconLoader(Dispatcher uiDispatcher)
    {
        _uiDispatcher = uiDispatcher;
        var worker = new Thread(Work) { IsBackground = true, Name = "NeoFences icon loader" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    public void Request(FenceItemView view, int sizePx) => _requests.TryAdd((view, sizePx));

    private void Work()
    {
        foreach (var (view, sizePx) in _requests.GetConsumingEnumerable())
        {
            var label = ShellItems.TryGetDisplayName(view.ItemRef);
            var image = ShellItems.TryGetImage(view.ItemRef, sizePx);
            BitmapSource? icon = null;
            if (image is not null)
            {
                icon = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null, image.Pixels, image.Width * 4);
                icon.Freeze(); // frozen: usable from the UI thread
            }
            _uiDispatcher.BeginInvoke(() =>
            {
                if (label is not null) view.Label = label;
                if (icon is not null) view.Icon = icon;
            });
        }
    }

    public void Dispose() => _requests.CompleteAdding();
}
```

- [ ] **Step 2: Replace the fence window and host**

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
    <Window.Resources>
        <Style x:Key="BannerButton" TargetType="Button">
            <Setter Property="Foreground" Value="White" />
            <Setter Property="Padding" Value="10,3" />
            <Setter Property="Margin" Value="0,0,6,0" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="Chrome" Background="#33FFFFFF" CornerRadius="4" Padding="{TemplateBinding Padding}">
                            <ContentPresenter />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Chrome" Property="Background" Value="#55FFFFFF" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
    <Border CornerRadius="8" BorderBrush="#40FFFFFF" BorderThickness="1">
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="30" />
                <RowDefinition Height="Auto" />
                <RowDefinition />
            </Grid.RowDefinitions>
            <TextBlock x:Name="TitleText" Foreground="White" FontWeight="SemiBold" Margin="12,0"
                       VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
            <!-- First run (Inbox only): ask once whether NeoFences should take over the desktop icons. -->
            <Border x:Name="TakeoverPrompt" Grid.Row="1" Visibility="Collapsed" Background="#26FFFFFF"
                    Margin="8,0,8,6" Padding="10,8" CornerRadius="6">
                <StackPanel>
                    <TextBlock Foreground="White" TextWrapping="Wrap"
                               Text="Hide the desktop icons and keep them only in fences?" />
                    <TextBlock Foreground="#B3FFFFFF" FontSize="11" TextWrapping="Wrap" Margin="0,2,0,6"
                               Text="Your files stay where they are. Turn it off any time from the right-click menu." />
                    <StackPanel Orientation="Horizontal">
                        <Button x:Name="PromptHideButton" Content="Hide them" Style="{StaticResource BannerButton}" />
                        <Button x:Name="PromptLaterButton" Content="Not now" Style="{StaticResource BannerButton}" />
                    </StackPanel>
                </StackPanel>
            </Border>
            <Border Grid.Row="2" BorderBrush="#26FFFFFF" BorderThickness="0,1,0,0" Background="#01000000">
                <Border.ContextMenu>
                    <ContextMenu x:Name="BodyContextMenu">
                        <MenuItem x:Name="NewFenceItem" Header="New fence" />
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
                            <Setter Property="Template">
                                <Setter.Value>
                                    <ControlTemplate TargetType="ListBoxItem">
                                        <Border x:Name="Chrome" Background="Transparent" CornerRadius="4" Margin="2" Padding="2,4">
                                            <ContentPresenter />
                                        </Border>
                                        <ControlTemplate.Triggers>
                                            <Trigger Property="IsMouseOver" Value="True">
                                                <Setter TargetName="Chrome" Property="Background" Value="#22FFFFFF" />
                                            </Trigger>
                                            <Trigger Property="IsSelected" Value="True">
                                                <Setter TargetName="Chrome" Property="Background" Value="#44FFFFFF" />
                                            </Trigger>
                                        </ControlTemplate.Triggers>
                                    </ControlTemplate>
                                </Setter.Value>
                            </Setter>
                        </Style>
                    </ListBox.ItemContainerStyle>
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Width="76">
                                <Image Source="{Binding Icon}" Width="48" Height="48" HorizontalAlignment="Center" />
                                <TextBlock Text="{Binding Label}" Foreground="White" FontSize="12" Margin="0,4,0,0"
                                           TextAlignment="Center" TextWrapping="Wrap" TextTrimming="CharacterEllipsis"
                                           MaxHeight="32" />
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
using NeoFences.Core.Layouts;
using NeoFences.Shell;

namespace NeoFences.App;

/// <summary>One fence on the desktop. Placement and persistence are the host's job; this window only reports moves.</summary>
public partial class FenceWindow : Window
{
    private const int WmExitSizeMove = 0x0232;
    private const double CornerRadiusDips = 8;
    private const double IconSizeDips = 48;

    private readonly ObservableCollection<FenceItemView> _items = [];

    public string FenceId { get; }

    public nint Handle { get; private set; }

    /// <summary>Raised after the user finishes moving or resizing, with the new physical-pixel rect.</summary>
    public event Action<FenceWindow, PixelRect>? MovedByUser;

    public event Action? NewFenceRequested;
    public event Action<bool>? TakeoverToggled;
    public event Action? ExitRequested;
    public event Action<string>? OpenRequested;
    /// <summary>The first-run question was answered: true = hide the desktop icons.</summary>
    public event Action<bool>? TakeoverPromptAnswered;

    public FenceWindow(string fenceId, string title, bool takeoverActive)
    {
        FenceId = fenceId;
        InitializeComponent();
        TitleText.Text = title;
        TakeoverItem.IsChecked = takeoverActive;
        NewFenceItem.Click += (_, _) => NewFenceRequested?.Invoke();
        TakeoverItem.Click += (_, _) => TakeoverToggled?.Invoke(TakeoverItem.IsChecked);
        ExitItem.Click += (_, _) => ExitRequested?.Invoke();
        ItemList.ItemsSource = _items;
        ItemList.MouseDoubleClick += OnItemDoubleClick;
        PromptHideButton.Click += (_, _) => TakeoverPromptAnswered?.Invoke(true);
        PromptLaterButton.Click += (_, _) => TakeoverPromptAnswered?.Invoke(false);
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

    public void ShowTakeoverPrompt(bool visible) => TakeoverPrompt.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Shows exactly these items in this order. Items already shown keep their loaded name and icon.</summary>
    public void SetItems(IReadOnlyList<string> itemRefs, IconLoader iconLoader)
    {
        if (_items.Select(view => view.ItemRef).SequenceEqual(itemRefs, StringComparer.Ordinal)) return;
        var existing = _items.ToDictionary(view => view.ItemRef, StringComparer.Ordinal);
        var iconSizePx = (int)Math.Round(IconSizeDips * VisualTreeHelper.GetDpi(this).DpiScaleX);
        _items.Clear();
        foreach (var itemRef in itemRefs)
        {
            if (!existing.TryGetValue(itemRef, out var view))
            {
                view = new FenceItemView(itemRef);
                iconLoader.Request(view, iconSizePx);
            }
            _items.Add(view);
        }
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
    private bool _sessionEnding;

    public event Action? ExitRequested;

    public FenceHost()
    {
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => SaveNow();
        _messages.ExplorerRestarted += OnExplorerRestarted;
        _messages.DisplayChanged += OnDisplayChanged;
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
        var window = new FenceWindow(fence.Id, fence.Title, _takeoverActive);
        window.MovedByUser += OnFenceMoved;
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
        var (reconciled, report) = FenceMembership.Reconcile(_config, DesktopItems.Enumerate());
        _config = reconciled;
        if (report.Suspicious) Log.Warning("desktop listing looks incomplete; kept all fenced items (missing ones stay until a later reconcile)");
        Log.Information("desktop reconciled: {AddedCount} added to the Inbox, {RemovedCount} removed", report.AddedToInbox.Count, report.Removed.Count);
        RefreshWindows();
        ScheduleSave();
    }

    private void StartDesktopWatcher()
    {
        try
        {
            _desktopWatcher = new DesktopWatcher();
            var dispatcher = Dispatcher.CurrentDispatcher;
            _desktopWatcher.Changed += change => dispatcher.BeginInvoke(() => OnDesktopChanged(change));
            _desktopWatcher.Overflowed += () => dispatcher.BeginInvoke(() =>
            {
                Log.Warning("desktop watcher lost events; reconciling");
                ReconcileDesktop();
            });
        }
        catch (Exception failure) when (failure is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Degrade (hard rule 7): fences still show what the startup reconcile found.
            Log.Error(failure, "desktop watcher could not start; changes show after a restart");
        }
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
            window.SetItems(fence.Items, _iconLoader);
            window.ShowTakeoverPrompt(showPrompt && fence.IsInbox);
        }
    }

    private static void OpenItem(string itemRef)
    {
        if (!ShellItems.TryOpen(itemRef)) Log.Warning("could not open {ItemRef}", itemRef);
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

- [ ] **Step 3: Build and test**

Run: `dotnet build NeoFences.slnx`, then `dotnet test NeoFences.slnx`
Expected: 0 warnings, 0 errors; `Passed: 92`.

- [ ] **Step 4: Smoke run (agent)**

Save as `<scratchpad>\m2b-smoke.ps1` and run it with `& "<scratchpad>\m2b-smoke.ps1"`. It restarts NeoFences from the repo build, then creates, renames and deletes `neofences-smoke.txt` on the Desktop. It touches no other file and leaves NeoFences running.
```powershell
# M2b smoke: start the built app, then create, rename and delete a test file on the Desktop and check the Inbox follows.
# Leaves NeoFences running (it is the user's live app). Never touches any file but its own test file.
$exe = "F:\projects\neo_fences\src\NeoFences.App\bin\Debug\net10.0-windows\NeoFences.exe"
$data = "$env:LOCALAPPDATA\NeoFences"; $cfg = "$data\config.json"; $desk = [Environment]::GetFolderPath('Desktop')
function Get-InboxItems { @(((Get-Content $cfg -Raw | ConvertFrom-Json).fences | Where-Object isInbox).items) }
function Wait-Until([scriptblock]$condition) { $deadline = (Get-Date).AddSeconds(10); while (-not (& $condition) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }; [bool](& $condition) }

if (Get-Process NeoFences -ErrorAction SilentlyContinue) { & $exe --exit; [void](Wait-Until { -not (Get-Process NeoFences -ErrorAction SilentlyContinue) }) }
function Get-ReconcileLines { @(Select-String -Path "$data\logs\neofences-*.log" -Pattern 'desktop reconciled') }
$before = (Get-ReconcileLines).Count
Start-Process $exe
"reconciled: " + (Wait-Until { (Get-ReconcileLines).Count -gt $before })
(Get-ReconcileLines)[-1].Line

Set-Content "$desk\neofences-smoke.txt" "smoke"
"created in Inbox: " + (Wait-Until { (Get-InboxItems) -contains "$desk\neofences-smoke.txt" })
Rename-Item "$desk\neofences-smoke.txt" "neofences-smoke-renamed.txt"
"renamed in Inbox: " + (Wait-Until { (Get-InboxItems) -contains "$desk\neofences-smoke-renamed.txt" -and (Get-InboxItems) -notcontains "$desk\neofences-smoke.txt" })
[System.IO.File]::Delete("$desk\neofences-smoke-renamed.txt")
"deleted from Inbox: " + (Wait-Until { -not ((Get-InboxItems) -like '*neofences-smoke*') })
"NeoFences running: " + [bool](Get-Process NeoFences -ErrorAction SilentlyContinue)
```
Expected:
- `reconciled: True`, followed by a `desktop reconciled: N added to the Inbox, M removed` line;
- `created in Inbox: True`, `renamed in Inbox: True`, `deleted from Inbox: True`;
- `NeoFences running: True`.

- [ ] **Step 5: Commit**

```powershell
git add src/NeoFences.App
git commit -m "feat: added desktop items to fences with icons, live changes, open and first-run prompt"
```

---

### Task 4: Verification on the real desktop

**Files:**
- Modify: `docs/TEST-CHECKLIST.md` (append section H)
- Create: `docs/research/m2b-desktop-items.md`

**Interfaces:**
- Consumes: the built app.
- Produces: results, plus fixes as separate `fix:` commits (with a Core test where the logic is in Core).

- [ ] **Step 1: Append section H to `docs/TEST-CHECKLIST.md`**

```markdown
## H — Desktop items (M2b+)
| ID | Steps | Expected |
|---|---|---|
| H1 | Start with no fenced items (or a new desktop file) | every visible desktop item (and enabled special icons, Recycle Bin first) lands in the Inbox; log `desktop reconciled: N added` |
| H2 | Fresh config (Takeover off, never answered): start; click "Not now"; restart | banner shows once in the Inbox only; after "Not now" it never returns; icons stay on the desktop |
| H3 | Look at the icons | app shortcuts show the app icon, folders the folder icon, images/videos a thumbnail; correct transparency, no black boxes |
| H4 | Look at the labels | Explorer names ("Steam", not "Steam.lnk"; extensions follow Explorer's setting); long names wrap to 2 lines with an ellipsis; tooltip shows the full name |
| H5 | Double-click a shortcut, a folder, Recycle Bin, and a shortcut whose target was deleted | each opens in front of other windows; the broken one logs `could not open` (or shows Windows' own dialog) and NeoFences keeps running |
| H6 | In Explorer: create a file on the Desktop, rename it, delete it (Recycle Bin) | the Inbox shows it within ~1 s, renames it in place, removes it; `config.json` follows |
| H7 | Unzip/copy ~500 files onto the Desktop, then delete them | all appear, then all disappear; if `lost events; reconciling` is logged the end result is still right |
| H8 | Click an item; Ctrl+click and Shift+click others; hover | single, multi and range selection highlight; hover highlight |
| H9 | Fence with more items than fit; mouse wheel over it | scrolls vertically, no horizontal scrollbar |
| H10 | Put a large video and a shortcut to a disconnected network drive on the Desktop; restart | fences appear at once and stay responsive; slow icons fill in later or stay blank |
| H11 | Turn Takeover on from the banner or menu | native icons hide; the menu item is checked; the banner is gone for good |
```

- [ ] **Step 2: Run H1–H11.** The agent runs what it can by script: H3, H4, H5 (Recycle Bin), H6, H8 via UI Automation or mouse input, and screenshots. **[USER]** H2 on a fresh profile is optional, plus H7, H9 and H10 if the agent cannot. Record each result, with screenshots where useful.

- [ ] **Step 3: Write `docs/research/m2b-desktop-items.md`.** Cover:
  - what was verified, and how;
  - icon and thumbnail quality at 100 % scaling;
  - every limitation found.

  Known limitations to confirm or refute:
  - the Recycle Bin icon does not switch full/empty until a restart;
  - toggling a special icon in "Desktop icon settings", and hiding or unhiding a file by attribute, show up only after a restart;
  - icons are not re-rendered after a DPI change;
  - shortcut arrows are not drawn (`IShellItemImageFactory` omits overlays).

- [ ] **Step 4: Commit**

```powershell
git add docs/TEST-CHECKLIST.md docs/research/m2b-desktop-items.md
git commit -m "docs: added M2b desktop item checks and results"
```

---

### Task 5: Docs sync

**Files:** `docs/DECISIONS.md`, `docs/ARCHITECTURE.md`, `docs/FEATURES.md`, `docs/ROADMAP.md`, `docs/SESSION-LOG.md`, `docs/hub/neofences-hq.html`

- [ ] **Step 1: Append ADR-014 to `docs/DECISIONS.md`**

```markdown
## ADR-014 — Desktop items: file-system listing, shell names and icons, ask-once first run
**Date:** 2026-10-02 · **Status:** Accepted

**Context.** M2b puts the real desktop into fences. It needs to decide four things: what counts as a desktop item, how changes reach the fences, where names and icons come from, and how a first run should treat Takeover. ADR-011 keeps Takeover off by default until the real sign-out check (C8) passes.

**Decision.**
- **Listing.** The desktop is the visible (not Hidden or System) entries of the user's Desktop folder (`SpecialFolder.DesktopDirectory`, which follows a OneDrive-redirected Desktop) and of the Public Desktop. On top of those come the special icons enabled under `HKCU\…\HideDesktopIcons\NewStartPanel` (Recycle Bin, This PC, User files, Network, Control Panel; Windows default: Recycle Bin only). Refs are paths or `::{CLSID}` (ADR-002). The order is special icons first, then by name.
- **Changes.** One `FileSystemWatcher` per folder (64 KB buffer). Each event becomes a Core `DesktopChange`, applied by `FenceMembership.Apply` on the UI thread. On a watcher error or overflow, a full `Reconcile` runs. Two things are not watched: special-icon toggles and attribute-only hide/unhide. Both are picked up at the next start. ponytail: upgrade path `SHChangeNotifyRegister`.
- **Names and icons.** `IShellItem` `SIGDN_NORMALDISPLAY` gives the label and `IShellItemImageFactory` the icon or thumbnail at 48 DIP × DPI. Both run on one dedicated background STA thread, and the result reaches the UI as a frozen `BitmapSource`. Until it arrives, the label is the file name and the icon is empty. Items already shown keep their loaded icon across refreshes.
- **Open.** `ShellExecute` with the default verb (`explorer.exe shell:::{CLSID}` for special icons), after `AllowSetForegroundWindow(ASFW_ANY)`. Fences never activate, so without that call the opened window could stay behind.
- **First run** (user decision, 2026-10-02): the Inbox shows a one-time banner, "Hide the desktop icons and keep them only in fences?" [Hide them] [Not now]. Any explicit choice, from the banner or the menu, sets `Settings.TakeoverPromptAnswered`. Takeover stays off until the user chooses it.
- **Keyboard** (arrows, Enter, Delete, F2) moves to M2c. Fences are `WS_EX_NOACTIVATE`, so keyboard input needs an activation design that keeps fences at the bottom.

**Consequences.**
- The Recycle Bin icon does not switch between full and empty while running.
- Icons are not re-rendered after a DPI change.
- Shortcut arrows are not drawn.
- Listing reads the file system, not Explorer's view. It therefore works before Explorer is ready at sign-in, but would miss shell-only desktop items such as namespace extensions (none seen on the dev machine).

Verified in `docs/research/m2b-desktop-items.md`.
```

- [ ] **Step 2: Update the other docs**
  - `ARCHITECTURE.md`: add the M2b classes (Shell: `DesktopItems`, `DesktopWatcher`, `ShellItems`; App: `IconLoader`, `FenceItemView`) and the item flow: start → `Reconcile` → `SetItems` → `IconLoader`; watcher → `Apply` → `SetItems`.
  - `FEATURES.md`: "Fences holding desktop icons" becomes done (M2b). Add a row for "First-run prompt in the Inbox" (done) and for "Keyboard navigation in fences" (M2c).
  - `ROADMAP.md`: tick M2b, and list the Task 4 limitations as M2c/M3 items.
  - `SESSION-LOG.md`: add an entry.

- [ ] **Step 3: Refresh the hub** (CLAUDE.md, "Project hub"): update the `HUB` object, `node --check` the script, then publish with `url` = the hub URL.

- [ ] **Step 4: Commit**

```powershell
git add docs
git commit -m "docs: added ADR-014 and synced docs and hub for M2b"
```
