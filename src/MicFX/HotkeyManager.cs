using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MicFX;

/// <summary>
/// Registers arbitrary global hotkeys against the main window's handle.
/// Registration failures (another app owns the combo) return false and are
/// otherwise ignored.
/// </summary>
public class HotkeyManager : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    private const int WmHotkey = 0x0312;
    private const int BaseId = 0x4D46;

    private readonly IntPtr handle;
    private readonly HwndSource hwndSource;
    private readonly HashSet<int> registered = new();

    /// <summary>Raised with the caller-supplied hotkey id.</summary>
    public event Action<int>? HotkeyPressed;

    public HotkeyManager(Window window)
    {
        handle = new WindowInteropHelper(window).EnsureHandle();
        hwndSource = HwndSource.FromHwnd(handle)!;
        hwndSource.AddHook(WndProc);
    }

    public bool Register(int id, uint modifiers, uint vk)
    {
        Unregister(id);
        if (!RegisterHotKey(handle, BaseId + id, modifiers, vk)) return false;
        registered.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (registered.Remove(id))
            UnregisterHotKey(handle, BaseId + id);
    }

    public void Clear()
    {
        foreach (int id in registered)
            UnregisterHotKey(handle, BaseId + id);
        registered.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            int id = wParam.ToInt32() - BaseId;
            if (registered.Contains(id))
            {
                HotkeyPressed?.Invoke(id);
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        hwndSource.RemoveHook(WndProc);
    }
}
