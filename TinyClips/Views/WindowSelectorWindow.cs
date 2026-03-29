using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using TinyClips.Capture;
using TinyClips.Helpers;

namespace TinyClips.Views;

/// <summary>
/// Full-screen transparent overlay for selecting a window to capture.
/// Enumerates visible windows, highlights the one under the cursor, and
/// returns its bounds as a CaptureRegion on click.
/// Uses a raw Win32 layered window — same pattern as RegionSelectorWindow.
/// </summary>
public sealed class WindowSelectorWindow : IDisposable
{
    private nint _hwnd;
    private nint _highlightedHandle;
    private Rectangle _highlightRect;
    private string _highlightTitle = "";
    private Rectangle _virtualBounds;
    private readonly List<WindowInfo> _windows = new();
    private readonly TaskCompletionSource<CaptureRegion?> _tcs = new();
    private GCHandle _wndProcHandle;
    private WndProcDelegate? _wndProc;

    private record WindowInfo(nint Handle, Rectangle Bounds, string Title);

    private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_SETCURSOR = 0x0020;
    private const int VK_ESCAPE = 0x1B;
    private const string ClassName = "TinyClips_WindowSelector";
    private const uint ColorKey = 0x00FF00FF; // BGR magenta — transparent via color key

    /// <summary>
    /// Show the window selector overlay and wait for the user to click a window.
    /// Returns null if cancelled.
    /// </summary>
    public static async Task<CaptureRegion?> SelectWindowAsync()
    {
        var selector = new WindowSelectorWindow();
        selector.EnumerateWindows();
        selector.Show();
        var result = await selector._tcs.Task;
        selector.Dispose();
        return result;
    }

    private void EnumerateWindows()
    {
        EnumWindows((hwnd, lParam) =>
        {
            if (!IsWindowVisible(hwnd)) return true;

            // Skip tool windows (hidden from taskbar/alt-tab)
            var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;

            GetWindowRect(hwnd, out var rect);
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 20 || height < 20) return true;

            int titleLen = GetWindowTextLength(hwnd);
            if (titleLen <= 0) return true;

            var sb = new StringBuilder(titleLen + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            string title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            _windows.Add(new WindowInfo(
                hwnd,
                new Rectangle(rect.Left, rect.Top, width, height),
                title));

            return true;
        }, nint.Zero);
    }

    private void Show()
    {
        _wndProc = WndProc;
        _wndProcHandle = GCHandle.Alloc(_wndProc);

        var hInstance = GetModuleHandle(null);
        var wndClass = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            lpszClassName = ClassName,
            hInstance = hInstance,
            hCursor = LoadCursor(nint.Zero, 32649) // IDC_HAND
        };
        RegisterClassEx(ref wndClass);

        _virtualBounds = ScreenInfo.GetVirtualScreenBounds();

        _hwnd = CreateWindowEx(
            NativeMethods.WS_EX_TOPMOST | 0x00080000, // WS_EX_TOPMOST | WS_EX_LAYERED
            ClassName, "Window Selector",
            unchecked((int)0x96000000), // WS_POPUP | WS_VISIBLE | WS_MAXIMIZE
            _virtualBounds.Left, _virtualBounds.Top,
            _virtualBounds.Width, _virtualBounds.Height,
            nint.Zero, nint.Zero, hInstance, nint.Zero);

