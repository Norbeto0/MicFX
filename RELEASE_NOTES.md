### Downloads

- `MicFX-Setup-<version>.exe` - installer (Start Menu entry, uninstaller)
- `MicFX.exe` - portable single-file build
- `MicFX-<version>-win-x64.zip` - portable build, zipped

The binaries are unsigned, so SmartScreen may warn on first run
(More info > Run anyway). In-app updates are not affected.

### Changes

- Noise suppression now has two engines, selectable under CLEAN-UP:
  - "AI (RNNoise)" (new default) - the recurrent-network denoiser from
    xiph.org that Discord's standard noise suppression is based on. It
    removes everything that is not voice. The native library is compiled
    from the pinned official source (v0.1.1) during the release build; if it
    cannot be loaded, MicFX falls back to the spectral engine.
  - "Spectral" - the previous suppressor with an adjustable strength slider.
- Input level normalization around the RNNoise model, so quiet microphones
  get full suppression quality.
- If Discord's Krisp/noise suppression breaks your soundboard audio: turn
  noise suppression off in Discord and enable it in MicFX instead. MicFX
  suppresses noise on the mic before the soundboard is mixed in, so clips
  are unaffected.

### First-time setup

1. Install [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as
   administrator, reboot).
2. In MicFX: select your microphone as input and "CABLE Input" as output,
   press Start.
3. In Discord/OBS: select "CABLE Output" as the microphone.

See the [README](https://github.com/Norbeto0/MicFX#readme) for full
instructions and troubleshooting.
