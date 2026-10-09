# NeoFences

A desktop organizer for Windows 11 (64-bit), in the spirit of Stardock Fences. Translucent **fences** on your
desktop hold **links** to your apps, games, files, folders and websites — never the files themselves — along with
widgets (a clock, the date, system stats) and live folder panels. Built for a gaming PC first: it goes quiet while you
play, and it never moves, renames or deletes anything of yours.

![NeoFences on a desktop: a Games fence with covers, an Apps fence, a Downloads folder panel and widgets](docs/guide/desktop.jpg)

**[Download for Windows 11](https://github.com/CipherSnowden/neo-fences/releases/latest/download/NeoFences.App-win-Setup.exe)**
· [Website](https://ciphersnowden.github.io/neo-fences/) · [Guide](docs/GUIDE.md) · Free ([licence](LICENSE.txt))

## Install

1. Download `NeoFences.App-win-Setup.exe` from the [latest release](https://github.com/CipherSnowden/neo-fences/releases/latest).
   If your browser warns that the file "isn't commonly downloaded", keep it (Edge: **…** → **Keep** → **Show more** →
   **Keep anyway**).
2. Run it. NeoFences installs for your user only (no administrator rights needed) and starts. Its icon sits in the
   notification area (the tray), next to the clock. On Windows 11 it may be behind the **^** arrow there; drag it
   onto the taskbar to keep it in view.
3. **The first time only**, Windows may show *"Windows protected your PC"*: click **More info → Run anyway**. NeoFences
   is unsigned, not unsafe: a code-signing certificate costs money every year, so Windows does not know the publisher
   yet. Instead, every download can be checked — see [Check your download](#check-your-download).

> **Smart App Control:** if it is on (some fresh Windows 11 installs), Windows blocks unsigned apps such as NeoFences
> and offers no "Run anyway". Turning it off is your decision — read
> [Microsoft's Smart App Control FAQ](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions)
> first.

NeoFences **updates itself**: when a new version is ready the tray menu shows *"Restart to update"*; if you ignore it,
the update installs the next time NeoFences exits. You can turn this off in **Settings → Updates**.

## Quick start

1. **Sort your desktop into fences:** on the first start, the welcome fence offers **Sort my desktop…** (later: click the
   tray icon, or right-click a fence → **Add** → **From desktop…**). Pick the groups you want (Games, Apps, Folders and files,
   Web links); each becomes a fence of links to what is on your desktop.
2. **Add more:** drag files, folders, apps from Start, or a web address onto a fence — or right-click a fence →
   **Add** → **Item…**.
3. **Tidy the desktop (optional):** in **Settings → General**, switch on **Hide desktop icons while NeoFences runs**. Your
   icons come back whenever NeoFences exits — even after a crash.
4. **Quick-hide:** double-click empty desktop to hide all fences (and the icons); double-click again to bring them back.
5. **Peek:** press **Ctrl+Alt+Space** to bring the fences above your open windows with the keyboard in one (arrows,
   **Enter**, **Tab** to the next fence); press it again or **Esc** to send them back.

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

## Check your download

Every release lists `SHA256SUMS.txt`, and GitHub keeps a signed record (a build attestation) that each file was built
from this repository by its release workflow. In PowerShell, in your Downloads folder:

```powershell
(Get-FileHash .\NeoFences.App-win-Setup.exe -Algorithm SHA256).Hash   # compare with the line in SHA256SUMS.txt
gh attestation verify .\NeoFences.App-win-Setup.exe --repo CipherSnowden/neo-fences   # needs the GitHub CLI
```

## Licence, privacy, security

- **Free** to use at home or at work and to share unchanged — see [LICENSE.txt](LICENSE.txt). The source is published so
  anyone can see what NeoFences does; it is not open source.
- **Privacy:** no telemetry, no accounts, no ads; the only network traffic is the update check and, if you turn them on,
  game covers and website icons — see [PRIVACY.md](PRIVACY.md).
- **Bugs and ideas:** the [issue forms](https://github.com/CipherSnowden/neo-fences/issues/new/choose).
  **Security problems:** privately, see [SECURITY.md](SECURITY.md).
- Third-party components and their licences: [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

## Building

Requirements: Windows 11 x64 and the .NET 10 SDK.

```
dotnet build
dotnet test
dotnet run --project src/NeoFences.App
```

Core (`src/NeoFences.Core`) is plain .NET: `dotnet test tests/NeoFences.Core.Tests` also runs on Linux, as CI does.
`build\pack.ps1 -Version x.y.z` makes an installer locally (Velopack). Releases are built by GitHub Actions from version
tags. Design notes, decisions (ADRs) and the test checklist are in [`docs/`](docs/).
