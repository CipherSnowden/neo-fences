# NeoFences

A desktop organizer for Windows 10 and 11 (64-bit), in the spirit of Stardock Fences. Translucent **fences** on your
desktop hold **links** to your apps, games, files, folders and websites — never the files themselves — along with
widgets (a clock, the date, system stats) and live folder panels. Built for a gaming PC first: it goes quiet while you
play, and it never moves, renames or deletes anything of yours.

![NeoFences on a desktop: a Games fence with covers, an Apps fence, a Downloads folder panel and widgets](docs/guide/desktop.png)

## Install

1. Open the [Releases](../../releases) page and download `NeoFences.App-win-Setup.exe` from the newest release.
2. Run it. NeoFences installs for your user only (no administrator rights needed) and starts. Its icon sits in the
   notification area (the tray), next to the clock.
3. **The first time only**, Windows may show *"Windows protected your PC"*: click **More info → Run anyway**. NeoFences
   is not code-signed yet, so Windows does not know the publisher.

> **Smart App Control:** on PCs where Windows' Smart App Control is on (some fresh Windows 11 installs), unsigned apps are
> blocked without a "Run anyway" button. NeoFences cannot run there until it is signed.

NeoFences **updates itself**: when a new version is ready the tray menu shows *"Restart to update"*; if you ignore it,
the update installs the next time NeoFences exits. You can turn this off in **Settings → Updates**.

## Quick start

1. **Sort your desktop into fences:** click the tray icon → **Add from desktop…** and pick the groups you want (Games,
   Apps, Folders and files, Web links). Each becomes a fence of links to what is on your desktop.
2. **Add more:** drag files, folders, apps from Start, or a web address onto a fence — or right-click a fence →
   **Add item…**.
3. **Tidy the desktop (optional):** in **Settings → General**, tick **Hide desktop icons while NeoFences runs**. Your
   icons come back whenever NeoFences exits — even after a crash.
4. **Quick-hide:** double-click empty desktop to hide all fences (and the icons); double-click again to bring them back.
5. **Peek:** press **Ctrl+Alt+Space** to bring the fences above your open windows; press it again or **Esc** to send them
   back.

The [guide](docs/GUIDE.md) explains everything else, with pictures. In the app: tray → **Help**.

## What it does

- [Fences](docs/GUIDE.md#1-fences): roll up to the title, lock, tabs, colours and a look that can follow your wallpaper
  (Wallpaper Engine too).
- [Items](docs/GUIDE.md#2-items): links to files, folders, apps and websites, with their own names, icons, arguments and
  notes; missing ones show it and can be located again.
- [Games](docs/GUIDE.md#3-games): your Steam, Epic, GOG, Ubisoft Connect, EA, Battle.net and Xbox games and your game
  folders, shown as covers.
- [Sizes and layout](docs/GUIDE.md#4-sizes-and-layout): anything from 1 × 1 to 4 × 4 cells, packed or at fixed spots.
- [Widgets](docs/GUIDE.md#5-widgets): a clock, the date and system stats (CPU, RAM, GPU, C:).
- [Folder panels](docs/GUIDE.md#6-folder-panels): a folder shown live inside a fence, as details, a list or icons.
- [Auto-collect](docs/GUIDE.md#7-auto-collect): new files of your desktop or a folder show up in a fence by themselves.
- [Snapshots](docs/GUIDE.md#9-snapshots) of your layout, and [game mode](docs/GUIDE.md#10-game-mode): fences go idle
  while a full-screen game runs.

## Your files are safe

- NeoFences only keeps **links**. Removing an item or deleting a fence never touches the file, folder, app or game it
  points at.
- Hidden desktop icons **always come back** when NeoFences exits — after a crash too, and if it is ended from Task
  Manager.
- Everything NeoFences keeps is in `%LOCALAPPDATA%\NeoFences` (settings, items, daily backups, snapshots, logs).

Uninstalling (Windows Settings → Apps) brings your desktop icons back and leaves that folder in place.

## Status and license

Free to download and use. The source is published for transparency; **all rights reserved** — it is not open source
(no license is granted to copy, modify or redistribute it).

## Building

Requirements: Windows 10/11 x64 and the .NET 10 SDK.

```
dotnet build
dotnet test
dotnet run --project src/NeoFences.App
```

`build\pack.ps1 -Version x.y.z` makes an installer locally (Velopack). Releases are built by GitHub Actions from version
tags. Design notes, decisions (ADRs) and the test checklist are in [`docs/`](docs/).
