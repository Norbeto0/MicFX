## MicFX v1.1 — the big one

One mic in, one output out — EQ, noise suppression, voice effects and a
soundboard, in the spirit of Voicemod / SteelSeries Sonar's mic channel.

### Download

- **MicFX-Setup-….exe** — installer (Start-Menu entry, uninstaller). Recommended.
- **MicFX.exe** — portable single file, just run it.
- **MicFX-…-win-x64.zip** — the portable exe, zipped.

> ⚠ Unsigned — Windows SmartScreen may warn on first run:
> click **More info → Run anyway**.

### New in v1.1

- 🧹 **Noise suppression** — spectral denoiser removes steady background noise
  (fans, AC, hum) while you talk, with adjustable strength (6–30 dB)
- 🎯 **Voice leveler** — broadcast-style compressor with auto make-up gain
- 👽 **4 new voice effects**: Megaphone, Alien (flanger), Whisper, Ghost (reverse echo)
- 📊 **Live spectrum visualizer** behind the EQ sliders
- 👤 **Profiles** — save/switch whole sound setups from the app or the tray menu
- 🔊 **Soundboard upgrades**: per-clip volume, custom global hotkeys, loop mode
  (click the tile again to stop), headphones-only preview — right-click a tile → Edit
- 🧭 **Guided first-run setup** — detects a missing VB-Cable and walks you through it,
  auto-selects CABLE Input when found
- 🔔 **Update check** — tells you in-app when a newer release is out
- 📦 **Installer** — proper setup exe with uninstaller

### Setup (one time)

1. Install the free [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as admin, reboot).
2. In MicFX: Microphone → your real mic · Output → **CABLE Input** · press **Start**.
3. In Discord/OBS/games: set the input device to **CABLE Output**.

Full instructions and troubleshooting in the repository README.