        // Color key (magenta) = fully transparent; alpha = dim the rest
        SetLayeredWindowAttributes(_hwnd, ColorKey, 160, 0x01 | 0x02);

        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOW);
        NativeMethods.SetForegroundWindow(_hwnd);
        SetCapture(_hwnd);
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_MOUSEMOVE:
            {
                var pt = PointFromLParam(lParam);
                int screenX = _virtualBounds.Left + pt.X;
                int screenY = _virtualBounds.Top + pt.Y;

                // Find the frontmost window containing the cursor (EnumWindows returns Z-order)
                WindowInfo? found = null;
                foreach (var win in _windows)
                {
                    if (win.Bounds.Contains(screenX, screenY))
                    {
                        found = win;
                        break;
                    }
                }

                if (found != null && found.Handle != _highlightedHandle)
                {
                    _highlightedHandle = found.Handle;
                    _highlightRect = found.Bounds;
                    _highlightTitle = found.Title;
                    InvalidateRect(hwnd, nint.Zero, true);
                }
                return nint.Zero;
            }

            case WM_LBUTTONDOWN:
                if (_highlightedHandle != nint.Zero)
                    FinishSelection();
                return nint.Zero;

            case WM_KEYDOWN:
                if ((int)wParam == VK_ESCAPE)
                    Cancel();
                return nint.Zero;

            case WM_PAINT:
                PaintOverlay(hwnd);
                return nint.Zero;

            case WM_ERASEBKGND:
                return new nint(1);

            case WM_SETCURSOR:
                SetCursor(LoadCursor(nint.Zero, 32649)); // IDC_HAND
                return new nint(1);
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void PaintOverlay(nint hwnd)
    {
        var ps = new PAINTSTRUCT();
        var hdc = BeginPaint(hwnd, ref ps);

        // Fill entire window with black (dimmed by layered alpha)
        GetClientRect(hwnd, out var clientRect);
        var blackBrush = CreateSolidBrush(0x00000000);
        FillRect(hdc, ref clientRect, blackBrush);
        DeleteObject(blackBrush);

        if (_highlightedHandle != nint.Zero)
        {
            // Convert screen coords to overlay client coords
            var selRect = new RECT_GDI
            {
                Left = _highlightRect.X - _virtualBounds.Left,
                Top = _highlightRect.Y - _virtualBounds.Top,
                Right = _highlightRect.X - _virtualBounds.Left + _highlightRect.Width,
                Bottom = _highlightRect.Y - _virtualBounds.Top + _highlightRect.Height
            };

            // Fill highlighted window area with color key → fully transparent
            var keyBrush = CreateSolidBrush(ColorKey);
            FillRect(hdc, ref selRect, keyBrush);
            DeleteObject(keyBrush);

            // White border around highlight
            var pen = CreatePen(0, 3, 0x00FFFFFF);
            var oldPen = SelectObject(hdc, pen);
            var oldBrush = SelectObject(hdc, GetStockObject(5)); // HOLLOW_BRUSH
            Win32Rectangle(hdc, selRect.Left, selRect.Top, selRect.Right, selRect.Bottom);
            SelectObject(hdc, oldPen);
            SelectObject(hdc, oldBrush);
            DeleteObject(pen);

            // Window title label
            var font = CreateFont(16, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 4, 0, "Segoe UI");
            var oldFont = SelectObject(hdc, font);
            SetTextColor(hdc, 0x00FFFFFF);
            SetBkMode(hdc, 1); // TRANSPARENT

            int labelY = selRect.Top - 24;
            if (labelY < 4) labelY = selRect.Bottom + 4;
            var textRect = new RECT_GDI
            {
                Left = selRect.Left,
                Top = labelY,
                Right = selRect.Left + 600,
                Bottom = labelY + 20
            };
            DrawText(hdc, _highlightTitle, -1, ref textRect, 0);
            SelectObject(hdc, oldFont);
            DeleteObject(font);
        }

        EndPaint(hwnd, ref ps);
    }

    private void FinishSelection()
    {
        var pt = new NativeMethods.POINT
        {
            X = _highlightRect.X + _highlightRect.Width / 2,
            Y = _highlightRect.Y + _highlightRect.Height / 2
        };
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var region = new CaptureRegion(_highlightRect, hMonitor);

        Close();
        _tcs.TrySetResult(region);
    }

    private void Cancel()
    {
        Close();
        _tcs.TrySetResult(null);
    }

    private void Close()
    {
        if (_hwnd != nint.Zero)
        {
            ReleaseCapture();
            DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
            UnregisterClass(ClassName, GetModuleHandle(null));
        }
    }

    private static Point PointFromLParam(nint lParam)
    {
        return new Point(
            (short)(lParam.ToInt32() & 0xFFFF),
            (short)((lParam.ToInt32() >> 16) & 0xFFFF));
    }

    public void Dispose()
    {
        Close();
        if (_wndProcHandle.IsAllocated)
            _wndProcHandle.Free();
    }

    // MARK: - Win32 Imports

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT_GDI { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public nint hdc;
        public bool fErase;
        public RECT_GDI rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public int style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public nint hIconSm;
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")] private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")] private static extern int GetWindowTextLength(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WNDCLASSEX lpWndClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string lpClassName, nint hInstance);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint hWnd, ref PAINTSTRUCT lpPaint);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(nint hWnd, nint lpRect, bool bErase);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hWnd, out RECT_GDI lpRect);
    [DllImport("user32.dll")] private static extern nint SetCapture(nint hWnd);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern nint LoadCursor(nint hInstance, int lpCursorName);
    [DllImport("user32.dll")] private static extern nint SetCursor(nint hCursor);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint crColor);
    [DllImport("gdi32.dll")] private static extern nint CreatePen(int fnPenStyle, int nWidth, uint crColor);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint hdc, nint hObject);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int fnObject);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint hObject);
    [DllImport("user32.dll")] private static extern int FillRect(nint hdc, ref RECT_GDI lprc, nint hbr);
    [DllImport("gdi32.dll", EntryPoint = "Rectangle")] private static extern bool Win32Rectangle(nint hdc, int l, int t, int r, int b);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(nint hdc, uint crColor);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint hdc, int iBkMode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DrawTextW")] private static extern int DrawText(nint hdc, string lpString, int nCount, ref RECT_GDI lpRect, uint uFormat);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateFont(int nHeight, int nWidth, int nEscapement, int nOrientation, int fnWeight, uint fdwItalic, uint fdwUnderline, uint fdwStrikeOut, uint fdwCharSet, uint fdwOutputPrecision, uint fdwClipPrecision, uint fdwQuality, uint fdwPitchAndFamily, string lpszFace);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? lpModuleName);
}
