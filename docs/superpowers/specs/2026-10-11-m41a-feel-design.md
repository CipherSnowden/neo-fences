# M41a — Feel (1.0.0-rc.3) — design

**Date:** 2026-10-11 · **Status:** approved in brainstorm, awaiting written-spec review
**Decisions:** ADR-063 (themes and motion), ADR-064 (one click opens), ADR-065 (widgets: sizes, gallery, Now playing and
the Windows SDK projection), ADR-066 (a hidden desktop belongs to the user) — added by the plan.
**Source:** friends' feedback on rc.1 (`docs/SESSION-LOG.md`, 2026-10-11); the owner's picks in the visual companion
(mockups in `.superpowers/brainstorm/910-1791664080/content/`, git-ignored): `titles-v2`, `themes`, `motion-v2`,
`typography`, `selection`, `menus`, `widgets-catalog`, `widgets-sizes`, `now-playing-v3`, `widgets-add`, `widget-board`,
`widgets-new`, `clock-stats`.
**Follows:** M41b "Settings" (option audit, Fence settings v4, Settings v2, recipes) → rc.4 → about a week → 1.0.0.

## Goal

Fences stop feeling "dead": they get themes with gentle life in them, titles that read well, motion that answers the
mouse, a click that just opens, and widgets that look like they belong on a modern desktop — Now playing first. When the
user double-clicks the desktop to hide everything, the desktop is fully theirs (Wallpaper Engine included). Hard rules
stand: never touch user files, icons always come back, no hooks beyond `WH_MOUSE_LL`, Win32/COM only in Shell, Core pure
and test-first, failures logged and degrade one feature. Everything that moves stops in game mode, when Windows'
animations are off, and when Motion is Off.

## 1. Themes (ADR-063)

A **theme** is the fence's whole look: background, edge, title ink, light. Per fence (Fence settings), with a default for
new fences (Settings → Appearance). Every theme keeps the fence's **accent** (its colour swatch / custom colour /
wallpaper accent, unchanged order) and the existing **strength** slider, shown as **Transparency** (same value, same
per-fence override).

| Theme | Look | What moves (Motion on) |
|---|---|---|
| **Classic** | Today's look with its three colour styles (accent edge, tinted glass, title strip). | Nothing. |
| **Aurora glass** | Frosted dark glass; soft accent-tinted colour light drifting behind the items. | The light drifts (one slow loop, ~20 s). |
| **Game HUD** | Cut corners (12 px chamfer), 1 px accent lines, a mono title (Cascadia Mono, fallback Consolas), faint corner ticks. | A thin scan line sweeps down every ~8 s. |
| **Neon** | Dark glass with a glowing accent edge and a lit title. | The glow breathes (4 s in, 4 s out, small range). |
| **Light Mica** | Bright frosted glass with depth: light veil, fine grain, a top highlight, dark ink. | Nothing. |

