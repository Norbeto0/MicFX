### Downloads

- `MicFX-Setup-<version>.exe` - installer (Start Menu entry, uninstaller)
- `MicFX.exe` - portable single-file build
- `MicFX-<version>-win-x64.zip` - portable build, zipped

The binaries are unsigned, so SmartScreen may warn on first run
(More info > Run anyway). In-app updates are not affected.

### Fixed

- The CLEAN-UP section could be cut off at the bottom of the left column with
  no way to scroll to it. The window sizes itself to fit that column, but the
  measurement was wrong: the column is a grid whose last row is stretch-sized,
  and such a grid reports only the space it already has rather than the space
  it needs, so the minimum height never grew. The column now sits in a scroll
  area, which measures it with unlimited height and makes the figure correct,
  still stretches it to fill the window when there is room, and falls back to
  a scrollbar instead of clipping when there is not.
- The minimum height is additionally capped to the screen's work area, so on a
  small display or at a high DPI scale the window can no longer ask to be
  taller than the screen.
- Custom soundboard hotkeys with a digit were displayed using the internal key
  name, for example "Ctrl+D1" instead of "Ctrl+1". Numeric, numpad and
  punctuation keys are now labelled the way they are typed.

### First-time setup

1. Install [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as
   administrator, reboot).
2. In MicFX: select your microphone as input and "CABLE Input" as output,
   press Start.
3. In Discord/OBS: select "CABLE Output" as the microphone.

See the [README](https://github.com/Norbeto0/MicFX#readme) for full
instructions and troubleshooting.
