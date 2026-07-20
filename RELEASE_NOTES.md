## MicFX v1.4 — cleaner one-screen layout

### Download

- **MicFX-Setup-….exe** — installer (Start-Menu entry, uninstaller). Recommended.
- **MicFX.exe** — portable single file, just run it.
- **MicFX-…-win-x64.zip** — the portable exe, zipped.

> ⚠ Unsigned — Windows SmartScreen may warn on first run:
> click **More info → Run anyway**. In-app updates don't retrigger it.

### Changed in v1.4

- 🍔 **Settings moved to a hamburger menu** (top-left ☰) — "Show voice effects"
  and "Run on Windows startup" now live in a flyout instead of taking up a panel.
- 📐 **No more scrollbar on the left panel** — devices, levels and clean-up now
  fit and fill the left column.
- 🔊 **Soundboard is now a column beside the equalizer** (narrower, parallel)
  instead of a wide strip along the bottom — the whole window reads in one row.

Includes everything from v1.3: in-app auto-update, spectral noise suppression,
voice leveler, 9 voice effects, profiles, spectrum visualizer, soundboard with
per-clip volume/hotkeys/loop/preview, guided VB-Cable setup, installer.

### Setup (one time)

1. Install the free [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as admin, reboot).
2. In MicFX: Microphone → your real mic · Output → **CABLE Input** · press **Start**.
3. In Discord/OBS/games: set the input device to **CABLE Output**.

Full instructions and troubleshooting in the [README](https://github.com/Norbeto0/MicFX#readme).
