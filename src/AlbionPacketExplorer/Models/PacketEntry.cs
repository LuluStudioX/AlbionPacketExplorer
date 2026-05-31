namespace AlbionPacketExplorer.Models;

public record PacketEntry(
    DateTime Timestamp,
    string Kind,
    int Code,
    Dictionary<string, ParamValue> Params)
{
    public int KeyCount => Params.Count;
    public string ResolvedSummary { get; set; } = string.Empty;
}
