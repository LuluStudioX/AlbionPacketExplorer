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
            var display = string.IsNullOrWhiteSpace(d.Description)
                ? d.Name
                : $"{d.Description} ({d.Name})";
            result.Add(new NetworkDeviceInfo(d.Name, display, i));
        }

        return result;
    }
}
