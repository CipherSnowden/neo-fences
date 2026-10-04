# NeoFences

A desktop organizer for Windows 10 and 11 (64-bit), in the spirit of Stardock Fences: translucent **fences** on the
desktop that hold your desktop icons, mirror folders (**Portals**), or list every installed game (**Game Library**).
Built for a gaming PC first — it steps out of the way while you play — and for reliability above everything: your files
are never lost or hidden, and your desktop icons always come back.

## Install

1. Open the [Releases](../../releases) page and download `NeoFences.App-win-Setup.exe` from the newest release.
2. Run it. NeoFences installs for your user only (no admin rights) and starts.
3. **The first time only**, Windows may show *"Windows protected your PC"*: click **More info → Run anyway**. NeoFences is
   not code-signed yet, so Windows does not know the publisher.

After that, NeoFences **updates itself**: when a new version is ready you get a notice and a tray item *"Restart to
update"*; if you ignore it, the update installs the next time you exit NeoFences. You can switch this off in
Settings → Updates.

> **Smart App Control:** on PCs where Windows' Smart App Control is turned on (some fresh Windows 11 installs), unsigned
> apps are blocked without a "Run anyway" button. NeoFences cannot run there until it is signed.

## What it does

- **Fences** for desktop icons: drag icons in, roll a fence up to its title, lock it, tabs (combine fences into one box).
- **Portals**: a fence that shows a folder, with subfolder browsing.
- **Game Library**: one fence with your Steam, Epic, GOG, Ubisoft, EA, Battle.net and Xbox games, plus your own game folders.
- **Snapshots** of your layout, **rules** that sort new desktop items into fences, **Peek** (Ctrl+Alt+Space) to bring
  fences above your windows.
- **Appearance**: background strength, three colour styles, per-fence colours, a title font, and fences coloured from your
  wallpaper (also Wallpaper Engine).
- **Game mode**: fences go idle while a full-screen game is in front.

Uninstalling (Settings → Apps) restores your desktop icons and keeps your settings in `%LOCALAPPDATA%\NeoFences`.

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
