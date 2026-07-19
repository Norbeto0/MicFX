## MicFX v1.2 — cleaner default layout

### Download

- **MicFX-Setup-….exe** — installer (Start-Menu entry, uninstaller). Recommended.
- **MicFX.exe** — portable single file, just run it.
- **MicFX-…-win-x64.zip** — the portable exe, zipped.

> ⚠ Unsigned — Windows SmartScreen may warn on first run:
> click **More info → Run anyway**.

### Changed in v1.2

- 🧩 The **voice effects panel is now hidden by default** — the main window
  focuses on devices, clean-up, EQ and the soundboard.
- ⚙ New **SETTINGS** section (left column) with a **"Show voice effects panel"**
  toggle. Hiding the panel switches the active effect back to *None*, so your
  voice can never be secretly stuck on an effect; applying a profile that uses
  an effect automatically brings the panel back.

Everything from v1.1 is included: spectral noise suppression, voice leveler,
9 voice effects, profiles, spectrum visualizer, soundboard with per-clip
volume/hotkeys/loop/preview, guided VB-Cable setup, update check, installer.

### Setup (one time)

1. Install the free [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as admin, reboot).
2. In MicFX: Microphone → your real mic · Output → **CABLE Input** · press **Start**.
3. In Discord/OBS/games: set the input device to **CABLE Output**.

Full instructions and troubleshooting in the repository README.
