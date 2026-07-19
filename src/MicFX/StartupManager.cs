using Microsoft.Win32;

namespace MicFX;

/// <summary>
/// Manages the per-user registry Run entry that launches MicFX (minimized to
/// the tray) when the user signs in to Windows. The registry is the single
/// source of truth — the checkbox reads it on startup rather than settings.json,
/// so external changes (e.g. Task Manager's Startup tab) stay in sync.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MicFX";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) != null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            string exe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine the application path.");
            key.SetValue(ValueName, $"\"{exe}\" --minimized");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
