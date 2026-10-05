# M29 — A guide for friends (0.17.0): build notes and results

Spec: `docs/superpowers/specs/2026-10-05-guide-design.md` · Decision: ADR-050 · Plan:
`docs/superpowers/plans/2026-10-05-m29-guide.md`

## Build

- Help entries (tray **Help**, Settings **Help (online guide)**) built in a scratch worktree and replay-verified (0
  warnings, 643 tests).
- README and `docs/GUIDE.md` written from the app's own strings: every menu entry, button and setting the docs name was
  collected from the XAML and the code and checked to exist (a scripted search for each).
- The screenshots and the live check of the Help entries run together (ruling in the ledger): one question and one desktop
  run for the user.