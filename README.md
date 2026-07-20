# MicFX

Real-time microphone processing for Windows: EQ, noise suppression, noise
gate, compressor, voice effects and a soundboard on a single mic input. The
processed signal is routed to a virtual audio device so other applications
(Discord, OBS, games) can use it as their microphone.

Pipeline (WASAPI shared mode, 48 kHz float):

```
mic -> gain -> noise suppression -> gate -> EQ -> compressor -> voice effect
    -> (+ soundboard) -> master volume -> output device (virtual cable)
```

## Features

- Graphic EQ with adjustable band count (10-20, log-spaced 31 Hz-16 kHz,
  +/-15 dB), presets, live spectrum display behind the sliders
- Spectral noise suppression (STFT with adaptive per-bin noise floor),
  adjustable strength
- Noise gate with adjustable threshold
- Compressor ("voice leveler") with automatic make-up gain
- Voice effects: robot, female, deep, chipmunk, cave echo, megaphone, alien,
  whisper, ghost; one intensity control; panel hidden by default, enabled in
  the settings menu
- Soundboard mixed into the mic stream: per-clip volume, loop mode, custom
  global hotkeys (Ctrl+Alt+1..9 by default), headphone-only preview
- Profiles for saving and switching complete configurations, also from the
  tray menu
- Self-monitoring on a separate output device
- Input/output level meters, mic gain, master volume
- Closing the window minimizes to the system tray; optional start with
  Windows (minimized)
- In-app auto-update from GitHub releases
- Settings persisted in `%APPDATA%\MicFX\settings.json`

## Setup

MicFX needs a virtual audio cable so other applications can read the
processed signal:

1. Install [VB-Audio Virtual Cable](https://vb-audio.com/Cable/). Run the
   installer as administrator and reboot. This adds a playback device
   ("CABLE Input") and a recording device ("CABLE Output").
2. Download `MicFX-Setup-<version>.exe` (installer) or `MicFX.exe` (portable)
   from the [releases page](../../releases). The binaries are unsigned, so
   SmartScreen may warn on first run.
3. In MicFX: select your microphone as input, "CABLE Input" as output, press
   Start. Optionally set your headphones as monitor and enable "Listen to
   myself".
4. Point the target application at the cable:

| Application | Setting |
|---|---|
| Discord | Settings > Voice & Video > Input Device > CABLE Output |
| OBS | Audio Input Capture source > CABLE Output |
| Other | Set CABLE Output as default recording device in Windows Sound settings |

## Building

Requires the .NET 8 SDK.

```
dotnet run --project src/MicFX
```

`publish.cmd` produces a self-contained single-file `dist\MicFX.exe`.

Releases are built by `.github/workflows/release.yml` (on a `v*` tag push or
manual dispatch): it publishes the exe, zips it, compiles the Inno Setup
installer and creates the GitHub release with all three assets.

## Troubleshooting

| Symptom | Fix |
|---|---|
| "Error: ... device in use" on Start | Another application holds the device in exclusive mode. Disable exclusive mode in the device's sound settings or close the other application. |
| Discord hears nothing | Check MicFX output is CABLE Input, Discord input is CABLE Output, and the engine is running (meters move when you talk). |
| Crackling or dropouts | Increase the WASAPI latency (the `50` ms in `AudioEngine.cs`) or close CPU-heavy applications. Pitch effects add roughly 40 ms of inherent latency. |
| Hotkeys do not fire | Another application owns the combination. MicFX skips combinations it cannot register; assign a different one per clip. |
| Callers hear an echo of themselves | Disable "Listen to myself", or make sure the monitor device is headphones rather than speakers. |

## Project layout

```
src/MicFX/
  Audio/
    AudioEngine.cs               real-time graph, start/stop, live parameters
    AudioDevices.cs              WASAPI endpoint enumeration
    NoiseSuppressionSampleProvider.cs  spectral denoiser
    NoiseGateSampleProvider.cs   envelope-follower gate
    CompressorSampleProvider.cs  compressor with auto make-up
    EqualizerSampleProvider.cs   10-20 band biquad peaking EQ
    SpectrumTapSampleProvider.cs sample window for the spectrum display
    RingModulatorSampleProvider.cs  robot voice
    MegaphoneSampleProvider.cs   bandpass + drive distortion
    FlangerSampleProvider.cs     alien voice (swept delay)
    WhisperSampleProvider.cs     envelope-modulated noise voice
    GhostSampleProvider.cs       reverse echo
    EchoSampleProvider.cs        cave echo (feedback delay)
    TeeSampleProvider.cs         split-off for self-monitoring
  Models/AppSettings.cs          JSON persistence, profiles
  HotkeyManager.cs               global hotkey registration
  MainWindow.xaml(.cs)           main UI
  EditSoundWindow.xaml(.cs)      per-clip editor (volume, loop, hotkey, preview)
  App.xaml                       theme
installer/MicFX.iss              Inno Setup script (built in CI)
```

## Credits

Uses [NAudio](https://github.com/naudio/NAudio) (MIT). VB-Audio Virtual Cable
is donationware by VB-Audio Software.
