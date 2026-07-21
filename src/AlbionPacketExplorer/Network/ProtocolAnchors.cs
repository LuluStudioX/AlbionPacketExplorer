namespace AlbionPacketExplorer.Network;

/// <summary>
/// Protocol codes that have never moved across Albion client versions - the ground-truth
/// landmarks the content-adaptive <see cref="Il2CppMetadataReader"/> depends on. One list,
/// two roles:
///   1. Value-base PIN (input): member names and layout are discovered structurally, but the
///      absolute value-blob base is ambiguous; these known name->value pairs pin it
///      (Il2CppMetadataReader.DiscoverValueBase).
///   2. Confidence AUDIT (output): after parsing, the scan re-checks each landmark; any mismatch
///      means the parse is untrustworthy -> low-confidence -> the diff is withheld from auto-report.
/// If Albion ever moves one of these (it never has), the audit trips loudly - update here, one place.
/// </summary>
public static class ProtocolAnchors
{
    public static readonly IReadOnlyList<Il2CppMetadataReader.Anchor> All =
    [
        new("EventCodes", "Leave", 1),
        new("EventCodes", "Move", 3),
        new("EventCodes", "NewSimpleItem", 32),
        new("EventCodes", "NewBuilding", 45),
        new("OperationCodes", "Move", 22),
    ];
}
