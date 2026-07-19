## MicFX v1.3 — auto-update

### Download

- **MicFX-Setup-….exe** — installer (Start-Menu entry, uninstaller). Recommended.
- **MicFX.exe** — portable single file, just run it.
- **MicFX-…-win-x64.zip** — the portable exe, zipped.

> ⚠ Unsigned — Windows SmartScreen may warn on first run:
> click **More info → Run anyway**. In-app updates don't retrigger it.

### New in v1.3

- 🔄 **In-app auto-update**: when a new release is out, the banner now has an
  **Install update** button — MicFX downloads it with a progress display,
  installs silently and restarts itself on the new version.
  - Installed copies update through the setup exe.
  - Portable copies swap their own exe in place, wherever you keep it.
- 🏷 Window title now shows the current version; after an update the status
  bar confirms it.
- 🔗 Repository moved to **github.com/Norbeto0/MicFX** (public) — which is
  what makes update checks and downloads possible.

Includes everything from v1.1/v1.2: spectral noise suppression, voice leveler,
9 voice effects (panel hidden by default — toggle in SETTINGS), profiles,
spectrum visualizer, soundboard with per-clip volume/hotkeys/loop/preview,
guided VB-Cable setup, installer.

### Setup (one time)

1. Install the free [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as admin, reboot).
2. In MicFX: Microphone → your real mic · Output → **CABLE Input** · press **Start**.
3. In Discord/OBS/games: set the input device to **CABLE Output**.

Full instructions and troubleshooting in the [README](https://github.com/Norbeto0/MicFX#readme).
