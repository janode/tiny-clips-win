using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using TinyClips.Helpers;
using TinyClips.Models;
using TinyClips.ViewModels;
using WinRT.Interop;
using Windows.Foundation;
using Image = Microsoft.UI.Xaml.Controls.Image;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace TinyClips.Views;

/// <summary>
/// Post-capture screenshot editor with crop, draw, arrow, blur, and text tools.
/// Receives a System.Drawing.Bitmap, displays it in a WinUI canvas with annotation
/// overlays, and composites all edits onto the bitmap when saving.
/// </summary>
public sealed partial class ScreenshotEditorWindow : Window
{
    // MARK: - Types

    private abstract class EditorAction
    {
        public UIElement? Visual { get; set; }
    }

    private sealed class DrawAction : EditorAction
    {
        public List<Windows.Foundation.Point> Points { get; } = [];
        public Windows.UI.Color Color { get; init; }
        public double Thickness { get; init; }
    }

    private sealed class ArrowAction : EditorAction
    {
        public Windows.Foundation.Point Start { get; init; }
        public Windows.Foundation.Point End { get; init; }
        public Windows.UI.Color Color { get; init; }
        public double Thickness { get; init; }
        public UIElement? Arrowhead { get; set; }
    }

    private sealed class BlurAction : EditorAction
    {
        public Rect Region { get; init; }
        public UIElement? Preview { get; set; }
    }

    private sealed class TextAction : EditorAction
    {
        public Windows.Foundation.Point Position { get; init; }
        public string Text { get; init; } = "";
        public Windows.UI.Color Color { get; init; }
        public double FontSize { get; init; }
    }

    // MARK: - State

    public ScreenshotEditorViewModel ViewModel { get; } = new();

    private Bitmap _bitmap;
    private string? _tempPath;
    private int _imageWidth;
    private int _imageHeight;
    private double _dpiScale = 1.0;

    private readonly List<EditorAction> _actions = [];
    private Windows.UI.Color _currentColor = Windows.UI.Color.FromArgb(255, 255, 59, 48);
    private double _currentFontSize = 24;

    // Pointer tracking
    private bool _isPointerDown;
    private Windows.Foundation.Point _pointerStart;

    // Active drawing objects
    private Polyline? _activePolyline;
    private DrawAction? _activeDrawAction;
    private Line? _activeArrowLine;
    private Rectangle? _activeBlurRect;

    // Crop overlay elements
    private Rectangle? _cropTop, _cropBottom, _cropLeft, _cropRight, _cropBorder;
    private Rect? _cropRect;

    // Text input
    private TextBox? _activeTextBox;

    // Tool toggle buttons for radio behavior
    private ToggleButton[] _toolToggles = [];

    // Callbacks
    public Action<string>? OnSaved;
    public Action? OnDiscarded;

    private bool _didCallback;

    // MARK: - Construction

    public ScreenshotEditorWindow(Bitmap screenshot)
    {
        _bitmap = screenshot;
        _imageWidth = screenshot.Width;
        _imageHeight = screenshot.Height;

        InitializeComponent();
        Title = "TinyClips — Edit Screenshot";

        _toolToggles = [CropToggle, DrawToggle, ArrowToggle, BlurToggle, TextToggle];

        Closed += OnWindowClosed;
        ConfigureWindowSize();
        _ = LoadImageAsync();
    }

    // MARK: - Window Configuration

    private void ConfigureWindowSize()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        _dpiScale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;

        var monitor = NativeMethods.GetMonitorRectAtCursor();
        int maxW = (int)(monitor.Width * 0.85);
        int maxH = (int)(monitor.Height * 0.85);

