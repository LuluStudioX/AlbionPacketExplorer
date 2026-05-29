using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Network;
using Avalonia.Threading;

namespace AlbionPacketExplorer.Services;

public sealed class CaptureSession : IDisposable
{
    private readonly Action<PacketEntry> _onPacket;
    private readonly Action<string> _onLog;

    private LiveCaptureProvider? _provider;
    private RawAlbionParser? _parser;

    public bool IsRunning => _provider?.IsRunning ?? false;

    public CaptureSession(Action<PacketEntry> onPacket, Action<string> onLog)
    {
        _onPacket = onPacket;
        _onLog = onLog;
    }

    public void Start(string? deviceName)
    {
        _parser = new RawAlbionParser();
        _parser.PacketReceived += OnParserPacketReceived;

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
            _parser.PacketReceived -= OnParserPacketReceived;

        _provider?.Stop();
        _provider = null;
        _parser = null;
    }

    private void OnParserPacketReceived(PacketEntry packet) =>
        Dispatcher.UIThread.Post(() => _onPacket(packet));
}
