## MicFX v1.5 — adjustable EQ bands & a window that fits itself

### Download

- **MicFX-Setup-….exe** — installer (Start-Menu entry, uninstaller). Recommended.
- **MicFX.exe** — portable single file, just run it.
- **MicFX-…-win-x64.zip** — the portable exe, zipped.

> ⚠ Unsigned — Windows SmartScreen may warn on first run:
> click **More info → Run anyway**. In-app updates don't retrigger it.

### New in v1.5

- 🎚 **Adjustable EQ resolution** — a slider in the ☰ settings menu picks
  10–20 bands (log-spaced 31 Hz–16 kHz, filter width adapts). Your current
  EQ curve is re-mapped onto the new bands, and presets/profiles work at any
  band count.
- 📐 **The window now measures itself**: the minimum height is exactly what
  DEVICES + LEVELS + CLEAN-UP need — nothing can be cut off and there's no
  blank corner. It opens at that exact height; if you enlarge it, the
  CLEAN-UP card stretches to keep the left column full.

Includes everything from v1.4.x: hamburger settings, in-app auto-update,
noise suppression, voice leveler, 9 voice effects, profiles, spectrum
visualizer, soundboard with per-clip volume/hotkeys/loop/preview, installer.

### Setup (one time)

1. Install the free [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as admin, reboot).
2. In MicFX: Microphone → your real mic · Output → **CABLE Input** · press **Start**.
3. In Discord/OBS/games: set the input device to **CABLE Output**.

Full instructions and troubleshooting in the [README](https://github.com/Norbeto0/MicFX#readme).
