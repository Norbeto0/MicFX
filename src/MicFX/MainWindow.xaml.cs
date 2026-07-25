using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MicFX.Audio;
using MicFX.Models;
using Microsoft.Win32;
using NAudio.Dsp;
using WinForms = System.Windows.Forms;

namespace MicFX;

public class SoundTile
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public float Volume { get; set; } = 1f;
    public bool Loop { get; set; }
    public uint HotkeyMods { get; set; }
    public uint HotkeyVk { get; set; }
    public string? HotkeyText { get; set; }
    public int Index { get; set; }
    public bool IsLooping { get; set; }

    public string StatusLabel =>
        IsLooping ? "⟳ looping — click to stop"
        : HotkeyText ?? (Index < 9 ? $"Ctrl+Alt+{Index + 1}" : "");
}

public partial class MainWindow : Window
{
    private const string RepoSlug = "Norbeto0/MicFX";

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
    private readonly List<Border> spectrumBars = new();
    private readonly Dictionary<SoundTile, object> loopingClips = new();
    private readonly float[] spectrumSamples = new float[SpectrumTapSampleProvider.WindowSize];
    private readonly Complex[] spectrumFft = new Complex[SpectrumTapSampleProvider.WindowSize];

    private float[] eqFrequencies = EqualizerSampleProvider.BuildFrequencies(10);
    private bool startHeightSet;
    private static readonly AudioDeviceInfo NoDevice = new("", "(none)");

    // Switch to the preferred mic as soon as it carries audio, but wait a while
    // before handing back, so a pause in speech is not mistaken for a
    // disconnect.
    private static readonly TimeSpan PreferredSoundWindow = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan SwitchBackAfterSilence = TimeSpan.FromSeconds(8);

    private DeviceWatcher? deviceWatcher;
    private DispatcherTimer? deviceDebounce;
    private DispatcherTimer? autoSwitchTimer;
    private readonly MicActivityProbe probe = new();
    private string? activeInputId;
    private HotkeyManager? hotkeys;
    private WinForms.NotifyIcon? trayIcon;
    private WinForms.ToolStripMenuItem? trayProfilesMenu;
    private DispatcherTimer? spectrumTimer;
    private bool initializing = true;
    private bool profilesUpdating;
    private bool reallyExit;
    private bool trayTipShown;
    private Action? noticeAction;
    private Action? noticeSecondaryAction;
    private Action? noticeCloseAction;
    private string? updateTag;
    private string? updateHtmlUrl;
    private string? updateSetupUrl;
    private string? updatePortableUrl;
    private bool updating;

    public MainWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        Title = $"MicFX v{version.Major}.{version.Minor}.{version.Build} — Microphone Processor";
        CleanUpAfterUpdate();

        settings.EqBandCount = Math.Clamp(settings.EqBandCount,
            EqualizerSampleProvider.MinBands, EqualizerSampleProvider.MaxBands);
        if (settings.EqGainsDb.Length != settings.EqBandCount)
        {
            var resized = new float[settings.EqBandCount];
            Array.Copy(settings.EqGainsDb, resized, Math.Min(settings.EqGainsDb.Length, resized.Length));
            settings.EqGainsDb = resized;
        }
        eqFrequencies = EqualizerSampleProvider.BuildFrequencies(settings.EqBandCount);
        BuildEqSliders(settings.EqGainsDb);
        BuildSpectrumBars();
        icSounds.ItemsSource = sounds;
        engine.LevelsAvailable += OnLevels;
        engine.ClipEnded += OnClipEndedUi;

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
        StartDeviceWatcher();
        hotkeys = new HotkeyManager(this);
        hotkeys.HotkeyPressed += id => Dispatcher.BeginInvoke(() => PlaySlot(id));
        RefreshHotkeys();
        StartSpectrumTimer();
        TryAutoStart();
        CheckCableSetup();
        _ = CheckForUpdatesAsync();
        if (Environment.GetCommandLineArgs().Contains("--updated"))
            txtStatus.Text = $"Updated to v{version.Major}.{version.Minor}.{version.Build} ✓";

