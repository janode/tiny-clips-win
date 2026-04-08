using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using NAudio.Wave;
using TinyClips.Models;

namespace TinyClips.Capture;

/// <summary>
/// Records screen to MP4 video using GDI+ frame capture + Media Foundation H.264 encoding.
/// Uses Windows built-in Media Foundation SinkWriter — no external dependencies needed.
/// </summary>
public sealed class VideoRecorder : IDisposable
{
    private volatile bool _isRecording;
    private Thread? _captureThread;
    private Rectangle _captureRect;
    private string? _outputPath;
    private int _fps;
    private bool _recordAudio;
    private SystemAudioCapture? _audioCapture;
    private DateTime _startTime;
    private Exception? _captureError;

    public bool IsRecording => _isRecording;
    public TimeSpan Elapsed => _isRecording ? DateTime.Now - _startTime : TimeSpan.Zero;

    public Action<TimeSpan>? OnElapsedChanged { get; set; }

    /// <summary>
    /// Start recording the given screen region.
    /// </summary>
    public Task StartAsync(Rectangle captureRect, string outputPath, int fps = 30, bool recordAudio = false)
    {
        if (_isRecording)
            throw new InvalidOperationException("Already recording.");

        _captureRect = captureRect;
        _outputPath = outputPath;
        _fps = fps;
        _recordAudio = recordAudio;
        _isRecording = true;
        _startTime = DateTime.Now;

        // Start audio capture before video thread so loopback is ready
        if (_recordAudio)
        {
            _audioCapture = new SystemAudioCapture();
            _audioCapture.Start();
        }

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
        _audioCapture?.Stop();
        _captureThread?.Join(10000);
        _audioCapture?.Dispose();
        _audioCapture = null;

        if (_captureError != null)
            throw new InvalidOperationException($"Recording failed: {_captureError.Message}", _captureError);

        return Task.FromResult(_outputPath ?? string.Empty);
    }