- **Defaults:** new installs — Aurora glass for new fences. Existing setups keep Classic on every fence (nothing changes
  under the user's feet); the first rc.3 start shows one "New: fence themes" note with a **Try them** button that opens
  Fence settings.
- **Rendering:** WPF inside today's layered window (ADR-011) — gradient brushes, geometry clips, a pre-rendered glow
  (no per-frame `DropShadowEffect`), a tiled grain bitmap. Real Mica is not possible on a layered window; Light Mica is
  drawn.
- **Ambient motion budget:** theme motion renders at most 20 frames a second, only for fences that are visible and not
  covered by a fullscreen window, never while rolled up or hidden. Target: with Motion "Lively" and 10 themed fences,
  NeoFences averages under 2 % CPU on the owner's PC (measured once, the owner asked first); if the measurement misses,
  ambient motion drops to 10 fps before anything else is cut.
- **Light/dark ink:** Light Mica uses dark ink; the other new themes use light ink with the crisp name shadow (§4).
- Model: `OwnLook.Theme` (nullable = like all fences), `AppearanceSettings.Theme` (default for new fences). The existing
  `ColourStyle` stays and applies to Classic only. Resolved by `FenceLook.Resolve` into `FenceStyle` (Core, test-first).

## 2. Titles

Per fence **title style**, with a default in Settings → Appearance:

- **Simple** — today's title, slightly larger display type (Segoe UI Variable Display).
- **Clean** — name left, a short accent underline, the item count right.
- **Pill** — the fence's **emoji** and name in a soft pill, count beside it.
- **Caps** — bold 12.5 px caps, letter-spacing 1.6, a small glowing accent marker on the left, count on the right
  (titles-v2 "Caps B").

Existing setups keep Simple; new installs default to Clean. **Emoji:** a new per-fence `Fence.Emoji` (one grapheme, or
none), picked in Fence settings (a small emoji grid plus "type your own"); shown by Pill, and before the name in the
other styles when set. The count is the number of items (widgets excluded), hidden when the fence is empty. Title on
hover (existing) now fades (150 ms) instead of snapping. Font family/size/weight options stay; alignment applies to
Simple and Clean (Pill and Caps are left-aligned by design).

## 3. Motion (ADR-063)

One global setting **Motion: Off / Calm / Lively** (Settings → Appearance). Default **Lively**. Treated as Off in game
mode, when Windows' "Animation effects" is off (`SystemParameters.ClientAreaAnimation`), and in safe mode.

| | Calm | Lively |
|---|---|---|
| Item hover | lift 2 px + soft accent glow, name brightens (120 ms, spring ease) | Calm + slight tilt (≤ 2°) and a light sweep across the icon |
| Item press | dip to 96 % (80 ms), spring back | Calm + a small accent ripple |
| Items arriving (open, add, tab switch, un-hide) | fade in | fade + rise, staggered 25 ms per item (max 300 ms total) |
| Tooltip | fades in | grows in from the item (scale 0.96 → 1) |
| Theme motion (§1) | half speed | full |
| Quick-hide / show, roll-up | as today | as today |

Hover/press are WPF animations on the item's render transform (no layout passes). Off = today's instant behaviour.

## 4. Clicking and text (ADR-064)

- **One click opens** (the owner: "just go or do"). Press and release on an item without moving opens it; moving 4 px
  or more starts a drag as today. No selection look after a click: hover highlight and the press dip only.
- **Multi-select stays:** Ctrl+click and the drag box still select; the selection shows only while Ctrl is held or the
  box is drawn, and while a multi-item drag or menu needs it. Keyboard (Peek) focus ring unchanged.
- **Setting:** Settings → "Open items with: **One click** (default) / Double click"; per fence (Fence settings) "Open
  items with: Like all fences / One click / Double click" (e.g. one click only in a games fence). Rename stays on F2 and
  the menu; double-click on the title still rolls up.
- Applies to everyone, existing setups too (the feedback was about the double-click + ring feel). The guide changes in
  the same commit (ADR-050).
- **Name style** (Settings → Appearance, all fences): **Crisp** (default; Segoe UI Variable Text, semibold-light weight,
  a tight 1 px shadow), **Chips** (each name on a soft dark pill; the pill is opaque, so names use ClearType via
  `RenderOptions.ClearTypeHint="Enabled"`), **Plain** (no shadow). rc.2's Display text mode stays underneath.

## 5. Menus

Setting **Fence menu: Guided** (default) **/ Compact** (Settings → Appearance).

- **Guided** (menus "C"): small section headings — *Add to this fence* (Item…, From desktop…, Games…, Folder panel…,
  Widget…), *This fence* (Rename, Fence settings…, Lock position), *Everything* (New fence ▸, Settings…), then Delete
  fence (red).
- **Compact** (menus "B"): a Windows 11 style icon row on top (Add item, Add widget, Rename, Fence settings, Lock), the
  rest as a short list below.
- Removed from the fence menu because Fence settings owns them (no duplicates): View ▸ (icon size, labels, layout),
  Colour, Refresh (moves into Fence settings' footer). Sort by and Auto-collect… stay (actions, not settings).
- "Widget…" opens the widget gallery (§6.3) instead of the Clock/Date/Stats submenu.

## 6. Widgets (ADR-065)

### 6.1 The set for rc.3

**Clock**, **System stats**, **Calendar** (the Date widget becomes Calendar — same `WidgetKind.Date` and target, shown as
"Calendar", so existing Date widgets simply become Calendars), and **Now playing** (new `WidgetKind.NowPlaying`). One
look: **Glass** — crisp glass cards, white values first, clear small-caps labels, accent bars (warm for temperatures).
No per-widget style setting. After 1.0 (1.1): Sound, Game monitor. Dropped: Drives, Note, Continue playing.

### 6.2 Sizes: Small, Medium, Large

Every widget has three designed layouts (bigger shows more, not a bigger copy):

| | Small (2×1) | Medium (2×2) | Large (4×2) |
|---|---|---|---|
| Clock | time + weekday | time, weekday and date | time with seconds, date, up to two world clocks (picked on its back) |
| System stats | CPU, GPU | CPU, CPU temp, GPU, GPU temp | the four + RAM |
| Calendar | date tile (month band, day) + weekday, month | the month (‹ › slide between months) | today panel (day, weekday, week number) + the month |
| Now playing | the island pill (spinning record, title, artist, level meter, play/pause) | square: sleeve + record, title, scrub bar, prev / play-pause / next | wide: glowing cover background, sleeve + record + tonearm, title, artist, album, player switch, scrub with times, shuffle, prev, play-pause, next, repeat |

- **Resizing:** drag the widget's corner (a grip appears on hover); the widget snaps to Small, Medium or Large with a
  small size label while dragging. The widget menu's **Size ▸ Small / Medium / Large** does the same. The free 4×4 size
  picker stays for items only.
- **Stored** as today's `VirtualItem.Size` span. Old widget spans map to the nearest size (Core `WidgetSize.Of(span)`:
  3 or more columns → Large; otherwise 1 row → Small, 2 or more rows → Medium), test-first; nothing is rewritten until
  the user resizes. Existing Date widgets (2×2) therefore show as Calendar Medium (the month, today marked).
- Calendar weeks start on the Windows first-day-of-week setting; weekends tinted; today in the accent.

### 6.3 Adding: the gallery flyout

- Hovering a fence shows a **+** on its title (all title styles); it and the fence menu's **Widget…** open a **gallery
  flyout** anchored to the fence: a search box, categories (All, Time, System, Media), live previews; hovering a preview
  shows **S / M / L** — a click adds that size at the end of the fence (it pops in with Motion on). A preview can be
  dragged onto any fence. Items fences only (as today); folder views and libraries don't show the +.

### 6.4 Setting up: flip

- Hovering a widget shows **⚙**; clicking it **flips** the widget (horizontal squash-through-zero, 2 × 160 ms; instant
  with Motion Off) to its back: its options (Clock: seconds, date line, 24-hour, world clocks; Stats: °C/°F; Calendar:
  none beyond size; Now playing: which player when several) and Size. **Done** flips back with the changes already live.
  The widget menu keeps Size ▸ and Remove.

### 6.5 Widget-only fences float ("board")

- Per-fence switch **Widgets float** (Fence settings): the fence drops its own glass and title; each widget draws as its
  own glass card on the wallpaper. Hovering the fence shows a faint dashed outline and the title above it, so it can be
  moved, resized and right-clicked as usual. Still a fence: snapshots, Peek, game mode, tabs.
- Offered once (a small hint on the fence) when a fence holds only widgets. A "Dashboard" recipe comes with M41b.

### 6.6 Now playing

The owner's design: `now-playing-v3` (the mix — the cover's colours fill the background, the record slides out of its
sleeve and spins).

