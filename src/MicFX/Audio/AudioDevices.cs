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

    /// <summary>True when the render endpoint exists and is currently usable (plugged in and enabled).</summary>
    public static bool IsActiveRender(string id)
    {
        try
        {
            using var device = GetDevice(id);
            return device.State == DeviceState.Active;
        }
        catch
        {
            return false; // unknown id: the device was removed from the system
        }
    }

    public const string NotConnectedSuffix = " (not connected)";

    /// <summary>
    /// The device list for a picker that must keep a remembered choice while
    /// that device is switched off or unplugged: when <paramref name="id"/> is
    /// not among <paramref name="devices"/>, a placeholder entry with the same
    /// id is appended, so the selection (and the saved setting) survives until
    /// the device comes back.
    /// </summary>
    public static List<AudioDeviceInfo> WithRemembered(IEnumerable<AudioDeviceInfo> devices, string? id, string? name)
    {
        var list = devices.ToList();
        if (!string.IsNullOrEmpty(id) && list.All(d => d.Id != id))
            list.Add(new AudioDeviceInfo(id, (name ?? "remembered device") + NotConnectedSuffix));
        return list;
    }

    public static string? GetDefaultRenderDeviceId()
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID
            : null;
    }

    /// <summary>
    /// Picks the capture device to run with. The preferred device is used only
    /// when it is both available and actually carrying audio — virtual
    /// microphones stay present in Windows even when nothing is streaming
    /// through them, so presence alone is not a usable signal.
    /// Returns null when neither device is available.
    /// </summary>
    public static string? ChooseInput(string? preferredId, string? primaryId,
        IReadOnlyCollection<string> availableIds, bool preferredHasSound = true)
    {
        if (preferredHasSound && !string.IsNullOrEmpty(preferredId) && availableIds.Contains(preferredId))
            return preferredId;
        if (!string.IsNullOrEmpty(primaryId) && availableIds.Contains(primaryId)) return primaryId;
        // The preferred device is all that is left: better a silent mic than none.
        if (!string.IsNullOrEmpty(preferredId) && availableIds.Contains(preferredId)) return preferredId;
        return null;
    }

    /// <summary>
    /// True when a capture device and a render device are the two ends of the
    /// same virtual cable — capturing from it while writing to it would feed
    /// the output straight back into the input. VB-Audio names both endpoints
    /// after the same adapter ("CABLE Output (VB-Audio Virtual Cable)" and
    /// "CABLE Input (VB-Audio Virtual Cable)"), so a shared adapter name that
    /// mentions a cable identifies the loop. A headset whose mic and speakers
    /// share an adapter name is not matched, since that is a legitimate setup.
    /// </summary>
    public static bool IsVirtualCableLoop(string inputName, string outputName)
    {
        string adapter = Adapter(inputName);
        return adapter.Length > 0
            && adapter.Equals(Adapter(outputName), StringComparison.OrdinalIgnoreCase)
            && adapter.Contains("cable", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The parenthesised adapter part of a Windows endpoint name.</summary>
    private static string Adapter(string name)
    {
        int open = name.LastIndexOf('(');
        int close = name.LastIndexOf(')');
        return open >= 0 && close > open ? name[(open + 1)..close].Trim() : "";
    }

    public static string? GetDefaultCaptureDeviceId()
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia).ID
            : null;
    }
}