    private void CaptureLoop()
    {
        // COM init required for Media Foundation
        int comHr = CoInitializeEx(nint.Zero, COINIT_MULTITHREADED);
        bool comInitialized = comHr >= 0;
        try
        {
        Marshal.ThrowExceptionForHR(MFStartup(MF_VERSION, 0));
        try
        {
            // Build encoder — with audio stream if loopback is active
            WaveFormat? audioFormat = _recordAudio ? _audioCapture?.WaveFormat : null;
            // H.264 requires even width and height
            int encWidth = _captureRect.Width & ~1;
            int encHeight = _captureRect.Height & ~1;
            if (encWidth < 2) encWidth = 2;
            if (encHeight < 2) encHeight = 2;
            var encoder = new MFEncoder(_outputPath!, encWidth, encHeight, _fps, audioFormat);
            try
            {
                var frameInterval = TimeSpan.FromSeconds(1.0 / _fps);
                long frameDuration = 10_000_000L / _fps; // 100-nanosecond units
                long videoTimestamp = 0;

                using var bitmap = new Bitmap(encWidth, encHeight, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(bitmap);
                var stopwatch = Stopwatch.StartNew();

                while (_isRecording)
                {
                    var frameStart = stopwatch.Elapsed;

                    try
                    {
                        graphics.CopyFromScreen(_captureRect.Left, _captureRect.Top, 0, 0,
                            new Size(encWidth, encHeight), CopyPixelOperation.SourceCopy);

                        var bmpData = bitmap.LockBits(
                            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                        try
                        {
                            encoder.WriteVideoFrame(bmpData.Scan0, bmpData.Stride * bmpData.Height,
                                videoTimestamp, frameDuration);
                        }
                        finally { bitmap.UnlockBits(bmpData); }

                        videoTimestamp += frameDuration;

                        // Drain buffered audio chunks and write them
                        _audioCapture?.DrainTo(chunk =>
                            encoder.WriteAudioSamples(chunk.Data, chunk.Timestamp, chunk.Duration));

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

                // Drain any remaining audio after loop ends
                _audioCapture?.DrainTo(chunk =>
                    encoder.WriteAudioSamples(chunk.Data, chunk.Timestamp, chunk.Duration));

                encoder.Finish();
            }
            finally { encoder.Dispose(); }
        }
        finally
        {
            MFShutdown();
        }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"VideoRecorder CaptureLoop error: {ex.Message}");
            _captureError = ex;
        }
        finally
        {
            if (comInitialized) CoUninitialize();
        }
    }

    public void Dispose()
    {
        if (_isRecording)
        {
            _isRecording = false;
            _audioCapture?.Stop();
            _captureThread?.Join(2000);
        }
        _audioCapture?.Dispose();
        _audioCapture = null;
    }

    // MARK: - Media Foundation Encoder (raw vtable calls — no .NET COM interop)

    private sealed class MFEncoder : IDisposable
    {
        private readonly int _width;
        private readonly int _height;
        private readonly nint _writer;
        private readonly int _videoStreamIndex;
        private readonly int _audioStreamIndex = -1;
        private readonly int _frameSize;
        private readonly bool _hasAudio;

        public unsafe MFEncoder(string outputPath, int width, int height, int fps, WaveFormat? audioFormat)
        {
            _width = width;
            _height = height;
            _frameSize = width * height * 4;
            _hasAudio = audioFormat != null;

            // Output type: H.264
            Marshal.ThrowExceptionForHR(MFCreateMediaType(out var videoOutputType));
            MF.SetGUID(videoOutputType, MF_MT_MAJOR_TYPE, MFMediaType_Video);
            MF.SetGUID(videoOutputType, MF_MT_SUBTYPE, MFVideoFormat_H264);
            MF.SetUINT32(videoOutputType, MF_MT_AVG_BITRATE, Math.Max(1_000_000u, (uint)(width * height * fps / 4)));
            MF.SetUINT32(videoOutputType, MF_MT_INTERLACE_MODE, 2); // MFVideoInterlace_Progressive
            MF.SetUINT64(videoOutputType, MF_MT_FRAME_SIZE, Pack2x32((uint)width, (uint)height));
            MF.SetUINT64(videoOutputType, MF_MT_FRAME_RATE, Pack2x32((uint)fps, 1));
            MF.SetUINT64(videoOutputType, MF_MT_PIXEL_ASPECT_RATIO, Pack2x32(1, 1));

            // Input type: RGB32 (matches GDI+ Format32bppArgb — both are BGRA in memory)
            Marshal.ThrowExceptionForHR(MFCreateMediaType(out var videoInputType));
            MF.SetGUID(videoInputType, MF_MT_MAJOR_TYPE, MFMediaType_Video);
            MF.SetGUID(videoInputType, MF_MT_SUBTYPE, MFVideoFormat_RGB32);
            MF.SetUINT32(videoInputType, MF_MT_INTERLACE_MODE, 2);
            MF.SetUINT64(videoInputType, MF_MT_FRAME_SIZE, Pack2x32((uint)width, (uint)height));
            MF.SetUINT64(videoInputType, MF_MT_FRAME_RATE, Pack2x32((uint)fps, 1));
            MF.SetUINT64(videoInputType, MF_MT_PIXEL_ASPECT_RATIO, Pack2x32(1, 1));

            // Create sink writer — .mp4 extension auto-selects MPEG-4 container
            Marshal.ThrowExceptionForHR(MFCreateSinkWriterFromURL(
                outputPath, nint.Zero, nint.Zero, out _writer));

            int streamIdx;
            Marshal.ThrowExceptionForHR(MF.SinkWriter_AddStream(_writer, videoOutputType, &streamIdx));
            _videoStreamIndex = streamIdx;
            Marshal.ThrowExceptionForHR(MF.SinkWriter_SetInputMediaType(_writer, _videoStreamIndex, videoInputType, nint.Zero));

            MF.Release(videoInputType);
            MF.Release(videoOutputType);

            // Audio stream: AAC output, PCM Float input (WASAPI loopback native format)
            if (_hasAudio && audioFormat != null)
            {
                // Output type: AAC
                Marshal.ThrowExceptionForHR(MFCreateMediaType(out var audioOutputType));
                MF.SetGUID(audioOutputType, MF_MT_MAJOR_TYPE, MFMediaType_Audio);
                MF.SetGUID(audioOutputType, MF_MT_SUBTYPE, MFAudioFormat_AAC);
                MF.SetUINT32(audioOutputType, MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                MF.SetUINT32(audioOutputType, MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)audioFormat.SampleRate);
                MF.SetUINT32(audioOutputType, MF_MT_AUDIO_NUM_CHANNELS, (uint)audioFormat.Channels);
                MF.SetUINT32(audioOutputType, MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 24000u); // ~192 kbps AAC
                MF.SetUINT32(audioOutputType, MF_MT_AUDIO_BLOCK_ALIGNMENT, 1);

                // Input type: IEEE Float PCM (native WASAPI loopback format)
                Marshal.ThrowExceptionForHR(MFCreateMediaType(out var audioInputType));
                MF.SetGUID(audioInputType, MF_MT_MAJOR_TYPE, MFMediaType_Audio);
                MF.SetGUID(audioInputType, MF_MT_SUBTYPE, MFAudioFormat_Float);
                MF.SetUINT32(audioInputType, MF_MT_AUDIO_BITS_PER_SAMPLE, (uint)audioFormat.BitsPerSample);
                MF.SetUINT32(audioInputType, MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)audioFormat.SampleRate);
                MF.SetUINT32(audioInputType, MF_MT_AUDIO_NUM_CHANNELS, (uint)audioFormat.Channels);
                MF.SetUINT32(audioInputType, MF_MT_AUDIO_AVG_BYTES_PER_SECOND, (uint)audioFormat.AverageBytesPerSecond);
                MF.SetUINT32(audioInputType, MF_MT_AUDIO_BLOCK_ALIGNMENT, (uint)audioFormat.BlockAlign);

                int audioIdx;
                Marshal.ThrowExceptionForHR(MF.SinkWriter_AddStream(_writer, audioOutputType, &audioIdx));
                _audioStreamIndex = audioIdx;
                Marshal.ThrowExceptionForHR(MF.SinkWriter_SetInputMediaType(_writer, _audioStreamIndex, audioInputType, nint.Zero));

                MF.Release(audioInputType);
                MF.Release(audioOutputType);
            }

            Marshal.ThrowExceptionForHR(MF.SinkWriter_BeginWriting(_writer));
        }

        public unsafe void WriteVideoFrame(nint frameData, int dataLength, long timestamp, long duration)
        {
            Marshal.ThrowExceptionForHR(MFCreateMemoryBuffer(_frameSize, out var buffer));
            try
            {
                nint pbData; int maxLen, curLen;
                MF.Buffer_Lock(buffer, &pbData, &maxLen, &curLen);
                try
                {
                    // Flip top-down (GDI+) to bottom-up (MF RGB32 default)
                    int stride = _width * 4;
                    byte* src = (byte*)frameData;
                    byte* dst = (byte*)pbData;
                    for (int y = 0; y < _height; y++)
                    {
                        Buffer.MemoryCopy(
                            src + (nint)y * stride,
                            dst + (nint)(_height - 1 - y) * stride,
                            stride, stride);
                    }
                }
                finally { MF.Buffer_Unlock(buffer); }
                MF.Buffer_SetCurrentLength(buffer, _frameSize);

                Marshal.ThrowExceptionForHR(MFCreateSample(out var sample));
                try
                {
                    MF.Sample_AddBuffer(sample, buffer);
                    MF.Sample_SetSampleTime(sample, timestamp);
                    MF.Sample_SetSampleDuration(sample, duration);
                    Marshal.ThrowExceptionForHR(MF.SinkWriter_WriteSample(_writer, _videoStreamIndex, sample));
                }
                finally { MF.Release(sample); }
            }
            finally { MF.Release(buffer); }
        }

        public unsafe void WriteAudioSamples(byte[] data, long timestamp, long duration)
        {
            if (!_hasAudio || _audioStreamIndex < 0) return;

            Marshal.ThrowExceptionForHR(MFCreateMemoryBuffer(data.Length, out var buffer));
            try
            {
                nint pbData; int maxLen, curLen;
                MF.Buffer_Lock(buffer, &pbData, &maxLen, &curLen);
                try
                {
                    Marshal.Copy(data, 0, pbData, data.Length);
                }
                finally { MF.Buffer_Unlock(buffer); }
                MF.Buffer_SetCurrentLength(buffer, data.Length);

                Marshal.ThrowExceptionForHR(MFCreateSample(out var sample));
                try
                {
                    MF.Sample_AddBuffer(sample, buffer);
                    MF.Sample_SetSampleTime(sample, timestamp);
                    MF.Sample_SetSampleDuration(sample, duration);
                    Marshal.ThrowExceptionForHR(MF.SinkWriter_WriteSample(_writer, _audioStreamIndex, sample));
                }
                finally { MF.Release(sample); }
            }
            finally { MF.Release(buffer); }
        }

        public void Finish() => MF.SinkWriter_Finalize(_writer);

        public void Dispose() => MF.Release(_writer);

        private static ulong Pack2x32(uint hi, uint lo) => ((ulong)hi << 32) | lo;
    }

    // MARK: - Raw COM vtable helpers (bypasses .NET COM interop entirely)

    private static unsafe class MF
    {
        // IUnknown::Release — vtable[2]
        public static uint Release(nint obj)
        {
            var vtable = *(nint**)obj;
            return ((delegate* unmanaged[Stdcall]<nint, uint>)vtable[2])(obj);
        }

        // IMFAttributes::SetUINT32 — vtable[21]  (IUnknown=3 + IMFAttributes index 18)
        public static void SetUINT32(nint obj, Guid key, uint value)
        {
            var vtable = *(nint**)obj;
            Marshal.ThrowExceptionForHR(
                ((delegate* unmanaged[Stdcall]<nint, Guid*, uint, int>)vtable[21])(obj, &key, value));
        }

        // IMFAttributes::SetUINT64 — vtable[22]  (IUnknown=3 + IMFAttributes index 19)
        public static void SetUINT64(nint obj, Guid key, ulong value)
        {
            var vtable = *(nint**)obj;
            Marshal.ThrowExceptionForHR(
                ((delegate* unmanaged[Stdcall]<nint, Guid*, ulong, int>)vtable[22])(obj, &key, value));
        }

        // IMFAttributes::SetGUID — vtable[24]  (IUnknown=3 + IMFAttributes index 21)
        public static void SetGUID(nint obj, Guid key, Guid value)
        {
            var vtable = *(nint**)obj;
            Marshal.ThrowExceptionForHR(
                ((delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, int>)vtable[24])(obj, &key, &value));
        }

        // IMFSinkWriter::AddStream — vtable[3]
        public static int SinkWriter_AddStream(nint writer, nint mediaType, int* streamIndex)
        {
            var vtable = *(nint**)writer;
            return ((delegate* unmanaged[Stdcall]<nint, nint, int*, int>)vtable[3])(writer, mediaType, streamIndex);
        }

        // IMFSinkWriter::SetInputMediaType — vtable[4]
        public static int SinkWriter_SetInputMediaType(nint writer, int streamIndex, nint inputType, nint encodingParams)
        {
            var vtable = *(nint**)writer;
            return ((delegate* unmanaged[Stdcall]<nint, int, nint, nint, int>)vtable[4])(writer, streamIndex, inputType, encodingParams);
        }

        // IMFSinkWriter::BeginWriting — vtable[5]
        public static int SinkWriter_BeginWriting(nint writer)
        {
            var vtable = *(nint**)writer;
            return ((delegate* unmanaged[Stdcall]<nint, int>)vtable[5])(writer);
        }

        // IMFSinkWriter::WriteSample — vtable[6]
        public static int SinkWriter_WriteSample(nint writer, int streamIndex, nint sample)
        {
            var vtable = *(nint**)writer;
            return ((delegate* unmanaged[Stdcall]<nint, int, nint, int>)vtable[6])(writer, streamIndex, sample);
        }

        // IMFSinkWriter::Finalize — vtable[11]
        public static void SinkWriter_Finalize(nint writer)
        {
            var vtable = *(nint**)writer;
            Marshal.ThrowExceptionForHR(
                ((delegate* unmanaged[Stdcall]<nint, int>)vtable[11])(writer));
        }

        // IMFSample::SetSampleTime — vtable[36]  (IUnknown=3 + IMFAttributes=30 + index 3)
        public static void Sample_SetSampleTime(nint sample, long time)
        {
            var vtable = *(nint**)sample;
            ((delegate* unmanaged[Stdcall]<nint, long, int>)vtable[36])(sample, time);
        }

        // IMFSample::SetSampleDuration — vtable[38]  (IUnknown=3 + IMFAttributes=30 + index 5)
        public static void Sample_SetSampleDuration(nint sample, long duration)
        {
            var vtable = *(nint**)sample;
            ((delegate* unmanaged[Stdcall]<nint, long, int>)vtable[38])(sample, duration);
        }

        // IMFSample::AddBuffer — vtable[42]  (IUnknown=3 + IMFAttributes=30 + index 9)
        public static void Sample_AddBuffer(nint sample, nint buffer)
        {
            var vtable = *(nint**)sample;
            ((delegate* unmanaged[Stdcall]<nint, nint, int>)vtable[42])(sample, buffer);
        }

        // IMFMediaBuffer::Lock — vtable[3]
        public static void Buffer_Lock(nint buffer, nint* ppbBuffer, int* pcbMaxLength, int* pcbCurrentLength)
        {
            var vtable = *(nint**)buffer;
            Marshal.ThrowExceptionForHR(
                ((delegate* unmanaged[Stdcall]<nint, nint*, int*, int*, int>)vtable[3])(buffer, ppbBuffer, pcbMaxLength, pcbCurrentLength));
        }

        // IMFMediaBuffer::Unlock — vtable[4]
        public static void Buffer_Unlock(nint buffer)
        {
            var vtable = *(nint**)buffer;
            ((delegate* unmanaged[Stdcall]<nint, int>)vtable[4])(buffer);
        }

        // IMFMediaBuffer::SetCurrentLength — vtable[6]
        public static void Buffer_SetCurrentLength(nint buffer, int length)
        {
            var vtable = *(nint**)buffer;
            ((delegate* unmanaged[Stdcall]<nint, int, int>)vtable[6])(buffer, length);
        }
    }

    // MARK: - Media Foundation P/Invoke

    private const uint MF_VERSION = 0x00020070;

    // Media type GUIDs
    private static Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    private static Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");
    private static Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");
    private static Guid MFMediaType_Audio = new("73647561-0000-0010-8000-00AA00389B71");
    private static Guid MFAudioFormat_AAC = new("00001610-0000-0010-8000-00AA00389B71");
    private static Guid MFAudioFormat_Float = new("00000003-0000-0010-8000-00AA00389B71");

    // Attribute GUIDs
    private static Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    private static Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    private static Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    private static Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    private static Guid MF_MT_AUDIO_NUM_CHANNELS = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    private static Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    private static Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    private static Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac5");

    private const uint COINIT_MULTITHREADED = 0x0;

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("mfplat.dll")]
    private static extern int MFStartup(uint version, uint dwFlags);

    [DllImport("mfplat.dll")]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll")]
    private static extern int MFCreateMediaType(out nint ppMFType);

    [DllImport("mfplat.dll")]
    private static extern int MFCreateSample(out nint ppIMFSample);

    [DllImport("mfplat.dll")]
    private static extern int MFCreateMemoryBuffer(int cbMaxLength, out nint ppBuffer);

    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
    private static extern int MFCreateSinkWriterFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string pwszOutputURL,
        nint pByteStream, nint pAttributes, out nint ppSinkWriter);
}