- **Source:** Windows' media sessions (`GlobalSystemMediaTransportControlsSessionManager`, WinRT) — whatever plays with
  Windows' media controls (Spotify, browsers, YouTube Music, Media Player). Read: title, artist, album, cover thumbnail,
  playing/paused, timeline (position, end; no end = **LIVE**), shuffle/repeat state, the source app. Commands: play,
  pause, previous, next, shuffle, repeat, seek — each shown only when the session allows it.
- **Behaviour:**
  - Play → the record slides out and spins up, the tonearm lowers; pause → the arm lifts, the record spins down and
    slides home (Motion Off: no spin, no slide — the state shows by the play/pause icon).
  - Track change → the sleeve dips and swaps, text slides, background colours cross-fade.
  - Scrub bar: thickens on hover with a time tip; drag to seek (if allowed). Live streams show a LIVE badge.
  - **Player switch** when several sessions exist (small app pills); default = Windows' current session.
  - **Nothing playing:** the last song dimmed with "Last: <title> · <artist>"; Play resumes it if its app still has a
    session, otherwise the widget just says "Nothing playing".
  - **Mouse wheel** over the widget changes the Windows master volume (Core Audio `IAudioEndpointVolume`, documented
    COM via CsWin32) with a slim volume indicator.
  - **Click the cover** brings the player's window to the front (best effort; logged when it can't).
- **Cost and limits:** the record spins at ≤ 30 fps only while playing, visible and Motion is on; game mode pauses the
  widget (no rendering, no session events processed except the latest state on resume). Cover thumbnails are read once
  per track, kept in memory only (never written to disk).
