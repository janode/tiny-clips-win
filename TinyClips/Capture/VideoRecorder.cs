using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using TinyClips.Models;

namespace TinyClips.Capture;

/// <summary>
/// Records screen to MP4 video using GDI+ frame capture + ffmpeg encoding.
/// Uses a separate ffmpeg process for encoding (simpler than MFSinkWriter COM interop).
/// Falls back to raw frame saving if ffmpeg is not available.
/// </summary>
public sealed class VideoRecorder : IDisposable
{
    private volatile bool _isRecording;
    private Thread? _captureThread;
    private Rectangle _captureRect;
    private string? _outputPath;
    private int _fps;
    private DateTime _startTime;

    // For ffmpeg-based encoding
    private Process? _ffmpegProcess;

    public bool IsRecording => _isRecording;
    public TimeSpan Elapsed => _isRecording ? DateTime.Now - _startTime : TimeSpan.Zero;

    public Action<TimeSpan>? OnElapsedChanged { get; set; }

    /// <summary>
    /// Start recording the given screen region.
    /// </summary>
    public Task StartAsync(Rectangle captureRect, string outputPath, int fps = 30)
    {
        if (_isRecording)
            throw new InvalidOperationException("Already recording.");

        _captureRect = captureRect;
        _outputPath = outputPath;
        _fps = fps;
        _isRecording = true;
        _startTime = DateTime.Now;

        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Name = "VideoRecorder_Capture",
            Priority = ThreadPriority.AboveNormal
        };
        _captureThread.Start();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stop recording and finalize the output file.
    /// </summary>
    public Task<string> StopAsync()
    {
        if (!_isRecording)
            throw new InvalidOperationException("Not recording.");

        _isRecording = false;
        _captureThread?.Join(5000);

        // Close ffmpeg stdin to signal end of input
        try
        {
            _ffmpegProcess?.StandardInput.BaseStream.Close();
            _ffmpegProcess?.WaitForExit(10000);
        }
        catch { }

        _ffmpegProcess?.Dispose();
        _ffmpegProcess = null;

        return Task.FromResult(_outputPath ?? string.Empty);
    }

    private void CaptureLoop()
    {
        var frameInterval = TimeSpan.FromSeconds(1.0 / _fps);
        var ffmpegPath = FindFfmpeg();

        if (ffmpegPath != null && _outputPath != null)
        {
            StartFfmpegProcess(ffmpegPath);
            CaptureToFfmpeg(frameInterval);
        }
        else if (_outputPath != null)
        {
            // Fallback: capture frames as individual images (user can convert later)
            CaptureToFrames(frameInterval);
        }
    }

    private void StartFfmpegProcess(string ffmpegPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-y -f rawvideo -pix_fmt bgra -s {_captureRect.Width}x{_captureRect.Height} " +
                        $"-r {_fps} -i pipe:0 -c:v libx264 -preset ultrafast " +
                        $"-crf 18 -pix_fmt yuv420p \"{_outputPath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _ffmpegProcess = Process.Start(psi);
    }

    private void CaptureToFfmpeg(TimeSpan frameInterval)
    {
        using var bitmap = new Bitmap(_captureRect.Width, _captureRect.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);

        var frameBytes = _captureRect.Width * _captureRect.Height * 4;
        var buffer = new byte[frameBytes];
        var stopwatch = Stopwatch.StartNew();

        while (_isRecording && _ffmpegProcess?.HasExited != true)
        {
            var frameStart = stopwatch.Elapsed;

            try
            {
                graphics.CopyFromScreen(_captureRect.Left, _captureRect.Top, 0, 0,
                    _captureRect.Size, CopyPixelOperation.SourceCopy);

                var bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

                System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, buffer, 0, frameBytes);
                bitmap.UnlockBits(bitmapData);

                _ffmpegProcess?.StandardInput.BaseStream.Write(buffer, 0, buffer.Length);

                OnElapsedChanged?.Invoke(DateTime.Now - _startTime);
            }
            catch
            {
                if (!_isRecording) break;
            }

            // Maintain frame rate
            var elapsed = stopwatch.Elapsed - frameStart;
            var sleepTime = frameInterval - elapsed;
            if (sleepTime > TimeSpan.Zero)
                Thread.Sleep(sleepTime);
        }
    }

    private void CaptureToFrames(TimeSpan frameInterval)
    {
        // Fallback: save individual frames as PNG for later assembly
        var framesDir = Path.Combine(
            Path.GetDirectoryName(_outputPath) ?? Path.GetTempPath(),
            "TinyClips_frames_" + Path.GetFileNameWithoutExtension(_outputPath));
        Directory.CreateDirectory(framesDir);

        int frameNumber = 0;
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

                bitmap.Save(Path.Combine(framesDir, $"frame_{frameNumber:D6}.png"),
                    System.Drawing.Imaging.ImageFormat.Png);
                frameNumber++;

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

    private static string? FindFfmpeg()
    {
        // Check bundled location first
        var appDir = AppContext.BaseDirectory;
        var bundled = Path.Combine(appDir, "ffmpeg.exe");
        if (File.Exists(bundled)) return bundled;

        // Check PATH
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "ffmpeg",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            var output = process?.StandardOutput.ReadToEnd().Trim();
            process?.WaitForExit();
            if (process?.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                return output.Split('\n')[0].Trim();
            }
        }
        catch { }

        return null;
    }

    public void Dispose()
    {
        if (_isRecording)
        {
            _isRecording = false;
            _captureThread?.Join(2000);
        }
        _ffmpegProcess?.Dispose();
    }
}
