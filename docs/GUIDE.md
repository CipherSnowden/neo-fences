# The NeoFences guide

NeoFences puts translucent **fences** on your desktop. A fence holds **links** to your things — apps, games, files,
folders, websites — plus widgets and live folder panels. It never moves, renames or deletes a file of yours: removing
something from a fence only removes the link.

Almost everything starts from two places:

- **The fence menu** — right-click a fence's title or its empty space. It comes in two shapes (**Settings → Look & feel →
  Menu style**): **Guided**, with small headings — *Add to this fence*, *This fence*, *Everything* — or **Compact**, a row
  of icons on top (add an item, rename, fence settings, lock; point at one for its name) over a short list.
- **The tray menu** — click NeoFences' icon in the notification area (left or right click). Windows 11 may tuck a new app's icon behind the **^** arrow next to the clock; drag it onto the taskbar to keep it in view.

![A desktop with NeoFences: Games, Apps, Downloads and a Desk fence with widgets](guide/desktop.jpg)

New here? Start with [Install](#install) and [First start](#first-start).

## Contents

1. [Fences](#1-fences)
2. [Items](#2-items)
3. [Games](#3-games)
4. [Sizes and layout](#4-sizes-and-layout)
5. [Widgets](#5-widgets)
6. [Folder panels](#6-folder-panels)
7. [Auto-collect](#7-auto-collect)
8. [The desktop: hiding icons, quick-hide, Peek](#8-the-desktop-hiding-icons-quick-hide-peek)
9. [Snapshots](#9-snapshots)
10. [Game mode](#10-game-mode)
11. [Updates](#11-updates)
12. [Settings](#12-settings)
13. [Your files are safe](#13-your-files-are-safe)
14. [Troubleshooting](#14-troubleshooting)
15. [Cheat-sheet](#15-cheat-sheet)

## Install

NeoFences runs on **Windows 11** (64-bit) and is free (see the [licence](../LICENSE.txt)).

1. Download **NeoFences.App-win-Setup.exe** from the
   [latest release](https://github.com/CipherSnowden/neo-fences/releases/latest).
2. Run it. Windows SmartScreen may say **"Windows protected your PC"**: NeoFences is not code-signed (a certificate costs
   money every year), and Windows warns about new unsigned apps until enough people have run them. Click **More info**,
   then **Run anyway**. NeoFences installs for your account only (no administrator rights), starts, and from then on
   keeps itself up to date.
3. If Windows says **Smart App Control** blocked NeoFences: that Windows 11 feature lets only signed or well-known apps
   run, and it has no "Run anyway". Turning it off is your decision — read
   [Microsoft's Smart App Control FAQ](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions)
   first.

Want to be sure the file is the real one? See "Check your download" in the
[README](https://github.com/CipherSnowden/neo-fences#check-your-download). To uninstall: Windows Settings → Apps →
Installed apps → NeoFences → Uninstall (your desktop icons come back; your files are never touched).

## First start

After installing, NeoFences starts with one fence: the **welcome**. It says what NeoFences is and offers three buttons:

![The welcome fence: Sort my desktop…, Add item…, Guide](guide/welcome.png)


- **Sort my desktop…** opens **Add from desktop…**: pick the groups you want (Games, Apps, Folders and files, Web links);
  each becomes a fence of links to what is on your desktop. **Hide desktop icons while NeoFences runs** is ticked there,
  so only the fences show (untick it to keep the icons; they come back whenever NeoFences exits). Once the new fences are made, the
  empty welcome fence goes away by itself.
- **Add item…** adds one file, folder, app or website to this fence.
- **Guide** opens this guide.

You can also drop files, folders or links onto the welcome fence: it then becomes an ordinary fence named "Fence"
(rename it from its menu). A Windows notice says where NeoFences' icon is: in the notification area next to the clock,
maybe behind the **^** arrow on Windows 11.

## 1. Fences

**Make a fence** in any of these ways:

- Tray → **New fence**, or fence menu → **New fence** → **Empty**.
- **Right-drag on empty desktop**: hold the right mouse button and draw a rectangle; let go and type the fence's name.
- Tray → **Add from desktop…** makes one fence per group of things on your desktop (see [Items](#2-items)).

**Name it:** fence menu → **Rename** (**Rename tab** in a fence with tabs; or double-click a tab's name).

**Move and resize:** drag the title to move; drag an edge or corner to resize. Fences snap to each other and to the
screen edges with a small gap; anywhere else they snap to an invisible 16-px grid, so fences line up.

**Roll up:** double-click the title, or fence menu → **Roll up**. The fence shrinks to its title bar; it opens again when
the mouse rests on it, or when you click its title (choose in **Fence settings… → Behaviour → Rolled-up fences open**).
Double-click again, or fence menu → **Unroll**, to unroll.

**Lock position:** fence menu → **Lock position**. A locked fence cannot be moved or resized by accident.

**Tabs:** drag a fence by its title onto another fence's title: both become tabs of one box. Click a tab to show it,
**Ctrl+Tab** to switch, fence menu → **Detach tab** to take it out again.

**Fence settings:** fence menu → **Fence settings…** (**Tab settings…** on a tab). Every look and behaviour setting of a
fence is here, in one window beside the fence. Each change shows at once — on the fence itself and in the window's
**preview** (the fence over your wallpaper, Wallpaper Engine's included; while Wallpaper Engine plays a playlist the
preview shows a plain background, since the wallpaper on screen keeps changing). Point at a setting: its part of the preview
lights up and the panel under the preview says what it does, its default and a tip.

- **This fence | All fences** (top): change this fence, or every fence at once. In **This fence**, each setting shows
  **This fence** (its own value) or **All fences** (it follows); **Use all fences'** next to an own value makes it follow
  again, and **⟲ Use all defaults** makes every setting follow. In **All fences**, a line says how many fences have their
  own value — **Make them follow** gives them all the new one. New fences follow All fences.
- **↶ Undo** (or **Ctrl+Z**) takes back the changes made in the window, one by one. **◐ Hold to compare**: press and
  hold it to see the fence as it was when you opened the window.
- **Look**: **Theme** — **Classic** (the look before), **Aurora glass** (soft coloured light drifting behind the items),
  **Game HUD** (cut corners, accent lines, a scan line, a mono title), **Neon** (a glowing edge that breathes, a lit
  title), **Light Mica** (bright glass for light wallpapers); **Accent colour** — **None**, **Windows accent** (follows
  Windows' accent colour), eight swatches, or **Custom…** (a colour field, a hue bar and a hex box); in All fences mode:
  fences without a colour get **None** or a colour **From the wallpaper** (Wallpaper Engine included); **Transparency**
  (**See-through** to **Solid**, the same in light and dark mode); **Colour style** (Classic only: **Accent edge**,
  **Tinted glass**, **Title strip**).
- **Title**: **Title style** — **Simple**, **Clean** (an accent underline and the number of items), **Pill** (emoji, name
  and count in a soft pill), **Caps** (a glowing marker, spaced capitals, the count); **Title font** (font, size,
  weight); **Title alignment**; **Show the title bar** (off: it shows while the pointer is over the fence, so you can
  still move, roll up and rename it). Caps sets the weight and Pill and Caps sit left — those rows say so.
- **Items**: **Layout** (**Grid** fills the fence in order; **Free** keeps each thing where you drop it), **Icon size**,
  **Names** (**Always**, or **On hover** for a tight grid), **Spacing** (**Compact**, **Normal**, **Roomy**; Grid only),
  **Widgets float** (a fence of only widgets).
- **Behaviour**: **Open items with** (**One click** or **Double click**), **Rolled-up fences open** (all fences),
  **Lock position**, **Auto-collect** (**Set up…**).
- The **emoji and name** sit at the top: click the emoji to pick one (or type your own; Windows' emoji show in one colour
  here), type a new name and press Enter.
- **Recipes** (bottom): one click for a whole look and behaviour — **🎮 Game shelf** (Game HUD, large icons, names on
  hover, one click), **💼 Work desk** (Light Mica, small icons with names, double click), **📥 Downloads inbox** (Aurora,
  names, double click; a folder panel in the fence shows Details, newest first), **◻ Minimal** (Aurora, almost see-through,
  title bar on hover, Caps titles, names on hover), and your own: **＋ Save as recipe…** keeps what the fence shows now
  under a name and emoji (right-click your own recipe to delete it). A recipe never touches your items, or where a fence
  sits and how big it is. In **This fence** it asks once and **Ctrl+Z** undoes it. In **All fences** it asks twice — the
  second time with **Only fences that hold games** — and takes a snapshot "Before …" first, so tray → **Restore snapshot**
  brings everything back.

**Delete:** fence menu → **Delete fence**. Only the fence and its links go; your files are not touched. NeoFences
takes a snapshot first; **Ctrl+Z** in a fence, or tray → **Undo delete**, brings it back for 2 minutes. (Keys reach a fence after
a click on the desktop or the fence; from another app use the tray.)

![The fence menu](guide/fence-menu.png)

## 2. Items

An item is a link with its own name, icon and note. The same file can be in several fences.

**Add items:**

- **Drag** files, folders or shortcuts from Explorer or the desktop onto a fence, an app from the Start menu, or a web
  address from your browser. The originals stay where they are.
- Fence menu → **Add** → **Item…**: type a path or a web address, or **Browse ▾** → **A file or program…**, **A folder…** or
  **An app…** (any app from Start, Store apps too). You can give it a name, arguments, **Run as administrator**, an icon
  (**Change icon ▾** → from a file, from a picture, or reset) and a note (shown when you hover it).
- Fence menu → **Add** → **From desktop…** (or tray → **Add from desktop…**): NeoFences reads your desktop and offers its things in groups — **Games**,
  **Apps**, **Folders and files**, **Web links**. Tick the groups you want; each becomes a fence of links (you can also
  switch on **Hide desktop icons while NeoFences runs** there).

**Use items:** one click opens an item, a game or a folder (or **Enter**); nothing stays selected. Prefer Windows' double
click? **Fence settings… → Behaviour → Open items with → Double click** — for **All fences**, or for one fence only (for
example one click in a games fence only). **Ctrl+click** or a drag box on empty fence space still selects several.
Right-click for NeoFences' menu: **Open**, **Run as administrator once** (to run it as administrator every time, tick
that in **Properties…**),
**Open file location**, **Copy path**, **Size**, **Properties…**, **Remove from fence**, and for a folder **Show as folder
panel**. **Shift+right-click** shows Windows' own menu instead — it says so at the top, because only there can a rename
or delete reach the real file.

**Move and copy:** drag items within a fence or to another fence; hold **Ctrl** to copy them instead. Drag them out to
Explorer or another app and that app gets a copy of the file — the original stays.

**Change one:** **Properties…** (or **F2** with its name selected, or **Alt+Enter**).

**Website icons:** a web link shows the site's own icon when **Find covers and website icons online** is on (Settings →
Games); otherwise a coloured letter (a red **Y** for YouTube) instead of your browser's icon.

**Missing items:** a link whose target is gone shows dimmed with a small **!** badge; one on a drive or network share
that is not connected just shows dimmed. Opening a missing one asks: **Locate…** (pick where it is now), **Remove from
fence**, or Cancel. After a Locate…, NeoFences offers to fix the other items that moved the same way, with a snapshot
first so it can be undone.

**Remove:** **Del** or **Remove from fence**. The file is never touched. An **Undo** bar shows for a few
seconds; **Ctrl+Z** in the fence works too.

**Order:** drag to rearrange, or fence menu → **Sort now** (**Name**, **Type**, **Date (newest first)**) to sort once. **Refresh** checks the links
again and reloads their icons.

![An item's menu](guide/item-menu.jpg)

## 3. Games

NeoFences finds your installed games — **Steam, Epic, GOG, Ubisoft Connect, EA app, Battle.net, Xbox / Microsoft
Store**, the **game folders** you add, and game shortcuts on your desktop — and shows them as cover tiles.

- Fence menu → **Add** → **Games…** lists every game NeoFences found; tick the ones you want in that fence.
- **Settings → Games**: **Look for games in** (which launchers to read), **Game folders (each sub-folder is a game)** —
  used when **My game folders** is ticked —, **New games go to** (the fence where newly installed games appear),
  **Refresh library now**, **Find covers and website icons online**, and **Go idle while a full-screen game runs**.
- A game's menu: **Open**, **Show as ▸ Cover tile / Icon**, **Choose cover…**, **Size** (the same grid as every item), **Open install folder**, **Copy path**,
  **Properties…**, **Remove from fence**. A game that is no longer installed says "Not installed".
- **Covers:** Steam games bring their cover from your disk. For the others NeoFences can **find covers online** on the
  Steam store: it asks ("Find covers online?"; **Not now** asks again the next time NeoFences starts), and the switch is **Settings → Games → Find covers and website
  icons online**. Only the names of games without a cover are sent, and only an exact name counts. A game without a cover
  shows its icon on a tile in its own colours.
- **Choose cover…** (a game's menu): pick one of the Steam store's covers for its name, **From a file…** (NeoFences keeps a
  copy; your picture is not moved), or **Reset to automatic**.

![A Games fence with covers](guide/games.jpg)

## 4. Sizes and layout

- **Size:** right-click an item, widget or panel → **Size** and pick from a 4 × 4 grid (1 × 1 up to 4 × 4 cells), or
  **Default size** (a game cover's default is one cell wide, two high). With several things selected, it sets them all. The arrow keys and **Enter** work in the grid too.
- **Layout:** fence menu → **Fence settings…** → **Items** → **Layout** → **Grid** fills the fence in order; **Free** keeps
  each thing where you drop it.
- **Icon size:** **Fence settings…** → **Items** → **Icon size** (Small, Medium, Large, Extra large) — for this fence or
  All fences.
- **Names:** **Fence settings…** → **Items** → **Names** → **Always**, or **On hover** for a tight grid that shows a name
  only when you point at it. A game cover then shows its name over itself.
- **Spacing:** **Fence settings…** → **Items** → **Spacing**: **Compact**, **Normal** or **Roomy** room between the
  things in a fence (they keep their size).

## 5. Widgets

Point at a fence and click the **+** on its title (or fence menu → **Widget…**): the gallery shows **Clock**, **Calendar**,
**System stats** and **Now playing** live. Point at one and click **S**, **M** or **L** to add it **Small**, **Medium** or
**Large** — a bigger size shows more, not just bigger.

- **Size:** drag a widget's corner (the grip shows when you point at it) — it snaps to Small, Medium or Large and says
  which while you drag — or right-click it → **Size** (or pick **S**, **M**, **L** in the gallery). A widget in a fence too
  narrow for its size shows the biggest one that fits (a Large widget in a narrow fence shows Medium); widen the fence to
  see it Large.
- **Settings:** point at a widget and click **⚙** (or right-click → **Widget settings…**): it flips over to its settings;
  changes show at once; **Done** flips it back.
- **Clock:** Small shows the time and the day, Medium adds the date, Large the seconds and up to two **world clocks** (pick
  the cities on the Large clock's back). The size decides what shows. It follows Windows' time format (12- or 24-hour).
- **Calendar** (the old Date widget): Small is today's page, Medium the month (**‹** **›** to look at other months), Large
  both, with the week number. The week starts as in Windows' region settings.
- **System stats** shows **CPU** and **GPU** use (Small), with **CPU TEMP** and **GPU TEMP** (Medium), and **RAM** in use
  (Large, e.g. "13.0 / 32 GB"). Temperatures in °F: on its back.
- **Now playing** shows what plays with Windows' media controls — Spotify, a browser tab, Media Player: the cover's colours
  glow behind the glass, the record spins out of its sleeve while it plays. **Previous**, **play/pause**, **next**, click
  the bar to jump, **shuffle** and **repeat** on Large (when the player allows them). Several players? Small pills on Large
  switch between them. The mouse wheel over it changes the volume; a click on the cover brings the player to the front.
  Nothing playing: the last song, dimmed. It reads only from Windows, on your PC.
- **Widgets float:** a fence with only widgets can drop its glass and title (Fence settings… → **Widgets float**, or the
  **Float** tip that shows once): each widget becomes its own glass card on the wallpaper. Point at the fence to see its
  outline and title — to move, resize or right-click it.
- Temperatures: Windows lets apps read the CPU temperature only through a hardware monitor. If **MSI Afterburner** or
  **HWiNFO64** (with its "Shared Memory Support" on) is running, NeoFences reads their values — read-only, nothing to
  set up. Without one, CPU TEMP shows "—" and the GPU temperature comes from Windows. The graphics card shown is the one
  using the most video memory (not a built-in one).
- Double-click the clock to open Windows' Clock app, the stats to open Task Manager (widgets keep the double-click when
  items open with one click).
- Widgets do no work while nobody can see them — in a game, while paused or quick-hidden, or rolled up.

![Widgets: a clock, the date and system stats](guide/widgets.png)

## 6. Folder panels

A folder panel shows a folder inside a fence, live and read-only.

- Fence menu → **Add** → **Folder panel…** adds one; fence menu → **New fence** → **Folder panel…** (or tray → **New folder
  panel…**) makes a new fence holding one
  that fills it; a folder item's menu → **Show as folder panel** turns it into one.
- Right-click the panel's name or empty space for its menu: **Open folder**, **Look** (**Details**, **List**,
  **Icons**), **Sort by**, **Panel settings…** (the folder, what it shows, **Only files like** your patterns, only the newest N), **Size**, **Fill
  fence** (when it is the fence's only element), **Show as icon**, **Remove from fence**.
- In Details, click a column header (**Name**, **Date modified**, **Type**, **Size**) to sort; click again to reverse.
- Double-click a subfolder to look inside; the **Back**, **Up** and **Back to the panel's folder** buttons (or **Backspace** and **Alt+Up**) bring
  you back. Next time the panel starts at its own folder again.
- Right-click an entry: **Open**, **Open file location**, **Copy path**, **Add to fence**. Drag entries out to copy them,
  or onto a fence to add links.
- Files dropped on a panel are refused — NeoFences never writes into the folder.

![A folder panel in Details](guide/panel.png)

## 7. Auto-collect

A fence can collect new files by itself. Fence menu → **Auto-collect…** → **New rule**:

- **Watch**: Desktop, Downloads, or **Choose folder…**.
- **Collect**: **Apps and shortcuts**, **Installers**, **Documents**, **Pictures**, **Archives**, **Anything** (takes every
  file, so the other kinds grey out) — and **Also files named** with your own patterns, like `*.iso`.
- The dialog shows how many files there match now. When you click OK it asks **"Add these N too?"** for those; after
  that, only new files are collected.

What happens next: a new matching file shows up in the fence within a couple of seconds, as a link — the file stays where
it is. If two fences' rules match, only one of them gets it (the fence made first). Remove a collected item and it stays removed.
Files that appear while NeoFences is closed are picked up at its next start. Nothing is collected during a game; it
catches up afterwards.

![The Auto-collect dialog](guide/auto-collect.png)

## 8. The desktop: hiding icons, quick-hide, Peek

- **Hide desktop icons:** **Settings → Desktop → Hide desktop icons while NeoFences runs**. They come back whenever
  NeoFences exits — also after a crash or if it is ended in Task Manager.
- **Quick-hide:** double-click empty desktop to hide every fence (and the desktop icons) at once; double-click again, or
  tray → **Quick-hide**, to bring them back. While they are hidden the desktop is all yours: Windows' right-click menu,
  dragging, and interactive wallpapers (Wallpaper Engine) work exactly as without NeoFences — it only waits for the
  double-click that brings the fences back.
- **Click wallpaper to reveal the desktop** (**Settings → Desktop**, off at first): click empty wallpaper and your
  windows step aside (like **Win+D**), so your fences and wallpaper are in view; click empty wallpaper again to bring them
  back. A double-click still quick-hides; a quick click on another window meanwhile cancels the reveal.
- **Gesture switches:** **Settings → Desktop → Double-click empty desktop hides all fences**, **Click wallpaper to reveal
  the desktop** and **Right-drag draws a new fence** turn each gesture on or off (each with a small picture of it). With
  all of them off, NeoFences does not watch mouse clicks at all.
- **Peek:** **Ctrl+Alt+Space** lifts all fences above your open windows and puts the keyboard in a fence — the one under
  the mouse, else the one you used last, else the first (left to right, top to bottom). An accent outline shows which
  fence has the keyboard. **Arrow keys**, **Home** and **End** move between items, **Enter** opens, **Tab** /
  **Shift+Tab** go to the next / previous fence. Press the hotkey again or **Esc** to send the fences back — the app you
  were in gets the keyboard back; a click outside or opening something also ends Peek. Change the keys in
  **Settings → Peek & keys → Peek hotkey**.

  ![Peek over Notepad: the Games fence has the keyboard (accent outline), its first cover the ring](guide/peek.jpg)

- **Pause:** tray → **Pause NeoFences** gives the desktop back to Windows (fences hidden, icons shown) until you resume.

## 9. Snapshots

A snapshot saves your fences, their places and their items.

- Tray → **Take snapshot**; tray → **Restore snapshot** → pick one.
- Every restore first saves "Before restore", so tray → **Restore snapshot** → **Undo the last restore or fix** takes you
  back.
- **Settings → Snapshots & safety**: **Take snapshot**, **Restore**, **Rename**, **Delete** (to the Recycle Bin), **Open
  snapshots folder**.
- NeoFences also takes one by itself before big changes (for example before an update reshapes your fences).

**Your whole setup in one file** (for another PC, or a fresh Windows):

- **Settings → Snapshots & safety → Export setup…** saves one `.neofences` file: your fences, their items, places and
  looks, your settings, your own recipes, and the pictures you chose as item icons and covers. Not the logs, not the game
  list (it is found again on any PC), and never your files themselves.
- **Settings → Snapshots & safety → Import setup…** checks the file (a NeoFences export, readable, not from a newer NeoFences),
  asks, saves your current setup as the snapshot "Before import", then puts the file's setup in place. To undo, restore
  "Before import". Items whose files or apps are not on this PC show as missing, as usual.

## 10. Game mode

While a full-screen game is in front, NeoFences goes idle: fences stay at the bottom, the desktop mouse gestures and Peek
are off, widgets and folder panels stop updating, auto-collect waits, and update downloads pause. Turn it off in
**Settings → Games → Go idle while a full-screen game runs**.

## 11. Updates

A test version for friends (a **release candidate**, e.g. 1.0.0-rc.1) updates to the next test version and then to the
finished release by itself; a normal copy never switches to a test version.

NeoFences checks for updates and downloads them in the background. When one is ready the tray menu shows **Restart to
update to v…**; if you ignore it, the update installs the next time NeoFences exits. **Settings → Updates & privacy**:
**Download updates automatically** (on or off), **Check now**, and what leaves your PC (with a link to the privacy note).

## 12. Settings

Tray or fence menu → **Settings…**. Coloured page tiles on the left open one page at a time; every setting says in one
line what it does; on/off settings are switches. **Search settings** (top left) finds any setting by its words — here and
in Fence settings; click a result to go there (a Fence settings result opens Fence settings for **All fences** at that
setting).

- **Look & feel** — **Change the look of all fences…** (Fence settings for All fences: theme, titles, colours, icon sizes,
  clicks), **Animations** (**Lively**, **Calm**, **Off**: items lift under the mouse and dip when pressed, the themes' light
  moves; always off in games and when Windows' animation effects are off), **Item names** (**Crisp**, **Chips** — each name
  on a soft pill, sharpest on busy wallpapers — or **Plain**), **Menu style** (**Guided** or **Compact**). The colour from
  the wallpaper is in **Change the look of all fences…** → **Accent colour**.
- **Desktop** — **Hide desktop icons while NeoFences runs**, the three desktop gestures with a picture each, **Start with
  Windows**, **Show shortcut arrows**.
- **Games** — see [Games](#3-games) and [Game mode](#10-game-mode).
- **Peek & keys** — **Peek hotkey** and the keys that work in a fence.
- **Snapshots & safety** — snapshots, **Export setup…** / **Import setup…**, and **Reset settings…**: every setting back to
  how NeoFences comes (asked first, a snapshot "Before reset" first). Your fences, items, own recipes, game folders and
  chosen covers stay.
- **Updates & privacy** — see [Updates](#11-updates).
- **Help & about** — the version, **Help (online guide)**, **Open logs folder**, **Open data folder**, **Licence and
  notices** (the licence; the third-party notices and the privacy note are next to it).

![Settings](guide/settings.png)

## 13. Your files are safe

- NeoFences keeps **links only**. Removing an item, a panel or a fence never touches a file, folder, app or game.
- It never writes into a folder you show in a panel or watch with auto-collect.
- Hidden desktop icons **always come back**: when NeoFences exits, crashes, or is ended in Task Manager (a small helper
  watches for that), and when you uninstall it.
- With **Find covers and website icons online** on, NeoFences sends the names of games without a cover to the Steam store
  and asks your web links' sites for their icons; nothing else leaves your PC. Updates come from GitHub. Pictures it finds
  are kept in `%LOCALAPPDATA%\NeoFences\covers`.
- Everything it keeps is in `%LOCALAPPDATA%\NeoFences`: settings (`config.json`), items (`items.json`), daily backups,
  snapshots and logs. If a file there is damaged, NeoFences starts from its backup.

## 14. Troubleshooting

- **Desktop icons stay hidden** (very unlikely): right-click the desktop → **View** → **Show desktop icons**.
- **The browser warns about the download** ("isn't commonly downloaded"): in Edge, **…** → **Keep** → **Show more** → **Keep anyway**; in Chrome, **Keep**. NeoFences is new and unsigned, so few people have downloaded it yet.
- **"Windows protected your PC"** when installing: **More info → Run anyway** (NeoFences is not code-signed yet). With
  **Smart App Control** on, Windows blocks unsigned apps without that button; NeoFences cannot run while Smart
  App Control is on.
- **A fence is off-screen** after changing monitors: NeoFences moves fences back onto a screen by itself when the displays
  change; if one still hides, **Settings → Snapshots & safety → Restore** an earlier layout.
- **An icon looks out of date** (an app updated its icon): fence menu → **Refresh** asks Windows again. NeoFences keeps
  icons in `%LOCALAPPDATA%\NeoFences\cache` to start faster; deleting that folder is safe (it fills again).
- **"NeoFences started in safe mode"**: it stopped unexpectedly several times in a row, so it started with fences
  only (no widgets updating, folder panels as plain folders, no gestures, no auto-collect). Tray → **Leave safe mode**
  starts it normally again. If it keeps stopping, report it with the log.
- **"NeoFences stopped after repeated crashes"**: safe mode stopped too. **Open logs** for the report; **Start from a
  backup** saves your current setup as a snapshot and starts safe mode with the newest daily backup; **Close** leaves it
  off until you start it again.
- **"Changes are not saved"**: NeoFences could not read or write its files (another program locking them, or a file from
  a newer version). Your fences work, but changes are lost at exit; restart NeoFences, and see the log.
- **Something went wrong:** **Settings → Help & about → Open logs folder** and look at the newest file; report problems
  with the **Bug** form on the project's [GitHub Issues page](https://github.com/CipherSnowden/neo-fences/issues/new/choose):
  what you did, what happened, and that log if you like.
  Logs never contain your Windows user name (the profile folder is written as `%USERPROFILE%`), but they name the
  folders and files your fences point at — look through a log before you attach it. Security problems: privately, see
  [SECURITY.md](../SECURITY.md).
- **CPU TEMP shows "—"**: start MSI Afterburner (with "CPU temperature" ticked in its Settings → Monitoring) or HWiNFO64 (in HWiNFO, turn on "Shared Memory Support" in its
  settings; the free version turns it off again after 12 hours).

## 15. Cheat-sheet

| Where | Do this | What happens |
|---|---|---|
| Empty desktop | Right-drag | Draw a new fence |
| Empty desktop | Double-click | Quick-hide (again: show) |
| Empty wallpaper | Click (when switched on) | Windows step aside; again: they come back |
| Anywhere | **Ctrl+Alt+Space** (then **Esc**) | Peek: fences above windows, the keyboard in a fence (and back to your app) |
| Peek | **Tab** / **Shift+Tab** | Next / previous fence |
| Fence title | Double-click | Roll up / unroll |
| Fence title | Drag onto another fence's title | Make tabs |
| Fence | **Ctrl+Tab** / **Ctrl+Shift+Tab** | Next / previous tab |
| Fence | Right-click | Fence menu |
| Item | Click (or double-click, if you chose it) or **Enter** | Open |
| Item | **Ctrl**+click | Select several |
| Fence title | **+** (pointing at the fence) | Widget gallery |
| Widget | **⚙** / drag its corner | Its settings (it flips over) / Small, Medium or Large |
| Item | Right-click / **Shift+right-click** | NeoFences' menu / Windows' menu |
| Item | **Del** | Remove from fence (the file stays) |
| Fence | **Ctrl+Z** | Undo the last removal or fence deletion |
| Item | **F2** / **Alt+Enter** | Properties |
| Item | Drag / **Ctrl**+drag | Move / copy (to another fence too) |
| Empty fence space | Drag | Select several |
| Folder panel | Double-click a subfolder; **Backspace**, **Alt+Up** | Look inside; back, up |
| Size grid | Arrow keys, **Enter** | Choose and set a size |
| Tray icon | Click | Tray menu |
