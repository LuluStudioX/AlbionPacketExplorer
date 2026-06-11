#nullable enable

using PacketDotNet;
using SharpPcap;

namespace AlbionPacketExplorer.Network;

public class LiveCaptureProvider : PacketProvider
{
    private static readonly HashSet<int> PhotonUdpPorts = [5055, 5056, 5058];

    // Auto-lock (Automatic mode): capture starts on every adapter, the first adapter that delivers
    // LockThreshold Photon datagrams wins the election and every other adapter is closed - so the
    // steady state is one adapter with zero dedupe overhead. While more than one adapter is open the
    // same datagram can arrive twice (e.g. a VPN TAP device mirroring the physical NIC), so a small
    // payload-hash ring drops duplicates during the election window only. If the locked adapter then
    // goes silent for RelockAfterSilenceMs while capture runs (VPN toggled, wifi to ethernet, ...),
    // the device list is re-enumerated and the election restarts.
    private const int LockThreshold = 20;
    private const int RelockAfterSilenceMs = 10_000;
    private const int DedupeRingSize = 512;

    private readonly IPacketReceiver _photonReceiver;
    private readonly string? _deviceName;
    private readonly Action<string>? _log;

    private readonly List<ILiveDevice> _devices = [];
    private volatile bool _running;

    // Election state. _electionGate guards _devices, the tallies and the dedupe ring; the arrival
    // path only takes it while no adapter is locked, so the post-lock hot path stays lock-free.
    private readonly object _electionGate = new();
    private readonly Dictionary<ILiveDevice, int> _photonCounts = [];
    private volatile ILiveDevice? _lockedDevice;
    private long _lastPhotonAtMs;
    private System.Timers.Timer? _watchdog;
    // Set under _electionGate by Stop. A watchdog rescan that is mid-TryOpen when Stop runs must
    // not adopt its freshly opened device into _devices (it would leak open past Stop); TryOpen
    // checks this flag under the gate and closes the device instead.
    private bool _stopped;

    private readonly HashSet<ulong> _recentHashes = [];
    private readonly ulong[] _hashRing = new ulong[DedupeRingSize];
    private int _hashCursor;

    public override bool IsRunning => _running;

    public LiveCaptureProvider(IPacketReceiver photonReceiver, string? deviceName, Action<string>? log = null)
    {
        _photonReceiver = photonReceiver ?? throw new ArgumentNullException(nameof(photonReceiver));
        _deviceName = deviceName;
        _log = log;
    }

    public override void Start()
    {
        if (_running) return;

        var toOpen = SelectDevices(CaptureDeviceList.Instance.OfType<ILiveDevice>().ToList());
        if (toOpen.Count == 0)
        {
            _log?.Invoke("SharpPcap: no suitable device found");
            return;
        }

        int failed = 0;
        foreach (var dev in toOpen)
        {
            if (!TryOpen(dev)) failed++;
        }

        bool any;
        lock (_electionGate) any = _devices.Count > 0;

        if (any)
        {
            _running = true;
            Volatile.Write(ref _lastPhotonAtMs, Environment.TickCount64);

            // Watchdog only in Automatic mode: a manually picked device is the user's choice even
            // when silent, so it never re-elects.
            if (_deviceName == null)
            {
                _watchdog = new System.Timers.Timer(RelockAfterSilenceMs / 2.0) { AutoReset = true };
                _watchdog.Elapsed += (_, _) => OnWatchdogTick();
                _watchdog.Start();
            }
        }
        else if (failed > 0)
        {
            // Every candidate failed. On Linux this is almost always missing capture privileges.
            _log?.Invoke(OperatingSystem.IsLinux()
                ? "SharpPcap: no device could be opened. Capture needs root, or grant the dotnet/app binary capture rights: sudo setcap cap_net_raw,cap_net_admin+eip <path>."
                : "SharpPcap: no device could be opened. Is Npcap (Windows) / libpcap (macOS) installed?");
        }
    }

