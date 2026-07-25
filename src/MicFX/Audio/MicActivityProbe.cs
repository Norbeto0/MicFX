using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MicFX.Audio;

/// <summary>
/// Watches a capture device for actual audio, without routing it anywhere.
///
/// Presence is not a usable signal for virtual microphones: Virtual Desktop's
/// mic (and similar) stays "Ready" in Windows whether or not the headset is
/// connected. What does change is the content — an idle virtual device emits
/// digital silence (WASAPI even flags the buffer as silent), while any live
/// microphone carries at least a noise floor. So the probe simply reports when
/// it last saw a non-zero sample.
/// </summary>
public sealed class MicActivityProbe : IDisposable
{
    private WasapiCapture? capture;
    private DateTime lastSoundUtc = DateTime.MinValue;

    /// <summary>Device currently being watched, or null.</summary>
    public string? DeviceId { get; private set; }

    /// <summary>Set when the device could not be opened (busy, exclusive-mode holder, …).</summary>
    public bool Failed { get; private set; }

    public bool HasSoundWithin(TimeSpan window) => DateTime.UtcNow - lastSoundUtc <= window;

    public void Watch(MMDevice device)
    {
        if (DeviceId == device.ID && capture != null) return;
        Stop();
        try
        {
            capture = new WasapiCapture(device, false, 50);
            capture.DataAvailable += OnData;
            capture.StartRecording();
            DeviceId = device.ID;
            Failed = false;
        }
        catch
        {
            Stop();
            Failed = true;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (!IsDigitalSilence(e.Buffer, e.BytesRecorded))
            lastSoundUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// True when every byte is zero. Works for any PCM/float format, since
    /// digital silence is all-zero bytes in each of them.
    /// </summary>
    public static bool IsDigitalSilence(byte[] buffer, int count)
    {
        for (int i = 0; i < count; i++)
            if (buffer[i] != 0) return false;
        return true;
    }

    public void Stop()
    {
        if (capture != null)
        {
            capture.DataAvailable -= OnData;
            try { capture.StopRecording(); } catch { }
            capture.Dispose();
            capture = null;
        }
        DeviceId = null;
        lastSoundUtc = DateTime.MinValue;
    }

    public void Dispose() => Stop();
}
