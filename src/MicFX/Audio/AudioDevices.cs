using NAudio.CoreAudioApi;

namespace MicFX.Audio;

public record AudioDeviceInfo(string Id, string Name)
{
    public override string ToString() => Name;
}

public static class AudioDevices
{
    public static List<AudioDeviceInfo> GetCaptureDevices() => Enumerate(DataFlow.Capture);
    public static List<AudioDeviceInfo> GetRenderDevices() => Enumerate(DataFlow.Render);

    private static List<AudioDeviceInfo> Enumerate(DataFlow flow)
    {
        var enumerator = new MMDeviceEnumerator();
        var list = new List<AudioDeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            list.Add(new AudioDeviceInfo(device.ID, device.FriendlyName));
        return list;
    }

    public static MMDevice GetDevice(string id)
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDevice(id);
    }

    public static string? GetDefaultRenderDeviceId()
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID
            : null;
    }

    public static string? GetDefaultCaptureDeviceId()
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia).ID
            : null;
    }
}
