# M31 — Modern widgets and sensors (0.19.0): build notes and results

Spec: `docs/superpowers/specs/2026-10-06-modern-widgets-design.md` · Decision: ADR-052 · Plan:
`docs/superpowers/plans/2026-10-06-m31-widgets.md`

## The look

Chosen in the visual companion: style B (dashboard tiles, semibold tight numbers, the date sideways) with style A's thin
accent bars. Fonts: Segoe UI Variable Display / Text (Segoe UI where missing); a soft shadow (`WidgetShadow`) instead of
the labels' halo; bars in the Windows accent (`SystemColors.AccentColor*`; the user's accent is red).

## Sensor layouts

- **MSI Afterburner** `MAHMSharedMemory`: header (signature `MAHM` 0x4D41484D, version 0x20000, header size, entry count,
  entry size 1324, …), entries of five 260-byte ANSI strings (name, units, localized name, localized units, format), then
  `float data` at 1300. `FLT_MAX` = no value; signature 0xDEAD once Afterburner closes. On the user's PC (Afterburner
  running elevated) a normal process reads it: 68 entries, among them "CPU temperature", "CPU usage", "RAM usage",
  "GPU1/GPU2 temperature", "GPU1/GPU2 usage", "GPU1/GPU2 memory usage" (GPU1 the integrated Radeon, 0.4 MB; GPU2 the
  graphics card, ~3.9 GB), clocks, power, fans, FPS, per-core values.
- **HWiNFO64** `Global\HWiNFO_SENS_SM2` (packed): signature `HWiS` 0x53695748 ("DEAD" while sharing is off), offsets,
  sizes and counts of the sensor and reading sections at 20–44; readings: type (1 temperature, 7 usage, 8 other),
  sensor index, id, 128-byte original label at 12, user label, unit, `double value` at 284. Not installed here: tested
  from built samples only.
- **Windows (D3DKMT)**: `D3DKMTOpenAdapterFromLuid` + `D3DKMTQueryAdapterInfo(KMTQAITYPE_ADAPTERPERFDATA = 62)`,
  `Temperature` in tenths of °C (WDDM 2.7). Checked here: the graphics card 51.7 °C, the integrated GPU 44 °C; a third
  (software) adapter answers STATUS_INVALID_PARAMETER.

## Build

- Prototype checked against the running Afterburner (CPU 50 °C, GPU 51 °C, RAM 14.5 / 31.1 GB) and in a visual probe
  (3-wide Desk fence); replay-verified on `main` (33 build errors before the code, 678 tests after, 0 warnings).
- The M30 leftovers: welcome heading ink, wrapping welcome buttons, Settings refresh after the welcome fence goes, the
  first-start notice waiting for the tray icon, one `UpdateEmptyHint`, one window refreshed, tray ids in order.
