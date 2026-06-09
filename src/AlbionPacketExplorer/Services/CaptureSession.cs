using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Network;
using Avalonia.Threading;

namespace AlbionPacketExplorer.Services;

public sealed class CaptureSession : IDisposable
{
    // Buffer decoded packets on the capture thread and flush them to the UI as one batch on a
    // timer, instead of posting one dispatcher callback per packet (which floods the UI thread at
    // capture rate).
    private const double FlushIntervalMs = 75;

    private readonly Action<IReadOnlyList<PacketEntry>> _onPacketBatch;
    private readonly Action<string> _onLog;
    private readonly Action<byte[]>? _onRaw;

    private readonly Lock _bufferLock = new();
    private List<PacketEntry> _buffer = [];
    private System.Timers.Timer? _flushTimer;

    private LiveCaptureProvider? _provider;
    private RawAlbionParser? _parser;

    public bool IsRunning => _provider?.IsRunning ?? false;

    /// <summary>When paused the device keeps sniffing but decoded packets are dropped, so the
    /// session and everything captured so far stay intact and capture resumes without a restart.</summary>
    public bool IsPaused { get; set; }

    public CaptureSession(Action<IReadOnlyList<PacketEntry>> onPacketBatch, Action<string> onLog, Action<byte[]>? onRaw = null)
    {
        _onPacketBatch = onPacketBatch;
        _onLog = onLog;
        _onRaw = onRaw;
    }

    public void Start(string? deviceName)
    {
        _parser = new RawAlbionParser();
        _parser.PacketReceived += OnParserPacketReceived;
        if (_onRaw != null) _parser.RawReceived += OnParserRawReceived;

        _provider = new LiveCaptureProvider(_parser, deviceName, msg =>
            Dispatcher.UIThread.Post(() => _onLog(msg)));

        _flushTimer = new System.Timers.Timer(FlushIntervalMs) { AutoReset = true };
        _flushTimer.Elapsed += (_, _) => Flush();

        try
        {
            _provider.Start();
            _flushTimer.Start();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Stop()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_parser != null)
        {
            _parser.PacketReceived -= OnParserPacketReceived;
            _parser.RawReceived -= OnParserRawReceived;
        }

        if (_flushTimer != null)
        {
            _flushTimer.Stop();
            _flushTimer.Dispose();
            _flushTimer = null;
        }

        _provider?.Stop();
        _provider = null;
        _parser = null;

        // Deliver anything captured after the last timer tick so nothing is lost on stop.
        Flush();
    }

    // Swap the buffer out under lock, then post the batch to the UI thread. Skips when empty.
    private void Flush()
    {
        List<PacketEntry> batch;
        lock (_bufferLock)
        {
            if (_buffer.Count == 0) return;
            batch = _buffer;
            _buffer = [];
        }

        Dispatcher.UIThread.Post(() => _onPacketBatch(batch));
    }

    private void OnParserPacketReceived(PacketEntry packet)
    {
        if (IsPaused) return;
        lock (_bufferLock) _buffer.Add(packet);
    }

    // Raw arrives on the capture thread; the collector handles its own threading.
    private void OnParserRawReceived(byte[] payload)
    {
        if (IsPaused) return;
        _onRaw?.Invoke(payload);
    }
}
