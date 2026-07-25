### Downloads

- `MicFX-Setup-<version>.exe` - installer (Start Menu entry, uninstaller)
- `MicFX.exe` - portable single-file build
- `MicFX-<version>-win-x64.zip` - portable build, zipped

The binaries are unsigned, so SmartScreen may warn on first run
(More info > Run anyway). In-app updates are not affected.

### Added

- Second microphone input with automatic switching. Pick an "Auto-switch to
  (when connected)" device under DEVICES: whenever that microphone is present
  MicFX captures from it, and when it disappears MicFX falls back to the
  primary one. The output stays on the same virtual cable throughout, so
  Discord, OBS and games never need to be reconfigured. Intended for VR
  headsets: connect the headset, its mic takes over; disconnect, the desk mic
  comes back. The choice is remembered by name while the device is unplugged.
- Monitor section with independent levels. The processed voice and the
  soundboard now feed the headphone output through separate volume sliders, so
  soundboard clips can be heard clearly while your own voice is turned down or
  off. Monitoring is no longer affected by the master output volume.

### Changed

- Lower latency in the Low and Lowest modes: the drift guard's bounds now
  scale with the selected mode instead of using one fixed setting, so buffered
  audio is trimmed sooner (Lowest tops out around 90 ms of internal buffering
  instead of 140 ms). The target is kept above one output period so a
  correction cannot introduce a click; verified with a simulation that found
  no inserted silence across repeated corrections.
- The status bar shows an estimated end-to-end latency for the current
  settings, and monitor outputs are now drift-guarded as well.

### First-time setup

1. Install [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as
   administrator, reboot).
2. In MicFX: select your microphone as input and "CABLE Input" as output,
   press Start.
3. In Discord/OBS: select "CABLE Output" as the microphone.

See the [README](https://github.com/Norbeto0/MicFX#readme) for full
instructions and troubleshooting.
