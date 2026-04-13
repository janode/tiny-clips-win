using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Gif;
using TinyClips.Models;
using ISImage = SixLabors.ImageSharp.Image;

namespace TinyClips.Capture;

/// <summary>
/// Records screen frames and encodes them as an animated GIF using ImageSharp.
/// </summary>
public sealed class GifWriter : IDisposable
{
    private volatile bool _isRecording;
    private Thread? _captureThread;
    private System.Drawing.Rectangle _captureRect;
    private string? _outputPath;
    private double _fps;
    private int _maxWidth;
    private DateTime _startTime;
    private readonly List<(byte[] Pixels, int Width, int Height)> _frames = new();

    public bool IsRecording => _isRecording;
    public TimeSpan Elapsed => _isRecording ? DateTime.Now - _startTime : TimeSpan.Zero;

    public Action<TimeSpan>? OnElapsedChanged { get; set; }

    public Task StartAsync(System.Drawing.Rectangle captureRect, string outputPath)
    {
        if (_isRecording)
            throw new InvalidOperationException("Already recording.");

        var settings = CaptureSettings.Instance;
        _captureRect = captureRect;
        _outputPath = outputPath;
        _fps = settings.GifFrameRate;
        _maxWidth = settings.GifMaxWidth;
        _isRecording = true;
        _startTime = DateTime.Now;
        _frames.Clear();

        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "GifWriter_Capture",
            Priority = ThreadPriority.AboveNormal
        };
        _captureThread.Start();

