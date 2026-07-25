### Downloads

- `MicFX-Setup-<version>.exe` - installer (Start Menu entry, uninstaller)
- `MicFX.exe` - portable single-file build
- `MicFX-<version>-win-x64.zip` - portable build, zipped

The binaries are unsigned, so SmartScreen may warn on first run
(More info > Run anyway). In-app updates are not affected.

### Removed

- Voice effects (robot, female, deep, chipmunk, cave echo, megaphone, alien,
  whisper, ghost) and the panel that hosted them. MicFX is a microphone
  processing tool: EQ, noise suppression, gate, compressor, monitoring and a
  soundboard. The signal path is correspondingly shorter, and the pitch
  shifter that added roughly 35 ms of latency when a pitch effect was active
  is gone with it.
- Existing settings and profiles keep working; the stored effect selections
  are ignored.

### First-time setup

1. Install [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as
   administrator, reboot).
2. In MicFX: select your microphone as input and "CABLE Input" as output,
   press Start.
3. In Discord/OBS: select "CABLE Output" as the microphone.

See the [README](https://github.com/Norbeto0/MicFX#readme) for full
instructions and troubleshooting.
