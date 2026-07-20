### Downloads

- `MicFX-Setup-<version>.exe` - installer (Start Menu entry, uninstaller)
- `MicFX.exe` - portable single-file build
- `MicFX-<version>-win-x64.zip` - portable build, zipped

The binaries are unsigned, so SmartScreen may warn on first run
(More info > Run anyway). In-app updates are not affected.

### Changes

- The EQ band count is adjustable between 10 and 20 from the settings menu.
  Bands stay log-spaced between 31 Hz and 16 kHz and the filter width follows
  the spacing. The current curve, presets and saved profiles are re-mapped
  onto the new band centres instead of being reset.
- The window now derives its minimum height from the measured height of the
  left column, so the clean-up section can no longer be cut off and there is
  no dead space below it. Enlarging the window stretches the last card
  instead of leaving a gap.

### First-time setup

1. Install [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as
   administrator, reboot).
2. In MicFX: select your microphone as input and "CABLE Input" as output,
   press Start.
3. In Discord/OBS: select "CABLE Output" as the microphone.

See the [README](https://github.com/Norbeto0/MicFX#readme) for full
instructions and troubleshooting.
