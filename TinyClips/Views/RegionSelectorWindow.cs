using System.Drawing;
using System.Runtime.InteropServices;
using TinyClips.Capture;
using TinyClips.Helpers;

namespace TinyClips.Views;

/// <summary>
/// Full-screen transparent overlay for selecting a screen region.
/// Uses a raw Win32 layered window for maximum reliability and performance.
/// Renders a crosshair cursor and a selection rectangle.
/// </summary>
public sealed class RegionSelectorWindow : IDisposable
{
    private nint _hwnd;
    private bool _isSelecting;
    private Point _startPoint;
    private Point _currentPoint;
    private bool _hasSelection;
    private readonly TaskCompletionSource<CaptureRegion?> _tcs = new();
    private GCHandle _wndProcHandle;
    private WndProcDelegate? _wndProc;
    private nint _crosshairCursor;
    private double _dpiScale = 1.0;

    private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_SETCURSOR = 0x0020;
    private const int VK_ESCAPE = 0x1B;
    private const string ClassName = "TinyClips_RegionSelector";
    // Magenta used as the transparent color key — this exact color becomes fully see-through
    private const uint ColorKey = 0x00FF00FF; // BGR magenta

    /// <summary>
    /// Show the region selector and wait for the user to select a region.
    /// Returns null if cancelled.
    /// </summary>
    public static async Task<CaptureRegion?> SelectRegionAsync()
    {
        var selector = new RegionSelectorWindow();
        selector.Show();
        var result = await selector._tcs.Task;
        selector.Dispose();
        return result;
    }

    private void Show()
    {
        _wndProc = WndProc;
        _wndProcHandle = GCHandle.Alloc(_wndProc);
        _crosshairCursor = LoadCursor(nint.Zero, 32515); // IDC_CROSS

        var hInstance = GetModuleHandle(null);
        var wndClass = new WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            lpszClassName = ClassName,
            hInstance = hInstance,
            hCursor = _crosshairCursor
        };
        RegisterClassEx(ref wndClass);

        // Get the virtual screen bounds (all monitors combined)
        var bounds = ScreenInfo.GetVirtualScreenBounds();

        _hwnd = CreateWindowEx(
            NativeMethods.WS_EX_TOPMOST | 0x00080000, // WS_EX_TOPMOST | WS_EX_LAYERED
            ClassName, "Region Selector",
            unchecked((int)0x96000000), // WS_POPUP | WS_VISIBLE | WS_MAXIMIZE
            bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            nint.Zero, nint.Zero, hInstance, nint.Zero);

