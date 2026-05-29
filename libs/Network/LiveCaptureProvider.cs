#nullable enable

using AlbionPacketExplorer.Abstractions;
using PacketDotNet;
using SharpPcap;

namespace AlbionPacketExplorer.Network;

public class LiveCaptureProvider : PacketProvider
{
    private static readonly HashSet<int> PhotonUdpPorts = [5055, 5056, 5058];

    private readonly IPhotonReceiver _photonReceiver;
    private readonly string? _deviceName;
    private readonly Action<string>? _log;

    private ILiveDevice? _device;
    private volatile bool _running;

    public override bool IsRunning => _running;

    public LiveCaptureProvider(IPhotonReceiver photonReceiver, string? deviceName, Action<string>? log = null)
    {
        _photonReceiver = photonReceiver ?? throw new ArgumentNullException(nameof(photonReceiver));
        _deviceName = deviceName;
        _log = log;
    }

    public override void Start()
    {
        if (_running) return;

        var chosen = SelectDevice();
        if (chosen == null)
        {
            _log?.Invoke("SharpPcap: no suitable device found");
            return;
        }

        chosen.Open(DeviceModes.Promiscuous, 1000);
        chosen.Filter = "udp port 5055 or udp port 5056 or udp port 5058";
        chosen.OnPacketArrival += OnPacketArrival;

        _device = chosen;
        _running = true;

        chosen.StartCapture();
        _log?.Invoke($"SharpPcap: capturing on {chosen.Description ?? chosen.Name}");
    }

    public override void Stop()
    {
        _running = false;
        var dev = _device;
        _device = null;

        if (dev == null) return;

        try
        {
            dev.OnPacketArrival -= OnPacketArrival;
            dev.StopCapture();
            dev.Close();
        }
        catch (Exception ex)
        {
            _log?.Invoke($"SharpPcap: stop error: {ex.Message}");
        }
    }

    private ILiveDevice? SelectDevice()
    {
        var devices = CaptureDeviceList.Instance;

        if (_deviceName != null)
            return devices.OfType<ILiveDevice>()
                .FirstOrDefault(d => d.Name.Equals(_deviceName, StringComparison.OrdinalIgnoreCase));

        return devices.OfType<ILiveDevice>().FirstOrDefault();
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        try
        {
            var raw = e.GetPacket();
            var parsed = Packet.ParsePacket(raw.LinkLayerType, raw.Data);

            var udp = parsed.Extract<UdpPacket>();
            if (udp?.PayloadData is not { Length: > 0 } payload) return;

            if (!IsPhotonTraffic(udp, payload)) return;

            _photonReceiver.ReceivePacket(payload);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"SharpPcap: packet error: {ex.Message}");
        }
    }

    private static bool IsPhotonTraffic(UdpPacket udp, byte[] payload) =>
        PhotonUdpPorts.Contains(udp.SourcePort) ||
        PhotonUdpPorts.Contains(udp.DestinationPort) ||
        LooksLikePhoton(payload);

    private static bool LooksLikePhoton(byte[] payload) =>
        payload.Length >= 3 && payload[0] is 0xF1 or 0xF2 or 0xFE;
}
