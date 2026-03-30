using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace TinyClips.ViewModels;

/// <summary>
/// Tool modes for the screenshot editor.
/// </summary>
public enum EditorTool { None, Crop, Draw, Arrow, Blur, Text }

/// <summary>
/// ViewModel for ScreenshotEditorWindow — manages tool selection state and
/// properties bar visibility via INotifyPropertyChanged + x:Bind.
/// </summary>
public sealed class ScreenshotEditorViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // MARK: - Tool State

    private EditorTool _activeTool = EditorTool.None;

    public EditorTool ActiveTool
    {
        get => _activeTool;
        set
        {
            if (_activeTool == value) return;
            _activeTool = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PropertiesBarVisible));
            OnPropertyChanged(nameof(AnnotationPropertiesVisible));
            OnPropertyChanged(nameof(CropPropertiesVisible));
            OnPropertyChanged(nameof(BlurPropertiesVisible));
        }
    }

    public Visibility PropertiesBarVisible =>
        _activeTool != EditorTool.None ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AnnotationPropertiesVisible =>
        _activeTool is EditorTool.Draw or EditorTool.Arrow or EditorTool.Text
            ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CropPropertiesVisible =>
        _activeTool == EditorTool.Crop ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BlurPropertiesVisible =>
        _activeTool == EditorTool.Blur ? Visibility.Visible : Visibility.Collapsed;

    // MARK: - Undo State

    private int _actionCount;

    public bool CanUndo => _actionCount > 0;

    public void UpdateActionCount(int count)
    {
        _actionCount = count;
        OnPropertyChanged(nameof(CanUndo));
    }
}
