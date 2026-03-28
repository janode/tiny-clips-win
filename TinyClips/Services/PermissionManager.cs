using Windows.Graphics.Capture;

namespace TinyClips.Services;

/// <summary>
/// Checks whether the Windows Graphics Capture API is available and accessible.
/// On Windows 11, screen capture doesn't need an explicit permission prompt
/// like macOS — the system shows a picker or consent dialog at capture time.
/// </summary>
public static class PermissionManager
{
    /// <summary>
    /// Returns true if the Graphics Capture API is supported on this system.
    /// </summary>
    public static bool IsCaptureSupported()
    {
        return GraphicsCaptureSession.IsSupported();
    }

    /// <summary>
    /// Request access to the programmatic capture API (Windows 11 22H2+).
    /// Returns true if granted.
    /// </summary>
    public static async Task<bool> RequestAccessAsync()
    {
        if (!IsCaptureSupported())
            return false;

        try
        {
            var access = await GraphicsCaptureAccess.RequestAccessAsync(
                GraphicsCaptureAccessKind.Programmatic);
            return access == Windows.Security.Authorization.AppCapabilityAccess.AppCapabilityAccessStatus.Allowed;
        }
        catch
        {
            // Older Windows builds don't have RequestAccessAsync — fall back to picker-based consent
            return true;
        }
    }
}