        // Color key (magenta) = fully transparent; alpha = dim the rest
        // LWA_COLORKEY (0x01) | LWA_ALPHA (0x02)
        SetLayeredWindowAttributes(_hwnd, ColorKey, 160, 0x01 | 0x02);

        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOW);
        NativeMethods.SetForegroundWindow(_hwnd);
        SetCapture(_hwnd);

        // Get DPI scale from the primary monitor (initial scale for the overlay)
        _dpiScale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        if (_dpiScale < 1.0) _dpiScale = 1.0;
    }

    private nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_LBUTTONDOWN:
                _startPoint = PointFromLParam(lParam);
                _currentPoint = _startPoint;
                _isSelecting = true;
                _hasSelection = false;
                InvalidateRect(hwnd, nint.Zero, true);
                return nint.Zero;

            case WM_MOUSEMOVE:
                SetCursor(_crosshairCursor);
                if (_isSelecting)
                {
                    _currentPoint = PointFromLParam(lParam);
                    InvalidateRect(hwnd, nint.Zero, true);
                }
                return nint.Zero;

            case WM_LBUTTONUP:
                if (_isSelecting)
                {
                    _currentPoint = PointFromLParam(lParam);
                    _isSelecting = false;
                    _hasSelection = true;
                    FinishSelection();
                }
                return nint.Zero;

            case WM_KEYDOWN:
                if ((int)wParam == VK_ESCAPE)
                {
                    Cancel();
                }
                return nint.Zero;

            case WM_PAINT:
                PaintOverlay(hwnd);
                return nint.Zero;

            case WM_ERASEBKGND:
                return new nint(1);

            case WM_SETCURSOR:
                SetCursor(_crosshairCursor);
                return new nint(1);
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void PaintOverlay(nint hwnd)
    {
        var ps = new PAINTSTRUCT();
        var hdc = BeginPaint(hwnd, ref ps);

        // Fill entire window with black (dimmed by the layered alpha)
        GetClientRect(hwnd, out var clientRect);
        var blackBrush = CreateSolidBrush(0x00000000);
        FillRect(hdc, ref clientRect, blackBrush);
        DeleteObject(blackBrush);

        if (_isSelecting || _hasSelection)
        {
            var selRect = GetSelectionRect();
            if (selRect.Width > 0 && selRect.Height > 0)
            {
                // Fill selection area with color key → fully transparent (clear view)
                var keyBrush = CreateSolidBrush(ColorKey);
                FillRect(hdc, ref selRect, keyBrush);
                DeleteObject(keyBrush);

                // White border around selection — scale pen width with DPI
                int penWidth = Math.Max(2, (int)(2 * _dpiScale));
                var pen = CreatePen(0, penWidth, 0x00FFFFFF);
                var oldPen = SelectObject(hdc, pen);
                var oldBrush = SelectObject(hdc, GetStockObject(5)); // HOLLOW_BRUSH
                Win32Rectangle(hdc, selRect.Left, selRect.Top, selRect.Right, selRect.Bottom);
                SelectObject(hdc, oldPen);
                SelectObject(hdc, oldBrush);
                DeleteObject(pen);

                // Dimensions label — scale font with DPI
                int fontSize = Math.Max(16, (int)(16 * _dpiScale));
                var sizeText = $"{selRect.Width} \u00d7 {selRect.Height}";
                var font = CreateFont(fontSize, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 4, 0, "Segoe UI");
                var oldFont = SelectObject(hdc, font);
                SetTextColor(hdc, 0x00FFFFFF);
                SetBkMode(hdc, 1); // TRANSPARENT

                // Position label above selection, or below if too close to top
                int labelPad = Math.Max(24, (int)(24 * _dpiScale));
                int labelHeight = Math.Max(20, (int)(20 * _dpiScale));
                int labelY = selRect.Top - labelPad;
                if (labelY < 4) labelY = selRect.Bottom + 4;
                var textRect = new RECT_GDI
                {
                    Left = selRect.Left,
                    Top = labelY,
                    Right = selRect.Left + (int)(200 * _dpiScale),
                    Bottom = labelY + labelHeight
                };
                DrawText(hdc, sizeText, -1, ref textRect, 0);
                SelectObject(hdc, oldFont);
                DeleteObject(font);
            }
        }

        EndPaint(hwnd, ref ps);
    }

    private RECT_GDI GetSelectionRect()
    {
        int left = Math.Min(_startPoint.X, _currentPoint.X);
        int top = Math.Min(_startPoint.Y, _currentPoint.Y);
        int right = Math.Max(_startPoint.X, _currentPoint.X);
        int bottom = Math.Max(_startPoint.Y, _currentPoint.Y);
        return new RECT_GDI { Left = left, Top = top, Right = right, Bottom = bottom };
    }

    private void FinishSelection()
    {
        var selRect = GetSelectionRect();
        int width = selRect.Right - selRect.Left;
        int height = selRect.Bottom - selRect.Top;

        if (!SelectionGeometry.MeetsMinimumSize(width, height))
        {
            // Selection too small — treat as cancel
            Cancel();
            return;
        }

        // Get the virtual screen offset
        var virtualBounds = ScreenInfo.GetVirtualScreenBounds();
        int screenX = virtualBounds.Left + selRect.Left;
        int screenY = virtualBounds.Top + selRect.Top;

        var pt = new NativeMethods.POINT { X = screenX + width / 2, Y = screenY + height / 2 };
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var scaleFactor = NativeMethods.GetMonitorScale(hMonitor);

        var rect = new Rectangle(screenX, screenY, width, height);
        var region = new CaptureRegion(rect, hMonitor, scaleFactor);

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
            // Unregister so the next instance can re-register with a fresh WndProc delegate
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

    // Win32 imports
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT_GDI { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

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
    [DllImport("user32.dll", EntryPoint = "LoadCursorW")] private static extern nint LoadCursor(nint hInstance, int lpCursorName);
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawText(nint hdc, string lpString, int nCount, ref RECT_GDI lpRect, uint uFormat);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateFont(int nHeight, int nWidth, int nEscapement, int nOrientation, int fnWeight, uint fdwItalic, uint fdwUnderline, uint fdwStrikeOut, uint fdwCharSet, uint fdwOutputPrecision, uint fdwClipPrecision, uint fdwQuality, uint fdwPitchAndFamily, string lpszFace);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? lpModuleName);
}
