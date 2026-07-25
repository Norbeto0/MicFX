### Downloads

- `MicFX-Setup-<version>.exe` - installer (Start Menu entry, uninstaller)
- `MicFX.exe` - portable single-file build
- `MicFX-<version>-win-x64.zip` - portable build, zipped

The binaries are unsigned, so SmartScreen may warn on first run
(More info > Run anyway). In-app updates are not affected.

### Fixed

- Auto-switching now follows audio activity instead of device presence.
  Virtual microphones such as Virtual Desktop's stay listed as "Ready" in
  Windows whether or not the headset is connected, so the previous
  presence-based rule handed the input over permanently as soon as the device
  existed. MicFX now listens to the chosen auto-switch device in the
  background and takes it over about a second after it starts carrying audio,
  returning to the primary microphone after eight seconds of digital silence.
  An idle virtual device emits exact digital silence, while any live
  microphone carries a noise floor, which is what makes the two
  distinguishable.
- Selecting the recording end of the same virtual cable that MicFX writes to
  (for example capturing "CABLE Output" while sending to "CABLE Input") is now
  refused with an explanation instead of starting a feedback loop in which the
  application processes its own output.

### First-time setup

1. Install [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as
   administrator, reboot).
2. In MicFX: select your microphone as input and "CABLE Input" as output,
   press Start.
3. In Discord/OBS: select "CABLE Output" as the microphone.

See the [README](https://github.com/Norbeto0/MicFX#readme) for full
instructions and troubleshooting.
