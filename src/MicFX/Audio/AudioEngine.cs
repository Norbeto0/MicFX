using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicFX.Audio;

/// <summary>
/// Owns the whole real-time graph:
///
///   mic capture → drift guard → resample → noise suppression → (mono→stereo)
///     → gain → meter → gate → EQ → spectrum tap → compressor
///     → pitch → ring-mod → megaphone → flanger → whisper → ghost → echo → tee ─┐
///                                                                              ├→ master → meter → output
///   soundboard clips → sub-mixer → soundboard volume → tee ─────────────────────┘
///
/// The two tees feed the monitor output through independent volume controls,
/// so the user can hear the soundboard loudly while keeping their own voice
/// quiet (or off) in their headphones.
///
/// Everything runs at 48 kHz stereo IEEE float. Parameter setters are safe to
/// call from the UI thread while the graph is running, and are remembered so a
/// later Start() picks them up.
/// </summary>
public class AudioEngine : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;

    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private WasapiCapture? capture;
    private WasapiOut? output;
    private WasapiOut? monitorOutput;
    private WasapiOut? previewOutput;
    private AudioFileReader? previewReader;

    private BufferedWaveProvider? micBuffer;
    private BufferedWaveProvider? monitorVoiceBuffer;
    private BufferedWaveProvider? monitorSoundBuffer;
    private MixingSampleProvider? soundMixer;

    private VolumeSampleProvider? micVolume;
    private VolumeSampleProvider? soundboardVolume;
    private VolumeSampleProvider? masterVolume;
    private VolumeSampleProvider? monitorVoiceVolume;
    private VolumeSampleProvider? monitorSoundVolume;
    private RnNoiseSampleProvider? aiDenoise;
    private NoiseSuppressionSampleProvider? denoise;
    private NoiseGateSampleProvider? gate;
    private EqualizerSampleProvider? eq;
    private SpectrumTapSampleProvider? spectrumTap;
    private CompressorSampleProvider? compressor;
    private SmbPitchShiftingSampleProvider? pitch;
    private RingModulatorSampleProvider? robot;
    private MegaphoneSampleProvider? megaphone;
    private FlangerSampleProvider? flanger;
    private WhisperSampleProvider? whisper;
    private GhostSampleProvider? ghost;
    private EchoSampleProvider? echo;
    private TeeSampleProvider? voiceTee;
    private TeeSampleProvider? soundTee;

    private readonly List<(ISampleProvider Tail, AudioFileReader Reader)> activeClips = new();
    private readonly object clipLock = new();

    // Last known parameters, applied on the next Start() when the graph is down.
    private float micGain = 1f;
    private float master = 1f;
    private float soundVol = 0.8f;
    private float[] eqGains = new float[10];
    private int eqBandCount = 10;
    private bool gateEnabled;
    private float gateThresholdDb = -45f;
    private bool denoiseEnabled;
    private float denoiseStrengthDb = 18f;
    private string denoiseMode = "Ai";
    private bool compEnabled;
    private float compAmount = 50f;
    private string effectName = "None";
    private int effectIntensity = 50;
    private string latencyMode = "Normal";
    private float monitorVoice = 1f;
    private float monitorSound = 1f;

    private float lastInputDb = -100f;

    public bool IsRunning { get; private set; }

    /// <summary>Set after Start(): e.g. "exclusive mic access" or a fallback warning.</summary>
    public string? CaptureNote { get; private set; }

    private int CaptureLatencyMs => latencyMode is "Low" or "Lowest" ? 10 : 20;
    private int OutputLatencyMs => latencyMode switch { "Lowest" => 20, "Low" => 25, _ => 50 };

    /// <summary>
    /// Drift-guard bounds, scaled to the latency mode. The target must stay
    /// above one output pull so a correction can't leave the buffer too empty
    /// to satisfy the next read (which would insert a click of silence); the
    /// ceiling caps how far latency can drift upward before being trimmed.
    /// </summary>
    private (int Target, int Ceiling) DriftBounds =>
        (OutputLatencyMs + CaptureLatencyMs + 10, OutputLatencyMs + CaptureLatencyMs + 60);

    /// <summary>Estimated mouth-to-output latency in ms, for display.</summary>
    public int EstimatedLatencyMs
    {
        get
        {
            int total = CaptureLatencyMs + OutputLatencyMs + 10; // + RNNoise framing
            if (denoiseEnabled && denoiseMode == "Spectral") total += 5;
            if (effectName is "Female" or "Deep" or "Chipmunk") total += 35;
            return total;
        }
    }

    /// <summary>Takes effect on the next Start(); the UI restarts the engine after changing it.</summary>
    public void SetLatencyMode(string mode) => latencyMode = mode;

    /// <summary>Raised from the audio thread with (input dB, output dB) roughly 20×/second.</summary>
    public event Action<float, float>? LevelsAvailable;

    /// <summary>Raised (from the audio thread) when a soundboard clip finishes or is stopped.</summary>
    public event Action<object>? ClipEnded;

    public void Start(MMDevice input, MMDevice render)
    {
        Stop();
        CaptureNote = null;

        capture = StartCapture(input);
        var format = NormalizeFormat(capture.WaveFormat);
        if (format.Channels > 2)
            throw new NotSupportedException(
                $"Input device has {format.Channels} channels; only mono/stereo microphones are supported.");

        micBuffer = new BufferedWaveProvider(format)
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true
        };
        capture.DataAvailable += (_, e) => micBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);

        // Keep mouth-to-output latency bounded despite mic/output clock drift.
        var (driftTarget, driftCeiling) = DriftBounds;
        ISampleProvider mic = new DriftCompensatingSampleProvider(micBuffer, driftTarget, driftCeiling);
        if (mic.WaveFormat.SampleRate != SampleRate)
            mic = new WdlResamplingSampleProvider(mic, SampleRate);
        aiDenoise = new RnNoiseSampleProvider(mic);
        denoise = new NoiseSuppressionSampleProvider(aiDenoise) { ReductionDb = denoiseStrengthDb };
        ApplyDenoise();
        mic = denoise;
        if (mic.WaveFormat.Channels == 1)
            mic = new MonoToStereoSampleProvider(mic);

        micVolume = new VolumeSampleProvider(mic) { Volume = micGain };
        var inputMeter = new MeteringSampleProvider(micVolume, SampleRate / 20);
        inputMeter.StreamVolume += (_, e) => lastInputDb = ToDb(MaxOf(e.MaxSampleValues));

        gate = new NoiseGateSampleProvider(inputMeter) { Enabled = gateEnabled, ThresholdDb = gateThresholdDb };
        eq = new EqualizerSampleProvider(gate, eqBandCount, eqGains);
        spectrumTap = new SpectrumTapSampleProvider(eq);
        compressor = new CompressorSampleProvider(spectrumTap) { Enabled = compEnabled };
        compressor.SetAmount(compAmount);

        pitch = new SmbPitchShiftingSampleProvider(compressor, 2048, 4, 1f);
        robot = new RingModulatorSampleProvider(pitch);
        megaphone = new MegaphoneSampleProvider(robot);
        flanger = new FlangerSampleProvider(megaphone);
        whisper = new WhisperSampleProvider(flanger);
        ghost = new GhostSampleProvider(whisper);
        echo = new EchoSampleProvider(ghost);

        soundMixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels))
        {
            ReadFully = true
        };
        soundMixer.MixerInputEnded += OnClipEnded;
        soundboardVolume = new VolumeSampleProvider(soundMixer) { Volume = soundVol };

        // Separate monitor taps for voice and soundboard, so each can have its
        // own level in the user's headphones.
        monitorVoiceBuffer = NewMonitorBuffer();
        monitorSoundBuffer = NewMonitorBuffer();
        voiceTee = new TeeSampleProvider(echo) { Sink = monitorVoiceBuffer, Enabled = false };
        soundTee = new TeeSampleProvider(soundboardVolume) { Sink = monitorSoundBuffer, Enabled = false };

        var mainMixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels))
        {
            ReadFully = true
        };
        mainMixer.AddMixerInput((ISampleProvider)voiceTee);
        mainMixer.AddMixerInput((ISampleProvider)soundTee);

        masterVolume = new VolumeSampleProvider(mainMixer) { Volume = master };
        var outputMeter = new MeteringSampleProvider(masterVolume, SampleRate / 20);
        outputMeter.StreamVolume += (_, e) =>
            LevelsAvailable?.Invoke(lastInputDb, ToDb(MaxOf(e.MaxSampleValues)));

        ApplyEffect(effectName, effectIntensity);

        output = new WasapiOut(render, AudioClientShareMode.Shared, true, OutputLatencyMs);
        output.Init(new SampleToWaveProvider(outputMeter));

        output.Play();
        IsRunning = true;
    }

    /// <summary>
    /// Opens and starts the capture device. In "Lowest" mode this tries WASAPI
    /// exclusive first (bypasses the Windows audio engine; fine here because
    /// only MicFX needs the raw mic), walking a list of common exclusive
    /// formats, then falls back to shared mode with a note.
    /// Recording is already running when this returns — the DataAvailable
    /// subscription is attached moments later, which only drops a few ms.
    /// </summary>
    private WasapiCapture StartCapture(MMDevice device)
    {
        int ms = CaptureLatencyMs;
        if (latencyMode == "Lowest")
        {
            var candidates = new[]
            {
                new WaveFormat(48000, 16, 2), new WaveFormat(48000, 16, 1),
                new WaveFormat(48000, 24, 2), new WaveFormat(48000, 24, 1),
                new WaveFormat(44100, 16, 2), new WaveFormat(44100, 16, 1),
            };
            foreach (var fmt in candidates)
            {
                var attempt = new WasapiCapture(device, true, ms)
                {
                    ShareMode = AudioClientShareMode.Exclusive,
                    WaveFormat = fmt
                };
                try
                {
                    attempt.StartRecording();
                    CaptureNote = "exclusive mic access";
                    return attempt;
                }
                catch
                {
                    attempt.Dispose();
                }
            }
            CaptureNote = "exclusive mode not supported by this mic — using shared";
        }

        var shared = new WasapiCapture(device, true, ms);
        shared.StartRecording();
        return shared;
    }

    private static BufferedWaveProvider NewMonitorBuffer() =>
        new(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels))
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true
        };

    /// <summary>
    /// Enable/disable monitoring on the given render device. Rebuilds the
    /// monitor chain, so call it only when the device or the on/off state
    /// changes — use SetMonitorVolumes for live level changes.
    /// </summary>
    public void SetMonitor(MMDevice? device, bool enabled)
    {
        if (voiceTee != null) voiceTee.Enabled = false;
        if (soundTee != null) soundTee.Enabled = false;
        try { monitorOutput?.Stop(); } catch { /* device may already be gone */ }
        monitorOutput?.Dispose();
        monitorOutput = null;
        monitorVoiceVolume = null;
        monitorSoundVolume = null;
        monitorVoiceBuffer?.ClearBuffer();
        monitorSoundBuffer?.ClearBuffer();

        if (!IsRunning || !enabled || device == null ||
            voiceTee == null || soundTee == null ||
            monitorVoiceBuffer == null || monitorSoundBuffer == null)
            return;

        // The monitor device has its own clock too, so guard both taps against drift.
        var (target, ceiling) = DriftBounds;
        monitorVoiceVolume = new VolumeSampleProvider(
            new DriftCompensatingSampleProvider(monitorVoiceBuffer, target, ceiling)) { Volume = monitorVoice };
        monitorSoundVolume = new VolumeSampleProvider(
            new DriftCompensatingSampleProvider(monitorSoundBuffer, target, ceiling)) { Volume = monitorSound };

        var monitorMixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels))
        {
            ReadFully = true
        };
        monitorMixer.AddMixerInput((ISampleProvider)monitorVoiceVolume);
        monitorMixer.AddMixerInput((ISampleProvider)monitorSoundVolume);

        monitorOutput = new WasapiOut(device, AudioClientShareMode.Shared, true, OutputLatencyMs);
        monitorOutput.Init(new SampleToWaveProvider(monitorMixer));
        monitorOutput.Play();
        voiceTee.Enabled = true;
        soundTee.Enabled = true;
    }

    /// <summary>Live monitor levels: how loud your own voice and the soundboard are in your headphones.</summary>
    public void SetMonitorVolumes(float voice, float soundboard)
    {
        monitorVoice = voice;
        monitorSound = soundboard;
        if (monitorVoiceVolume != null) monitorVoiceVolume.Volume = voice;
        if (monitorSoundVolume != null) monitorSoundVolume.Volume = soundboard;
    }

    public void Stop()
    {
        IsRunning = false;
        StopAllClips();
        try { capture?.StopRecording(); } catch { }
        try { output?.Stop(); } catch { }
        try { monitorOutput?.Stop(); } catch { }
        capture?.Dispose();
        capture = null;
        output?.Dispose();
        output = null;
        monitorOutput?.Dispose();
        monitorOutput = null;
        if (soundMixer != null) soundMixer.MixerInputEnded -= OnClipEnded;
        micBuffer = null;
        monitorVoiceBuffer = null;
        monitorSoundBuffer = null;
        soundMixer = null;
        micVolume = null;
        soundboardVolume = null;
        masterVolume = null;
        monitorVoiceVolume = null;
        monitorSoundVolume = null;
        aiDenoise?.Dispose();
        aiDenoise = null;
        denoise = null;
        gate = null;
        eq = null;
        spectrumTap = null;
        compressor = null;
        pitch = null;
        robot = null;
        megaphone = null;
        flanger = null;
        whisper = null;
        ghost = null;
        echo = null;
        voiceTee = null;
        soundTee = null;
    }

    // ---------- live parameters ----------

    public void SetMicGain(float value)
    {
        micGain = value;
        if (micVolume != null) micVolume.Volume = value;
    }

    public void SetMasterVolume(float value)
    {
        master = value;
        if (masterVolume != null) masterVolume.Volume = value;
    }

    public void SetSoundboardVolume(float value)
    {
        soundVol = value;
        if (soundboardVolume != null) soundboardVolume.Volume = value;
    }

    public void SetGate(bool enabled, float thresholdDb)
    {
        gateEnabled = enabled;
        gateThresholdDb = thresholdDb;
        if (gate != null)
        {
            gate.Enabled = enabled;
            gate.ThresholdDb = thresholdDb;
        }
    }

    /// <summary>True when the native RNNoise library is loadable on this machine.</summary>
    public static bool AiDenoiseAvailable => RnNoiseSampleProvider.IsAvailable;

    /// <summary>Why AI suppression is unavailable, when it is.</summary>
    public static string? AiDenoiseUnavailableReason => RnNoiseSampleProvider.UnavailableReason;

    public void SetDenoise(bool enabled, float strengthDb, string mode)
    {
        denoiseEnabled = enabled;
        denoiseStrengthDb = strengthDb;
        denoiseMode = mode;
        ApplyDenoise();
    }

    private void ApplyDenoise()
    {
        bool useAi = denoiseEnabled && denoiseMode == "Ai" && RnNoiseSampleProvider.IsAvailable;
        if (aiDenoise != null)
            aiDenoise.Enabled = useAi;
        if (denoise != null)
        {
            denoise.Enabled = denoiseEnabled && !useAi;
            denoise.ReductionDb = denoiseStrengthDb;
        }
    }

    public void SetCompressor(bool enabled, float amount)
    {
        compEnabled = enabled;
        compAmount = amount;
        if (compressor != null)
        {
            compressor.Enabled = enabled;
            compressor.SetAmount(amount);
        }
    }

    public void SetEqGain(int band, float db)
    {
        if (band < 0 || band >= eqGains.Length) return;
        eqGains[band] = db;
        eq?.SetGain(band, db);
    }

    public void SetEqBands(int count, float[] gains)
    {
        eqBandCount = Math.Clamp(count, EqualizerSampleProvider.MinBands, EqualizerSampleProvider.MaxBands);
        eqGains = new float[eqBandCount];
        Array.Copy(gains, eqGains, Math.Min(gains.Length, eqGains.Length));
        eq?.SetBands(eqBandCount, eqGains);
    }

    public void SetEffect(string name, int intensity)
    {
        effectName = name;
        effectIntensity = intensity;
        ApplyEffect(name, intensity);
    }

    private void ApplyEffect(string name, int intensity)
    {
        if (pitch == null || robot == null || megaphone == null || flanger == null ||
            whisper == null || ghost == null || echo == null)
            return;

        float t = Math.Clamp(intensity, 0, 100) / 100f;
        pitch.PitchFactor = 1f;
        robot.Enabled = false;
        megaphone.Enabled = false;
        flanger.Enabled = false;
        whisper.Enabled = false;
        ghost.Enabled = false;
        echo.Enabled = false;

        switch (name)
        {
            case "Robot":
                robot.Enabled = true;
                robot.Frequency = 25f + 75f * t;
                robot.Mix = 0.9f;
                break;
            case "Female":
                pitch.PitchFactor = 1.2f + 0.5f * t;
                break;
            case "Deep":
                pitch.PitchFactor = 0.85f - 0.35f * t;
                break;
            case "Chipmunk":
                pitch.PitchFactor = 1.4f + 0.6f * t;
                break;
            case "Cave":
                echo.Enabled = true;
                echo.Feedback = 0.15f + 0.45f * t;
                echo.Mix = 0.3f + 0.3f * t;
                break;
            case "Megaphone":
                megaphone.Enabled = true;
                megaphone.Drive = 2f + 6f * t;
                break;
            case "Alien":
                flanger.Enabled = true;
                flanger.Rate = 0.15f + 1.35f * t;
                flanger.Feedback = 0.3f + 0.35f * t;
                break;
            case "Whisper":
                whisper.Enabled = true;
                whisper.NoiseLevel = 0.4f + 0.8f * t;
                break;
            case "Ghost":
                ghost.Enabled = true;
                ghost.Mix = 0.25f + 0.5f * t;
                break;
        }
    }

    // ---------- spectrum ----------

    /// <summary>Copies the newest post-EQ samples for the UI spectrum. False when not running.</summary>
    public bool CopySpectrumSamples(float[] dest)
    {
        var tap = spectrumTap;
        if (!IsRunning || tap == null) return false;
        tap.CopyLatest(dest);
        return true;
    }

    // ---------- soundboard ----------

    /// <summary>Starts a clip into the mic mix. Returns an opaque handle usable with StopClip.</summary>
    public object? PlayClip(string path, float volume, bool loop)
    {
        if (!IsRunning || soundMixer == null) return null;

        var reader = new AudioFileReader(path);
        if (reader.WaveFormat.Channels > 2)
        {
            reader.Dispose();
            throw new NotSupportedException("Only mono/stereo audio files are supported.");
        }

        ISampleProvider clip = reader;
        if (clip.WaveFormat.SampleRate != SampleRate)
            clip = new WdlResamplingSampleProvider(clip, SampleRate);
        if (clip.WaveFormat.Channels == 1)
            clip = new MonoToStereoSampleProvider(clip);
        if (loop)
            clip = new LoopingSampleProvider(clip, reader);
        var tail = new VolumeSampleProvider(clip) { Volume = volume };

        lock (clipLock)
            activeClips.Add((tail, reader));
        soundMixer.AddMixerInput((ISampleProvider)tail);
        return tail;
    }

    /// <summary>Stops one clip previously started with PlayClip.</summary>
    public void StopClip(object handle)
    {
        if (handle is not ISampleProvider tail) return;
        try { soundMixer?.RemoveMixerInput(tail); } catch { }
        lock (clipLock)
        {
            int index = activeClips.FindIndex(c => ReferenceEquals(c.Tail, tail));
            if (index >= 0)
            {
                activeClips[index].Reader.Dispose();
                activeClips.RemoveAt(index);
            }
        }
        ClipEnded?.Invoke(handle);
    }

    private void OnClipEnded(object? sender, SampleProviderEventArgs e)
    {
        lock (clipLock)
        {
            int index = activeClips.FindIndex(c => ReferenceEquals(c.Tail, e.SampleProvider));
            if (index >= 0)
            {
                activeClips[index].Reader.Dispose();
                activeClips.RemoveAt(index);
            }
        }
        ClipEnded?.Invoke(e.SampleProvider);
    }

    public void StopAllClips()
    {
        List<(ISampleProvider Tail, AudioFileReader Reader)> stopped;
        soundMixer?.RemoveAllMixerInputs();
        lock (clipLock)
        {
            stopped = new List<(ISampleProvider, AudioFileReader)>(activeClips);
            foreach (var clip in activeClips)
                clip.Reader.Dispose();
            activeClips.Clear();
        }
        foreach (var clip in stopped)
            ClipEnded?.Invoke(clip.Tail);
    }

    /// <summary>
    /// Plays a clip to the monitor/default device only — never into the mic
    /// mix. Works even when the engine is stopped.
    /// </summary>
    public void PreviewClip(string path, float volume, MMDevice? device)
    {
        StopPreview();
        previewReader = new AudioFileReader(path) { Volume = volume };
        var target = device ?? GetDefaultRender();
        if (target == null)
        {
            previewReader.Dispose();
            previewReader = null;
            throw new InvalidOperationException("No playback device available for preview.");
        }
        previewOutput = new WasapiOut(target, AudioClientShareMode.Shared, true, 100);
        previewOutput.Init(previewReader);
        previewOutput.Play();
    }

    public void StopPreview()
    {
        try { previewOutput?.Stop(); } catch { }
        previewOutput?.Dispose();
        previewOutput = null;
        previewReader?.Dispose();
        previewReader = null;
    }

    private static MMDevice? GetDefaultRender()
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : null;
    }

    // ---------- helpers ----------

    private static float MaxOf(float[] values)
    {
        float max = 0f;
        foreach (var v in values)
            max = Math.Max(max, v);
        return max;
    }

    private static float ToDb(float amplitude) =>
        amplitude <= 1e-5f ? -100f : 20f * MathF.Log10(amplitude);

    /// <summary>
    /// WASAPI shared-mode formats usually arrive as WaveFormatExtensible; the
    /// NAudio sample-provider converters only understand the plain encodings,
    /// so translate while keeping the identical byte layout.
    /// </summary>
    private static WaveFormat NormalizeFormat(WaveFormat format)
    {
        if (format is WaveFormatExtensible ext)
        {
            return ext.SubFormat == IeeeFloatSubFormat
                ? WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels)
                : new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels);
        }
        return format;
    }

    public void Dispose()
    {
        Stop();
        StopPreview();
    }
}

/// <summary>Restarts its file reader whenever the chain runs dry, forever.</summary>
internal class LoopingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly AudioFileReader reader;

    public WaveFormat WaveFormat => source.WaveFormat;

    public LoopingSampleProvider(ISampleProvider source, AudioFileReader reader)
    {
        this.source = source;
        this.reader = reader;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = source.Read(buffer, offset + total, count - total);
            if (n == 0)
            {
                if (reader.Position == 0) break; // empty/unreadable file — don't spin
                reader.Position = 0;
                continue;
            }
            total += n;
        }
        return total;
    }
}
