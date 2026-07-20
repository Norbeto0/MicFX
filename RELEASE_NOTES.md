## MicFX v1.4.1 — layout fixes

### Download

- **MicFX-Setup-….exe** — installer (Start-Menu entry, uninstaller). Recommended.
- **MicFX.exe** — portable single file, just run it.
- **MicFX-…-win-x64.zip** — the portable exe, zipped.

> ⚠ Unsigned — Windows SmartScreen may warn on first run:
> click **More info → Run anyway**. In-app updates don't retrigger it.

### Fixed in v1.4.1

- 📐 The window can no longer be shrunk so far that the **CLEAN-UP** section
  gets cut off — the minimum height now always fits the whole left column.
- 🔊 **Soundboard moved to a bottom strip beside the left column** (spanning
  under the equalizer only), and the left panel keeps its full height —
  matching how it was sketched: left column tall, EQ top-right, soundboard
  below it.

Includes everything from v1.4: hamburger settings menu, in-app auto-update,
spectral noise suppression, voice leveler, 9 voice effects, profiles, spectrum
visualizer, soundboard with per-clip volume/hotkeys/loop/preview, guided
VB-Cable setup, installer.

### Setup (one time)

1. Install the free [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as admin, reboot).
2. In MicFX: Microphone → your real mic · Output → **CABLE Input** · press **Start**.
3. In Discord/OBS/games: set the input device to **CABLE Output**.

Full instructions and troubleshooting in the [README](https://github.com/Norbeto0/MicFX#readme).
