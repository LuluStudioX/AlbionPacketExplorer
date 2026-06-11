using System.Buffers.Binary;
using System.Text;

namespace AlbionPacketExplorer.Network;

/// <summary>
/// Minimal, dependency-free reader for Unity IL2CPP <c>global-metadata.dat</c> files. It extracts
/// the members of named enums (e.g. <c>Albion.Common.Photon.EventCodes</c>) straight from the
/// metadata tables: no external dumper, no native code, fully cross-platform managed parsing.
///
/// <para>Albion's protocol enums are dense and gapless (every ordinal present), so a member's wire
/// value is <c>base + declarationIndex</c>. We read only the first member's literal from the
/// default-value blob to pin <c>base</c>; the caller validates the result against known anchors,
/// which also guards against an unexpected metadata layout.</para>
///
/// <para>Targets metadata version 31 (Unity 2022+, current Albion client). The header tables we
/// read all precede the version-specific tail, so v29-v31 share this layout; any other version, a
/// bad sanity value, or a parse error yields <c>null</c> and the caller degrades gracefully.</para>
/// </summary>
public static class Il2CppMetadataReader
{
    private const uint Sanity = 0xFAB11BAF;
    private const int TypeDefinitionSize = 88; // Il2CppTypeDefinition, v27-v31
    private const int FieldSize = 12;          // Il2CppFieldDefinition: nameIndex, typeIndex, token
    private const int FieldDefaultValueSize = 12; // fieldIndex, typeIndex, dataIndex

    public sealed record EnumMember(string Name, int Value);
    public sealed record EnumDump(string Namespace, string Name, IReadOnlyList<EnumMember> Members);

    /// <summary>
    /// Reads the requested enums from a metadata file. Returns one <see cref="EnumDump"/> per target
    /// that was found, keyed by enum name. Returns <c>null</c> if the file can't be read or the
    /// format isn't recognised; individual missing targets are simply absent from the result.
    /// </summary>
    public static IReadOnlyDictionary<string, EnumDump>? ReadEnums(
        string metadataPath, IReadOnlyCollection<(string Namespace, string Name)> targets)
    {
        try
        {
            var data = File.ReadAllBytes(metadataPath);
            return Parse(data, targets);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, EnumDump>? Parse(
        byte[] d, IReadOnlyCollection<(string Namespace, string Name)> targets)
    {
        if (d.Length < 0x100) return null;
        if (ReadU32(d, 0) != Sanity) return null;
        var version = ReadI32(d, 4);
        if (version is < 29 or > 31) return null; // only the layout we've verified

        // Header is sanity + version followed by (offset,size) u32 pairs. We need a handful of them.
        // Indices are the position of each pair in that fixed sequence.
        int Pair(int idx) => ReadI32(d, 8 + idx * 8);          // offset
        int stringOff       = Pair(2);
        int fdvOff          = Pair(7),  fdvSize = ReadI32(d, 8 + 7 * 8 + 4);
        int defaultDataOff  = Pair(8);
        int fieldsOff       = Pair(11);
        int typeDefsOff     = Pair(19), typeDefsSize = ReadI32(d, 8 + 19 * 8 + 4);

        string Name(int strIdx) => ReadCString(d, stringOff + strIdx);

        // fieldDefaultValues: global field index -> offset into the default-value data blob.
        var fieldToData = new Dictionary<int, int>(fdvSize / FieldDefaultValueSize);
        for (int b = fdvOff; b < fdvOff + fdvSize; b += FieldDefaultValueSize)
            fieldToData[ReadI32(d, b)] = ReadI32(d, b + 8);

        var wanted = new HashSet<(string, string)>(targets);
        var result = new Dictionary<string, EnumDump>();
        int typeCount = typeDefsSize / TypeDefinitionSize;

        for (int i = 0; i < typeCount; i++)
        {
            int tdBase = typeDefsOff + i * TypeDefinitionSize;
            var name = Name(ReadI32(d, tdBase + 0));
            var ns = Name(ReadI32(d, tdBase + 4));
            if (!wanted.Contains((ns, name))) continue;

            int fieldStart = ReadI32(d, tdBase + 32);
            int fieldCount = ReadU16(d, tdBase + 68);
            var members = ReadEnumMembers(d, fieldsOff, defaultDataOff, fieldToData, Name, fieldStart, fieldCount);
            if (members != null)
                result[name] = new EnumDump(ns, name, members);
        }

        return result.Count == 0 ? null : result;
    }

    private static List<EnumMember>? ReadEnumMembers(
        byte[] d, int fieldsOff, int defaultDataOff, Dictionary<int, int> fieldToData,
        Func<int, string> name, int fieldStart, int fieldCount)
    {
        // Member fields in declaration order, skipping the hidden `value__` backing field.
        var memberFields = new List<(int FieldIndex, string Name)>(fieldCount);
        for (int k = 0; k < fieldCount; k++)
        {
            int fieldIndex = fieldStart + k;
            var fieldName = name(ReadI32(d, fieldsOff + fieldIndex * FieldSize));
            if (fieldName == "value__") continue;
            memberFields.Add((fieldIndex, fieldName));
        }
        if (memberFields.Count == 0) return null;

        // The literal value width = smallest gap between members' data offsets (1/2/4 bytes).
        var dataOffsets = memberFields
            .Where(m => fieldToData.ContainsKey(m.FieldIndex))
            .Select(m => fieldToData[m.FieldIndex])
            .OrderBy(x => x).ToList();
        if (dataOffsets.Count == 0) return null;

        int width = 4;
        for (int j = 1; j < dataOffsets.Count; j++)
        {
            int gap = dataOffsets[j] - dataOffsets[j - 1];
            if (gap is 1 or 2 or 4) { width = gap; break; }
        }

        // base = the first member's literal; remaining values follow from the gapless ordinal.
        if (!fieldToData.TryGetValue(memberFields[0].FieldIndex, out int firstData)) return null;
        int baseValue = ReadSigned(d, defaultDataOff + firstData, width);

        var members = new List<EnumMember>(memberFields.Count);
        for (int k = 0; k < memberFields.Count; k++)
            members.Add(new EnumMember(memberFields[k].Name, baseValue + k));
        return members;
    }

    private static int ReadSigned(byte[] d, int off, int width) => width switch
    {
        1 => (sbyte) d[off],
        2 => BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(off, 2)),
        _ => BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(off, 4))
    };

    private static uint ReadU32(byte[] d, int off) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(off, 4));
    private static int ReadI32(byte[] d, int off) => BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(off, 4));
    private static int ReadU16(byte[] d, int off) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(off, 2));

    private static string ReadCString(byte[] d, int off)
    {
        int end = off;
        while (end < d.Length && d[end] != 0) end++;
        return Encoding.UTF8.GetString(d, off, end - off);
    }
}
