# NeoFences — Appearance (M14) design

**Date:** 2026-10-04 · **Status:** draft for review · **Target:** v1.7.0 · **Parity:** Fences per-fence colours and
title fonts; NeoFences extras "Blur tint preference" and "Wallpaper-adaptive accent colour"

## 1. Goal and user choices

Fences that match the wallpaper and are easy to tell apart, with the blur over Wallpaper Engine never getting worse.
User choices (2026-10-04, with browser mockups):

| Question | User choice |
|---|---|
| Scope | Background strength, per-fence colour, title font, wallpaper accent (all four). |
| How a colour shows | All three looks, as a preference: **Accent edge**, **Tinted glass**, **Title strip**. |
| Background strength | One slider, Clear → Solid; the tone follows Windows' dark/light app mode. |
| Title font | A global default; every fence can override it. |
| Wallpaper accent | One switch: fences without their own colour take the wallpaper's colour. |
| Fence colours | The 8 tab colours plus Custom… (any colour). |
| Accent source | Approach 1: read the wallpaper's own image (Wallpaper Engine preview → Windows wallpaper → Windows accent), no screen capture. Replaces the first pick "sample the desktop": same goal, works with WE, free during games. |

Success: with the defaults, every fence looks exactly as in v1.6; the user can make the background darker or lighter in
both tones; give Games a red tinted glass and a Bahnschrift title while other fences keep the default; turn on the
wallpaper accent and see uncoloured fences take a colour from the WE wallpaper (currently "Detroit: Become Human -
Kara"), which changes when the WE wallpaper changes; nothing costs anything while a game runs.

Non-goals: icon colour tint; theme packs; per-fence background strength; animated or live-sampled accent colours;
appearance inside snapshots (they keep fence fields only, as today); a tone choice other than following Windows.

Probe of the user's PC (read-only, 2026-10-04): Wallpaper Engine at `C:\Program Files (x86)\Steam\steamapps\common\
wallpaper_engine`, running; `config.json` → `<user>.general.wallpaperconfig.selectedwallpapers.Monitor0.file` =
`…\workshop\content\431960\1405736695\scene.pkg`; that folder has `project.json` (`"preview": "preview.jpg"`, title
"Detroit: Become Human - Kara") and `preview.jpg`. Windows wallpaper: a Spotlight image (`…\DesktopSpotlight\Assets\
Images\image_3.jpg`).

## 2. Settings and config

`Settings.Appearance` (new) — defaults reproduce v1.6:

| Field | Values | Default |
|---|---|---|
| `strengthDark` | 0–85 (% veil) | 0 (Clear: today's dark look) |
| `strengthLight` | 0–85 | 72 (today's light veil, 0xB8) |
| `colourStyle` | `accentEdge` / `tintedGlass` / `titleStrip` | `accentEdge` |
| `wallpaperAccent` | bool | false |
| `titleFont` | `{ family, size, weight }`; size 12 / 14 / 17 / 20, weight `regular` / `semiBold` / `bold` | `{ "Segoe UI", 14, semiBold }` |

`Fence` (new fields): `customColor` (`#RRGGBB`, wins over the existing `tabColor` swatch) and `titleFont` (optional
override; each of family, size and weight may stay null = inherited).

Schema version 3 (ADR-033 rule): the normalizer stamps it; v1.6 opens a v3 config read-only and never strips the new
fields. An older config gets the defaults. A broken `customColor`, an out-of-range strength or size, or an unknown
style is repaired to the default by the normalizer. One `pre-schema-3-config.json` copy is kept in `backups\` the way
the schema-2 copy is (ADR-034).

## 3. How a fence's look is decided (Core, pure)

`FenceLook.Resolve(appearance, fence, tone, accent)` returns colours and the font for one fence:

1. **Colour:** `customColor`, else the `tabColor` swatch, else the wallpaper accent of the fence's monitor (when the
   switch is on), else none — none is exactly today's neutral look.
2. **Veil:** tone ink (dark: near-black 10,10,14; light: 242,242,242) at the strength of the current tone.
   - **Accent edge:** veil unchanged; border, title text and the 48×3 bar take the colour.
   - **Tinted glass:** the colour mixed into the veil, with at least 22 % tint so the glass shows at Clear; the border takes the colour at 70 %.
   - **Title strip:** veil unchanged; the title bar background takes the colour at 75 %.
3. **Contrast:** when the title strip or tinted glass under the title is light (relative luminance > 0.5), the title text
   switches to dark ink. Item labels keep the tone's ink and the existing label shadow.
4. **Font:** the fence override's set parts over the global default. The title bar height follows the size (26 / 30 /
   34 / 38 px) and the window's caption area with it.

**Boxes with tabs:** the box uses the front tab's look (colour and font); switching tabs restyles the box; headers keep
their own colour marks.

## 4. Wallpaper accent

**Source, per monitor** (Shell, read-only, first that works):
1. Wallpaper Engine running (`wallpaper32` / `wallpaper64`): its `config.json` → selected wallpaper per monitor → the
   wallpaper folder's `project.json` `preview` file (jpg / png / gif first frame).
2. Windows' wallpaper for that monitor (`IDesktopWallpaper::GetWallpaper`).
3. Windows' accent colour.

WE's monitor keys (`Monitor0`, …) are mapped to Windows monitors in display order; the prototype confirms the format.
If the mapping is unclear, every monitor uses WE's first entry.

**Dominant colour** (Core, pure, `AccentColor.FromPixels`): the App decodes the image at about 64 px wide; Core buckets
the pixels by hue (36 buckets), weights them by saturation × value, ignores near-black, near-white and greys, takes the
strongest bucket's average and clamps it to a readable lightness (HSL 40–65 %). A greyscale image yields null → the next
source (Windows' accent).

**Updates:** WE's `config.json` changed (watched, debounced 2 s); Windows' wallpaper-change notice
(`WM_SETTINGCHANGE` / `SPI_SETDESKWALLPAPER`); a display change; the switch turned on. Decoding runs off the UI thread.
No timer, no screen capture. `// ponytail:` WE closed without a wallpaper change keeps the accent until the next
change; upgrade path: watch the WE process.

## 5. Where it is set (App)

- **Settings → Appearance** (new section, changes apply live): Colour style (three preview tiles); Background strength
  (the slider for the current tone, "Light mode keeps its own value"); "Colour fences from the wallpaper" (switch, the
  current accent swatch and its source, e.g. "From Wallpaper Engine: Detroit: Become Human - Kara"); Title font (font,
  size, weight; the font list shows each name in its face).
- **Fence menu → Colour** (today's item, extended): None (follows the wallpaper), the 8 swatches, Custom… (Windows'
  colour picker, `ChooseColor` via CsWin32 in `NeoFences.Shell`; Cancel changes nothing).
- **Fence menu → Title font** (new): Use the default; Font ▸, Size ▸, Weight ▸.

## 6. Reliability and hard rules

- Hard rule 4/5: WE config reading, `IDesktopWallpaper`, the wallpaper-change notice and `ChooseColor` live in
  `NeoFences.Shell` (CsWin32). Core has the resolver, the colour maths and the WE config parser. The App decodes images
  and applies brushes.
- Hard rule 6: no new NuGet.
- Hard rule 7: any failure in the accent chain falls to the next source, logged once; a missing font falls back to
  Segoe UI keeping the stored name; a fence never looks broken, at worst neutral.
- Game mode: nothing runs on a timer; watchers only.
- Performance: brushes rebuilt only when a fence's look inputs change.

## 7. Testing

- **Core (xUnit, test-first):** the defaults give v1.6's exact brushes in both tones; the colour order (custom > swatch >
  accent > none); each style's brushes; contrast switch; font inheritance and title heights; dominant colour (a red
  image → red, greyscale → null, near-black ignored, lightness clamped); WE `config.json` and `project.json` parsing
  (missing keys, odd paths); schema 3 upgrade and repairs.
- **Live check** (consent unless the user said the PC is unattended; config and the installed copy restored):
  screenshots and pixel checks of a test fence in each style, the slider at both ends, a font change, the accent switch
  over the WE wallpaper.
- **Checklist:** new section AA (styles, slider in both tones, per-fence font, accent with WE and with a Windows
  wallpaper, WE wallpaper change, monitor change, missing font, a light Windows theme).

## 8. Delivery

M14 → v1.7.0, one batch: prototype in scratch, live check, plan, execute, review, merge, release. ADR-036 records the
decisions (style preference, schema 3, the accent source chain instead of screen sampling).
