using System.IO;
using System.Text.Json;

namespace MicFX.Models;

public class SoundClipSetting
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public float Volume { get; set; } = 1f;
}

public class AppSettings
{
    public string? InputDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }
    public string? MonitorDeviceId { get; set; }
    public bool MonitorEnabled { get; set; }
    public float MicGain { get; set; } = 1f;
    public float MasterVolume { get; set; } = 1f;
    public float SoundboardVolume { get; set; } = 0.8f;
    public bool GateEnabled { get; set; }
    public float GateThresholdDb { get; set; } = -45f;
    public float[] EqGainsDb { get; set; } = new float[10];
    public string Effect { get; set; } = "None";
    public int EffectIntensity { get; set; } = 50;
    public List<SoundClipSetting> Sounds { get; set; } = new();

    private static string SettingsDir =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MicFX");

    private static string SettingsPath => System.IO.Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt settings file — start fresh rather than crash on launch.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Not being able to persist settings should never take the app down.
        }
    }
}
