using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MicFX.Audio;
using MicFX.Models;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace MicFX;

public class SoundTile
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public float Volume { get; set; } = 1f;
    public int Index { get; set; }
    public string HotkeyLabel => Index < 9 ? $"Ctrl+Alt+{Index + 1}" : "";
}

public partial class MainWindow : Window
{
    private static readonly Dictionary<string, float[]> EqPresets = new()
    {
        ["Flat"] = new float[10],
        ["Warm Broadcast"] = new float[] { 2, 3, 2, 1, -1, 0, 1, 3, 2, 1 },
        ["Bright & Clear"] = new float[] { -2, -1, 0, 0, 1, 2, 3, 4, 3, 2 },
        ["Cut The Mud"] = new float[] { -5, -4, -3, -3, -1, 0, 1, 2, 1, 0 },
        ["Bass Boost"] = new float[] { 5, 4, 3, 1, 0, 0, 0, 0, 0, 0 },
    };

    private readonly AudioEngine engine = new();
    private readonly AppSettings settings = AppSettings.Load();
    private readonly ObservableCollection<SoundTile> sounds = new();
    private readonly List<Slider> eqSliders = new();
    private HotkeyManager? hotkeys;
    private WinForms.NotifyIcon? trayIcon;
    private bool initializing = true;
    private bool reallyExit;
    private bool trayTipShown;

    public MainWindow()
    {
        InitializeComponent();
        BuildEqSliders();
        icSounds.ItemsSource = sounds;
        engine.LevelsAvailable += OnLevels;

        comboEqPreset.Items.Add("Presets…");
        foreach (var name in EqPresets.Keys)
            comboEqPreset.Items.Add(name);
        comboEqPreset.SelectedIndex = 0;

        PopulateDevices();
        ApplySettingsToUi();
        try { chkRunOnBoot.IsChecked = StartupManager.IsEnabled(); } catch { }
        initializing = false;

        // Everything below must not depend on the window being visible —
        // with --minimized (run on boot) the window starts hidden in the tray.
        CreateTrayIcon();
        hotkeys = new HotkeyManager(this);
        hotkeys.SlotPressed += index => Dispatcher.BeginInvoke(() => PlaySlot(index));
        TryAutoStart();

        Closing += (_, e) =>
        {
            if (!reallyExit)
            {
                e.Cancel = true;
                Hide();
                if (!trayTipShown && trayIcon != null)
                {
                    trayIcon.ShowBalloonTip(2500, "MicFX is still running",
                        "Your mic keeps processing in the background. Right-click the tray icon to exit.",
                        WinForms.ToolTipIcon.Info);
                    trayTipShown = true;
                }
                return;
            }
            CollectSettings();
            settings.Save();
            hotkeys?.Dispose();
            engine.Dispose();
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
        };
    }

    private void CreateTrayIcon()
    {
        using var iconStream =
            Application.GetResourceStream(new Uri("pack://application:,,,/Assets/micfx.ico"))!.Stream;
        trayIcon = new WinForms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(iconStream),
            Visible = true,
            Text = "MicFX — mic effects"
        };
        trayIcon.DoubleClick += (_, _) => ShowFromTray();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open MicFX", null, (_, _) => ShowFromTray());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ForceExit());
        trayIcon.ContextMenuStrip = menu;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>Bypasses close-to-tray and really shuts the app down.</summary>
    public void ForceExit()
    {
        reallyExit = true;
        Close();
    }

    // ---------- setup ----------

