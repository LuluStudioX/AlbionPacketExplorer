namespace AlbionPacketExplorer.Models;

public class KeyStats
{
    public string Key { get; set; } = "";
    public int PresenceCount { get; set; }
    public int TotalPackets { get; set; }
    public double PresencePct => TotalPackets == 0 ? 0 : (double) PresenceCount / TotalPackets * 100;
    public HashSet<string> Types { get; } = [];
    public List<object?> SampleValues { get; } = [];
}
