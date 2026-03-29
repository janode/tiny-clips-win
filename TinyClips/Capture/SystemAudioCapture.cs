using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace TinyClips.Capture;

/// <summary>
/// Captures system audio via WASAPI loopback.
/// Buffers PCM samples with timestamps (100ns MF units) for the video encoder to consume.
/// </summary>
public sealed class SystemAudioCapture : IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private readonly ConcurrentQueue<AudioChunk> _queue = new();
    private long _timestampOffset; // 100ns units from recording start
    private bool _started;

    public WaveFormat? WaveFormat => _capture?.WaveFormat;

    public readonly record struct AudioChunk(byte[] Data, long Timestamp, long Duration);

    public void Start()
    {
        _capture = new WasapiLoopbackCapture();
        _timestampOffset = 0;
        _started = false;

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();
    }

    public void Stop()
    {
        _capture?.StopRecording();
    }

    /// <summary>
    /// Drain all buffered audio chunks. Called from the video capture thread.
    /// </summary>
    public int DrainTo(Action<AudioChunk> consumer)
    {
        int count = 0;
        while (_queue.TryDequeue(out var chunk))
        {
            consumer(chunk);
            count++;
        }
        return count;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        // Calculate duration of this chunk in 100ns units
        var wf = _capture!.WaveFormat;
        long bytesPerSecond = (long)wf.SampleRate * wf.BlockAlign;
        long duration = e.BytesRecorded * 10_000_000L / bytesPerSecond;

        if (!_started)
        {
            _started = true;
            _timestampOffset = 0;
        }

        var data = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, data, 0, e.BytesRecorded);

        _queue.Enqueue(new AudioChunk(data, _timestampOffset, duration));
        _timestampOffset += duration;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // Nothing needed — the encoder drains remaining chunks on stop
    }

    public void Dispose()
    {
        _capture?.Dispose();
        _capture = null;
    }
}