    private void BuildEqSliders()
    {
        for (int i = 0; i < EqualizerSampleProvider.Frequencies.Length; i++)
        {
            int band = i;
            float freq = EqualizerSampleProvider.Frequencies[i];

            var valueLabel = new TextBlock
            {
                Text = "0 dB",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)FindResource("SubtleTextBrush")
            };
            var freqLabel = new TextBlock
            {
                Text = freq >= 1000 ? $"{freq / 1000:0.#}k" : $"{freq:0}",
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var slider = new Slider
            {
                Orientation = Orientation.Vertical,
                Minimum = -15,
                Maximum = 15,
                Value = 0,
                TickFrequency = 3,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            slider.ValueChanged += (_, e) =>
            {
                valueLabel.Text = $"{e.NewValue:+0.#;-0.#;0} dB";
                if (initializing) return;
                settings.EqGainsDb[band] = (float)e.NewValue;
                engine.SetEqGain(band, (float)e.NewValue);
            };

            var panel = new DockPanel { Margin = new Thickness(4, 0, 4, 0) };
            DockPanel.SetDock(valueLabel, Dock.Top);
            DockPanel.SetDock(freqLabel, Dock.Bottom);
            panel.Children.Add(valueLabel);
            panel.Children.Add(freqLabel);
            panel.Children.Add(slider);

            eqPanel.Children.Add(panel);
            eqSliders.Add(slider);
        }
    }

    private void PopulateDevices()
    {
        bool wasInitializing = initializing;
        initializing = true;

        var selectedMic = (comboMic.SelectedItem as AudioDeviceInfo)?.Id ?? settings.InputDeviceId;
        var selectedOut = (comboOutput.SelectedItem as AudioDeviceInfo)?.Id ?? settings.OutputDeviceId;
        var selectedMon = (comboMonitor.SelectedItem as AudioDeviceInfo)?.Id
                          ?? settings.MonitorDeviceId
                          ?? AudioDevices.GetDefaultRenderDeviceId();

        comboMic.ItemsSource = AudioDevices.GetCaptureDevices();
        var renderDevices = AudioDevices.GetRenderDevices();
        comboOutput.ItemsSource = renderDevices;
        comboMonitor.ItemsSource = renderDevices.ToList();

        SelectById(comboMic, selectedMic);
        SelectById(comboOutput, selectedOut);
        SelectById(comboMonitor, selectedMon);

        initializing = wasInitializing;
    }

    private static void SelectById(ComboBox combo, string? id)
    {
        if (id == null) return;
        foreach (AudioDeviceInfo item in combo.Items)
        {
            if (item.Id == id)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private void ApplySettingsToUi()
    {
        sliderMicGain.Value = settings.MicGain;
        sliderMaster.Value = settings.MasterVolume;
        sliderSoundVol.Value = settings.SoundboardVolume;
        chkGate.IsChecked = settings.GateEnabled;
        sliderGate.Value = settings.GateThresholdDb;
        chkMonitor.IsChecked = settings.MonitorEnabled;
        sliderIntensity.Value = settings.EffectIntensity;

        for (int i = 0; i < eqSliders.Count && i < settings.EqGainsDb.Length; i++)
            eqSliders[i].Value = settings.EqGainsDb[i];

        foreach (ListBoxItem item in lstEffects.Items)
        {
            if ((string)item.Tag == settings.Effect)
            {
                item.IsSelected = true;
                break;
            }
        }

        foreach (var clip in settings.Sounds)
            sounds.Add(new SoundTile { Name = clip.Name, Path = clip.Path, Volume = clip.Volume });
        RefreshTileIndexes();

        // Push restored values into the engine so they apply on Start().
        engine.SetMicGain(settings.MicGain);
        engine.SetMasterVolume(settings.MasterVolume);
        engine.SetSoundboardVolume(settings.SoundboardVolume);
        engine.SetGate(settings.GateEnabled, settings.GateThresholdDb);
        for (int i = 0; i < settings.EqGainsDb.Length && i < eqSliders.Count; i++)
            engine.SetEqGain(i, settings.EqGainsDb[i]);
        engine.SetEffect(settings.Effect, settings.EffectIntensity);
    }

    private void CollectSettings()
    {
        settings.InputDeviceId = (comboMic.SelectedItem as AudioDeviceInfo)?.Id;
        settings.OutputDeviceId = (comboOutput.SelectedItem as AudioDeviceInfo)?.Id;
        settings.MonitorDeviceId = (comboMonitor.SelectedItem as AudioDeviceInfo)?.Id;
        settings.MonitorEnabled = chkMonitor.IsChecked == true;
        settings.MicGain = (float)sliderMicGain.Value;
        settings.MasterVolume = (float)sliderMaster.Value;
        settings.SoundboardVolume = (float)sliderSoundVol.Value;
        settings.GateEnabled = chkGate.IsChecked == true;
        settings.GateThresholdDb = (float)sliderGate.Value;
        settings.Effect = CurrentEffectTag();
        settings.EffectIntensity = (int)sliderIntensity.Value;
        settings.Sounds = sounds
            .Select(s => new SoundClipSetting { Name = s.Name, Path = s.Path, Volume = s.Volume })
            .ToList();
    }

    // ---------- engine control ----------

    private void TryAutoStart()
    {
        if (comboMic.SelectedItem != null && comboOutput.SelectedItem != null)
            StartEngine();
    }

    private void StartEngine()
    {
        if (comboMic.SelectedItem is not AudioDeviceInfo mic ||
            comboOutput.SelectedItem is not AudioDeviceInfo render)
        {
            txtStatus.Text = "Select a microphone and an output device first.";
            return;
        }

        try
        {
            engine.Start(AudioDevices.GetDevice(mic.Id), AudioDevices.GetDevice(render.Id));
            ApplyMonitor();
            btnStartStop.Content = "Stop";
            txtStatus.Text = $"Running — {mic.Name}  →  {render.Name}";
        }
        catch (Exception ex)
        {
            engine.Stop();
            btnStartStop.Content = "Start";
            txtStatus.Text = "Error: " + ex.Message;
        }
    }

    private void StopEngine()
    {
        engine.Stop();
        btnStartStop.Content = "Start";
        txtStatus.Text = "Stopped";
        meterIn.Value = 0;
        meterOut.Value = 0;
    }

    private void RestartIfRunning()
    {
        if (engine.IsRunning) StartEngine();
    }

    private void ApplyMonitor()
    {
        if (!engine.IsRunning) return;
        try
        {
            var device = comboMonitor.SelectedItem is AudioDeviceInfo info
                ? AudioDevices.GetDevice(info.Id)
                : null;
            engine.SetMonitor(device, chkMonitor.IsChecked == true);
        }
        catch (Exception ex)
        {
            txtStatus.Text = "Monitor error: " + ex.Message;
        }
    }

    private void OnLevels(float inputDb, float outputDb)
    {
        Dispatcher.BeginInvoke(() =>
        {
            meterIn.Value = DbToPercent(inputDb);
            meterOut.Value = DbToPercent(outputDb);
        });
    }

    private static double DbToPercent(float db) => Math.Clamp((db + 60) / 60 * 100, 0, 100);

    private string CurrentEffectTag() =>
        (lstEffects.SelectedItem as ListBoxItem)?.Tag as string ?? "None";

    // ---------- UI event handlers ----------

    private void StartStop_Click(object sender, RoutedEventArgs e)
    {
        if (engine.IsRunning) StopEngine();
        else StartEngine();
    }

    private void RefreshDevices_Click(object sender, RoutedEventArgs e) => PopulateDevices();

    private void RunOnBoot_Changed(object sender, RoutedEventArgs e)
    {
        if (initializing) return;
        try
        {
            StartupManager.SetEnabled(chkRunOnBoot.IsChecked == true);
            txtStatus.Text = chkRunOnBoot.IsChecked == true
                ? "MicFX will start minimized when you sign in to Windows."
                : "Removed from Windows startup.";
        }
        catch (Exception ex)
        {
            txtStatus.Text = "Startup setting error: " + ex.Message;
        }
    }

    private void Device_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (initializing) return;
        RestartIfRunning();
    }

    private void Monitor_Changed(object sender, RoutedEventArgs e)
    {
        if (initializing) return;
        ApplyMonitor();
    }

    private void MicGain_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (lblMicGain != null) lblMicGain.Text = $"{e.NewValue * 100:0}%";
        if (initializing) return;
        engine.SetMicGain((float)e.NewValue);
    }

    private void Master_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (lblMaster != null) lblMaster.Text = $"{e.NewValue * 100:0}%";
        if (initializing) return;
        engine.SetMasterVolume((float)e.NewValue);
    }

