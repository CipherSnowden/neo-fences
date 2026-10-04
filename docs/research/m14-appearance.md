# M14 — Appearance: results

**Date:** 2026-10-04 · **Machine:** Windows 11 Pro 25H2 (26200), NeoFences 1.6.2 installed, Wallpaper Engine running.
**Build:** M14 prototype (Debug) · **Checklist:** `docs/TEST-CHECKLIST.md` section AA · **Decision:** ADR-036.

## Read-only probes

- Wallpaper Engine: `C:\Program Files (x86)\Steam\steamapps\common\wallpaper_engine\config.json` →
  `cipher.general.wallpaperconfig.selectedwallpapers.Monitor0.file` = `…\workshop\content\431960\1405736695\scene.pkg`;
  that folder's `project.json` names `preview.jpg` ("Detroit: Become Human - Kara"). Windows' wallpaper: a Spotlight image.
- `WallpaperSources.Read` on the user's PC: one monitor (0,0)-(1920,1080), source Wallpaper Engine, 85 ms in all.
- Accent: first `#566376` (a muted slate from the snowy background), then — with a saturation floor of 45 % — `#385E94`
  (steel blue, same hue). Windows' accent colour: `#E81123`.

## Live check on the prototype (the user's go; config and installed copy restored)

| Check | Result |
|---|---|
| AA4 Accent edge, Inbox custom red | **Pass**: red outline, pinkish title, bar. |
| AA4/AA10 Tinted glass at 40 % with the accent on | **Pass**: Inbox red glass; the uncoloured fence took `#385E94` from the WE wallpaper (log: "wallpaper accent: Wallpaper Engine: Detroit: Become Human - Kara #385E94"). |
| AA6/AA7 Title strip yellow, Bahnschrift 20 Bold on the Inbox | **Pass**: yellow strip, dark bold title, taller title bar, 60 % veil. |
| Settings → Appearance | **Pass**: style tiles, slider 40 (dark mode), "From Wallpaper Engine: Detroit: Become Human - Kara". |
| Firefox Picture-in-Picture window open (user) | **Pass**: stayed topmost over a fence corner, never minimized, same place afterwards. |
| Config | byte-identical after the restore; the installed 1.6.2 restarted. |

A power cut happened between building the prototype and this check: the installed 1.6.2 came back by itself after the
reboot with its config and icons, no warnings in the log.

## Core (test-first)

16 new tests (419 in all): v1.6 defaults in both tones, strength per tone, the colour order, each style, title contrast,
font inheritance and title heights, dominant colour (vivid hue wins, grey none, lightness and saturation floors), Wallpaper
Engine files (selection, preview, title, a preview path that leaves the folder), schema 3 repairs and round trip, the
fence menu edits.

## Branch check after the final review (the user's go; config and installed copy restored)

| Check | Result |
|---|---|
| Three styles, accent, Settings → Appearance | **Pass** as above; the yellow strip keeps its dark title; the accent is `#385E94` |
| Firefox Picture-in-Picture | **Pass**: unchanged (visible, topmost, same place) |
| Log | 0 warnings or errors; config byte-identical afterwards. |
