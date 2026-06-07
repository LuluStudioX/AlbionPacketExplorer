using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Network;
using AlbionPacketExplorer.Network.Handlers;
using Avalonia.Threading;

namespace AlbionPacketExplorer.Services;

public sealed class CaptureSession : IDisposable
{
    private readonly Action<PacketEntry> _onPacket;
    private readonly Action<string> _onLog;
    private readonly Action<byte[]>? _onRaw;

    private LiveCaptureProvider? _provider;
    private RawAlbionParser? _parser;

    public AlbionHandlerRegistry Handlers { get; } = new();

    public bool IsRunning => _provider?.IsRunning ?? false;

    public CaptureSession(Action<PacketEntry> onPacket, Action<string> onLog, Action<byte[]>? onRaw = null)
    {
        _onPacket = onPacket;
        _onLog = onLog;
        _onRaw = onRaw;
    }

    public void Start(string? deviceName)
    {
        _parser = new RawAlbionParser();
        _parser.PacketReceived += OnParserPacketReceived;
        if (_onRaw != null) _parser.RawReceived += OnParserRawReceived;
        _parser.AttachHandlers(Handlers.BuildParser());

        _provider = new LiveCaptureProvider(_parser, deviceName, msg =>
            Dispatcher.UIThread.Post(() => _onLog(msg)));

        try
        {
            _provider.Start();
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

        _provider?.Stop();
        _provider = null;
        _parser = null;
    }

    private void OnParserPacketReceived(PacketEntry packet) =>
        Dispatcher.UIThread.Post(() => _onPacket(packet));

    // Raw arrives on the capture thread; the collector handles its own threading.
    private void OnParserRawReceived(byte[] payload) => _onRaw?.Invoke(payload);
}
