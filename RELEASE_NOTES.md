### Downloads

- `MicFX-Setup-<version>.exe` - installer (Start Menu entry, uninstaller)
- `MicFX.exe` - portable single-file build
- `MicFX-<version>-win-x64.zip` - portable build, zipped

The binaries are unsigned, so SmartScreen may warn on first run
(More info > Run anyway). In-app updates are not affected.

### Fixed

- The window opened far taller than it needed to be. Sizing it to fit the
  whole left column made sense while that column could be clipped, but now
  that the column scrolls the two no longer have to match. The window opens
  at the height that shows the column, capped so it never takes over the
  screen, and can be made considerably smaller by hand.
- The left column itself is more compact: tighter card padding and spacing,
  the device Refresh button moved up beside the Microphone label, and the
  noise suppression strength slider is hidden rather than greyed out while
  the AI engine is selected, since it only applies to the spectral one.

### First-time setup

1. Install [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as
   administrator, reboot).
2. In MicFX: select your microphone as input and "CABLE Input" as output,
   press Start.
3. In Discord/OBS: select "CABLE Output" as the microphone.

See the [README](https://github.com/Norbeto0/MicFX#readme) for full
instructions and troubleshooting.
