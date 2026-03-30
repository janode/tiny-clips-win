using System.Drawing;
using System.Runtime.InteropServices;

namespace TinyClips.Capture;

/// <summary>
/// Region definition for a capture area on a specific monitor.
/// </summary>
public sealed class CaptureRegion
{
    /// <summary>
    /// The region in screen coordinates (virtual desktop space).
    /// </summary>
    public Rectangle ScreenRect { get; }

    /// <summary>
    /// The monitor that contains this region.
    /// </summary>
    public nint MonitorHandle { get; }

    public double ScaleFactor { get; }

    public CaptureRegion(Rectangle screenRect, nint monitorHandle, double scaleFactor = 1.0)
    {
        ScreenRect = screenRect;
        MonitorHandle = monitorHandle;
        ScaleFactor = scaleFactor;
    }

    /// <summary>
    /// Create a full-screen region for the primary monitor.
    /// </summary>
    public static CaptureRegion? FullScreenPrimary()
    {
        var hMonitor = Helpers.NativeMethods.MonitorFromPoint(
            new Helpers.NativeMethods.POINT { X = 0, Y = 0 },
            Helpers.NativeMethods.MONITOR_DEFAULTTOPRIMARY);

        if (hMonitor == nint.Zero) return null;

        var info = new Helpers.NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<Helpers.NativeMethods.MONITORINFO>()
        };
        Helpers.NativeMethods.GetMonitorInfo(hMonitor, ref info);

        var rect = new Rectangle(
            info.rcMonitor.Left, info.rcMonitor.Top,
            info.rcMonitor.Width, info.rcMonitor.Height);
        return new CaptureRegion(rect, hMonitor, Helpers.NativeMethods.GetMonitorScale(hMonitor));
    }

    /// <summary>
    /// Create a full-screen region for the monitor at the cursor position.
    /// </summary>
    public static CaptureRegion? FullScreenAtCursor()
    {
        Helpers.NativeMethods.GetCursorPos(out var pt);
        var hMonitor = Helpers.NativeMethods.MonitorFromPoint(
            pt, Helpers.NativeMethods.MONITOR_DEFAULTTONEAREST);

        if (hMonitor == nint.Zero) return null;

        var info = new Helpers.NativeMethods.MONITORINFO
        {
            cbSize = Marshal.SizeOf<Helpers.NativeMethods.MONITORINFO>()
        };
        Helpers.NativeMethods.GetMonitorInfo(hMonitor, ref info);

        var rect = new Rectangle(
            info.rcMonitor.Left, info.rcMonitor.Top,
            info.rcMonitor.Width, info.rcMonitor.Height);
        return new CaptureRegion(rect, hMonitor, Helpers.NativeMethods.GetMonitorScale(hMonitor));
    }
}

/// <summary>
/// Provides screen geometry information for multi-monitor setups.
/// </summary>
public static class ScreenInfo
{
    public static Rectangle GetVirtualScreenBounds()
    {
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return new Rectangle(left, top, width, height);
    }

    public static List<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(nint.Zero, nint.Zero, (hMonitor, hdcMonitor, lprcMonitor, dwData) =>
        {
            var info = new Helpers.NativeMethods.MONITORINFO
            {
                cbSize = Marshal.SizeOf<Helpers.NativeMethods.MONITORINFO>()
            };
            Helpers.NativeMethods.GetMonitorInfo(hMonitor, ref info);

            monitors.Add(new MonitorInfo
            {
                Handle = hMonitor,
                Bounds = new Rectangle(
                    info.rcMonitor.Left, info.rcMonitor.Top,
                    info.rcMonitor.Width, info.rcMonitor.Height),
                WorkArea = new Rectangle(
                    info.rcWork.Left, info.rcWork.Top,
                    info.rcWork.Width, info.rcWork.Height),
                IsPrimary = (info.dwFlags & 1) != 0
            });
            return true;
        }, nint.Zero);
        return monitors;
    }

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private delegate bool EnumMonitorsDelegate(nint hMonitor, nint hdcMonitor,
        nint lprcMonitor, nint dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip,
        EnumMonitorsDelegate lpfnEnum, nint dwData);
}

public sealed class MonitorInfo
{
    public nint Handle { get; init; }
    public Rectangle Bounds { get; init; }
    public Rectangle WorkArea { get; init; }
    public bool IsPrimary { get; init; }
}
