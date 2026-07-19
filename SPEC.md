# MicFX — Improved Prompt / Product Spec

This is the refined version of the original request ("a Windows app like Voicemod, with
Sonar/Voicemeeter-style mic settings, but only for one mic input and output").
It is the prompt the application in this repository was built from.

---

## The improved prompt

> **Role**: You are a senior Windows desktop + audio DSP engineer.
>
> **Goal**: Build a production-quality Windows 10/11 desktop application called **MicFX** —
> a real-time microphone effects station. Think *Voicemod* (voice effects + soundboard)
> combined with the mic channel of *SteelSeries Sonar / Voicemeeter* (EQ, gate, gain,
> routing), but deliberately scoped to **one microphone input and one output**. No
> multi-bus mixing, no speaker/game/chat channels.
>
> **Audio pipeline** (real time, low latency, WASAPI shared mode, 48 kHz float):
>
> ```
> Microphone → Mic gain → Noise gate → 10-band EQ → Voice effect
>            → (+ Soundboard mix) → Master volume → Output device
> ```
>
> The output device is expected to be a **virtual audio cable** (e.g. the free VB-Audio
> "CABLE Input"), so that Discord, OBS, or any game can select the processed signal as
> its microphone. Do **not** write a kernel audio driver — document the VB-Cable routing
> instead.
>
> **Functional requirements**
>
> 1. **Devices** — dropdowns listing all active capture and render devices by their
>    friendly Windows names; a refresh button; the last used devices are remembered and
>    the engine auto-starts on launch when they are still present. Changing a device
>    while running restarts the engine seamlessly.
> 2. **10-band graphic EQ** — bands at 31 / 62 / 125 / 250 / 500 Hz and 1 / 2 / 4 / 8 / 16 kHz,
>    ±15 dB vertical sliders with live dB readouts, plus presets (Flat, Warm Broadcast,
>    Bright & Clear, Cut The Mud, Bass Boost) and a reset button.
> 3. **Noise gate** — on/off toggle and threshold slider (−80…−20 dB) with fast attack
>    and smooth release so words are not clipped.
> 4. **Mic gain, master volume and live level meters** for input and output.
> 5. **Voice effects** (selectable list, one active at a time, with an intensity slider):
>    *None*, *Robot* (ring modulator), *Female* (pitch up), *Deep Male* (pitch down),
>    *Chipmunk*, *Cave Echo* (feedback delay). Effects switch instantly without
>    restarting the stream.
> 6. **Soundboard** — add local audio files (wav/mp3/m4a/wma/aiff), shown as a grid of
>    tiles; clicking a tile plays it *into the mic stream* mixed with the voice; global
>    hotkeys **Ctrl+Alt+1…9** trigger the first nine tiles even when the app is not
>    focused; per-board volume slider; Stop All button; right-click a tile to remove it.
> 7. **Self-monitoring** — a "listen to myself" toggle that plays the processed signal
>    to a separately selectable monitor device (your headphones), so the user can hear
>    their own effects.
> 8. **Persistence** — all settings (devices, EQ, gate, effect, volumes, soundboard
>    entries) stored as JSON in `%APPDATA%\MicFX` and restored on launch.
> 9. **UI** — a single-window modern dark theme: devices/levels on the left, effect list
>    in the middle, EQ on the right, soundboard docked at the bottom. Clear status text
>    for engine state and errors (device busy, file missing, …).
>
> **Technical requirements**
>
> - .NET 8, WPF, C#; NAudio for WASAPI capture/render, resampling, mixing and DSP.
> - WASAPI event-driven shared mode, small buffers (~20–50 ms); internal graph at
>   48 kHz stereo IEEE float; automatic resampling/up-mixing of any mic format.
> - EQ as per-channel biquad peaking filters; pitch shifting via a phase-vocoder
>   (SMB) pitch shifter; robot as sine ring modulation; cave as a feedback delay line.
> - Robust error handling: a failing device or unreadable file must surface in the
>   status bar, never crash the app.
> - Ship a `publish.cmd` that produces a single self-contained x64 `.exe`
>   (no .NET install required on the target machine), and a README covering
>   VB-Cable installation and Discord/OBS setup.
>
> **Out of scope (v1)**: custom virtual audio driver, multiple inputs/outputs or buses,
> VST plugin hosting, noise suppression AI, formant-correct voice conversion,
> per-app routing, macOS/Linux.
>
> **Acceptance criteria**: build succeeds with `dotnet build`; with VB-Cable installed,
> Discord set to "CABLE Output" receives the EQ'd, effected mic signal; soundboard
> clips are audible to the remote side while the user keeps talking; settings survive
> an app restart.

---

## Why these choices

- **VB-Audio Virtual Cable instead of a custom driver** — signed kernel audio drivers
  require WHQL/EV signing and are far outside a v1; every comparable DIY tool routes
  through VB-Cable. Voicemod ships its own driver only because it is a commercial product.
- **WASAPI shared mode** — exclusive mode would steal the mic from other apps and break
  device mix formats; shared mode keeps latency acceptable (~50–100 ms end to end).
- **NAudio** — mature, MIT-licensed, has WASAPI, resampling, mixing, biquads and an SMB
  pitch shifter out of the box; no native dependencies to ship.
