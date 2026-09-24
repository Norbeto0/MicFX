### Downloads

- `MicFX-Setup-<version>.exe` - installer (Start Menu entry, uninstaller)
- `MicFX.exe` - portable single-file build
- `MicFX-<version>-win-x64.zip` - portable build, zipped

The binaries are unsigned, so SmartScreen may warn on first run
(More info > Run anyway). In-app updates are not affected.

### Fixes

- Headphone monitor stayed silent if the headphones were switched on after
  MicFX started (e.g. after boot), or were turned off and on while it was
  running, even though it showed as enabled. It now opens them as soon as
  they appear and recovers within about a second after the device resets
  (power cycle, replug, sample rate change).
- The monitor device is remembered while the headphones are off. The list
  shows it as "(not connected)" instead of switching to the Windows default
  device.
- If the output device (the virtual cable) resets, the engine restarts itself
  instead of running silently.

### Spectral noise suppression

- Rewritten. The old version set each frequency's gain from that instant's
  noise level, so random noise peaks got through as short tones (the chirpy,
  watery "musical noise"), and it removed only about 8 dB whatever the strength
  was set to. It now estimates speech per frequency over several frames
  (decision-directed, Ephraim-Malah) and holds pauses at the set level.
  Measured on real speech with fan, hiss and hum noise at 10-20 dB SNR:

  | Strength | Noise removed in pauses, 1.10.0 | 1.10.1 |
  |----------|---------------------------------|--------|
  | 18 dB    | 8 dB                            | 18 dB  |
  | 30 dB    | 9 dB                            | 30 dB  |

  Musical noise (log kurtosis ratio of the leftover noise, 0 = same character
  as the original noise, only quieter) went from 1.4 to 0.0.
- A single frame of voice can no longer pull the noise estimate up, so held
  notes and soft syllables are not learned as noise and suppressed later.
- The AI engine is still the better choice for anything but steady noise.

### Voice leveler

- Make-up gain now applies to your voice, not to the silence between words.
  At 100 it used to lift pauses by up to 13.75 dB, which undid most of the
  noise suppression and made the leftover noise pump up between words.
  After spectral suppression at 18 dB: pauses at -62.5 dBFS instead of -51,
  same speech level.

### Layout

- The equalizer stops growing at a comfortable height; extra window height
  goes to the soundboard.

### First-time setup

1. Install [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as
   administrator, reboot).
2. In MicFX: select your microphone as input and "CABLE Input" as output,
   press Start.
3. In Discord/OBS: select "CABLE Output" as the microphone.

See the [README](https://github.com/Norbeto0/MicFX#readme) for full
instructions and troubleshooting.
