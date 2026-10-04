# Spikes — THROWAWAY

Feasibility experiments. Never referenced from `src/`. Findings live in `docs/research/`;
the code here may be deleted once its milestone closes.

## M0.DesktopLayer
Proves spec §4.1–4.5/§4.7 (acrylic, Win+D, Explorer restart, icon hide + watchdog, mouse hook,
game detection). Plan: `docs/superpowers/plans/2026-10-02-m0-desktop-layer-spike.md`.

    dotnet test spikes/M0.slnx
    dotnet run --project spikes/M0.DesktopLayer

Log: `%LOCALAPPDATA%\NeoFences\spike-m0\lab.log`.
If desktop icons stay hidden: right-click desktop → View → Show desktop icons.
