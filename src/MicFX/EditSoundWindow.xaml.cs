using System.Windows;
using System.Windows.Input;

namespace MicFX;

/// <summary>
/// Per-clip editor: name, volume, loop, custom global hotkey, preview.
/// Hotkeys require a modifier (Ctrl/Alt/Shift/Win) unless an F-key is used,
/// so a bare letter can never be hijacked system-wide.
/// </summary>
public partial class EditSoundWindow : Window
{
    public string SoundName => txtName.Text.Trim();
    public float ClipVolume => (float)sliderVolume.Value;
    public bool Loop => chkLoop.IsChecked == true;
    public uint HotkeyMods { get; private set; }
    public uint HotkeyVk { get; private set; }
    public string? HotkeyText { get; private set; }

    private readonly Action<float>? preview;

    public EditSoundWindow(Window owner, string name, float volume, bool loop,
        uint hotkeyMods, uint hotkeyVk, string? hotkeyText, Action<float>? preview)
    {
        InitializeComponent();
        Owner = owner;
        this.preview = preview;
        txtName.Text = name;
        sliderVolume.Value = volume;
        chkLoop.IsChecked = loop;
        HotkeyMods = hotkeyMods;
        HotkeyVk = hotkeyVk;
        HotkeyText = hotkeyText;
        txtHotkey.Text = hotkeyText ?? "(default)";
    }

    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (lblVolume != null) lblVolume.Text = $"{e.NewValue * 100:0}%";
    }

    private void Hotkey_KeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.Escape or Key.Back or Key.Delete)
        {
            HotkeyMods = 0;
            HotkeyVk = 0;
            HotkeyText = null;
            txtHotkey.Text = "(default)";
            lblHotkeyHint.Text = "";
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return; // modifier alone — wait for the real key

        var mods = Keyboard.Modifiers;
        bool isFKey = key >= Key.F1 && key <= Key.F24;
        if (mods == ModifierKeys.None && !isFKey)
        {
            lblHotkeyHint.Text = "Combine with Ctrl/Alt/Shift, or use an F-key.";
            return;
        }

        uint winMods = 0;
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) { winMods |= HotkeyManager.ModControl; parts.Add("Ctrl"); }
        if (mods.HasFlag(ModifierKeys.Alt)) { winMods |= HotkeyManager.ModAlt; parts.Add("Alt"); }
        if (mods.HasFlag(ModifierKeys.Shift)) { winMods |= HotkeyManager.ModShift; parts.Add("Shift"); }
        if (mods.HasFlag(ModifierKeys.Windows)) { winMods |= HotkeyManager.ModWin; parts.Add("Win"); }
        parts.Add(key.ToString());

        HotkeyMods = winMods;
        HotkeyVk = (uint)KeyInterop.VirtualKeyFromKey(key);
        HotkeyText = string.Join("+", parts);
        txtHotkey.Text = HotkeyText;
        lblHotkeyHint.Text = "";
    }

    private void Preview_Click(object sender, RoutedEventArgs e) => preview?.Invoke(ClipVolume);

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (SoundName.Length == 0) return;
        DialogResult = true;
    }
}
