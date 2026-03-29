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
        try
        {
        Marshal.ThrowExceptionForHR(MFStartup(MF_VERSION, 0));
        try
        {
            // Build encoder — with audio stream if loopback is active
            WaveFormat? audioFormat = _recordAudio ? _audioCapture?.WaveFormat : null;
            var encoder = new MFEncoder(_outputPath!, _captureRect.Width, _captureRect.Height, _fps, audioFormat);
            try
            {
                var frameInterval = TimeSpan.FromSeconds(1.0 / _fps);
                long frameDuration = 10_000_000L / _fps; // 100-nanosecond units
                long videoTimestamp = 0;

                using var bitmap = new Bitmap(_captureRect.Width, _captureRect.Height, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(bitmap);
                var stopwatch = Stopwatch.StartNew();

                while (_isRecording)
                {
                    var frameStart = stopwatch.Elapsed;

                    try
                    {
                        graphics.CopyFromScreen(_captureRect.Left, _captureRect.Top, 0, 0,
                            _captureRect.Size, CopyPixelOperation.SourceCopy);

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

    // MARK: - Media Foundation Encoder

    private sealed class MFEncoder : IDisposable
    {
        private readonly IMFSinkWriter _writer;
        private readonly int _videoStreamIndex;
        private readonly int _audioStreamIndex = -1;
        private readonly int _frameSize;
        private readonly bool _hasAudio;

        public MFEncoder(string outputPath, int width, int height, int fps, WaveFormat? audioFormat)
        {
            _frameSize = width * height * 4;
            _hasAudio = audioFormat != null;

            // Output type: H.264
            Marshal.ThrowExceptionForHR(MFCreateMediaType(out var videoOutputType));
            SetGUID(videoOutputType, MF_MT_MAJOR_TYPE, MFMediaType_Video);
            SetGUID(videoOutputType, MF_MT_SUBTYPE, MFVideoFormat_H264);
            SetUINT32(videoOutputType, MF_MT_AVG_BITRATE, Math.Max(1_000_000u, (uint)(width * height * fps / 4)));
            SetUINT32(videoOutputType, MF_MT_INTERLACE_MODE, 2); // MFVideoInterlace_Progressive
            SetUINT64(videoOutputType, MF_MT_FRAME_SIZE, Pack2x32((uint)width, (uint)height));
            SetUINT64(videoOutputType, MF_MT_FRAME_RATE, Pack2x32((uint)fps, 1));
            SetUINT64(videoOutputType, MF_MT_PIXEL_ASPECT_RATIO, Pack2x32(1, 1));

            // Input type: RGB32 (matches GDI+ Format32bppArgb — both are BGRA in memory)
            Marshal.ThrowExceptionForHR(MFCreateMediaType(out var videoInputType));
            SetGUID(videoInputType, MF_MT_MAJOR_TYPE, MFMediaType_Video);
            SetGUID(videoInputType, MF_MT_SUBTYPE, MFVideoFormat_RGB32);
            SetUINT32(videoInputType, MF_MT_INTERLACE_MODE, 2);
            SetUINT64(videoInputType, MF_MT_FRAME_SIZE, Pack2x32((uint)width, (uint)height));
            SetUINT64(videoInputType, MF_MT_FRAME_RATE, Pack2x32((uint)fps, 1));
            SetUINT64(videoInputType, MF_MT_PIXEL_ASPECT_RATIO, Pack2x32(1, 1));

            // Create sink writer — .mp4 extension auto-selects MPEG-4 container
            Marshal.ThrowExceptionForHR(MFCreateSinkWriterFromURL(
                outputPath, nint.Zero, nint.Zero, out _writer));
            Marshal.ThrowExceptionForHR(_writer.AddStream(videoOutputType, out _videoStreamIndex));
            Marshal.ThrowExceptionForHR(_writer.SetInputMediaType(_videoStreamIndex, videoInputType, nint.Zero));

            Marshal.ReleaseComObject(videoInputType);
            Marshal.ReleaseComObject(videoOutputType);

            // Audio stream: AAC output, PCM Float input (WASAPI loopback native format)
            if (_hasAudio && audioFormat != null)
            {
                // Output type: AAC
                Marshal.ThrowExceptionForHR(MFCreateMediaType(out var audioOutputType));
                SetGUID(audioOutputType, MF_MT_MAJOR_TYPE, MFMediaType_Audio);
                SetGUID(audioOutputType, MF_MT_SUBTYPE, MFAudioFormat_AAC);
                SetUINT32(audioOutputType, MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                SetUINT32(audioOutputType, MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)audioFormat.SampleRate);
                SetUINT32(audioOutputType, MF_MT_AUDIO_NUM_CHANNELS, (uint)audioFormat.Channels);
                SetUINT32(audioOutputType, MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 24000u); // ~192 kbps AAC
                SetUINT32(audioOutputType, MF_MT_AUDIO_BLOCK_ALIGNMENT, 1);

                // Input type: IEEE Float PCM (native WASAPI loopback format)
                Marshal.ThrowExceptionForHR(MFCreateMediaType(out var audioInputType));
                SetGUID(audioInputType, MF_MT_MAJOR_TYPE, MFMediaType_Audio);
                SetGUID(audioInputType, MF_MT_SUBTYPE, MFAudioFormat_Float);
                SetUINT32(audioInputType, MF_MT_AUDIO_BITS_PER_SAMPLE, (uint)audioFormat.BitsPerSample);
                SetUINT32(audioInputType, MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)audioFormat.SampleRate);
                SetUINT32(audioInputType, MF_MT_AUDIO_NUM_CHANNELS, (uint)audioFormat.Channels);
                SetUINT32(audioInputType, MF_MT_AUDIO_AVG_BYTES_PER_SECOND, (uint)audioFormat.AverageBytesPerSecond);
                SetUINT32(audioInputType, MF_MT_AUDIO_BLOCK_ALIGNMENT, (uint)audioFormat.BlockAlign);

                Marshal.ThrowExceptionForHR(_writer.AddStream(audioOutputType, out _audioStreamIndex));
                Marshal.ThrowExceptionForHR(_writer.SetInputMediaType(_audioStreamIndex, audioInputType, nint.Zero));

                Marshal.ReleaseComObject(audioInputType);
                Marshal.ReleaseComObject(audioOutputType);
            }

            Marshal.ThrowExceptionForHR(_writer.BeginWriting());
        }

        public void WriteVideoFrame(nint frameData, int dataLength, long timestamp, long duration)
        {
            Marshal.ThrowExceptionForHR(MFCreateMemoryBuffer(_frameSize, out var buffer));
            try
            {
                buffer.Lock(out var pbData, out _, out _);
                try
                {
                    unsafe
                    {
                        Buffer.MemoryCopy((void*)frameData, (void*)pbData,
                            _frameSize, Math.Min(dataLength, _frameSize));
                    }
                }
                finally { buffer.Unlock(); }
                buffer.SetCurrentLength(_frameSize);

                Marshal.ThrowExceptionForHR(MFCreateSample(out var sample));
                try
                {
                    sample.AddBuffer(buffer);
                    sample.SetSampleTime(timestamp);
                    sample.SetSampleDuration(duration);
                    Marshal.ThrowExceptionForHR(_writer.WriteSample(_videoStreamIndex, sample));
                }
                finally { Marshal.ReleaseComObject(sample); }
            }
            finally { Marshal.ReleaseComObject(buffer); }
        }

        public void WriteAudioSamples(byte[] data, long timestamp, long duration)
        {
            if (!_hasAudio || _audioStreamIndex < 0) return;

            Marshal.ThrowExceptionForHR(MFCreateMemoryBuffer(data.Length, out var buffer));
            try
            {
                buffer.Lock(out var pbData, out _, out _);
                try
                {
                    Marshal.Copy(data, 0, pbData, data.Length);
                }
                finally { buffer.Unlock(); }
                buffer.SetCurrentLength(data.Length);

                Marshal.ThrowExceptionForHR(MFCreateSample(out var sample));
                try
                {
                    sample.AddBuffer(buffer);
                    sample.SetSampleTime(timestamp);
                    sample.SetSampleDuration(duration);
                    Marshal.ThrowExceptionForHR(_writer.WriteSample(_audioStreamIndex, sample));
                }
                finally { Marshal.ReleaseComObject(sample); }
            }
            finally { Marshal.ReleaseComObject(buffer); }
        }

        public void Finish() => _writer.FinalizeWriting();

        public void Dispose() => Marshal.ReleaseComObject(_writer);

        private static void SetGUID(IMFMediaType type, Guid key, Guid value)
            => Marshal.ThrowExceptionForHR(type.SetGUID(ref key, ref value));

        private static void SetUINT32(IMFMediaType type, Guid key, uint value)
            => Marshal.ThrowExceptionForHR(type.SetUINT32(ref key, value));

        private static void SetUINT64(IMFMediaType type, Guid key, ulong value)
            => Marshal.ThrowExceptionForHR(type.SetUINT64(ref key, value));

        private static ulong Pack2x32(uint hi, uint lo) => ((ulong)hi << 32) | lo;
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

    [DllImport("mfplat.dll")]
    private static extern int MFStartup(uint version, uint dwFlags);

    [DllImport("mfplat.dll")]
    private static extern int MFShutdown();

    [DllImport("mfplat.dll")]
    private static extern int MFCreateMediaType(
        [MarshalAs(UnmanagedType.Interface)] out IMFMediaType ppMFType);

    [DllImport("mfplat.dll")]
    private static extern int MFCreateSample(
        [MarshalAs(UnmanagedType.Interface)] out IMFSample ppIMFSample);

    [DllImport("mfplat.dll")]
    private static extern int MFCreateMemoryBuffer(
        int cbMaxLength, [MarshalAs(UnmanagedType.Interface)] out IMFMediaBuffer ppBuffer);

    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
    private static extern int MFCreateSinkWriterFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string pwszOutputURL,
        nint pByteStream, nint pAttributes,
        [MarshalAs(UnmanagedType.Interface)] out IMFSinkWriter ppSinkWriter);

    // MARK: - Media Foundation COM Interfaces

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("a27003cf-2354-4f2a-8d6a-ab7cff15437e")]
    private interface IMFSinkWriter
    {
        [PreserveSig] int AddStream([MarshalAs(UnmanagedType.Interface)] IMFMediaType pTargetMediaType, out int pdwStreamIndex);
        [PreserveSig] int SetInputMediaType(int dwStreamIndex, [MarshalAs(UnmanagedType.Interface)] IMFMediaType pInputMediaType, nint pEncodingParameters);
        [PreserveSig] int BeginWriting();
        [PreserveSig] int WriteSample(int dwStreamIndex, [MarshalAs(UnmanagedType.Interface)] IMFSample pSample);
        [PreserveSig] int SendStreamTick(int dwStreamIndex, long llTimestamp);
        [PreserveSig] int PlaceMarker(int dwStreamIndex, nint pvContext);
        [PreserveSig] int NotifyEndOfSegment(int dwStreamIndex);
        [PreserveSig] int Flush(int dwStreamIndex);
        [PreserveSig] int FinalizeWriting();
        [PreserveSig] int GetServiceForStream(int dwStreamIndex, ref Guid guidService, ref Guid riid, out nint ppvObject);
        [PreserveSig] int GetStatistics(int dwStreamIndex, nint pStats);
    }

    // IMFMediaType — includes all 30 IMFAttributes methods + 5 IMFMediaType methods
    // Stubs for unused methods maintain correct vtable layout
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
    private interface IMFMediaType
    {
        // IMFAttributes methods (30 — indices 0–29)
        void GetItem();                     // 0
        void GetItemType();                 // 1
        void CompareItem();                 // 2
        void Compare();                     // 3
        void GetUINT32_();                  // 4
        void GetUINT64_();                  // 5
        void GetDouble_();                  // 6
        void GetGUID_();                    // 7
        void GetStringLength();             // 8
        void GetString();                   // 9
        void GetAllocatedString();          // 10
        void GetBlobSize();                 // 11
        void GetBlob();                     // 12
        void GetAllocatedBlob();            // 13
        void GetUnknown();                  // 14
        void SetItem();                     // 15
        void DeleteItem();                  // 16
        void DeleteAllItems();              // 17
        [PreserveSig] int SetUINT32([In] ref Guid guidKey, uint unValue);   // 18
        [PreserveSig] int SetUINT64([In] ref Guid guidKey, ulong unValue);  // 19
        void SetDouble();                   // 20
        [PreserveSig] int SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);  // 21
        void SetString();                   // 22
        void SetBlob();                     // 23
        void SetUnknown();                  // 24
        void LockStore();                   // 25
        void UnlockStore();                 // 26
        void GetCount();                    // 27
        void GetItemByIndex();              // 28
        void CopyAllItems();                // 29
        // IMFMediaType methods (5 — indices 30–34)
        void GetMajorType();                // 30
        void IsCompressedFormat();          // 31
        void IsEqual();                     // 32
        void GetRepresentation();           // 33
        void FreeRepresentation();          // 34
    }

    // IMFSample — includes all 30 IMFAttributes methods + 14 IMFSample methods
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
    private interface IMFSample
    {
        // IMFAttributes methods (30 — indices 0–29)
        void GetItem();
        void GetItemType();
        void CompareItem();
        void Compare();
        void GetUINT32_();
        void GetUINT64_();
        void GetDouble_();
        void GetGUID_();
        void GetStringLength();
        void GetString();
        void GetAllocatedString();
        void GetBlobSize();
        void GetBlob();
        void GetAllocatedBlob();
        void GetUnknown();
        void SetItem();
        void DeleteItem();
        void DeleteAllItems();
        void SetUINT32();
        void SetUINT64();
        void SetDouble();
        void SetGUID();
        void SetString();
        void SetBlob();
        void SetUnknown();
        void LockStore();
        void UnlockStore();
        void GetCount();
        void GetItemByIndex();
        void CopyAllItems();
        // IMFSample methods (14 — indices 30–43)
        void GetSampleFlags();              // 30
        void SetSampleFlags();              // 31
        void GetSampleTime();               // 32
        void SetSampleTime(long hnsSampleTime);           // 33
        void GetSampleDuration();           // 34
        void SetSampleDuration(long hnsSampleDuration);   // 35
        void GetBufferCount();              // 36
        void GetBufferByIndex();            // 37
        void ConvertToContiguousBuffer();   // 38
        void AddBuffer([MarshalAs(UnmanagedType.Interface)] IMFMediaBuffer pBuffer);  // 39
        void RemoveBufferByIndex();         // 40
        void RemoveAllBuffers();            // 41
        void GetTotalLength();              // 42
        void CopyToBuffer();                // 43
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("045FA593-8799-42b8-BC8D-8968C6453507")]
    private interface IMFMediaBuffer
    {
        void Lock(out nint ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);
        void Unlock();
        void GetCurrentLength(out int pcbCurrentLength);
        void SetCurrentLength(int cbCurrentLength);
        void GetMaxLength(out int pcbMaxLength);
    }
}
