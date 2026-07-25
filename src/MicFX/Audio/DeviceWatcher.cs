using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MicFX.Audio;

/// <summary>
/// Raises an event whenever the set of audio endpoints changes — a device is
/// plugged in, removed, enabled or disabled. Used to switch automatically to
/// the preferred microphone when it appears (e.g. a VR headset mic showing up
/// when Virtual Desktop connects) and back when it goes away.
///
/// Callbacks arrive on a COM thread; subscribers must marshal to the UI thread.
/// </summary>
public class DeviceWatcher : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();
    private bool registered;

    public event Action? Changed;

    public DeviceWatcher()
    {
        enumerator.RegisterEndpointNotificationCallback(this);
        registered = true;
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Changed?.Invoke();
    public void OnDeviceAdded(string pwstrDeviceId) => Changed?.Invoke();
    public void OnDeviceRemoved(string deviceId) => Changed?.Invoke();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        if (registered)
        {
            try { enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
            registered = false;
        }
        enumerator.Dispose();
    }
}