    // Opens one device (promiscuous + Photon BPF filter), hooks the arrival handler and starts its
    // capture thread. Adds it to _devices on success. Shared by Start and re-election.
    private bool TryOpen(ILiveDevice dev)
    {
        try
        {
            dev.Open(DeviceModes.Promiscuous, 1000);

            // VPN tunnels, 802.11-monitor and leftover libpcap pseudo-devices use a link-layer
            // type that cannot carry (or cannot BPF-filter) IP/UDP. Skip them cleanly instead of
            // letting the filter assignment throw "link-layer type filtering not implemented".
            if (!IsFilterableLinkType(dev.LinkType))
            {
                _log?.Invoke($"SharpPcap: skipping {dev.Name} (link type {dev.LinkType} not capturable)");
                dev.Close();
                return true; // not a failure, just not capturable
            }

            dev.Filter = "udp port 5055 or udp port 5056 or udp port 5058";
            dev.OnPacketArrival += OnPacketArrival;
            dev.StartCapture();

            bool adopted;
            lock (_electionGate)
            {
                adopted = !_stopped;
                if (adopted) _devices.Add(dev);
            }
            if (!adopted)
            {
                // Stop ran while this rescan was opening the device; close it instead of leaking it.
                try { dev.OnPacketArrival -= OnPacketArrival; dev.StopCapture(); dev.Close(); }
                catch { /* mid-close races are harmless */ }
                return false;
            }

            _log?.Invoke($"SharpPcap: capturing on {dev.Description ?? dev.Name}");
            return true;
        }
        catch (Exception ex)
        {
            try { dev.Close(); } catch { /* already closed / never opened */ }
            _log?.Invoke($"SharpPcap: failed to open {dev.Name}: {ex.Message}");
            return false;
        }
    }

    // Link-layer types whose payload is plain IP and on which a BPF "udp port" filter compiles:
    // real NICs (Ethernet), the Linux cooked-capture path (SLL) and raw-IP tunnels/loopback.
    private static bool IsFilterableLinkType(LinkLayers link) => link switch
    {
        LinkLayers.Ethernet => true,
        LinkLayers.LinuxSll => true,
        LinkLayers.LinuxSll2 => true,
        LinkLayers.Raw => true,
        LinkLayers.RawLegacy => true,
        LinkLayers.IPv4 => true,
        LinkLayers.IPv6 => true,
        LinkLayers.Null => true,
        LinkLayers.Loop => true,
        _ => false
    };

    public override void Stop()
    {
        _running = false;

        _watchdog?.Stop();
        _watchdog?.Dispose();
        _watchdog = null;

        List<ILiveDevice> snapshot;
        lock (_electionGate)
        {
            _stopped = true;
            snapshot = _devices.ToList();
            _devices.Clear();
            _lockedDevice = null;
            _photonCounts.Clear();
            ResetDedupeRing();
        }

        CloseDevices(snapshot);
    }

    private void CloseDevices(IReadOnlyList<ILiveDevice> devices)
    {
        foreach (var dev in devices)
        {
            try
            {
                dev.OnPacketArrival -= OnPacketArrival;
                dev.StopCapture();
                dev.Close();
            }
            catch (Exception ex)
            {
                _log?.Invoke($"SharpPcap: stop error on {dev.Name}: {ex.Message}");
            }
        }
    }

