### Downloads

- `MicFX-Setup-<version>.exe` - installer (Start Menu entry, uninstaller)
- `MicFX.exe` - portable single-file build
- `MicFX-<version>-win-x64.zip` - portable build, zipped

The binaries are unsigned, so SmartScreen may warn on first run
(More info > Run anyway). In-app updates are not affected.

### Changed

- Interface overhaul. Sliders, checkboxes, scrollbars, level meters, text
  fields, tooltips and context menus were still drawn by Windows' default
  control templates, which do not match a dark interface: pale square
  checkboxes, thin grey slider tracks and full-width light scrollbars. All of
  them are now themed:
  - Sliders have a rounded track, an accent-filled portion and a round thumb
    that highlights on hover and while dragging; clicking anywhere on the
    track jumps to that position.
  - Checkboxes are rounded and fill with the accent colour when checked.
  - Scrollbars are slim, rounded and sit flush against the content.
  - Level meters are rounded and lose their border.
  - Tooltips and right-click menus follow the dark palette.
- Slightly deeper background, softer panel corners and more consistent
  spacing and type sizes throughout.
- The header shows a status dot (grey stopped, green running, red on error),
  and the status line reveals the full text as a tooltip when it is too long
  to fit.

### First-time setup

1. Install [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as
   administrator, reboot).
2. In MicFX: select your microphone as input and "CABLE Input" as output,
   press Start.
3. In Discord/OBS: select "CABLE Output" as the microphone.

See the [README](https://github.com/Norbeto0/MicFX#readme) for full
instructions and troubleshooting.
