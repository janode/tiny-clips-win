using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VKey = Windows.System.VirtualKey;

namespace TinyClips.Views;

/// <summary>
/// A TextBox-like control that captures a keyboard shortcut (modifiers + key).
/// Click to start recording, press desired key combo, click again or Esc to cancel.
/// Stores the result as Win32 modifier flags and virtual key code.
/// </summary>
public sealed class ShortcutRecorderControl : Button
{
    private bool _isRecording;

    public int Modifiers { get; private set; }
    public int VirtualKey { get; private set; }

    /// <summary>
    /// Fired when the user successfully records a new shortcut.
    /// </summary>
    public Action<int, int>? OnShortcutChanged { get; set; }

    public ShortcutRecorderControl()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        MinWidth = 200;
        Padding = new Thickness(12, 8, 12, 8);
        Click += OnButtonClick;
    }

    public void SetShortcut(int mod, int vk)
    {
        Modifiers = mod;
        VirtualKey = vk;
        Content = FormatShortcut(mod, vk);
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        if (!_isRecording)
        {
            base.OnKeyDown(e);
            return;
        }

        e.Handled = true;
        var key = e.Key;

        // Esc cancels recording
        if (key == VKey.Escape)
        {
            StopRecording(cancelled: true);
            return;
        }

        // Ignore bare modifier keys — wait for the actual key
        if (key is VKey.Control or VKey.Shift or VKey.Menu or VKey.LeftControl
            or VKey.RightControl or VKey.LeftShift or VKey.RightShift
            or VKey.LeftMenu or VKey.RightMenu or VKey.LeftWindows or VKey.RightWindows)
        {
            return;
        }

        // Build modifier mask (MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4)
        int mod = 0;
        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VKey.Control);
        if ((state & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0) mod |= 0x0002;
        state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VKey.Menu);
        if ((state & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0) mod |= 0x0001;
        state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VKey.Shift);
        if ((state & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0) mod |= 0x0004;

        // Require at least one modifier
        if (mod == 0)
        {
            return;
        }

        int vk = (int)key;
        Modifiers = mod;
        VirtualKey = vk;
        Content = FormatShortcut(mod, vk);
        StopRecording(cancelled: false);
        OnShortcutChanged?.Invoke(mod, vk);
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            StopRecording(cancelled: true);
        }
        else
        {
            StartRecording();
        }
    }

    private void StartRecording()
    {
        _isRecording = true;
        Content = "Press shortcut…";
        Focus(FocusState.Programmatic);
    }

    private void StopRecording(bool cancelled)
    {
        _isRecording = false;
        if (cancelled)
        {
            Content = FormatShortcut(Modifiers, VirtualKey);
        }
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (_isRecording) StopRecording(cancelled: true);
    }

    public static string FormatShortcut(int mod, int vk)
    {
        var parts = new List<string>();
        if ((mod & 0x0002) != 0) parts.Add("Ctrl");
        if ((mod & 0x0001) != 0) parts.Add("Alt");
        if ((mod & 0x0004) != 0) parts.Add("Shift");
        if ((mod & 0x0008) != 0) parts.Add("Win");

        string keyName = vk switch
        {
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
            0xBE => ".",
            0xBC => ",",
            0xBB => "=",
            0xBD => "-",
            0xBA => ";",
            0xDB => "[",
            0xDD => "]",
            0xDC => "\\",
            0xDE => "'",
            0xC0 => "`",
            0xBF => "/",
            0x20 => "Space",
            0x09 => "Tab",
            0x0D => "Enter",
            0x08 => "Backspace",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            _ => $"0x{vk:X2}"
        };
        parts.Add(keyName);
        return string.Join(" + ", parts);
    }
}