        Loaded += (_, _) => UpdateMinHeight();
        noticeBar.IsVisibleChanged += (_, _) =>
            Dispatcher.BeginInvoke(UpdateMinHeight, DispatcherPriority.Loaded);

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
            autoSwitchTimer?.Stop();
            deviceDebounce?.Stop();
            deviceWatcher?.Dispose();
            probe.Dispose();
            hotkeys?.Dispose();
            engine.Dispose();
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
        };
    }

    // ---------- tray ----------

    private void CreateTrayIcon()
    {
        using var iconStream =
            Application.GetResourceStream(new Uri("pack://application:,,,/Assets/micfx.ico"))!.Stream;
        trayIcon = new WinForms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(iconStream),
            Visible = true,
            Text = "MicFX — microphone processor"
        };
        trayIcon.DoubleClick += (_, _) => ShowFromTray();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open MicFX", null, (_, _) => ShowFromTray());
        trayProfilesMenu = new WinForms.ToolStripMenuItem("Profiles");
        menu.Items.Add(trayProfilesMenu);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ForceExit());
        menu.Opening += (_, _) => RebuildTrayProfiles();
        trayIcon.ContextMenuStrip = menu;
    }

    private void RebuildTrayProfiles()
    {
        if (trayProfilesMenu == null) return;
        trayProfilesMenu.DropDownItems.Clear();
        if (settings.Profiles.Count == 0)
        {
            trayProfilesMenu.DropDownItems.Add(
                new WinForms.ToolStripMenuItem("(no profiles yet)") { Enabled = false });
            return;
        }
        foreach (var profile in settings.Profiles)
        {
            string name = profile.Name;
            trayProfilesMenu.DropDownItems.Add(name, null,
                (_, _) => Dispatcher.BeginInvoke(() => comboProfile.SelectedItem = name));
        }
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

    /// <summary>
    /// The window's minimum height is whatever the left column actually needs
    /// (measured, not guessed) — CLEAN-UP can never be cut off. The first
    /// layout also snaps the window to exactly that height.
    /// </summary>
    private void UpdateMinHeight()
    {
        if (!IsLoaded || ActualHeight <= 0 || mainArea.ActualHeight <= 0) return;
        double chrome = ActualHeight - mainArea.ActualHeight;
        if (chrome <= 0 || double.IsNaN(chrome)) return;
        double min = Math.Ceiling(leftPanel.DesiredSize.Height + chrome) + 2;
        MinHeight = min;
        if (!startHeightSet)
        {
            Height = min;
            startHeightSet = true;
        }
        else if (ActualHeight < min)
        {
            Height = min;
        }
    }

    private void BuildEqSliders(float[]? gains = null)
    {
        for (int i = 0; i < eqFrequencies.Length; i++)
        {
            int band = i;
            float freq = eqFrequencies[i];

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
                if (band < settings.EqGainsDb.Length)
                    settings.EqGainsDb[band] = (float)e.NewValue;
                engine.SetEqGain(band, (float)e.NewValue);
            };
            if (gains != null && i < gains.Length)
                slider.Value = gains[i];

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

    private void BuildSpectrumBars()
    {
        for (int i = 0; i < eqFrequencies.Length; i++)
        {
            var bar = new Border
            {
                Background = (Brush)FindResource("AccentBrush"),
                Opacity = 0.22,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(12, 0, 12, 0),
                CornerRadius = new CornerRadius(2, 2, 0, 0),
                Height = 2
            };
            spectrumPanel.Children.Add(bar);
            spectrumBars.Add(bar);
        }
    }

    private void PopulateDevices()
    {
        bool wasInitializing = initializing;
        initializing = true;

        var selectedMic = (comboMic.SelectedItem as AudioDeviceInfo)?.Id ?? settings.InputDeviceId;
        var selectedPreferred = (comboMicPreferred.SelectedItem as AudioDeviceInfo)?.Id
                                ?? settings.PreferredInputDeviceId;
        var selectedOut = (comboOutput.SelectedItem as AudioDeviceInfo)?.Id ?? settings.OutputDeviceId;
        var selectedMon = (comboMonitor.SelectedItem as AudioDeviceInfo)?.Id
                          ?? settings.MonitorDeviceId
                          ?? AudioDevices.GetDefaultRenderDeviceId();

        var captureDevices = AudioDevices.GetCaptureDevices();
        comboMic.ItemsSource = captureDevices;

        // The auto-switch list keeps a remembered device even while it is
        // unplugged, so the choice survives the headset being disconnected.
        var preferredItems = new List<AudioDeviceInfo> { NoDevice };
        preferredItems.AddRange(captureDevices);
        if (!string.IsNullOrEmpty(selectedPreferred) && preferredItems.All(d => d.Id != selectedPreferred))
        {
            string remembered = settings.PreferredInputDeviceName ?? "remembered device";
            preferredItems.Add(new AudioDeviceInfo(selectedPreferred, $"{remembered} (not connected)"));
        }
        comboMicPreferred.ItemsSource = preferredItems;

        var renderDevices = AudioDevices.GetRenderDevices();
        comboOutput.ItemsSource = renderDevices;
        comboMonitor.ItemsSource = renderDevices.ToList();

        SelectById(comboMic, selectedMic);
        SelectById(comboOutput, selectedOut);
        SelectById(comboMonitor, selectedMon);
        SelectById(comboMicPreferred, selectedPreferred);
        if (comboMicPreferred.SelectedItem == null)
            comboMicPreferred.SelectedItem = NoDevice;

        if (comboMic.SelectedItem == null)
            SelectById(comboMic, AudioDevices.GetDefaultCaptureDeviceId());

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
        chkDenoise.IsChecked = settings.DenoiseEnabled;
        sliderDenoise.Value = settings.DenoiseStrengthDb;
        foreach (ComboBoxItem item in comboDenoiseMode.Items)
        {
            if ((string)item.Tag == settings.DenoiseMode)
            {
                item.IsSelected = true;
                break;
            }
        }
        sliderDenoise.IsEnabled = settings.DenoiseMode == "Spectral";
        lblDenoise.Text = settings.DenoiseMode == "Spectral" ? $"{settings.DenoiseStrengthDb:0} dB" : "AI";
        chkComp.IsChecked = settings.CompressorEnabled;
        sliderComp.Value = settings.CompressorAmount;
        chkMonitor.IsChecked = settings.MonitorEnabled;
        sliderMonitorVoice.Value = settings.MonitorVoiceVolume;
        sliderMonitorSound.Value = settings.MonitorSoundVolume;
        engine.SetMonitorVolumes(settings.MonitorVoiceVolume, settings.MonitorSoundVolume);
        sliderEqBands.Value = settings.EqBandCount;
        lblEqBands.Text = settings.EqBandCount.ToString();
        foreach (ComboBoxItem item in comboLatency.Items)
        {
            if ((string)item.Tag == settings.LatencyMode)
            {
                item.IsSelected = true;
                break;
            }
        }
        engine.SetLatencyMode(settings.LatencyMode);


        foreach (var clip in settings.Sounds)
        {
            sounds.Add(new SoundTile
            {
                Name = clip.Name,
                Path = clip.Path,
                Volume = clip.Volume,
                Loop = clip.Loop,
                HotkeyMods = clip.HotkeyMods,
                HotkeyVk = clip.HotkeyVk,
                HotkeyText = clip.HotkeyText
            });
        }
        RefreshTileIndexes();

        PopulateProfilesCombo(settings.ActiveProfile);

        // Push restored values into the engine so they apply on Start().
        engine.SetMicGain(settings.MicGain);
        engine.SetMasterVolume(settings.MasterVolume);
        engine.SetSoundboardVolume(settings.SoundboardVolume);
        engine.SetGate(settings.GateEnabled, settings.GateThresholdDb);
        engine.SetDenoise(settings.DenoiseEnabled, settings.DenoiseStrengthDb, settings.DenoiseMode);
        engine.SetCompressor(settings.CompressorEnabled, settings.CompressorAmount);
        engine.SetEqBands(settings.EqBandCount, settings.EqGainsDb);
    }

    private void CollectSettings()
    {
        settings.InputDeviceId = (comboMic.SelectedItem as AudioDeviceInfo)?.Id;
        if (comboMicPreferred.SelectedItem is AudioDeviceInfo preferred && preferred.Id.Length > 0)
        {
            settings.PreferredInputDeviceId = preferred.Id;
            // Strip the "(not connected)" suffix so it isn't baked into the name.
            settings.PreferredInputDeviceName = preferred.Name.Replace(" (not connected)", "");
        }
        else
        {
            settings.PreferredInputDeviceId = null;
            settings.PreferredInputDeviceName = null;
        }
        settings.OutputDeviceId = (comboOutput.SelectedItem as AudioDeviceInfo)?.Id;
        settings.MonitorDeviceId = (comboMonitor.SelectedItem as AudioDeviceInfo)?.Id;
        settings.MonitorEnabled = chkMonitor.IsChecked == true;
        settings.MonitorVoiceVolume = (float)sliderMonitorVoice.Value;
        settings.MonitorSoundVolume = (float)sliderMonitorSound.Value;
        settings.MicGain = (float)sliderMicGain.Value;
        settings.MasterVolume = (float)sliderMaster.Value;
        settings.SoundboardVolume = (float)sliderSoundVol.Value;
        settings.GateEnabled = chkGate.IsChecked == true;
        settings.GateThresholdDb = (float)sliderGate.Value;
        settings.DenoiseEnabled = chkDenoise.IsChecked == true;
        settings.DenoiseStrengthDb = (float)sliderDenoise.Value;
        settings.DenoiseMode = CurrentDenoiseMode();
        settings.CompressorEnabled = chkComp.IsChecked == true;
        settings.CompressorAmount = (float)sliderComp.Value;
        settings.EqBandCount = eqFrequencies.Length;
        settings.LatencyMode = CurrentLatencyMode();
        settings.ActiveProfile = comboProfile.SelectedItem as string;
        settings.Sounds = sounds
            .Select(s => new SoundClipSetting
            {
                Name = s.Name,
                Path = s.Path,
                Volume = s.Volume,
                Loop = s.Loop,
                HotkeyMods = s.HotkeyMods,
                HotkeyVk = s.HotkeyVk,
                HotkeyText = s.HotkeyText
            })
            .ToList();
    }

    // ---------- engine control ----------

    private void TryAutoStart()
    {
        if (comboMic.SelectedItem != null && comboOutput.SelectedItem != null)
            StartEngine();
    }

    /// <summary>
    /// The mic to actually run with: the auto-switch device while it is
    /// carrying audio, otherwise the primary one.
    /// </summary>
    private AudioDeviceInfo? ResolveInputDevice()
    {
        var available = AudioDevices.GetCaptureDevices();
        var ids = available.Select(d => d.Id).ToHashSet();
        string? primary = (comboMic.SelectedItem as AudioDeviceInfo)?.Id;
        string? preferred = PreferredInputId();
        string? chosen = AudioDevices.ChooseInput(preferred, primary, ids, PreferredHasSound(preferred));
        return chosen == null ? null : available.First(d => d.Id == chosen);
    }

    private string? PreferredInputId()
    {
        string? id = (comboMicPreferred.SelectedItem as AudioDeviceInfo)?.Id;
        return string.IsNullOrEmpty(id) ? null : id;
    }

    /// <summary>
    /// Whether the preferred mic is currently carrying audio. While it is the
    /// active input the engine's own stream answers that; otherwise the probe
    /// listening to it does.
    /// </summary>
    private bool PreferredHasSound(string? preferredId)
    {
        if (preferredId == null) return false;
        if (engine.IsRunning && activeInputId == preferredId)
            return engine.SinceInputSound < SwitchBackAfterSilence;
        return probe.DeviceId == preferredId && probe.HasSoundWithin(PreferredSoundWindow);
    }

    /// <summary>
    /// Keeps a probe on the preferred mic whenever it is not the one in use, so
    /// its activity can be seen without routing it anywhere.
    /// </summary>
    private void UpdateProbe()
    {
        string? preferred = PreferredInputId();
        if (preferred == null || (engine.IsRunning && activeInputId == preferred))
        {
            probe.Stop();
            return;
        }
        try
        {
            probe.Watch(AudioDevices.GetDevice(preferred));
        }
        catch
        {
            probe.Stop(); // device vanished between enumeration and opening
        }
    }

    private void EvaluateAutoSwitch()
    {
        if (!engine.IsRunning || PreferredInputId() == null) return;
        var chosen = ResolveInputDevice();
        if (chosen != null && chosen.Id != activeInputId)
            StartEngine();
        else
            UpdateProbe();
    }

    private void StartEngine()
    {
        if (comboOutput.SelectedItem is not AudioDeviceInfo render)
        {
            txtStatus.Text = "Select an output device first.";
            return;
        }
        if (ResolveInputDevice() is not AudioDeviceInfo mic)
        {
            txtStatus.Text = "Select a microphone first.";
            return;
        }

        if (AudioDevices.IsVirtualCableLoop(mic.Name, render.Name))
        {
            StopEngine();
            txtStatus.Text = $"\"{mic.Name}\" is the other end of \"{render.Name}\" — " +
                             "capturing it would feed the output back into itself. Pick a real microphone.";
            return;
        }

        try
        {
            engine.Start(AudioDevices.GetDevice(mic.Id), AudioDevices.GetDevice(render.Id));
            activeInputId = mic.Id;
            UpdateProbe();
            ApplyMonitor();
            btnStartStop.Content = "Stop";
            txtStatus.Text = $"Running — {mic.Name}  →  {render.Name}  (~{engine.EstimatedLatencyMs} ms)";
            if (engine.CaptureNote != null)
                txtStatus.Text += $"  ({engine.CaptureNote})";
        }
        catch (Exception ex)
        {
            engine.Stop();
            activeInputId = null;
            btnStartStop.Content = "Start";
            txtStatus.Text = "Error: " + ex.Message;
        }
    }

    /// <summary>
    /// Watches for devices appearing and disappearing so the preferred mic can
    /// take over (and hand back) without touching anything downstream.
    /// Notifications arrive on a COM thread and can burst, so they are
    /// marshalled to the UI thread and debounced.
    /// </summary>
    private void StartDeviceWatcher()
    {
        deviceDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        deviceDebounce.Tick += (_, _) =>
        {
            deviceDebounce!.Stop();
            OnDevicesChanged();
        };

        deviceWatcher = new DeviceWatcher();
        deviceWatcher.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            deviceDebounce!.Stop();
            deviceDebounce.Start();
        });

        // Presence changes alone cannot tell whether a virtual mic is live, so
        // poll its activity as well.
        autoSwitchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        autoSwitchTimer.Tick += (_, _) => EvaluateAutoSwitch();
        autoSwitchTimer.Start();
        UpdateProbe();
    }

    private void OnDevicesChanged()
    {
        PopulateDevices();
        if (!engine.IsRunning) return;

        var chosen = ResolveInputDevice();
        if (chosen == null)
        {
            StopEngine();
            txtStatus.Text = "Microphone disconnected.";
            return;
        }
        if (chosen.Id != activeInputId)
            StartEngine(); // restarts on the newly preferred/available mic
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

    // ---------- spectrum ----------

    private void StartSpectrumTimer()
    {
        spectrumTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(66) };
        spectrumTimer.Tick += (_, _) => UpdateSpectrum();
        spectrumTimer.Start();
    }

    private void UpdateSpectrum()
    {
        if (!IsVisible) return;
        if (!engine.CopySpectrumSamples(spectrumSamples))
        {
            foreach (var bar in spectrumBars) bar.Height = 2;
            return;
        }

        int n = SpectrumTapSampleProvider.WindowSize;
        for (int i = 0; i < n; i++)
        {
            spectrumFft[i].X = spectrumSamples[i] * (float)FastFourierTransform.HannWindow(i, n);
            spectrumFft[i].Y = 0f;
        }
        FastFourierTransform.FFT(true, 11, spectrumFft);

        double maxHeight = Math.Max(spectrumPanel.ActualHeight - 4, 10);
        float step = eqFrequencies.Length > 1 ? MathF.Sqrt(eqFrequencies[1] / eqFrequencies[0]) : 1.5f;
        for (int b = 0; b < spectrumBars.Count && b < eqFrequencies.Length; b++)
        {
            float f = eqFrequencies[b];
            int lo = Math.Max(1, (int)(f / step * n / AudioEngine.SampleRate));
            int hi = Math.Min(n / 2 - 1, Math.Max(lo + 1, (int)(f * step * n / AudioEngine.SampleRate)));
            float sum = 0f;
            for (int i = lo; i <= hi; i++)
                sum += MathF.Sqrt(spectrumFft[i].X * spectrumFft[i].X + spectrumFft[i].Y * spectrumFft[i].Y);
            float avg = sum / (hi - lo + 1);
            float db = 20f * MathF.Log10(avg + 1e-9f);
            double t = Math.Clamp((db + 65) / 60.0, 0, 1);
            spectrumBars[b].Height = Math.Max(2, t * maxHeight);
        }
    }

    // ---------- notices (VB-Cable setup, updates) ----------

    private void ShowNotice(string text, string? actionText, Action? action,
        string? secondaryText = null, Action? secondary = null, Action? onClose = null)
    {
        txtNotice.Text = text;
        noticeAction = action;
        noticeSecondaryAction = secondary;
        noticeCloseAction = onClose;
        btnNoticeAction.Content = actionText;
        btnNoticeAction.Visibility = actionText != null ? Visibility.Visible : Visibility.Collapsed;
        btnNoticeSecondary.Content = secondaryText;
        btnNoticeSecondary.Visibility = secondaryText != null ? Visibility.Visible : Visibility.Collapsed;
        noticeBar.Visibility = Visibility.Visible;
    }

    private void HideNotice() => noticeBar.Visibility = Visibility.Collapsed;

    private void NoticeAction_Click(object sender, RoutedEventArgs e) => noticeAction?.Invoke();

    private void NoticeSecondary_Click(object sender, RoutedEventArgs e) => noticeSecondaryAction?.Invoke();

    private void NoticeClose_Click(object sender, RoutedEventArgs e)
    {
        noticeCloseAction?.Invoke();
        HideNotice();
    }

    private void CheckCableSetup()
    {
        var cable = comboOutput.Items.Cast<AudioDeviceInfo>()
            .FirstOrDefault(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));

        if (cable != null)
        {
            if (comboOutput.SelectedItem == null)
            {
                SelectById(comboOutput, cable.Id);
                TryAutoStart();
            }
            return;
        }

        if (settings.CableNoticeDismissed) return;

        ShowNotice(
            "No virtual cable found. MicFX needs one so Discord/OBS can use the processed mic — " +
            "install the free VB-Audio Cable (run as admin, then reboot).",
            "Get VB-Cable", () => OpenUrl("https://vb-audio.com/Cable/"),
            "Re-check", () =>
            {
                PopulateDevices();
                var found = comboOutput.Items.Cast<AudioDeviceInfo>()
                    .FirstOrDefault(d => d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    SelectById(comboOutput, found.Id);
                    HideNotice();
                    txtStatus.Text = "Virtual cable found and selected as output.";
                    TryAutoStart();
                }
            },
            onClose: () => settings.CableNoticeDismissed = true);
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MicFX-Updater");
            string json = await http.GetStringAsync(
                $"https://api.github.com/repos/{RepoSlug}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? tag = root.GetProperty("tag_name").GetString();
            string? url = root.GetProperty("html_url").GetString();
            if (tag == null || url == null) return;

            var latest = Version.Parse(tag.TrimStart('v', 'V'));
            var asm = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            var current = new Version(asm.Major, asm.Minor, asm.Build);
            if (latest <= current || noticeBar.Visibility == Visibility.Visible) return;

            updateTag = tag;
            updateHtmlUrl = url;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string? name = asset.GetProperty("name").GetString();
                    string? download = asset.GetProperty("browser_download_url").GetString();
                    if (name == null || download == null) continue;
                    if (name.StartsWith("MicFX-Setup", StringComparison.OrdinalIgnoreCase))
                        updateSetupUrl = download;
                    else if (name.Equals("MicFX.exe", StringComparison.OrdinalIgnoreCase))
                        updatePortableUrl = download;
                }
            }

            bool canAutoUpdate = updateSetupUrl != null || updatePortableUrl != null;
            ShowNotice($"MicFX {tag} is available (you have v{current}).",
                canAutoUpdate ? "Install update" : "Download",
                canAutoUpdate ? StartUpdate : () => OpenUrl(url),
                "What's new", () => OpenUrl(url));
        }
        catch
        {
            // Offline or rate-limited — stay quiet.
        }
    }

    /// <summary>
    /// Downloads and applies the update. Installed copies (in
    /// %LOCALAPPDATA%\Programs\MicFX) run the setup silently, which replaces
    /// the files and relaunches; portable copies swap their own exe.
    /// </summary>
    private async void StartUpdate()
    {
        if (updating) return;
        updating = true;
        btnNoticeAction.IsEnabled = false;
        btnNoticeSecondary.IsEnabled = false;
        btnNoticeClose.IsEnabled = false;
        try
        {
            bool installed = IsInstalledCopy();
            string? url = installed
                ? updateSetupUrl ?? updatePortableUrl
                : updatePortableUrl ?? updateSetupUrl;
            if (url == null)
            {
                OpenUrl(updateHtmlUrl ?? $"https://github.com/{RepoSlug}/releases");
                return;
            }

            bool viaSetup = url == updateSetupUrl;
            string dest = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                viaSetup ? $"MicFX-Setup-{updateTag}.exe" : $"MicFX-{updateTag}.exe.download");
            await DownloadFileAsync(url, dest,
                percent => txtNotice.Text = $"Downloading MicFX {updateTag}… {percent:0}%");

            txtNotice.Text = "Installing — MicFX will restart itself…";
            if (viaSetup)
            {
                Process.Start(new ProcessStartInfo(dest,
                    "/VERYSILENT /SUPPRESSMSGBOXES /FORCECLOSEAPPLICATIONS /NORESTART /AUTORELAUNCH=1")
                {
                    UseShellExecute = true
                });
            }
            else
            {
                // A running exe can be renamed but not overwritten: shift the
                // old one aside, drop the new one in place, relaunch delayed
                // (so the single-instance mutex of this process is gone).
                string exe = Environment.ProcessPath
                    ?? throw new InvalidOperationException("Cannot determine the application path.");
                string old = exe + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);
                File.Move(dest, exe);
                Process.Start(new ProcessStartInfo("cmd.exe",
                    $"/c timeout /t 2 /nobreak >nul & start \"\" \"{exe}\" --updated")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            ForceExit();
        }
        catch (Exception ex)
        {
            txtNotice.Text = "Update failed: " + ex.Message;
            btnNoticeAction.IsEnabled = true;
            btnNoticeSecondary.IsEnabled = true;
            btnNoticeClose.IsEnabled = true;
            updating = false;
        }
    }

    private static bool IsInstalledCopy()
    {
        string exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";
        string installDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MicFX");
        return string.Equals(
            System.IO.Path.TrimEndingDirectorySeparator(exeDir),
            System.IO.Path.TrimEndingDirectorySeparator(installDir),
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task DownloadFileAsync(string url, string dest, Action<double> progress)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MicFX-Updater");
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? -1;

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var file = File.Create(dest);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            if (total > 0) progress(done * 100.0 / total);
        }
    }

    /// <summary>Removes the leftover .old exe from a previous portable self-update.</summary>
    private static void CleanUpAfterUpdate()
    {
        try
        {
            string old = (Environment.ProcessPath ?? "") + ".old";
            if (File.Exists(old)) File.Delete(old);
        }
        catch
        {
            // Previous instance may still be exiting — it'll be cleaned next launch.
        }
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    // ---------- profiles ----------

    private void PopulateProfilesCombo(string? select)
    {
        profilesUpdating = true;
        comboProfile.Items.Clear();
        foreach (var profile in settings.Profiles)
            comboProfile.Items.Add(profile.Name);
        if (select != null && comboProfile.Items.Contains(select))
            comboProfile.SelectedItem = select;
        profilesUpdating = false;
    }

    private Profile CaptureProfile(string name) => new()
    {
        Name = name,
        MicGain = (float)sliderMicGain.Value,
        MasterVolume = (float)sliderMaster.Value,
        GateEnabled = chkGate.IsChecked == true,
        GateThresholdDb = (float)sliderGate.Value,
        DenoiseEnabled = chkDenoise.IsChecked == true,
        DenoiseStrengthDb = (float)sliderDenoise.Value,
        DenoiseMode = CurrentDenoiseMode(),
        CompressorEnabled = chkComp.IsChecked == true,
        CompressorAmount = (float)sliderComp.Value,
        EqGainsDb = eqSliders.Select(s => (float)s.Value).ToArray(),
    };

    private void ApplyProfile(Profile p)
    {
        sliderMicGain.Value = p.MicGain;
        sliderMaster.Value = p.MasterVolume;
        chkGate.IsChecked = p.GateEnabled;
        sliderGate.Value = p.GateThresholdDb;
        chkDenoise.IsChecked = p.DenoiseEnabled;
        sliderDenoise.Value = p.DenoiseStrengthDb;
        foreach (ComboBoxItem item in comboDenoiseMode.Items)
        {
            if ((string)item.Tag == p.DenoiseMode)
            {
                item.IsSelected = true;
                break;
            }
        }
        chkComp.IsChecked = p.CompressorEnabled;
        sliderComp.Value = p.CompressorAmount;
        var profileGains = MapGainsToBands(p.EqGainsDb, eqFrequencies);
        for (int i = 0; i < eqSliders.Count && i < profileGains.Length; i++)
            eqSliders[i].Value = profileGains[i];
        txtStatus.Text = $"Profile \"{p.Name}\" applied.";
    }

    private void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || profilesUpdating) return;
        if (comboProfile.SelectedItem is not string name) return;
        var profile = settings.Profiles.FirstOrDefault(p => p.Name == name);
        if (profile != null) ApplyProfile(profile);
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        if (comboProfile.SelectedItem is not string name)
        {
            NewProfile_Click(sender, e);
            return;
        }
        int index = settings.Profiles.FindIndex(p => p.Name == name);
        if (index >= 0)
        {
            settings.Profiles[index] = CaptureProfile(name);
            settings.Save();
            txtStatus.Text = $"Profile \"{name}\" saved.";
        }
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog(this, "New profile", "Profile name:",
            $"Profile {settings.Profiles.Count + 1}");
        if (dialog.ShowDialog() != true) return;

        string name = dialog.Value;
        int existing = settings.Profiles.FindIndex(p => p.Name == name);
        var profile = CaptureProfile(name);
        if (existing >= 0) settings.Profiles[existing] = profile;
        else settings.Profiles.Add(profile);
        settings.Save();
        PopulateProfilesCombo(name);
        txtStatus.Text = $"Profile \"{name}\" saved.";
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (comboProfile.SelectedItem is not string name) return;
        settings.Profiles.RemoveAll(p => p.Name == name);
        settings.Save();
        PopulateProfilesCombo(null);
        txtStatus.Text = $"Profile \"{name}\" deleted.";
    }

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

    private void MonitorVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (lblMonitorVoice != null) lblMonitorVoice.Text = $"{sliderMonitorVoice.Value * 100:0}%";
        if (lblMonitorSound != null) lblMonitorSound.Text = $"{sliderMonitorSound.Value * 100:0}%";
        if (initializing) return;
        engine.SetMonitorVolumes((float)sliderMonitorVoice.Value, (float)sliderMonitorSound.Value);
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

    private string CurrentDenoiseMode() =>
        (comboDenoiseMode.SelectedItem as ComboBoxItem)?.Tag as string ?? "Ai";

    private string CurrentLatencyMode() =>
        (comboLatency.SelectedItem as ComboBoxItem)?.Tag as string ?? "Normal";

    private void Latency_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (initializing) return;
        settings.LatencyMode = CurrentLatencyMode();
        engine.SetLatencyMode(settings.LatencyMode);
        RestartIfRunning();
    }

    private void Denoise_Changed(object sender, RoutedEventArgs e)
    {
        if (lblDenoise == null || sliderDenoise == null || comboDenoiseMode == null) return;
        string mode = CurrentDenoiseMode();
        bool spectral = mode == "Spectral";
        sliderDenoise.IsEnabled = spectral;
        lblDenoise.Text = spectral ? $"{sliderDenoise.Value:0} dB" : "AI";
        if (initializing) return;

        engine.SetDenoise(chkDenoise.IsChecked == true, (float)sliderDenoise.Value, mode);
        if (chkDenoise.IsChecked == true && mode == "Ai" && !AudioEngine.AiDenoiseAvailable)
            txtStatus.Text = "AI suppression unavailable, using spectral instead " +
                             $"({AudioEngine.AiDenoiseUnavailableReason ?? "unknown reason"}).";
    }

    private void Comp_Changed(object sender, RoutedEventArgs e)
    {
        if (lblComp != null) lblComp.Text = $"{sliderComp.Value:0}";
        if (initializing) return;
        engine.SetCompressor(chkComp.IsChecked == true, (float)sliderComp.Value);
    }

    private void EqPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (initializing) return;
        if (comboEqPreset.SelectedItem is not string name || !EqPresets.TryGetValue(name, out var gains))
            return;
        var mapped = MapGainsToBands(gains, eqFrequencies);
        for (int i = 0; i < eqSliders.Count && i < mapped.Length; i++)
            eqSliders[i].Value = mapped[i];
    }

    /// <summary>Re-maps a gain curve onto a different set of band centres (linear in log-frequency).</summary>
    private static float[] MapGainsToBands(float[] srcGains, float[] dstFreqs)
    {
        var srcFreqs = EqualizerSampleProvider.BuildFrequencies(srcGains.Length);
        if (srcFreqs.Length == dstFreqs.Length) return srcGains;
        return MapGainsToBandsFrom(srcFreqs, srcGains, dstFreqs);
    }

    private void EqBands_Changed(object sender, RoutedEventArgs e)
    {
        if (lblEqBands != null) lblEqBands.Text = $"{sliderEqBands.Value:0}";
        if (initializing) return;
        int count = (int)sliderEqBands.Value;
        if (count == eqFrequencies.Length) return;

        var newFreqs = EqualizerSampleProvider.BuildFrequencies(count);
        var newGains = MapGainsToBandsFrom(eqFrequencies, settings.EqGainsDb, newFreqs);
        eqFrequencies = newFreqs;
        settings.EqBandCount = count;
        settings.EqGainsDb = newGains;
        engine.SetEqBands(count, newGains);

        eqPanel.Children.Clear();
        eqSliders.Clear();
        spectrumPanel.Children.Clear();
        spectrumBars.Clear();
        BuildEqSliders(newGains);
        BuildSpectrumBars();
    }

    private static float[] MapGainsToBandsFrom(float[] srcFreqs, float[] srcGains, float[] dstFreqs)
    {
        var result = new float[dstFreqs.Length];
        for (int i = 0; i < dstFreqs.Length; i++)
        {
            double f = Math.Log(dstFreqs[i]);
            if (srcGains.Length == 0) break;
            if (f <= Math.Log(srcFreqs[0])) { result[i] = srcGains[0]; continue; }
            if (f >= Math.Log(srcFreqs[Math.Min(srcGains.Length, srcFreqs.Length) - 1]))
            {
                result[i] = srcGains[Math.Min(srcGains.Length, srcFreqs.Length) - 1];
                continue;
            }
            int j = 1;
            while (j < srcFreqs.Length && Math.Log(srcFreqs[j]) < f) j++;
            double f0 = Math.Log(srcFreqs[j - 1]);
            double f1 = Math.Log(srcFreqs[j]);
            double t = (f - f0) / (f1 - f0);
            float g0 = srcGains[Math.Min(j - 1, srcGains.Length - 1)];
            float g1 = srcGains[Math.Min(j, srcGains.Length - 1)];
            result[i] = (float)(g0 * (1 - t) + g1 * t);
        }
        return result;
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
        RefreshHotkeys();
    }

    private void EditSound_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is not SoundTile tile) return;

        var dialog = new EditSoundWindow(this, tile.Name, tile.Volume, tile.Loop,
            tile.HotkeyMods, tile.HotkeyVk, tile.HotkeyText,
            preview: volume => TryPreview(tile.Path, volume));
        bool? ok = dialog.ShowDialog();
        engine.StopPreview();
        if (ok != true) return;

        tile.Name = dialog.SoundName;
        tile.Volume = dialog.ClipVolume;
        tile.Loop = dialog.Loop;
        tile.HotkeyMods = dialog.HotkeyMods;
        tile.HotkeyVk = dialog.HotkeyVk;
        tile.HotkeyText = dialog.HotkeyText;
        RefreshTileIndexes();
        RefreshHotkeys();
    }

    private void TryPreview(string path, float volume)
    {
        if (!File.Exists(path))
        {
            txtStatus.Text = $"File not found: {path}";
            return;
        }
        try
        {
            var device = comboMonitor.SelectedItem is AudioDeviceInfo info
                ? AudioDevices.GetDevice(info.Id)
                : null;
            engine.PreviewClip(path, volume, device);
        }
        catch (Exception ex)
        {
            txtStatus.Text = "Preview error: " + ex.Message;
        }
    }

    private void RemoveSound_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is SoundTile tile)
        {
            if (loopingClips.TryGetValue(tile, out var handle))
                engine.StopClip(handle);
            sounds.Remove(tile);
            RefreshTileIndexes();
            RefreshHotkeys();
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
        // A looping tile acts as a toggle.
        if (tile.IsLooping && loopingClips.TryGetValue(tile, out var running))
        {
            engine.StopClip(running);
            return;
        }

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
            var handle = engine.PlayClip(tile.Path, tile.Volume, tile.Loop);
            if (tile.Loop && handle != null)
            {
                loopingClips[tile] = handle;
                tile.IsLooping = true;
                icSounds.Items.Refresh();
            }
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Can't play \"{tile.Name}\": {ex.Message}";
        }
    }

    private void OnClipEndedUi(object handle)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var entry = loopingClips.FirstOrDefault(kv => ReferenceEquals(kv.Value, handle));
            if (entry.Key != null)
            {
                entry.Key.IsLooping = false;
                loopingClips.Remove(entry.Key);
                icSounds.Items.Refresh();
            }
        });
    }

    private void RefreshTileIndexes()
    {
        for (int i = 0; i < sounds.Count; i++)
            sounds[i].Index = i;
        icSounds.Items.Refresh();
    }

    private void RefreshHotkeys()
    {
        if (hotkeys == null) return;
        hotkeys.Clear();
        for (int i = 0; i < sounds.Count; i++)
        {
            var tile = sounds[i];
            if (tile.HotkeyVk != 0)
                hotkeys.Register(i, tile.HotkeyMods, tile.HotkeyVk);
            else if (i < 9)
                hotkeys.Register(i, HotkeyManager.ModControl | HotkeyManager.ModAlt, (uint)('1' + i));
        }
    }
}
