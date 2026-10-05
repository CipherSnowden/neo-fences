# M29 — A guide for friends (0.17.0) — design

**Date:** 2026-10-05 · **Status:** approved in brainstorm, awaiting written-spec review
**Decisions:** ADR-050 (help lives on GitHub, linked from the app)

## Goal

NeoFences is ready to be tried by friends, but its README still describes the pre-pivot design (Takeover, Portals, rules
that move desktop icons). This milestone gives a newcomer what they need: a correct README, one guide to every feature
with screenshots, and a way to reach it from the app. The user chose: help on GitHub, linked from the app (no in-app help
window); screenshots from a demo setup (nothing personal in the public repo).

Hard rules stand. The app change is two menu entries that open a web page through Windows (ShellExecute via NeoFences.Shell's
existing open path); nothing else changes.

Success: a friend reads the README, installs past SmartScreen, follows the quick start to a sorted desktop in five minutes,
and finds any feature (with a picture) in the guide; the tray's Help opens that guide.

## 1. README.md (rewritten)

- What NeoFences is, in two sentences: translucent fences on the desktop that hold **links** to your apps, games, files,
  folders and websites — never the files themselves — plus widgets and live folder panels; built for a gaming PC.
- One screenshot (the demo desktop).
- **Install**: download `NeoFences.App-win-Setup.exe` from Releases; per-user, no admin; the first-time SmartScreen step
  ("More info → Run anyway", not signed yet); the Smart App Control note; updates install themselves (Settings → Updates).
- **Quick start** (5 steps): right-click the tray icon → Add from desktop… (sort the desktop into fences) · drag things in
  · Settings → hide desktop icons (optional) · double-click the empty desktop to quick-hide · Ctrl+Alt+Space to Peek.
- **What it does**: a short list, each linking to its guide section.
- **Your files are safe**: removing an item or a fence never touches a file; desktop icons always come back (also after a
  crash); everything NeoFences keeps is in `%LOCALAPPDATA%\NeoFences`.
- Uninstall, Status and license (unchanged: all rights reserved), Building (kept, updated commands).

## 2. docs/GUIDE.md

One page with a contents list; plain words, second person, short sections, a screenshot where it helps:

1. **Fences** — create (tray → New fence, right-drag on the empty desktop, fence menu), rename, move and resize, roll up
   (double-click the title; hover or click opens), lock, tabs (drop a fence on another's title; Detach tab), colours and the
   look (Settings → Appearance; wallpaper colours, Wallpaper Engine).
2. **Items** — drag files, folders, apps or links in; Add item… (path, website, or an app from Start); Add from desktop…;
   open, Run as administrator, Open file location, Copy path; Properties (name, icon, arguments, note); missing items
   (dimmed with a badge; Locate…, and the offer to fix others from the same place); Remove never touches the file;
   Shift+right-click for Windows' own menu (with its warning); Sort by; Refresh.
3. **Games** — where NeoFences finds games (launchers, game folders, desktop game shortcuts); Add games…; covers or icons;
   where new games go (Settings → Game library); "not installed".
4. **Sizes and layout** — Size ▸ (1 × 1 to 4 × 4, the keyboard), Layout ▸ Flow (packed) or Free (fixed spots), icon size,
   labels always or on hover.
5. **Widgets** — Add widget ▸ Clock, Date, System stats; clock options; sizes; they rest while you play.
6. **Folder panels** — Add folder panel… / New folder panel… / Show as folder panel; Details, List, Icons; sorting by a
   column; browsing in (Back / Up / Home); Fill fence; read-only (drops refused, Windows' menu with Shift).
7. **Auto-collect** — Auto-collect… rules (Desktop or a folder; kinds and patterns); what happens to new files; "Add these
   N too?"; removed stays removed; nothing moves.
8. **The desktop** — hide desktop icons (and that they always come back); quick-hide (double-click empty desktop); Peek
   (Ctrl+Alt+Space, Esc); Pause from the tray.
9. **Snapshots** — take, restore, undo the last restore or fix; automatic ones (before big changes).
10. **Game mode** — what goes quiet while a full-screen game runs.
11. **Updates** — automatic, Restart to update, check now, turning it off.
12. **Settings** — a short tour of each card.
13. **Your files are safe** — the promises (never touches files; icons come back; data folder; backups and snapshots).
14. **Troubleshooting** — desktop icons stuck hidden (right-click desktop → View → Show desktop icons); SmartScreen /
    Smart App Control; a fence off-screen (display changes); where the logs are (Settings → Open logs folder); reporting a
    problem (GitHub issues).
15. **Cheat-sheet** — every mouse gesture and key in one table.

Screenshots: about 8 PNGs in `docs/guide/` (the demo desktop, the fence menu, an item menu, a game fence with covers,
widgets, a Details panel, the Auto-collect dialog, Settings), cropped, each under ~400 KB.

## 3. Screenshots from a demo setup

A script builds demo fences on a backed-up copy of the user's data — neutral titles ("Games", "Apps", "Downloads",
"Desk"), the user's real game covers (public art), an Apps fence of common apps, a Details panel and an auto-collect rule on
a demo folder (`%USERPROFILE%\NeoFences-guide-demo`, created and removed by the script), widgets — takes the shots, then
restores the data and removes the demo folder. Before committing, every shot is checked for personal names (the user's
name, private files, the email). Taking the shots drives the mouse for about two minutes: asked first.

## 4. In the app

- Tray menu: **Help** (above Settings…), opening the guide's GitHub page in the default browser.
- Settings → About and logs: a **Help (online guide)** button beside Open logs folder.
- Both through the existing open path (off the UI thread, a failure logged). The URL is
  `https://github.com/CipherSnowden/neo-fences/blob/main/docs/GUIDE.md` (public).

## 5. Testing and release

- No Core change. `TEST-CHECKLIST` section **AO**: tray Help and Settings Help open the guide; the README's links and the
  guide's contents links work on GitHub; screenshots show nothing personal.
- The plan: the docs and screenshots are written in the session (no prototype needed for prose); the two menu entries as
  a small patch, replay-verified; Opus review of the docs for accuracy against the app (every step in the guide must match
  what the app does); release **0.17.0**, asked first.

## Out of scope

An in-app help window; translations; code signing; a website; video.
