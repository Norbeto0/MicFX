### Downloads

- `MicFX-Setup-<version>.exe` - installer (Start Menu entry, uninstaller)
- `MicFX.exe` - portable single-file build
- `MicFX-<version>-win-x64.zip` - portable build, zipped

The binaries are unsigned, so SmartScreen may warn on first run
(More info > Run anyway). In-app updates are not affected.

### Noise suppression

- The AI engine is upgraded from RNNoise 0.1.1 to 0.2, xiph's retrained and
  larger model. Measured on real speech (CMU ARCTIC, male and female voices)
  mixed with four noise types at 0-20 dB SNR, noise left in pauses between
  words:

  | Noise          | 0.1.1   | 0.2     |
  |----------------|---------|---------|
  | Keyboard clicks| -4 dB   | -32 dB  |
  | Preamp hiss    | -14 dB  | -61 dB  |
  | Fan / AC       | -30 dB  | -65 dB  |
  | Mains hum      | -20 dB  | -40 dB  |

  Voice quality (SI-SDR improvement) also rises from +3.9 to +4.9 dB. CPU use
  is unchanged at about 2% of one core per channel.
- New strength control for the AI engine ("Max" by default). Lower values
  blend some of the original signal back in, which can sound more natural;
  at 24 dB it measured slightly better voice quality than full strength. The
  original signal is delayed to line up exactly with the model output, which
  avoids the comb filtering a plain blend would cause.
- New "Silence between phrases" option (AI engine only): mutes the mic when
  you are not speaking, using the model's own speech detection rather than
  loudness, so desk knocks and key presses do not open it. Calibrated on the
  same recordings: at realistic room noise it keeps 99.0-99.6% of speech
  frames and never opened on noise alone. In a very loud room (noise as loud
  as your voice) it can occasionally clip a syllable; leave it off there.
- Removed the input level normalization added in 1.6. Tested on real speech
  it made no measurable difference.
- Latency: the new model takes 20 ms instead of 10 ms. The spectral engine is
  unaffected.

### Also new

- Safety limiter on the output and the headphone monitor: nothing leaves
  MicFX above -1 dBFS, so your voice plus a loud clip cannot distort in
  Discord or blast your ears. 1 ms lookahead; audio below the ceiling is
  passed through unchanged.
- Level meters turn red when the mic input clips (lower Mic gain) or when the
  output limiter has to act (you are louder than needed). Hover for details.
- Soundboard loudness matching ("Even out loudness", on by default): each clip
  is measured once using ITU-R BS.1770 and played at about -20 LUFS, so quiet
  and loud clips come out at similar volume. Quiet clips are raised at most
  12 dB. Per-clip volume still applies on top.
- Theme color in the settings menu: eight presets or any custom color.
  Applies immediately.
- "Check for updates" button in the settings menu, next to the installed
  version. The automatic check at startup is unchanged.
- The audio code now has a test suite in the repository (44 tests), and the
  release build runs it against the Windows RNNoise library it just compiled.

### Fixes

- Clicking the settings button while the menu was open reopened it instead of
  closing it.

### First-time setup

1. Install [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (run as
   administrator, reboot).
2. In MicFX: select your microphone as input and "CABLE Input" as output,
   press Start.
3. In Discord/OBS: select "CABLE Output" as the microphone.

See the [README](https://github.com/Norbeto0/MicFX#readme) for full
instructions and troubleshooting.
