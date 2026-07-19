using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicFX.Audio;

/// <summary>
/// Owns the whole real-time graph:
///
///   mic capture → resample → noise suppression → (mono→stereo) → gain → meter
///     → gate → EQ → spectrum tap → compressor
///     → pitch → ring-mod → megaphone → flanger → whisper → ghost → echo ─┐
///                                                                        ├→ master → meter → tee → output
///   soundboard clips → sub-mixer → soundboard volume ─────────────────────┘      (tee feeds the monitor output)
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
    private BufferedWaveProvider? monitorBuffer;
    private MixingSampleProvider? soundMixer;

    private VolumeSampleProvider? micVolume;
    private VolumeSampleProvider? soundboardVolume;
    private VolumeSampleProvider? masterVolume;
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
    private TeeSampleProvider? tee;

    private readonly List<(ISampleProvider Tail, AudioFileReader Reader)> activeClips = new();
    private readonly object clipLock = new();

    // Last known parameters, applied on the next Start() when the graph is down.
    private float micGain = 1f;
    private float master = 1f;
    private float soundVol = 0.8f;
    private readonly float[] eqGains = new float[EqualizerSampleProvider.Frequencies.Length];
    private bool gateEnabled;
    private float gateThresholdDb = -45f;
    private bool denoiseEnabled;
    private float denoiseStrengthDb = 18f;
    private bool compEnabled;
    private float compAmount = 50f;
    private string effectName = "None";
    private int effectIntensity = 50;

    private float lastInputDb = -100f;

    public bool IsRunning { get; private set; }

    /// <summary>Raised from the audio thread with (input dB, output dB) roughly 20×/second.</summary>
    public event Action<float, float>? LevelsAvailable;

    /// <summary>Raised (from the audio thread) when a soundboard clip finishes or is stopped.</summary>
    public event Action<object>? ClipEnded;

    public void Start(MMDevice input, MMDevice render)
    {
        Stop();

        capture = new WasapiCapture(input, true, 20);
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

        ISampleProvider mic = micBuffer.ToSampleProvider();
        if (mic.WaveFormat.SampleRate != SampleRate)
            mic = new WdlResamplingSampleProvider(mic, SampleRate);
        denoise = new NoiseSuppressionSampleProvider(mic)
        {
            Enabled = denoiseEnabled,
            ReductionDb = denoiseStrengthDb
        };
        mic = denoise;
        if (mic.WaveFormat.Channels == 1)
            mic = new MonoToStereoSampleProvider(mic);

        micVolume = new VolumeSampleProvider(mic) { Volume = micGain };
        var inputMeter = new MeteringSampleProvider(micVolume, SampleRate / 20);
        inputMeter.StreamVolume += (_, e) => lastInputDb = ToDb(MaxOf(e.MaxSampleValues));

        gate = new NoiseGateSampleProvider(inputMeter) { Enabled = gateEnabled, ThresholdDb = gateThresholdDb };
        eq = new EqualizerSampleProvider(gate);
        for (int band = 0; band < eqGains.Length; band++)
            eq.SetGain(band, eqGains[band]);
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

        var mainMixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels))
        {
            ReadFully = true
        };
        mainMixer.AddMixerInput((ISampleProvider)echo);
        mainMixer.AddMixerInput((ISampleProvider)soundboardVolume);

        masterVolume = new VolumeSampleProvider(mainMixer) { Volume = master };
        var outputMeter = new MeteringSampleProvider(masterVolume, SampleRate / 20);
        outputMeter.StreamVolume += (_, e) =>
            LevelsAvailable?.Invoke(lastInputDb, ToDb(MaxOf(e.MaxSampleValues)));

        monitorBuffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels))
        {
            BufferDuration = TimeSpan.FromSeconds(2),
            DiscardOnBufferOverflow = true
        };
        tee = new TeeSampleProvider(outputMeter) { Sink = monitorBuffer, Enabled = false };

        ApplyEffect(effectName, effectIntensity);

        output = new WasapiOut(render, AudioClientShareMode.Shared, true, 50);
        output.Init(new SampleToWaveProvider(tee));

        capture.StartRecording();
        output.Play();
        IsRunning = true;
    }

    /// <summary>Enable/disable self-monitoring on the given render device. Safe to call any time while running.</summary>
    public void SetMonitor(MMDevice? device, bool enabled)
    {
        if (tee != null) tee.Enabled = false;
        try { monitorOutput?.Stop(); } catch { /* device may already be gone */ }
        monitorOutput?.Dispose();
        monitorOutput = null;
        monitorBuffer?.ClearBuffer();

        if (!IsRunning || !enabled || device == null || tee == null || monitorBuffer == null)
            return;

        monitorOutput = new WasapiOut(device, AudioClientShareMode.Shared, true, 50);
        monitorOutput.Init(monitorBuffer);
        monitorOutput.Play();
        tee.Enabled = true;
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
        monitorBuffer = null;
        soundMixer = null;
        micVolume = null;
        soundboardVolume = null;
        masterVolume = null;
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
        tee = null;
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

    public void SetDenoise(bool enabled, float strengthDb)
    {
        denoiseEnabled = enabled;
        denoiseStrengthDb = strengthDb;
        if (denoise != null)
        {
            denoise.Enabled = enabled;
            denoise.ReductionDb = strengthDb;
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
        eqGains[band] = db;
        eq?.SetGain(band, db);
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
