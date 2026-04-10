using System.ComponentModel;
using System.Runtime.InteropServices;
using TinyClips.Helpers;

namespace TinyClips.Services;

/// <summary>
/// Registers and manages global hotkeys via Win32 RegisterHotKey.
/// Uses a hidden message-only window to receive WM_HOTKEY messages.
/// </summary>
public sealed partial class HotKeyManager : IDisposable
{
    private const int HOTKEY_SCREENSHOT = 1;
    private const int HOTKEY_VIDEO = 2;
    private const int HOTKEY_GIF = 3;
    private const int HOTKEY_STOP = 4;

    private nint _hwnd;
    private NativeWindow? _messageWindow;
    private Action? _onScreenshot;
    private Action? _onRecordVideo;
    private Action? _onRecordGif;
    private Action? _onStop;

    public void Initialize()
    {
        _messageWindow = new NativeWindow(OnMessage);
        _hwnd = _messageWindow.Handle;
    }

    public void RegisterCaptureHotKeys(
        int screenshotVk, int screenshotMod, Action onScreenshot,
        int videoVk, int videoMod, Action onRecordVideo,
        int gifVk, int gifMod, Action onRecordGif)
    {
        if (_hwnd == nint.Zero) return;

        _onScreenshot = onScreenshot;
        _onRecordVideo = onRecordVideo;
        _onRecordGif = onRecordGif;

        // Unregister previous registrations
        NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_SCREENSHOT);
        NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_VIDEO);
        NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_GIF);

        LogHotKey("Screenshot", NativeMethods.RegisterHotKey(_hwnd, HOTKEY_SCREENSHOT,
            (uint)screenshotMod | NativeMethods.MOD_NOREPEAT, (uint)screenshotVk));
        LogHotKey("Video", NativeMethods.RegisterHotKey(_hwnd, HOTKEY_VIDEO,
            (uint)videoMod | NativeMethods.MOD_NOREPEAT, (uint)videoVk));
        LogHotKey("GIF", NativeMethods.RegisterHotKey(_hwnd, HOTKEY_GIF,
            (uint)gifMod | NativeMethods.MOD_NOREPEAT, (uint)gifVk));
    }

    public void RegisterStopHotKey(Action onStop)
    {
        if (_hwnd == nint.Zero) return;
        _onStop = onStop;
        NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_STOP);
        // Ctrl+. to stop recording
        NativeMethods.RegisterHotKey(_hwnd, HOTKEY_STOP,
            NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT, 0xBE); // VK_OEM_PERIOD
    }

    public void UnregisterStopHotKey()
    {
        NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_STOP);
        _onStop = null;
    }

    private nint OnMessage(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = (int)wParam;
            switch (id)
            {
                case HOTKEY_SCREENSHOT:
                    _onScreenshot?.Invoke();
                    return nint.Zero;
                case HOTKEY_VIDEO:
                    _onRecordVideo?.Invoke();
                    return nint.Zero;
                case HOTKEY_GIF:
                    _onRecordGif?.Invoke();
                    return nint.Zero;
                case HOTKEY_STOP:
                    _onStop?.Invoke();
                    return nint.Zero;
            }
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static void LogHotKey(string name, bool success)
    {
        if (!success)
            AppLog.Error($"HotKey '{name}' registration failed (already registered by another app?)");
    }

    public void Dispose()
    {
        if (_hwnd != nint.Zero)
        {
            NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_SCREENSHOT);
            NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_VIDEO);
            NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_GIF);
            NativeMethods.UnregisterHotKey(_hwnd, HOTKEY_STOP);
        }
        _messageWindow?.Dispose();
    }

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    /// <summary>
    /// A hidden message-only window for receiving WM_HOTKEY.
    /// </summary>
    private sealed class NativeWindow : IDisposable
    {
        private const string ClassName = "TinyClips_HotKeyMessageWindow";

        public nint Handle { get; }

        private readonly WndProcDelegate _wndProc;
        private GCHandle _gcHandle;

        private delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

        public NativeWindow(Func<nint, uint, nint, nint, nint> callback)
        {
            _wndProc = new WndProcDelegate((h, m, w, l) => callback(h, m, w, l));
            _gcHandle = GCHandle.Alloc(_wndProc);

            var wndClass = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                lpszClassName = ClassName,
                hInstance = GetModuleHandle(null)
            };

            RegisterClassEx(ref wndClass);

            Handle = CreateWindowEx(
                0, ClassName, "TinyClips HotKey", 0,
                0, 0, 0, 0,
                HWND_MESSAGE, nint.Zero, wndClass.hInstance, nint.Zero);
        }

        public void Dispose()
        {
            if (Handle != nint.Zero)
                DestroyWindow(Handle);
            if (_gcHandle.IsAllocated)
                _gcHandle.Free();
        }

        private static readonly nint HWND_MESSAGE = new(-3);

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
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszClassName;
            public nint hIconSm;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint CreateWindowEx(
            int dwExStyle, string lpClassName, string lpWindowName,
            int dwStyle, int x, int y, int nWidth, int nHeight,
            nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(nint hWnd);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint GetModuleHandle(string? lpModuleName);
    }
}
