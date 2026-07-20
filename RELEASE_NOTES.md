### Downloads

- `MicFX-Setup-<version>.exe` - installer (Start Menu entry, uninstaller)
- `MicFX.exe` - portable single-file build
- `MicFX-<version>-win-x64.zip` - portable build, zipped

The binaries are unsigned, so SmartScreen may warn on first run
(More info > Run anyway). In-app updates are not affected.

### Changes

- New latency setting in the settings menu with three modes:
  - Normal: previous behaviour (20 ms capture / 50 ms output buffers), the
    safest choice.
  - Low: 10 ms capture / 25 ms output buffers.
  - Lowest: same buffers as Low plus WASAPI exclusive-mode mic capture,
    which bypasses the Windows audio engine entirely. Only MicFX can use
    the mic while the engine runs (other apps read the virtual cable, so
    nothing is lost). If the device refuses exclusive mode, MicFX falls
    back to shared and says so in the status bar.
- Changing the mode restarts the engine automatically. Roughly, Normal is
  ~90 ms from mouth to the virtual cable, Lowest around 40-50 ms. Pitch
  effects (female/deep/chipmunk) add ~35 ms on top; other effects add none.
- If low modes crackle on your machine, switch back to Normal.

### First-time setup

1. Install [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as
   administrator, reboot).
2. In MicFX: select your microphone as input and "CABLE Input" as output,
   press Start.
3. In Discord/OBS: select "CABLE Output" as the microphone.

See the [README](https://github.com/Norbeto0/MicFX#readme) for full
instructions and troubleshooting.