        return Task.CompletedTask;
    }

    public async Task<string> StopAsync()
    {
        if (!_isRecording)
            throw new InvalidOperationException("Not recording.");

        _isRecording = false;
        _captureThread?.Join(5000);

        if (_frames.Count == 0 || _outputPath == null)
            throw new InvalidOperationException("No frames were captured.");

        await EncodeGifAsync();
        return _outputPath;
    }

    private void CaptureLoop()
    {
        var frameInterval = TimeSpan.FromSeconds(1.0 / _fps);
        var stopwatch = Stopwatch.StartNew();

        while (_isRecording)
        {
            var frameStart = stopwatch.Elapsed;

            try
            {
                using var bitmap = new Bitmap(_captureRect.Width, _captureRect.Height, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(_captureRect.Left, _captureRect.Top, 0, 0,
                    _captureRect.Size, CopyPixelOperation.SourceCopy);

                // Extract raw pixel data
                var bitmapData = bitmap.LockBits(
                    new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                var bytes = new byte[bitmapData.Stride * bitmapData.Height];
                System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, bytes, 0, bytes.Length);
                bitmap.UnlockBits(bitmapData);

                lock (_frames)
                {
                    _frames.Add((bytes, bitmap.Width, bitmap.Height));
                }

                OnElapsedChanged?.Invoke(DateTime.Now - _startTime);
            }
            catch
            {
                if (!_isRecording) break;
            }

            var elapsed = stopwatch.Elapsed - frameStart;
            var sleepTime = frameInterval - elapsed;
            if (sleepTime > TimeSpan.Zero)
                Thread.Sleep(sleepTime);
        }
    }

    private async Task EncodeGifAsync()
    {
        await Task.Run(() =>
        {
            if (_frames.Count == 0) return;

            var (firstPixels, srcWidth, srcHeight) = _frames[0];

            // Calculate scaled dimensions
            int destWidth = srcWidth;
            int destHeight = srcHeight;
            if (destWidth > _maxWidth)
            {
                double scale = (double)_maxWidth / destWidth;
                destWidth = _maxWidth;
                destHeight = (int)(srcHeight * scale);
            }

            // Ensure even dimensions
            destWidth = destWidth & ~1;
            destHeight = destHeight & ~1;
            if (destWidth < 2) destWidth = 2;
            if (destHeight < 2) destHeight = 2;

            int frameDelay = Math.Max(1, (int)(100.0 / _fps)); // GIF delay in centiseconds

            using var gif = new Image<Rgba32>(destWidth, destHeight);
            gif.Metadata.GetGifMetadata().RepeatCount = 0; // Loop forever

            Image<Rgba32>? previousFrame = null;

            try
            {
                for (int i = 0; i < _frames.Count; i++)
                {
                    var (pixels, w, h) = _frames[i];
                    using var frame = BgraToImageSharp(pixels, w, h);

                    if (frame.Width != destWidth || frame.Height != destHeight)
                    {
                        frame.Mutate(x => x.Resize(destWidth, destHeight));
                    }

                    Image<Rgba32> frameToAdd;

                    if (i == 0 || previousFrame == null)
                    {
                        // First frame: add as-is (full image)
                        frameToAdd = frame;
                    }
                    else
                    {
                        // Subsequent frames: diff against previous, transparent for unchanged pixels
                        frameToAdd = ComputeFrameDiff(previousFrame, frame);
                    }

                    var addedFrame = gif.Frames.AddFrame(frameToAdd.Frames.RootFrame);
                    var frameMeta = addedFrame.Metadata.GetGifMetadata();
                    frameMeta.FrameDelay = frameDelay;
                    frameMeta.DisposalMethod = GifDisposalMethod.NotDispose;

                    if (frameToAdd != frame)
                        frameToAdd.Dispose();

                    previousFrame?.Dispose();
                    previousFrame = frame.Clone();
                }
            }
            finally
            {
                previousFrame?.Dispose();
            }

            // Remove the default placeholder frame created by new Image<>(...)
            gif.Frames.RemoveFrame(0);

            var dir = Path.GetDirectoryName(_outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var encoder = new GifEncoder
            {
                ColorTableMode = GifColorTableMode.Global
            };
            gif.Save(_outputPath!, encoder);
        });
    }

    /// <summary>
    /// Computes a diff frame: unchanged pixels become transparent, changed pixels are kept.
    /// Uses a small tolerance to absorb minor GDI+ capture noise.
    /// </summary>
    internal static Image<Rgba32> ComputeFrameDiff(Image<Rgba32> previous, Image<Rgba32> current)
    {
        int width = current.Width;
        int height = current.Height;
        var diff = new Image<Rgba32>(width, height, new Rgba32(0, 0, 0, 0));

        bool allSame = true;

        for (int y = 0; y < height; y++)
        {
            var prevRow = previous.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y);
            var curRow = current.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y);
            var diffRow = diff.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y);

            for (int x = 0; x < width; x++)
            {
                ref readonly var prev = ref prevRow[x];
                ref readonly var cur = ref curRow[x];

                // Tolerance of 2 per channel absorbs minor GDI+ capture noise
                if (Math.Abs(prev.R - cur.R) <= 2 &&
                    Math.Abs(prev.G - cur.G) <= 2 &&
                    Math.Abs(prev.B - cur.B) <= 2)
                {
                    // Unchanged — leave transparent (already default)
                }
                else
                {
                    diffRow[x] = cur;
                    allSame = false;
                }
            }
        }

        // If entire frame is unchanged, mark a single pixel to avoid an empty frame
        if (allSame)
        {
            diff.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(0)[0] = current[0, 0];
        }

        return diff;
    }

    internal static Image<Rgba32> BgraToImageSharp(byte[] bgraPixels, int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        int stride = width * 4;

        for (int y = 0; y < height; y++)
        {
            var rowSpan = image.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y);
            int rowOffset = y * stride;

            for (int x = 0; x < width; x++)
            {
                int pixelOffset = rowOffset + x * 4;
                byte b = bgraPixels[pixelOffset];
                byte g = bgraPixels[pixelOffset + 1];
                byte r = bgraPixels[pixelOffset + 2];
                // CopyFromScreen (BitBlt) leaves alpha undefined/zero — force opaque
                rowSpan[x] = new Rgba32(r, g, b, 255);
            }
        }

        return image;
    }

    public void Dispose()
    {
        if (_isRecording)
        {
            _isRecording = false;
            _captureThread?.Join(2000);
        }
        _frames.Clear();
    }
}
