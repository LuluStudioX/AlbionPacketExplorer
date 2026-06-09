using SharpPcap;

namespace AlbionPacketExplorer.Network;

public static class NetworkDeviceScanner
{
    public static IReadOnlyList<NetworkDeviceInfo> GetDevices()
    {
        var result = new List<NetworkDeviceInfo>();
        var devices = CaptureDeviceList.Instance;

        for (int i = 0; i < devices.Count; i++)
        {
            var d = devices[i];
            if (IsPseudoDevice(d.Name)) continue;   // hide libpcap pseudo-devices (Linux)
            var display = string.IsNullOrWhiteSpace(d.Description)
                ? d.Name
                : $"{d.Description} ({d.Name})";
            result.Add(new NetworkDeviceInfo(d.Name, display, i));
        }

        return result;
    }

    // libpcap on Linux enumerates capture pseudo-devices alongside real NICs: the "any" aggregator,
    // loopback, and the dbus / netfilter / usb / bluetooth monitors. None carry Albion's UDP game
    // traffic and several reject a BPF "udp port" filter outright ("D-Bus link-layer type filtering
    // not implemented"), so they are hidden from the picker and skipped in capture-all mode. Real NIC
    // names (eth*, wlan*, enp*, the Windows \Device\NPF_{GUID} form) never match.
    public static bool IsPseudoDevice(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();

        if (n.Equals("any", StringComparison.OrdinalIgnoreCase) ||
            n.Equals("lo", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var p in PseudoPrefixes)
            if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return true;

        return false;
    }

    private static readonly string[] PseudoPrefixes =
        ["dbus-", "nflog", "nfqueue", "nfdrop", "usbmon", "bluetooth"];
}