    private void SoundVol_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (initializing) return;
        engine.SetSoundboardVolume((float)e.NewValue);
    }

    private void Gate_Changed(object sender, RoutedEventArgs e)
    {
        if (lblGate != null) lblGate.Text = $"{sliderGate.Value:0} dB";
        if (initializing) return;
        engine.SetGate(chkGate.IsChecked == true, (float)sliderGate.Value);
    }

    private void Effect_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (initializing) return;
        engine.SetEffect(CurrentEffectTag(), (int)sliderIntensity.Value);
    }

    private void Intensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (lblIntensity != null) lblIntensity.Text = $"{e.NewValue:0}";
        if (initializing) return;
        engine.SetEffect(CurrentEffectTag(), (int)e.NewValue);
    }

    private void EqPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (initializing) return;
        if (comboEqPreset.SelectedItem is not string name || !EqPresets.TryGetValue(name, out var gains))
            return;
        for (int i = 0; i < eqSliders.Count && i < gains.Length; i++)
            eqSliders[i].Value = gains[i];
    }

    private void EqReset_Click(object sender, RoutedEventArgs e)
    {
        foreach (var slider in eqSliders)
            slider.Value = 0;
        comboEqPreset.SelectedIndex = 0;
    }

    // ---------- soundboard ----------

    private void AddSound_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Audio files|*.wav;*.mp3;*.m4a;*.wma;*.aiff;*.aif|All files|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true) return;

        foreach (var path in dialog.FileNames)
        {
            sounds.Add(new SoundTile
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(path),
                Path = path
            });
        }
        RefreshTileIndexes();
    }

    private void RemoveSound_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is SoundTile tile)
        {
            sounds.Remove(tile);
            RefreshTileIndexes();
        }
    }

    private void SoundTile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is SoundTile tile)
            PlayTile(tile);
    }

    private void StopAll_Click(object sender, RoutedEventArgs e) => engine.StopAllClips();

    private void PlaySlot(int index)
    {
        if (index >= 0 && index < sounds.Count)
            PlayTile(sounds[index]);
    }

    private void PlayTile(SoundTile tile)
    {
        if (!engine.IsRunning)
        {
            txtStatus.Text = "Start the engine to play sounds.";
            return;
        }
        if (!File.Exists(tile.Path))
        {
            txtStatus.Text = $"File not found: {tile.Path}";
            return;
        }
        try
        {
            engine.PlayClip(tile.Path, tile.Volume);
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Can't play \"{tile.Name}\": {ex.Message}";
        }
    }

    private void RefreshTileIndexes()
    {
        for (int i = 0; i < sounds.Count; i++)
            sounds[i].Index = i;
        icSounds.Items.Refresh();
    }
}
