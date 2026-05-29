namespace AlbionPacketExplorer.Models;

public class CodeStats
{
    public string Kind { get; set; } = "";
    public int Code { get; set; }
    public int Count { get; set; }
    public Dictionary<string, KeyStats> Keys { get; } = [];
}