- **Platform (ADR-065):** `NeoFences.Shell` (and App) move to `net10.0-windows10.0.22621.0` to use the WinRT
  projection that ships with the .NET SDK (no NuGet package; adds the Windows SDK projection assemblies to the
  self-contained output — the ADR records the size difference, measured in the plan). Windows 11 only (ADR-059) is
  unchanged. All WinRT use stays in Shell behind a small `MediaSessions` class; failures (no sessions API, a player that
  throws) log and show "Nothing playing".
- **Privacy:** read locally from Windows; nothing sent anywhere. `PRIVACY.md` gains one line.

## 7. Desktop clicks (ADR-066)

The owner: double-click the desktop to hide the fences, then the desktop — dragging, right-clicking, Wallpaper Engine's
interactive wallpapers — must behave exactly as without NeoFences.

- **Hidden = hands off:** while the fences are quick-hidden, the mouse hook only **watches**: it never swallows or
  replays any input (no right-press swallow, no draw gesture); the only thing it reacts to is a double-click on empty
  desktop, which shows the fences again (as today). The tray "Show fences" and the hotkey still work.
- **Visible:** right-press swallowing stays only while **Draw a fence with right-drag** is on (as today); with it off
  the hook passes right-presses straight through. The plan checks why right-click and drag on the desktop misbehaved
  in the feedback (a VM probe with Wallpaper Engine is not possible — no GPU — so the probe uses the plain desktop and,
  if it does not reproduce, a recorder on the owner's PC asked first).
- **Click wallpaper to reveal desktop** (Settings, next to the desktop gestures; **off** by default; macOS-style): a
  single click on a visible bit of empty wallpaper does "show desktop" (Win+D's effect, through the shell's
  ToggleDesktop) so the fences and wallpaper are in view; clicking empty wallpaper again or Win+D brings the windows
  back. It fires only after Windows' double-click time passes without a second click, so a double-click still hides the
  fences. Not while fences are hidden, not in game mode.
- **Draw a fence with right-drag** stays a switch (as today).

## 8. Settings added in rc.3 (placed in today's windows; M41b redesigns them)

Global (Settings → Appearance unless noted): default theme for new fences, default title style, Motion, Name style, Fence
menu, Open items with; next to the desktop gestures: Click wallpaper to reveal desktop. Per fence (Fence settings): Theme,
Transparency (the existing strength slider, renamed), Title style, Emoji, Open items with, Widgets float. Every new
per-fence value is nullable ("Like all fences"), stored in `OwnLook`/`Fence` like today; no schema bump — new fields
default when absent (the existing pattern); an rc.2 opening an rc.3 config ignores them.

## 9. Testing

- **Core, test-first:** theme/title/name-style/click-mode resolution (per-fence over global, Classic keeps
  `ColourStyle`); `WidgetSize.Of`/`SpanOf` and old-span mapping; Calendar month grid (first day of week, leading and
  trailing days, week number, leap years); Clock world-clock text; Now playing timeline text (elapsed, remaining, LIVE)
  and "can do" flags; title count; emoji validation (one grapheme); hook policy (`DesktopClickPolicy`: swallow / pass /
  reveal, from hidden, gestures, game mode — pure, so the Shell hook only asks it).
- **VM checks (`NF-Win11-Dev`, live):** one click opens an item; Ctrl+click selects without opening; quick-hide by
  double-click then right-click on the desktop shows Explorer's menu and left-drag draws Windows' selection box; Now
  playing shows a track played in Media Player in the VM (a short audio file copied in) and pause/next work; a themed
  fence with Motion Lively keeps CPU low (recorded, the VM has no GPU so it is only a regression signal).
- **Release-candidate pass:** gains `one-click-open` and `hidden-desktop-passthrough`.
- **On the owner's PC (asked first, heads-up given):** the ambient motion CPU measurement (§1) and a look at Now playing
  with Spotify and a YouTube tab.
- `docs/TEST-CHECKLIST.md` gains an M41a section; `docs/GUIDE.md` changes with every setting, gesture and menu change.

## Not in M41a

Fence settings v4, Settings v2, the option audit, recipes (M41b) · Sound and Game monitor widgets (1.1) · tinting the
fence with the cover's colours · Rings/Minimal widget styles · widgets outside fences · per-virtual-desktop fences.
