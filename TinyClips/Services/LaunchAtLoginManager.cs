using Microsoft.Win32;

namespace TinyClips.Services;

/// <summary>
/// Manages the "launch at login" registry entry for the current user.
/// Uses HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
public static class LaunchAtLoginManager
{
    private const string AppName = "TinyClips";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(AppName) != null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null) return;

        if (enabled)
        {
            string exePath = Environment.ProcessPath ?? "";
            if (!string.IsNullOrEmpty(exePath))
                key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }
}
