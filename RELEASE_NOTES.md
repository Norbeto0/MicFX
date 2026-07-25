### Downloads

- `MicFX-Setup-<version>.exe` - installer (Start Menu entry, uninstaller)
- `MicFX.exe` - portable single-file build
- `MicFX-<version>-win-x64.zip` - portable build, zipped

The binaries are unsigned, so SmartScreen may warn on first run
(More info > Run anyway). In-app updates are not affected.

### Fixed

- AI (RNNoise) noise suppression reported "library not found" and silently fell
  back to spectral suppression on every installation. The native library was
  compiled correctly by the release build but was placed next to the
  executable, while every distribution form - the portable single file, the
  zip and the installer - ships `MicFX.exe` on its own, so the library never
  reached the machine. It is now embedded in the executable and unpacked to
  `%LOCALAPPDATA%\MicFX\native` on first use, which works for all three forms.
- When AI suppression genuinely cannot be used, the status bar now reports the
  underlying reason instead of a generic message.
- The release build verifies that the compiled library exports the expected
  entry points and that it is still embedded, so this class of packaging
  mistake fails the build instead of shipping.

### First-time setup

1. Install [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as
   administrator, reboot).
2. In MicFX: select your microphone as input and "CABLE Input" as output,
   press Start.
3. In Discord/OBS: select "CABLE Output" as the microphone.

See the [README](https://github.com/Norbeto0/MicFX#readme) for full
instructions and troubleshooting.
