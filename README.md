# MicFX 🎙

A Windows desktop app in the spirit of **Voicemod** and the mic channel of
**SteelSeries Sonar / Voicemeeter** — but deliberately scoped to a single
microphone input and a single output.

Real-time pipeline (48 kHz, WASAPI shared mode):

```
Microphone → Mic gain → Noise gate → 10-band EQ → Voice effect
           → (+ Soundboard) → Master volume → Output device
```

**Features**

- 🎚 **10-band EQ** (31 Hz – 16 kHz, ±15 dB) with presets: Flat, Warm Broadcast,
  Bright & Clear, Cut The Mud, Bass Boost
- 🚪 **Noise gate** with adjustable threshold, fast attack / smooth release
- 🤖 **Voice effects**: Robot (ring modulator), Female, Deep Male, Chipmunk,
  Cave Echo — each with an intensity slider, switchable live
- 🔊 **Soundboard**: play wav/mp3/m4a/wma clips *into your mic signal*, global
  hotkeys **Ctrl+Alt+1…9**, per-board volume, Stop All
- 🎧 **Self-monitoring** ("listen to myself") on a separate device
- 📈 Live input/output level meters, mic gain & master volume
- 🖥 **Close-to-tray**: clicking ✕ minimizes MicFX to the system tray (exit via the tray icon)
- 🚀 **Run on Windows startup** (optional) — starts minimized in the tray
- 💾 All settings persisted in `%APPDATA%\MicFX\settings.json`

The full product spec this app was built from is in [SPEC.md](SPEC.md).

---

## 1. One-time setup: install a virtual audio cable

MicFX processes your mic and plays the result to an output device. For other
apps (Discord, OBS, games) to *see* that processed signal as a microphone, you
route it through a free virtual cable:

1. Download **VB-Audio Virtual Cable**: <https://vb-audio.com/Cable/>
2. Extract and run `VBCABLE_Setup_x64.exe` **as administrator**, then reboot.
3. Windows now has two new devices:
   - Playback: **CABLE Input** ← MicFX sends processed audio here
   - Recording: **CABLE Output** ← other apps use this as their "microphone"

> Why no built-in driver? Signed Windows kernel audio drivers require
> WHQL/EV certification — Voicemod ships one because it's a commercial product.
> VB-Cable is the standard free equivalent and works identically.

## 2. Run MicFX

**Easiest:** download `MicFX.exe` from the
[Releases page](../../releases) — it's self-contained, no .NET install needed.
(The exe is unsigned, so SmartScreen may warn on first run: **More info → Run anyway**.)

Or build from source with the .NET 8 SDK:

```
dotnet run --project src/MicFX
```

Then in MicFX:
   - **Microphone** → your real mic
   - **Output** → **CABLE Input (VB-Audio Virtual Cable)**
   - **Monitor** → your headphones, tick *Listen to myself* to hear the effects
   - Press **Start**

## 3. Point your apps at the cable

| App | Setting |
|---|---|
| Discord | Settings → Voice & Video → Input Device → **CABLE Output** |
| OBS | Add Audio Input Capture source → **CABLE Output** |
| Games / others | Set default recording device to **CABLE Output** in Windows Sound settings |

Talk — your voice arrives EQ'd, gated and effected; soundboard clips are mixed
in on top while you keep talking.

## Building a standalone .exe

On a Windows machine with the .NET 8 SDK:

```
publish.cmd
```

This produces a single self-contained `dist\MicFX.exe` (~150 MB, no .NET
install required on the target machine).

## Troubleshooting

| Symptom | Fix |
|---|---|
| "Error: … device in use" on Start | Another app holds the device in exclusive mode — disable exclusive mode in the device's Sound settings, or close the other app. |
| Discord hears nothing | Check MicFX **Output** is CABLE Input, Discord input is CABLE Output, and the engine is **Running** (green meters move when you talk). |
| Robotic crackling / dropouts | Raise the WASAPI latency (see `AudioEngine.cs`, the `50` ms in `WasapiOut`) or close CPU-heavy apps. Pitch effects add ~40 ms inherent latency. |
| Hotkeys don't fire | Another app owns Ctrl+Alt+number. MicFX skips combos it can't register. |
| Echo of yourself in calls | Turn off *Listen to myself*, or make sure your monitor device is headphones, not speakers. |

## Project layout

```
src/MicFX/
  Audio/
    AudioEngine.cs               the whole real-time graph, start/stop, live parameters
    AudioDevices.cs              WASAPI endpoint enumeration
    EqualizerSampleProvider.cs   10-band biquad peaking EQ
    NoiseGateSampleProvider.cs   envelope-follower downward gate
    RingModulatorSampleProvider.cs  robot voice
    EchoSampleProvider.cs        cave echo (feedback delay)
    TeeSampleProvider.cs         split-off for self-monitoring
  Models/AppSettings.cs          JSON persistence (%APPDATA%\MicFX)
  HotkeyManager.cs               global Ctrl+Alt+1…9 hotkeys
  MainWindow.xaml(.cs)           UI: devices, EQ, effects, soundboard
  App.xaml                       dark theme
```

## License / credits

Uses [NAudio](https://github.com/naudio/NAudio) (MIT). VB-Audio Virtual Cable
is donationware by VB-Audio Software.
