namespace AlbionPacketExplorer.Models;

// A value type (not a heap class) so a packet's params carry no per-value 16-byte object header.
// Value stays boxed for now (de-boxing is a separate, deferred effort).
public readonly record struct ParamValue(string Type, object? Value);
