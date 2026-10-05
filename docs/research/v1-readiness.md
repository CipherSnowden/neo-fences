# NeoFences 1.0 readiness review (2026-10-06, version 0.19.1)

Seven read-only reviews (features, reliability, performance, UX and customization, icons and game tiles, Linux/macOS,
release without signing), merged. The plan they led to is in `ROADMAP.md` → "Path to 1.0.0".

## Verdict

The engine is sound: data safety (atomic saves, backups, corrupt-file recovery), icon recovery (watchdog) and idle cost
(0.00 s CPU over 15 s; fences usable ~0.7 s after start) are at release quality. Missing for strangers: a safety net when
something fails on their PC, a modern finish on menus, icons and game tiles, deeper per-fence customization, and proof
that it works beyond the owner's PC (Windows 11 25H2, one monitor, Wallpaper Engine).

## Blockers

1. A crash loop is silent and permanent: after 3 crashes in 10 minutes the watchdog stops and the fences vanish at every
   sign-in. Needs a message and a safe mode (last backup, no icon hiding, no gestures). → M33
2. Never run on another PC: Windows 10 and Windows 11 22H2/23H2 (the pre-24H2 desktop-layer path has never run), two
   monitors with mixed scaling, monitor sleep/unplug. → M38 (Hyper-V) and the release candidate

## Must / should for 1.0, by area

- **Reliability** (M33): silent lost saves when a file is locked at start; no undo for Del / delete fence; gestures
  (double-click quick-hide, right-drag) cannot be switched off and keep the mouse hook installed; icons hidden even when
  the recovery marker could not be written; nobody restarts the watchdog; a damaged file can crash the start instead of
  falling back; two background tasks fail silently; logs have no size cap and carry the Windows user name; a crashing
  release never gets its fix (update only on clean exit).
- **Icons and game tiles** (M34): only Steam games get posters (7 of 12 on the owner's PC have none); the no-art tile
  stretches the exe icon over a grey slab and crops it at 2×2 (bug in `FenceItemView.ApplySize`); covers keep square
  corners over rounded tiles; the tile background is a fixed `#66000000`; no layout rounding or high-quality scaling
  (soft icons at 125/150%); missing items use a 32 px icon scaled up; every website shows the browser's icon; a 44 px gap
  between cover rows; 2×2 covers are no taller than 1×2; flat white selection.
- **Look and UX** (M35): three menu styles (WPF default fence/item menus, a native light tray menu, Fluent dialogs); a
  20-entry fence menu; old MessageBox confirmations; Settings as one long scroll with checkboxes; the Win95 colour picker.
- **Customization** (M36): no per-fence settings window (transparency, title alignment/font, hide title bar, labels,
  spacing); no export/import; no look presets; one customizable hotkey.
- **Performance** (M37): no icon cache; every fence item built (no virtualization); a shadow effect per label on layered
  windows; a per-second widget timer that never stops; untested at 500 items / 50 fences; 268 MB working set mostly
  runtime (try ready-to-run).
- **Compatibility and accessibility** (M38): fence windows all named "NeoFences fence"; invisible keyboard focus; no
  High Contrast; no keyboard way into the fences; English only; x64 only.
- **Release and trust** (M39): unsigned is workable (SmartScreen asks once per new installer; Smart App Control blocks
  fresh Windows 11 installs — README explains turning it off); needs a licence, PRIVACY, SECURITY, third-party notices,
  issue templates, checksums and build attestations, a landing page; submit installers to Microsoft (false-positive
  review) and VirusTotal; a winget entry.

## Signing without money

- SignPath Foundation: open-source licences only — not eligible.
- Azure Trusted Signing: individuals only in the US and Canada — not eligible from India.
- Certum: about €50–70 for the first year — the cheapest real certificate, later.
- Microsoft Store: free individual registration; Microsoft signs an MSIX build. A second packaging route — after 1.0.

## Linux and macOS

About 80% of Core is portable today (it builds anywhere; about 10 files assume Windows paths). Shell (224 Win32 APIs)
and the WPF App are Windows-only. Before 1.0: keep Core free of Windows code and run its tests on Linux in CI (1–3 days).
After 1.0: an Avalonia spike (1–2 weeks), the Windows UI ported to Avalonia (6–10 weeks), a KDE Plasma MVP (6–8 weeks),
native Wayland layer-shell later; GNOME needs a separate extension; Wayland forbids global desktop gestures. macOS
afterwards (2–3 months, a Mac to test on, Apple Developer ID $99/year — unsigned apps are effectively blocked).

## Owner decisions (2026-10-06)

Safety net first; online game covers in 1.0 as an opt-in (off by default); a short freeware licence; Hyper-V test
machines for M38; a release candidate for friends before 1.0.0.
