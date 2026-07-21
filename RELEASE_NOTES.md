### Downloads

- `MicFX-Setup-<version>.exe` - installer (Start Menu entry, uninstaller)
- `MicFX.exe` - portable single-file build
- `MicFX-<version>-win-x64.zip` - portable build, zipped

The binaries are unsigned, so SmartScreen may warn on first run
(More info > Run anyway). In-app updates are not affected.

### Fixed

- Latency no longer creeps up over time (previously it could grow to a second
  or more, needing a restart). The microphone and the output device run on
  independent clocks; when the mic ran slightly fast, its capture buffer kept
  filling and delay accumulated. MicFX now watches the capture backlog and
  trims the oldest audio when it grows past a threshold, keeping mouth-to-
  output latency bounded (roughly 40-140 ms depending on drift). This was most
  noticeable in the Low/Lowest latency modes.

### First-time setup

1. Install [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as
   administrator, reboot).
2. In MicFX: select your microphone as input and "CABLE Input" as output,
   press Start.
3. In Discord/OBS: select "CABLE Output" as the microphone.

See the [README](https://github.com/Norbeto0/MicFX#readme) for full
instructions and troubleshooting.
