using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MicFX.Audio;

/// <summary>
/// Integrated loudness per ITU-R BS.1770 (the LUFS measure used by EBU R128 and
/// streaming platforms), used to even out soundboard clip volumes.
///
/// Signal is K-weighted (a high-shelf pre-filter and a high-pass), squared and
/// summed over channels in 100 ms sub-blocks; 400 ms blocks with 75% overlap
/// are built from four consecutive sub-blocks. Blocks below -70 LUFS are
/// dropped (absolute gate), then blocks more than 10 LU below the level of the
/// remaining ones (relative gate). Streaming, so long files stay cheap.
/// Expects interleaved stereo at 48 kHz.
/// </summary>
public sealed class LoudnessMeter
{
    public const int SampleRate = 48000;
    private const int SubBlock = SampleRate / 10; // 100 ms

    /// <summary>Loudness soundboard clips are brought to.</summary>
    public const double TargetLufs = -20.0;

    /// <summary>Quiet clips are raised at most this much, so noise in them isn't blown up.</summary>
    public const double MaxBoostDb = 12.0;

    private readonly Biquad[] shelf = { Biquad.Shelf(), Biquad.Shelf() };
    private readonly Biquad[] highPass = { Biquad.HighPass(), Biquad.HighPass() };
    private readonly List<double> subBlocks = new();
    private double acc;
    private int accFrames;

    public void Add(float[] interleavedStereo, int count)
    {
        for (int i = 0; i + 1 < count; i += 2)
        {
            double l = highPass[0].Process(shelf[0].Process(interleavedStereo[i]));
            double r = highPass[1].Process(shelf[1].Process(interleavedStereo[i + 1]));
            acc += l * l + r * r;
            if (++accFrames == SubBlock)
            {
                subBlocks.Add(acc);
                acc = 0;
                accFrames = 0;
            }
        }
    }

    /// <summary>Gated integrated loudness in LUFS, or null for silence.</summary>
    public double? IntegratedLufs()
    {
        // Mean square summed over channels, per 400 ms block.
        var blocks = new List<double>();
        if (subBlocks.Count >= 4)
        {
            for (int j = 0; j + 4 <= subBlocks.Count; j++)
                blocks.Add((subBlocks[j] + subBlocks[j + 1] + subBlocks[j + 2] + subBlocks[j + 3]) / (4.0 * SubBlock));
        }
        else
        {
            // Shorter than one block (common for sound effects): the standard
            // is undefined here, so measure the whole clip as a single block.
            int frames = subBlocks.Count * SubBlock + accFrames;
            if (frames == 0) return null;
            blocks.Add((subBlocks.Sum() + acc) / frames);
        }

        var absolute = blocks.Where(z => Lufs(z) > -70.0).ToList();
        if (absolute.Count == 0) return null;
        double relativeGate = Lufs(absolute.Average()) - 10.0;
        var gated = absolute.Where(z => Lufs(z) > relativeGate).ToList();
        return gated.Count == 0 ? null : Lufs(gated.Average());
    }

    private static double Lufs(double meanSquareSum) => -0.691 + 10.0 * Math.Log10(meanSquareSum + 1e-30);

    /// <summary>Linear gain that brings a clip of the given loudness to <see cref="TargetLufs"/>.</summary>
    public static float NormalizationGain(double lufs) =>
        (float)Math.Pow(10.0, Math.Min(TargetLufs - lufs, MaxBoostDb) / 20.0);

    /// <summary>
    /// Measures a file as it will be played: resampled to 48 kHz, mono
    /// duplicated to both channels. Null when unreadable, silent or not mono/stereo.
    /// </summary>
    public static double? MeasureFile(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            if (reader.WaveFormat.Channels > 2) return null;
            ISampleProvider p = reader;
            if (p.WaveFormat.SampleRate != SampleRate) p = new WdlResamplingSampleProvider(p, SampleRate);
            if (p.WaveFormat.Channels == 1) p = new MonoToStereoSampleProvider(p);

            var meter = new LoudnessMeter();
            var buffer = new float[SampleRate]; // 0.5 s stereo
            int n;
            while ((n = p.Read(buffer, 0, buffer.Length)) > 0)
                meter.Add(buffer, n);
            return meter.IntegratedLufs();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Direct-form I biquad in double precision (K-weighting coefficients are for 48 kHz).</summary>
    private sealed class Biquad
    {
        private readonly double b0, b1, b2, a1, a2;
        private double x1, x2, y1, y2;

        private Biquad(double b0, double b1, double b2, double a1, double a2)
        {
            this.b0 = b0; this.b1 = b1; this.b2 = b2; this.a1 = a1; this.a2 = a2;
        }

        // ITU-R BS.1770-4, stage 1: head-related high-shelf pre-filter.
        public static Biquad Shelf() => new(1.53512485958697, -2.69169618940638, 1.19839281085285,
                                             -1.69065929318241, 0.73248077421585);

        // Stage 2: RLB high-pass.
        public static Biquad HighPass() => new(1.0, -2.0, 1.0, -1.99004745483398, 0.99007225036621);

        public double Process(double x)
        {
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x;
            y2 = y1; y1 = y;
            return y;
        }
    }
}
