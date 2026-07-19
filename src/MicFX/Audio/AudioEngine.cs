using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicFX.Audio;

/// <summary>
/// Owns the whole real-time graph:
///
///   mic capture → gain → meter → gate → EQ → pitch → ring-mod → echo ─┐
///                                                                     ├→ master → meter → tee → output
///   soundboard clips → sub-mixer → soundboard volume ─────────────────┘         (tee feeds the monitor output)
///
/// Everything runs at 48 kHz stereo IEEE float; the mic is resampled/up-mixed
/// as needed. Parameter setters are safe to call from the UI thread while the
/// graph is running, and are remembered so a later Start() picks them up.
/// </summary>
public class AudioEngine : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;

    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private WasapiCapture? capture;
    private WasapiOut? output;
    private WasapiOut? monitorOutput;

    private BufferedWaveProvider? micBuffer;
    private BufferedWaveProvider? monitorBuffer;
    private MixingSampleProvider? soundMixer;

    private VolumeSampleProvider? micVolume;
    private VolumeSampleProvider? soundboardVolume;
    private VolumeSampleProvider? masterVolume;
    private NoiseGateSampleProvider? gate;
    private EqualizerSampleProvider? eq;
    private SmbPitchShiftingSampleProvider? pitch;
    private RingModulatorSampleProvider? robot;
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
    private string effectName = "None";
    private int effectIntensity = 50;

    private float lastInputDb = -100f;

    public bool IsRunning { get; private set; }

    /// <summary>Raised from the audio thread with (input dB, output dB) roughly 20×/second.</summary>
    public event Action<float, float>? LevelsAvailable;

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
        if (mic.WaveFormat.Channels == 1)
            mic = new MonoToStereoSampleProvider(mic);

        micVolume = new VolumeSampleProvider(mic) { Volume = micGain };
        var inputMeter = new MeteringSampleProvider(micVolume, SampleRate / 20);
        inputMeter.StreamVolume += (_, e) => lastInputDb = ToDb(MaxOf(e.MaxSampleValues));

        gate = new NoiseGateSampleProvider(inputMeter) { Enabled = gateEnabled, ThresholdDb = gateThresholdDb };
        eq = new EqualizerSampleProvider(gate);
        for (int band = 0; band < eqGains.Length; band++)
            eq.SetGain(band, eqGains[band]);
        pitch = new SmbPitchShiftingSampleProvider(eq, 2048, 4, 1f);
        robot = new RingModulatorSampleProvider(pitch);
        echo = new EchoSampleProvider(robot);

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
        mainMixer.AddMixerInput(echo);
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
        gate = null;
        eq = null;
        pitch = null;
        robot = null;
        echo = null;
        tee = null;
    }

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
        if (pitch == null || robot == null || echo == null) return;

        float t = Math.Clamp(intensity, 0, 100) / 100f;
        pitch.PitchFactor = 1f;
        robot.Enabled = false;
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
        }
    }

    public void PlayClip(string path, float volume)
    {
        if (!IsRunning || soundMixer == null) return;

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
        var tail = new VolumeSampleProvider(clip) { Volume = volume };

        lock (clipLock)
            activeClips.Add((tail, reader));
        soundMixer.AddMixerInput((ISampleProvider)tail);
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
    }

    public void StopAllClips()
    {
        soundMixer?.RemoveAllMixerInputs();
        lock (clipLock)
        {
            foreach (var clip in activeClips)
                clip.Reader.Dispose();
            activeClips.Clear();
        }
    }

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

    public void Dispose() => Stop();
}
