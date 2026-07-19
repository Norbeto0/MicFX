using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Pass-through provider that copies everything it reads into a
/// BufferedWaveProvider sink — used to feed the self-monitoring output
/// without disturbing the main stream.
/// </summary>
public class TeeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private byte[] byteBuffer = Array.Empty<byte>();

    public BufferedWaveProvider? Sink { get; set; }
    public bool Enabled { get; set; }
    public WaveFormat WaveFormat => source.WaveFormat;

    public TeeSampleProvider(ISampleProvider source) => this.source = source;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        var sink = Sink;
        if (Enabled && sink != null && read > 0)
        {
            int bytes = read * sizeof(float);
            if (byteBuffer.Length < bytes) byteBuffer = new byte[bytes];
            Buffer.BlockCopy(buffer, offset * sizeof(float), byteBuffer, 0, bytes);
            sink.AddSamples(byteBuffer, 0, bytes);
        }
        return read;
    }
}