        var (w, h) = DpiHelper.CalculateEditorWindowSize(
            _imageWidth, _imageHeight, _dpiScale, maxW, maxH);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
        AppWindow.Move(new Windows.Graphics.PointInt32(
            monitor.Left + (monitor.Width - w) / 2,
            monitor.Top + (monitor.Height - h) / 2));
    }

    private async Task LoadImageAsync()
    {
        // Save bitmap to temp file for BitmapImage display
        _tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"TinyClips_edit_{Guid.NewGuid():N}.png");
        await Task.Run(() => _bitmap.Save(_tempPath, System.Drawing.Imaging.ImageFormat.Png));

        var bi = new BitmapImage();
        bi.UriSource = new Uri(_tempPath);
        ScreenshotImage.Source = bi;

        // Size canvas to image dimensions in DIPs (physical pixels / dpiScale)
        // so the preview matches the actual screen size of the captured area
        double dipW = DpiHelper.PhysicalToDip(_imageWidth, _dpiScale);
        double dipH = DpiHelper.PhysicalToDip(_imageHeight, _dpiScale);
        ScreenshotImage.Width = dipW;
        ScreenshotImage.Height = dipH;
        CanvasContainer.Width = dipW;
        CanvasContainer.Height = dipH;
        AnnotationCanvas.Width = dipW;
        AnnotationCanvas.Height = dipH;

        // Register keyboard shortcuts
        Content.KeyDown += OnKeyDown;
    }

    // MARK: - Tool Selection

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked) return;
        var tag = clicked.Tag as string;

        // Commit any pending text
        CommitActiveText();

        // Radio behavior: uncheck all others
        foreach (var tb in _toolToggles)
        {
            if (tb != clicked) tb.IsChecked = false;
        }

        // If unchecking, go to None
        if (clicked.IsChecked != true)
        {
            SetActiveTool(EditorTool.None);
            return;
        }

        var tool = tag switch
        {
            "Crop" => EditorTool.Crop,
            "Draw" => EditorTool.Draw,
            "Arrow" => EditorTool.Arrow,
            "Blur" => EditorTool.Blur,
            "Text" => EditorTool.Text,
            _ => EditorTool.None
        };
        SetActiveTool(tool);
    }

    private void SetActiveTool(EditorTool tool)
    {
        // Clear crop overlay when leaving crop mode
        if (ViewModel.ActiveTool == EditorTool.Crop && tool != EditorTool.Crop)
            ClearCropOverlay();

        ViewModel.ActiveTool = tool;
        UpdateCanvasCursor();
    }

    private void UpdateCanvasCursor()
    {
        AnnotationCanvas.ChangeCursor(
            ViewModel.ActiveTool switch
            {
                EditorTool.Crop => InputSystemCursorShape.Cross,
                EditorTool.Draw => InputSystemCursorShape.Cross,
                EditorTool.Arrow => InputSystemCursorShape.Cross,
                EditorTool.Blur => InputSystemCursorShape.Cross,
                EditorTool.Text => InputSystemCursorShape.IBeam,
                _ => InputSystemCursorShape.Arrow
            });
    }

    // MARK: - Color & Size

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string hex)
        {
            _currentColor = ParseHexColor(hex);
        }
    }

    private static Windows.UI.Color ParseHexColor(string hex)
    {
        var (r, g, b) = ArrowGeometry.ParseHexColor(hex);
        return Windows.UI.Color.FromArgb(255, r, g, b);
    }

    // MARK: - Canvas Pointer Events

    private void OnCanvasPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(AnnotationCanvas).Position;
        _isPointerDown = true;
        _pointerStart = pos;
        AnnotationCanvas.CapturePointer(e.Pointer);

        switch (ViewModel.ActiveTool)
        {
            case EditorTool.Draw:
                BeginDraw(pos);
                break;
            case EditorTool.Arrow:
                BeginArrow(pos);
                break;
            case EditorTool.Blur:
                BeginBlurRect(pos);
                break;
            case EditorTool.Crop:
                BeginCrop(pos);
                break;
            case EditorTool.Text:
                PlaceText(pos);
                break;
        }
    }

    private void OnCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPointerDown) return;
        var pos = e.GetCurrentPoint(AnnotationCanvas).Position;

        switch (ViewModel.ActiveTool)
        {
            case EditorTool.Draw:
                ContinueDraw(pos);
                break;
            case EditorTool.Arrow:
                ContinueArrow(pos);
                break;
            case EditorTool.Blur:
                ContinueBlurRect(pos);
                break;
            case EditorTool.Crop:
                ContinueCrop(pos);
                break;
        }
    }

    private void OnCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPointerDown) return;
        _isPointerDown = false;
        AnnotationCanvas.ReleasePointerCapture(e.Pointer);
        var pos = e.GetCurrentPoint(AnnotationCanvas).Position;

        switch (ViewModel.ActiveTool)
        {
            case EditorTool.Draw:
                FinishDraw();
                break;
            case EditorTool.Arrow:
                FinishArrow(pos);
                break;
            case EditorTool.Blur:
                FinishBlurRect(pos);
                break;
            case EditorTool.Crop:
                FinishCrop(pos);
                break;
        }
    }

    // MARK: - Draw Tool

    private void BeginDraw(Windows.Foundation.Point pos)
    {
        var thickness = StrokeSizeSlider.Value;
        var polyline = new Polyline
        {
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        };
        polyline.Points.Add(pos);
        AnnotationCanvas.Children.Add(polyline);

        _activePolyline = polyline;
        _activeDrawAction = new DrawAction
        {
            Color = _currentColor,
            Thickness = thickness,
            Visual = polyline
        };
        _activeDrawAction.Points.Add(pos);
    }

    private void ContinueDraw(Windows.Foundation.Point pos)
    {
        _activePolyline?.Points.Add(pos);
        _activeDrawAction?.Points.Add(pos);
    }

    private void FinishDraw()
    {
        if (_activeDrawAction is { Points.Count: >= 2 })
        {
            _actions.Add(_activeDrawAction);
            ViewModel.UpdateActionCount(_actions.Count);
        }
        else
        {
            // Too few points — remove visual
            if (_activePolyline != null)
                AnnotationCanvas.Children.Remove(_activePolyline);
        }
        _activePolyline = null;
        _activeDrawAction = null;
    }

    // MARK: - Arrow Tool

    private void BeginArrow(Windows.Foundation.Point pos)
    {
        var thickness = StrokeSizeSlider.Value;
        var line = new Line
        {
            X1 = pos.X, Y1 = pos.Y,
            X2 = pos.X, Y2 = pos.Y,
            Stroke = new SolidColorBrush(_currentColor),
            StrokeThickness = thickness,
            IsHitTestVisible = false
        };
        AnnotationCanvas.Children.Add(line);
        _activeArrowLine = line;
    }

    private void ContinueArrow(Windows.Foundation.Point pos)
    {
        if (_activeArrowLine == null) return;
        _activeArrowLine.X2 = pos.X;
        _activeArrowLine.Y2 = pos.Y;
    }

    private void FinishArrow(Windows.Foundation.Point pos)
    {
        if (_activeArrowLine == null) return;

        double dx = pos.X - _pointerStart.X;
        double dy = pos.Y - _pointerStart.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 5)
        {
            AnnotationCanvas.Children.Remove(_activeArrowLine);
            _activeArrowLine = null;
            return;
        }

        // Add arrowhead
        var arrowhead = CreateArrowhead(
            _pointerStart, pos, StrokeSizeSlider.Value, _currentColor);
        AnnotationCanvas.Children.Add(arrowhead);

        var action = new ArrowAction
        {
            Start = _pointerStart,
            End = pos,
            Color = _currentColor,
            Thickness = StrokeSizeSlider.Value,
            Visual = _activeArrowLine,
            Arrowhead = arrowhead
        };
        _actions.Add(action);
        ViewModel.UpdateActionCount(_actions.Count);

        _activeArrowLine = null;
    }

    private static Polygon CreateArrowhead(
        Windows.Foundation.Point start, Windows.Foundation.Point end,
        double thickness, Windows.UI.Color color)
    {
        var result = ArrowGeometry.CalculateArrowhead(start.X, start.Y, end.X, end.Y, thickness);
        if (result is null) return new Polygon();

        var (tip, left, right) = result.Value;
        var polygon = new Polygon
        {
            Fill = new SolidColorBrush(color),
            IsHitTestVisible = false
        };
        polygon.Points.Add(new Windows.Foundation.Point(tip.X, tip.Y));
        polygon.Points.Add(new Windows.Foundation.Point(left.X, left.Y));
        polygon.Points.Add(new Windows.Foundation.Point(right.X, right.Y));
        return polygon;
    }

    // MARK: - Blur Tool

    private void BeginBlurRect(Windows.Foundation.Point pos)
    {
        var rect = new Rectangle
        {
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 255, 255, 255)),
            StrokeThickness = 1,
            StrokeDashArray = [4, 4],
            Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 128, 128, 128)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(rect, pos.X);
        Canvas.SetTop(rect, pos.Y);
        rect.Width = 0;
        rect.Height = 0;
        AnnotationCanvas.Children.Add(rect);
        _activeBlurRect = rect;
    }

    private void ContinueBlurRect(Windows.Foundation.Point pos)
    {
        if (_activeBlurRect == null) return;

        double x = Math.Min(pos.X, _pointerStart.X);
        double y = Math.Min(pos.Y, _pointerStart.Y);
        double w = Math.Abs(pos.X - _pointerStart.X);
        double h = Math.Abs(pos.Y - _pointerStart.Y);

        Canvas.SetLeft(_activeBlurRect, x);
        Canvas.SetTop(_activeBlurRect, y);
        _activeBlurRect.Width = w;
        _activeBlurRect.Height = h;
    }

    private void FinishBlurRect(Windows.Foundation.Point pos)
    {
        if (_activeBlurRect == null) return;

        double x = Math.Min(pos.X, _pointerStart.X);
        double y = Math.Min(pos.Y, _pointerStart.Y);
        double w = Math.Abs(pos.X - _pointerStart.X);
        double h = Math.Abs(pos.Y - _pointerStart.Y);

        if (w < 5 || h < 5)
        {
            AnnotationCanvas.Children.Remove(_activeBlurRect);
            _activeBlurRect = null;
            return;
        }

        var region = new Rect(x, y, w, h);

        // Generate pixelated preview
        var previewImage = CreateBlurPreview(region);
        if (previewImage != null)
        {
            Canvas.SetLeft(previewImage, x);
            Canvas.SetTop(previewImage, y);
            previewImage.IsHitTestVisible = false;
            AnnotationCanvas.Children.Add(previewImage);

            // Remove the dashed rectangle, replace with preview
            AnnotationCanvas.Children.Remove(_activeBlurRect);

            var action = new BlurAction
            {
                Region = region,
                Visual = _activeBlurRect,
                Preview = previewImage
            };
            _actions.Add(action);
        }
        else
        {
            // Fallback: keep the dashed rect as indicator
            var action = new BlurAction
            {
                Region = region,
                Visual = _activeBlurRect
            };
            _actions.Add(action);
        }

        ViewModel.UpdateActionCount(_actions.Count);
        _activeBlurRect = null;
    }

    private Image? CreateBlurPreview(Rect region)
    {
        try
        {
            int x = Math.Max(0, (int)region.X);
            int y = Math.Max(0, (int)region.Y);
            int w = Math.Min((int)region.Width, _imageWidth - x);
            int h = Math.Min((int)region.Height, _imageHeight - y);
            if (w <= 0 || h <= 0) return null;

            var sourceRect = new System.Drawing.Rectangle(x, y, w, h);
            using var cropped = _bitmap.Clone(sourceRect, _bitmap.PixelFormat);
            using var pixelated = PixelateBitmap(cropped, 12);

            // Convert to BitmapImage via temp stream
            using var ms = new MemoryStream();
            pixelated.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;

            var bi = new BitmapImage();
            var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            ms.CopyTo(ras.AsStreamForWrite());
            ras.Seek(0);
            bi.SetSource(ras);

            var image = new Image
            {
                Source = bi,
                Width = w,
                Height = h,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill
            };
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap PixelateBitmap(Bitmap source, int blockSize)
    {
        int w = source.Width;
        int h = source.Height;
        var result = new Bitmap(w, h, source.PixelFormat);

        // Downscale then upscale for pixelation effect
        int smallW = Math.Max(1, w / blockSize);
        int smallH = Math.Max(1, h / blockSize);

        using var small = new Bitmap(smallW, smallH);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(source, 0, 0, smallW, smallH);
        }
        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(small, 0, 0, w, h);
        }
        return result;
    }

    // MARK: - Crop Tool

    private void BeginCrop(Windows.Foundation.Point pos)
    {
        ClearCropOverlay();
        _cropRect = new Rect(pos.X, pos.Y, 0, 0);
        CreateCropOverlayElements();
    }

    private void ContinueCrop(Windows.Foundation.Point pos)
    {
        double x = Math.Max(0, Math.Min(pos.X, _pointerStart.X));
        double y = Math.Max(0, Math.Min(pos.Y, _pointerStart.Y));
        double right = Math.Min(_imageWidth, Math.Max(pos.X, _pointerStart.X));
        double bottom = Math.Min(_imageHeight, Math.Max(pos.Y, _pointerStart.Y));

        _cropRect = new Rect(x, y, right - x, bottom - y);
        UpdateCropOverlayPositions();
    }

    private void FinishCrop(Windows.Foundation.Point pos)
    {
        if (_cropRect is { Width: >= 10, Height: >= 10 })
        {
            ApplyCropButton.IsEnabled = true;
        }
        else
        {
            ClearCropOverlay();
            _cropRect = null;
        }
    }

    private void CreateCropOverlayElements()
    {
        var maskBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(160, 0, 0, 0));
        var borderBrush = new SolidColorBrush(Colors.White);

        _cropTop = new Rectangle { Fill = maskBrush, IsHitTestVisible = false };
        _cropBottom = new Rectangle { Fill = maskBrush, IsHitTestVisible = false };
        _cropLeft = new Rectangle { Fill = maskBrush, IsHitTestVisible = false };
        _cropRight = new Rectangle { Fill = maskBrush, IsHitTestVisible = false };
        _cropBorder = new Rectangle
        {
            Stroke = borderBrush,
            StrokeThickness = 2,
            StrokeDashArray = [6, 3],
            Fill = new SolidColorBrush(Colors.Transparent),
            IsHitTestVisible = false
        };

        AnnotationCanvas.Children.Add(_cropTop);
        AnnotationCanvas.Children.Add(_cropBottom);
        AnnotationCanvas.Children.Add(_cropLeft);
        AnnotationCanvas.Children.Add(_cropRight);
        AnnotationCanvas.Children.Add(_cropBorder);
    }

    private void UpdateCropOverlayPositions()
    {
        if (_cropRect is not { } cr) return;

        double left = cr.X;
        double top = cr.Y;
        double right = cr.X + cr.Width;
        double bottom = cr.Y + cr.Height;

        // Top mask
        if (_cropTop != null)
        {
            Canvas.SetLeft(_cropTop, 0); Canvas.SetTop(_cropTop, 0);
            _cropTop.Width = _imageWidth; _cropTop.Height = Math.Max(0, top);
        }
        // Bottom mask
        if (_cropBottom != null)
        {
            Canvas.SetLeft(_cropBottom, 0); Canvas.SetTop(_cropBottom, bottom);
            _cropBottom.Width = _imageWidth; _cropBottom.Height = Math.Max(0, _imageHeight - bottom);
        }
        // Left mask
        if (_cropLeft != null)
        {
            Canvas.SetLeft(_cropLeft, 0); Canvas.SetTop(_cropLeft, top);
            _cropLeft.Width = Math.Max(0, left); _cropLeft.Height = Math.Max(0, bottom - top);
        }
        // Right mask
        if (_cropRight != null)
        {
            Canvas.SetLeft(_cropRight, right); Canvas.SetTop(_cropRight, top);
            _cropRight.Width = Math.Max(0, _imageWidth - right); _cropRight.Height = Math.Max(0, bottom - top);
        }
        // Border
        if (_cropBorder != null)
        {
            Canvas.SetLeft(_cropBorder, left); Canvas.SetTop(_cropBorder, top);
            _cropBorder.Width = Math.Max(0, cr.Width); _cropBorder.Height = Math.Max(0, cr.Height);
        }
    }

    private void ClearCropOverlay()
    {
        UIElement?[] elements = [_cropTop, _cropBottom, _cropLeft, _cropRight, _cropBorder];
        foreach (var el in elements)
        {
            if (el != null) AnnotationCanvas.Children.Remove(el);
        }
        _cropTop = _cropBottom = _cropLeft = _cropRight = _cropBorder = null;
        _cropRect = null;
        ApplyCropButton.IsEnabled = false;
    }

    private void OnApplyCropClick(object sender, RoutedEventArgs e)
    {
        ApplyCrop();
    }

    private void OnCancelCropClick(object sender, RoutedEventArgs e)
    {
        ClearCropOverlay();
    }

    private void ApplyCrop()
    {
        if (_cropRect is not { Width: >= 10, Height: >= 10 } cr) return;

        int x = Math.Max(0, (int)cr.X);
        int y = Math.Max(0, (int)cr.Y);
        int w = Math.Min((int)cr.Width, _imageWidth - x);
        int h = Math.Min((int)cr.Height, _imageHeight - y);

        try
        {
            var cropRect = new System.Drawing.Rectangle(x, y, w, h);
            var cropped = _bitmap.Clone(cropRect, _bitmap.PixelFormat);
            _bitmap.Dispose();
            _bitmap = cropped;

            // Clear all annotations (they'd be misaligned after crop)
            ClearAllAnnotations();

            _imageWidth = _bitmap.Width;
            _imageHeight = _bitmap.Height;

            // Refresh display
            _ = RefreshImageDisplay();
        }
        catch (Exception ex)
        {
            Services.NotificationService.Instance.ShowErrorNotification($"Crop failed: {ex.Message}");
        }

        ClearCropOverlay();
        SetActiveTool(EditorTool.None);
        foreach (var tb in _toolToggles) tb.IsChecked = false;
    }

    private void ClearAllAnnotations()
    {
        AnnotationCanvas.Children.Clear();
        _actions.Clear();
        ViewModel.UpdateActionCount(_actions.Count);
    }

    private async Task RefreshImageDisplay()
    {
        // Save updated bitmap to temp
        if (_tempPath != null)
        {
            try { File.Delete(_tempPath); } catch (Exception ex) { Services.AppLog.Error("Delete temp file failed", ex); }
        }
        _tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"TinyClips_edit_{Guid.NewGuid():N}.png");
        await Task.Run(() => _bitmap.Save(_tempPath, System.Drawing.Imaging.ImageFormat.Png));

        var bi = new BitmapImage();
        bi.UriSource = new Uri(_tempPath);
        ScreenshotImage.Source = bi;

        ScreenshotImage.Width = _imageWidth;
        ScreenshotImage.Height = _imageHeight;
        CanvasContainer.Width = _imageWidth;
        CanvasContainer.Height = _imageHeight;
        AnnotationCanvas.Width = _imageWidth;
        AnnotationCanvas.Height = _imageHeight;
    }

    // MARK: - Text Tool

    private void PlaceText(Windows.Foundation.Point pos)
    {
        CommitActiveText();

        var textBox = new TextBox
        {
            Text = "",
            PlaceholderText = "Type here...",
            FontSize = _currentFontSize,
            Foreground = new SolidColorBrush(_currentColor),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 30, 30, 30)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 255, 255, 255)),
            MinWidth = 100,
            MaxWidth = _imageWidth - pos.X,
            Padding = new Thickness(4, 2, 4, 2)
        };

        Canvas.SetLeft(textBox, pos.X);
        Canvas.SetTop(textBox, pos.Y);
        AnnotationCanvas.Children.Add(textBox);
        textBox.Focus(FocusState.Programmatic);

        _activeTextBox = textBox;
    }

    private void CommitActiveText()
    {
        if (_activeTextBox == null) return;

        var text = _activeTextBox.Text.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            double x = Canvas.GetLeft(_activeTextBox);
            double y = Canvas.GetTop(_activeTextBox);

            // Replace TextBox with a TextBlock
            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = _currentFontSize,
                Foreground = new SolidColorBrush(_currentColor),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(textBlock, x);
            Canvas.SetTop(textBlock, y);

            AnnotationCanvas.Children.Remove(_activeTextBox);
            AnnotationCanvas.Children.Add(textBlock);

            var action = new TextAction
            {
                Position = new Windows.Foundation.Point(x, y),
                Text = text,
                Color = _currentColor,
                FontSize = _currentFontSize,
                Visual = textBlock
            };
            _actions.Add(action);
            ViewModel.UpdateActionCount(_actions.Count);
        }
        else
        {
            AnnotationCanvas.Children.Remove(_activeTextBox);
        }

        _activeTextBox = null;
    }

    // MARK: - Undo

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        Undo();
    }

    private void Undo()
    {
        if (_actions.Count == 0) return;
        var last = _actions[^1];
        _actions.RemoveAt(_actions.Count - 1);

        if (last.Visual != null)
            AnnotationCanvas.Children.Remove(last.Visual);

        if (last is ArrowAction arrow && arrow.Arrowhead != null)
            AnnotationCanvas.Children.Remove(arrow.Arrowhead);

        if (last is BlurAction blur && blur.Preview != null)
            AnnotationCanvas.Children.Remove(blur.Preview);

        ViewModel.UpdateActionCount(_actions.Count);
    }

    // MARK: - Save & Discard

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        CommitActiveText();
        SaveAndClose();
    }

    private void OnDiscardClick(object sender, RoutedEventArgs e)
    {
        DiscardAndClose();
    }

    private void SaveAndClose()
    {
        try
        {
            // Composite all annotations onto the bitmap
            CompositeAnnotations();

            // Save to final path
            string path = Services.SaveService.Instance.GeneratePath(CaptureType.Screenshot);
            SaveBitmapToPath(_bitmap, path);

            _didCallback = true;
            OnSaved?.Invoke(path);
            Close();
        }
        catch (Exception ex)
        {
            Services.NotificationService.Instance.ShowErrorNotification($"Save failed: {ex.Message}");
        }
    }

    private void DiscardAndClose()
    {
        _didCallback = true;
        OnDiscarded?.Invoke();
        Close();
    }

    // MARK: - Compositing (GDI+)

    private void CompositeAnnotations()
    {
        using var g = Graphics.FromImage(_bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Canvas coordinates are in DIPs; bitmap is in physical pixels.
        // Scale all annotation coordinates by _dpiScale.
        float s = (float)_dpiScale;

        foreach (var action in _actions)
        {
            switch (action)
            {
                case DrawAction draw:
                    CompositeDraw(g, draw, s);
                    break;
                case ArrowAction arrow:
                    CompositeArrow(g, arrow, s);
                    break;
                case BlurAction blur:
                    CompositeBlur(blur, s);
                    break;
                case TextAction text:
                    CompositeText(g, text, s);
                    break;
            }
        }
    }

    private static void CompositeDraw(Graphics g, DrawAction draw, float s)
    {
        if (draw.Points.Count < 2) return;
        using var pen = new Pen(ToDrawingColor(draw.Color), (float)draw.Thickness * s)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        var points = draw.Points.Select(p => new PointF((float)p.X * s, (float)p.Y * s)).ToArray();
        g.DrawLines(pen, points);
    }

    private static void CompositeArrow(Graphics g, ArrowAction arrow, float s)
    {
        using var pen = new Pen(ToDrawingColor(arrow.Color), (float)arrow.Thickness * s)
        {
            StartCap = LineCap.Round
        };

        // Draw line
        g.DrawLine(pen,
            (float)arrow.Start.X * s, (float)arrow.Start.Y * s,
            (float)arrow.End.X * s, (float)arrow.End.Y * s);

        // Draw arrowhead as filled triangle
        double dx = arrow.End.X - arrow.Start.X;
        double dy = arrow.End.Y - arrow.Start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1) return;

        double ux = dx / length;
        double uy = dy / length;
        double headLen = Math.Max(arrow.Thickness * 4, 12) * s;
        double headW = Math.Max(arrow.Thickness * 2.5, 8) * s;

        var baseX = (float)(arrow.End.X * s - ux * headLen);
        var baseY = (float)(arrow.End.Y * s - uy * headLen);

        var headPoints = new[]
        {
            new PointF((float)arrow.End.X * s, (float)arrow.End.Y * s),
            new PointF((float)(baseX + uy * headW), (float)(baseY - ux * headW)),
            new PointF((float)(baseX - uy * headW), (float)(baseY + ux * headW))
        };
        using var brush = new SolidBrush(ToDrawingColor(arrow.Color));
        g.FillPolygon(brush, headPoints);
    }

    private void CompositeBlur(BlurAction blur, float s)
    {
        int x = Math.Max(0, (int)(blur.Region.X * s));
        int y = Math.Max(0, (int)(blur.Region.Y * s));
        int w = Math.Min((int)(blur.Region.Width * s), _imageWidth - x);
        int h = Math.Min((int)(blur.Region.Height * s), _imageHeight - y);
        if (w <= 0 || h <= 0) return;

        var sourceRect = new System.Drawing.Rectangle(x, y, w, h);
        using var cropped = _bitmap.Clone(sourceRect, _bitmap.PixelFormat);
        using var pixelated = PixelateBitmap(cropped, 12);

        using var g = Graphics.FromImage(_bitmap);
        g.DrawImage(pixelated, x, y, w, h);
    }

    private static void CompositeText(Graphics g, TextAction text, float s)
    {
        using var brush = new SolidBrush(ToDrawingColor(text.Color));
        using var font = new Font("Segoe UI", (float)text.FontSize * 0.75f * s, FontStyle.Bold, GraphicsUnit.Point);
        g.DrawString(text.Text, font, brush, (float)text.Position.X * s, (float)text.Position.Y * s);
    }

    private static System.Drawing.Color ToDrawingColor(Windows.UI.Color c)
    {
        return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
    }

    private static void SaveBitmapToPath(Bitmap bitmap, string outputPath)
    {
        var dir = System.IO.Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var settings = CaptureSettings.Instance;

        if (settings.ScreenshotFormat == Models.ImageFormat.Jpeg)
        {
            var encoder = ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
            if (encoder != null)
            {
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)settings.JpegQuality);
                bitmap.Save(outputPath, encoder, encoderParams);
            }
            else
            {
                bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
        }
        else
        {
            bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    // MARK: - Keyboard Shortcuts

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (ctrl && e.Key == Windows.System.VirtualKey.Z)
        {
            Undo();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Windows.System.VirtualKey.S)
        {
            CommitActiveText();
            SaveAndClose();
            e.Handled = true;
            return;
        }

        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            if (ViewModel.ActiveTool == EditorTool.Crop && _cropRect != null)
            {
                ClearCropOverlay();
                e.Handled = true;
                return;
            }
            if (_activeTextBox != null)
            {
                CommitActiveText();
                e.Handled = true;
                return;
            }
            // Deselect tool
            SetActiveTool(EditorTool.None);
            foreach (var tb in _toolToggles) tb.IsChecked = false;
            e.Handled = true;
            return;
        }

        // Tool shortcuts (only when not typing in text box)
        if (_activeTextBox != null) return;

        switch (e.Key)
        {
            case Windows.System.VirtualKey.C:
                SelectToggle(CropToggle, EditorTool.Crop);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.D:
                SelectToggle(DrawToggle, EditorTool.Draw);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.A:
                SelectToggle(ArrowToggle, EditorTool.Arrow);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.B:
                SelectToggle(BlurToggle, EditorTool.Blur);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.T:
                SelectToggle(TextToggle, EditorTool.Text);
                e.Handled = true;
                break;
        }
    }

    private void SelectToggle(ToggleButton target, EditorTool tool)
    {
        CommitActiveText();
        foreach (var tb in _toolToggles)
            tb.IsChecked = tb == target;
        SetActiveTool(tool);
    }

    // MARK: - Cleanup

    private void OnWindowClosed(object sender, WindowEventArgs e)
    {
        // Clean up temp file
        if (_tempPath != null)
        {
            try { File.Delete(_tempPath); } catch (Exception ex) { Services.AppLog.Error("Delete temp file failed", ex); }
        }

        _bitmap?.Dispose();

        if (!_didCallback)
            OnDiscarded?.Invoke();
    }
}

// MARK: - Cursor Extension

internal static class CanvasCursorExtension
{
    public static void ChangeCursor(this UIElement element, InputSystemCursorShape shape)
    {
        element.ChangeCursor(InputSystemCursor.Create(shape));
    }

    public static void ChangeCursor(this UIElement element, InputCursor cursor)
    {
        typeof(UIElement).InvokeMember(
            "ProtectedCursor",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.SetProperty | System.Reflection.BindingFlags.Instance,
            null, element, [cursor]);
    }
}
