using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MicFX;

/// <summary>
/// Registers global hotkeys Ctrl+Alt+1…9 for the first nine soundboard slots.
/// Registration failures (another app owns the combo) are silently skipped.
/// </summary>
public class HotkeyManager : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const int WmHotkey = 0x0312;
    private const int BaseId = 0x4D46;
    private const int SlotCount = 9;

    private readonly IntPtr handle;
    private readonly HwndSource hwndSource;
    private readonly List<int> registered = new();

    /// <summary>Raised with the 0-based soundboard slot index.</summary>
    public event Action<int>? SlotPressed;

    public HotkeyManager(Window window)
    {
        handle = new WindowInteropHelper(window).EnsureHandle();
        hwndSource = HwndSource.FromHwnd(handle)!;
        hwndSource.AddHook(WndProc);

        for (int slot = 0; slot < SlotCount; slot++)
        {
            uint vk = (uint)('1' + slot);
            if (RegisterHotKey(handle, BaseId + slot, ModControl | ModAlt, vk))
                registered.Add(BaseId + slot);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            int id = wParam.ToInt32();
            if (id >= BaseId && id < BaseId + SlotCount)
            {
                SlotPressed?.Invoke(id - BaseId);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (int id in registered)
            UnregisterHotKey(handle, id);
        registered.Clear();
        hwndSource.RemoveHook(WndProc);
    }
}
