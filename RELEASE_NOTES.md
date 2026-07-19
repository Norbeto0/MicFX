## MicFX — real-time mic effects station for Windows

One mic in, one output out — EQ, noise gate, voice effects and a soundboard,
in the spirit of Voicemod / SteelSeries Sonar's mic channel.

### Download

- **MicFX.exe** — self-contained single file, no .NET install needed. Just run it.
- **MicFX-…-win-x64.zip** — the same exe, zipped for a smaller download.

> ⚠ The exe is unsigned, so Windows SmartScreen may warn on first run:
> click **More info → Run anyway**.

### Highlights

- 🎚 10-band EQ (31 Hz–16 kHz, ±15 dB) with presets
- 🚪 Noise gate, mic gain, master volume, live level meters
- 🤖 Voice effects with intensity control: Robot, Female, Deep Male, Chipmunk, Cave Echo
- 🔊 Soundboard mixed into your mic feed, global hotkeys Ctrl+Alt+1…9
- 🎧 Self-monitoring ("listen to myself") on a separate device
- 🖥 Closing the window minimizes to the system tray; optional **Run on Windows startup** (starts minimized)
- 💾 Settings persisted in `%APPDATA%\MicFX`

### Setup (one time)

1. Install the free [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as admin, reboot).
2. In MicFX: Microphone → your real mic · Output → **CABLE Input** · press **Start**.
3. In Discord/OBS/games: set the input device to **CABLE Output**.

Full instructions and troubleshooting in the repository README.