    private List<ILiveDevice> SelectDevices(List<ILiveDevice> all)
    {
        if (_deviceName != null)
        {
            var single = all.FirstOrDefault(d => d.Name.Equals(_deviceName, StringComparison.OrdinalIgnoreCase));
            return single != null ? [single] : [];
        }

        return all;
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        try
        {
            // Post-lock hot path: only the winner is still capturing; a straggler callback from a
            // loser mid-close is dropped without touching the gate.
            var locked = _lockedDevice;
            if (locked != null && !ReferenceEquals(sender, locked)) return;

            var raw = e.GetPacket();
            var parsed = Packet.ParsePacket(raw.LinkLayerType, raw.Data);

            var udp = parsed.Extract<UdpPacket>();
            if (udp?.PayloadData is not { Length: > 0 } payload) return;

            if (!IsPhotonTraffic(udp, payload)) return;

            if (locked == null && sender is ILiveDevice device && !AcceptWhileElecting(device, payload))
                return;

            Volatile.Write(ref _lastPhotonAtMs, Environment.TickCount64);
            _photonReceiver.ReceivePacket(payload);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"SharpPcap: packet error: {ex.Message}");
        }
    }

    /// <summary>
    /// Election-window arrival: drops datagrams already seen via another adapter (payload hash ring)
    /// and tallies Photon traffic per adapter; the first adapter to reach <see cref="LockThreshold"/>
    /// becomes the locked one. Returns false when the datagram is a duplicate and must not be parsed.
    /// </summary>
    private bool AcceptWhileElecting(ILiveDevice device, byte[] payload)
    {
        ILiveDevice? winner = null;
        lock (_electionGate)
        {
            // Lost the race to a concurrent arrival that locked first.
            if (_lockedDevice != null)
                return ReferenceEquals(device, _lockedDevice);

            ulong h = Fnv1a(payload);
            if (!_recentHashes.Add(h)) return false;
            ulong evicted = _hashRing[_hashCursor];
            if (evicted != 0) _recentHashes.Remove(evicted);
            _hashRing[_hashCursor] = h;
            _hashCursor = (_hashCursor + 1) % DedupeRingSize;

            int n = _photonCounts.GetValueOrDefault(device) + 1;
            _photonCounts[device] = n;
            if (n >= LockThreshold)
                winner = device;
        }

        if (winner != null) LockTo(winner);
        return true;
    }

    private void LockTo(ILiveDevice winner)
    {
        List<ILiveDevice> losers;
        lock (_electionGate)
        {
            if (_lockedDevice != null) return;
            _lockedDevice = winner;
            _photonCounts.Clear();
            ResetDedupeRing();
            losers = _devices.Where(d => !ReferenceEquals(d, winner)).ToList();
            _devices.RemoveAll(d => !ReferenceEquals(d, winner));
        }

        _log?.Invoke($"SharpPcap: locked to {winner.Description ?? winner.Name}");
        if (losers.Count == 0) return;

        // Close losers off the capture callback: StopCapture joins the device's capture thread, and
        // one of the losers may be inside its own callback right now.
        Task.Run(() => CloseDevices(losers));
    }

    // Re-elects when the locked adapter delivers no Photon traffic for RelockAfterSilenceMs: the
    // device list is re-enumerated fresh (adapters appear/disappear with VPNs and docks) and every
    // capturable adapter is opened again. The watchdog is a no-op while unlocked, so a silent
    // network (game closed) just leaves the election pending without device churn.
    private void OnWatchdogTick()
    {
        if (!_running || _lockedDevice == null) return;
        if (Environment.TickCount64 - Volatile.Read(ref _lastPhotonAtMs) < RelockAfterSilenceMs) return;

        _log?.Invoke("SharpPcap: locked adapter went silent, rescanning all adapters");

        HashSet<string> alreadyOpen;
        lock (_electionGate)
        {
            if (_stopped) return; // Stop won the race; leave teardown state alone
            _lockedDevice = null;
            _photonCounts.Clear();
            ResetDedupeRing();
            alreadyOpen = _devices.Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            // CaptureDeviceList.New() re-enumerates; Instance caches the list from process start.
            var fresh = CaptureDeviceList.New().OfType<ILiveDevice>()
                .Where(d => !alreadyOpen.Contains(d.Name))
                .ToList();
            foreach (var dev in fresh)
                TryOpen(dev);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"SharpPcap: rescan failed: {ex.Message}");
        }
    }

    private void ResetDedupeRing()
    {
        _recentHashes.Clear();
        Array.Clear(_hashRing);
        _hashCursor = 0;
    }

    // FNV-1a 64: cheap, allocation-free payload fingerprint for the election-window dedupe. Photon
    // datagrams carry sequence numbers and timestamps, so identical hashes across DIFFERENT packets
    // are practically impossible within a 512-entry window.
    private static ulong Fnv1a(byte[] data)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in data)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash;
    }

    private static bool IsPhotonTraffic(UdpPacket udp, byte[] payload) =>
        PhotonUdpPorts.Contains(udp.SourcePort) ||
        PhotonUdpPorts.Contains(udp.DestinationPort) ||
        LooksLikePhoton(payload);

    private static bool LooksLikePhoton(byte[] payload) =>
        payload.Length >= 3 && payload[0] is 0xF1 or 0xF2 or 0xFE;
}
