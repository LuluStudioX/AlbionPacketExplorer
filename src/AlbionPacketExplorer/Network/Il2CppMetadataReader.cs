using System.Buffers.Binary;
using System.Text;

namespace AlbionPacketExplorer.Network;

/// <summary>
/// Dependency-free reader for Unity IL2CPP <c>global-metadata.dat</c> that extracts the members
/// (name + wire value) of named enums such as <c>Albion.Common.Photon.EventCodes</c>.
///
/// <para><b>Version-adaptive by design.</b> It does NOT trust the header's declared metadata
/// version or the header's (offset,size) pair ORDER - Albion ships a non-standard header (the
/// 2026-07-21 "Radiant Wilds Patch 4" build declares version 39 and reorders the pair table, which
/// broke every version-indexed reader). Instead it discovers each table it needs by CONTENT:
/// it finds the target enum's type by the (nameIndex, namespaceIndex) signature, derives the
/// type-definition stride and the field-start field offset from the layout itself, walks the
/// field table for member names, and reads member values from the field-default-value blob.</para>
///
/// <para><b>Anchor-gated.</b> The literal values live in the default-value data blob, whose base
/// can't be pinned structurally alone; the caller passes a few <see cref="Anchor"/>s (members whose
/// value is known and has never moved). The blob base + literal width are solved so that ALL anchors
/// read back correct. If discovery fails at any step - unknown layout, no anchor solution, a member
/// name that isn't a valid identifier - the whole read returns <c>null</c> and the caller degrades to
/// "scan unavailable" rather than reporting a wrong diff. A mis-parse can never fabricate a change.</para>
///
/// <para>Structural assumptions kept (stable across all IL2CPP versions to date, since they are the
/// FIRST fields of their structs): type-definition begins with nameIndex(i32), namespaceIndex(i32);
/// field-definition begins with nameIndex(i32) and is 12 bytes; field-default-value begins with
/// fieldIndex(i32) and carries dataIndex(i32) at +8, 12 bytes. What varies by version - table bases,
/// the type-definition size, and the byte offset of fieldStart within it - is all discovered.</para>
/// </summary>
public static class Il2CppMetadataReader
{
    private const uint Sanity = 0xFAB11BAF;
    private const int FieldSize = 12;             // Il2CppFieldDefinition: nameIndex, typeIndex, token
    private const int FieldDefaultValueSize = 12; // fieldIndex, typeIndex, dataIndex

    public sealed record EnumMember(string Name, int Value);
    public sealed record EnumDump(string Namespace, string Name, IReadOnlyList<EnumMember> Members);

    /// <summary>A member whose value is known and has never moved; used to pin the value blob.</summary>
    public sealed record Anchor(string Enum, string Member, int Value);

    /// <summary>
    /// Reads the requested enums from a metadata file, one <see cref="EnumDump"/> per target found.
    /// Returns <c>null</c> if the file can't be read, the format isn't recognised, or the anchors
    /// can't be satisfied (which is the safe failure - never a wrong answer).
    /// </summary>
    public static IReadOnlyDictionary<string, EnumDump>? ReadEnums(
        string metadataPath,
        IReadOnlyCollection<(string Namespace, string Name)> targets,
        IReadOnlyCollection<Anchor>? anchors = null)
    {
        try
        {
            var data = File.ReadAllBytes(metadataPath);
            return Parse(data, targets, anchors ?? []);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, EnumDump>? Parse(
        byte[] d,
        IReadOnlyCollection<(string Namespace, string Name)> targets,
        IReadOnlyCollection<Anchor> anchors)
    {
        if (d.Length < 0x100 || ReadU32(d, 0) != Sanity) { Log("sanity"); return null; }
        if (targets.Count == 0) return null;

        // 1+2) String table base, a pivot type, and the type-definition stride - together, so the
        // stride's sibling check rejects coincidental (strOff, type) matches.
        var pivot = targets.First();
        if (!DiscoverStringTableAndStride(d, pivot.Namespace, pivot.Name,
                out int strOff, out int pivotTd, out int stride))
        { Log("stringtable"); return null; }
        Log($"strOff={strOff} pivotTd={pivotTd} stride={stride}");

        // Locate every target type up front (pivot first); their type offsets cross-validate layout.
        var located = new List<(string Ns, string Name, int Td)>();
        foreach (var (ns, name) in targets)
        {
            int td = name == pivot.Name && ns == pivot.Namespace ? pivotTd : FindType(d, strOff, ns, name);
            if (td >= 0) located.Add((ns, name, td));
        }
        if (located.Count == 0) return null;

        // 3) fieldStart offset within a type-definition, and the field-table base. The pivot enum's
        // anchor member names must appear in its discovered member run - pins the layout uniquely.
        var pivotMembers = anchors.Where(a => a.Enum == pivot.Name).Select(a => a.Member).ToHashSet();
        if (!DiscoverFieldLayout(d, strOff, stride, located.Select(t => t.Td).ToList(), pivotMembers,
                out int fso, out int fieldsOff))
        { Log("fieldlayout"); return null; }
        Log($"fso={fso} fieldsOff={fieldsOff}");

        // 4) Names + field-default dataIndex per target (no values yet).
        var staged = new List<Staged>();
        foreach (var (ns, name, td) in located)
        {
            var s = StageEnum(d, strOff, fieldsOff, td, stride, fso, ns, name);
            if (s is null) { Log($"stage:{name}"); return null; } // located but unreadable -> unsafe, bail
            staged.Add(s);
        }
        if (staged.Count == 0) return null;

        Log($"staged={staged.Count} members[0]={staged[0].Members.Count}");
        // 5) Literal width (spacing of consecutive members' dataIndex) + value-blob base (from anchors).
        int width = DiscoverWidth(staged);
        if (width == 0) { Log("width"); return null; }
        Log($"width={width}");
        if (!DiscoverValueBase(d, staged, anchors, width, out int dataOff)) { Log("valuebase"); return null; }
        Log($"dataOff={dataOff}");

        // 6) Materialise.
        var result = new Dictionary<string, EnumDump>();
        foreach (var s in staged)
        {
            var members = new List<EnumMember>(s.Members.Count);
            foreach (var (mName, dataIndex) in s.Members)
                members.Add(new EnumMember(mName, ReadSigned(d, dataOff + dataIndex, width)));
            result[s.Name] = new EnumDump(s.Namespace, s.Name, members);
        }
        return result.Count == 0 ? null : result;
    }

    private sealed record Staged(
        string Namespace, string Name, IReadOnlyList<(string Name, int DataIndex)> Members);

    private static void Log(string msg)
    {
        if (Environment.GetEnvironmentVariable("IL2CPP_TRACE") == "1")
            Console.Error.WriteLine($"[il2cpp] {msg}");
    }

    // --- discovery steps -----------------------------------------------------------------------

    /// <summary>
    /// Finds the string-table base, the pivot type-definition, and the type stride together.
    ///
    /// <para>The string base can't be derived from the enum's own name offset alone: with
    /// strOff = rawNameOff - a, the name always "resolves", so that check is vacuous. Instead we take
    /// candidate bases from the header's raw offset numbers (used only as candidates, not trusted for
    /// meaning), and for each require that the type's EXACT (nameIndex, namespaceIndex) = (rawName -
    /// strOff, rawNs - strOff) 8-byte signature physically exists in the file. Two specific large
    /// integers appearing together only happens at the real base. The match is then confirmed by a
    /// cluster of sibling types sharing the namespace at a single stride.</para>
    /// </summary>
    private static bool DiscoverStringTableAndStride(
        byte[] d, string ns, string name, out int strOff, out int typeDef, out int stride)
    {
        strOff = 0; typeDef = -1; stride = 0;
        int rawName = FindCString(d, name);
        int rawNs = FindCString(d, ns);
        if (rawName < 0 || rawNs < 0) return false;

        var sig = new byte[8];
        var seen = new HashSet<int>();
        // Header = sanity(4) + version(4) + a run of u32 offset/size fields; read a generous slice of
        // those u32s as candidate string-table bases. Values only - the header's field ORDER is not
        // trusted (Albion reorders it).
        for (int off = 8; off + 4 <= Math.Min(d.Length, 8 + 200 * 4); off += 4)
        {
            int cand = ReadI32(d, off);
            if (cand <= 0 || cand >= rawNs || !seen.Add(cand)) continue;
            int nameIdx = rawName - cand, nsIdx = rawNs - cand;
            if (nameIdx <= 0 || nsIdx <= 0) continue;
            BinaryPrimitives.WriteInt32LittleEndian(sig.AsSpan(0, 4), nameIdx);
            BinaryPrimitives.WriteInt32LittleEndian(sig.AsSpan(4, 4), nsIdx);
            int q = IndexOf(d, sig, 0);
            if (q < 0) continue;
            int s = StrideFromSiblings(d, cand, nsIdx);
            if (s > 0) { strOff = cand; typeDef = q; stride = s; return true; }
        }
        return false;
    }

    /// <summary>
    /// Stride of the types sharing <paramref name="nsIdx"/>: they sit contiguously, so the gaps
    /// share one GCD. Requires at least 3 siblings whose name resolves to an identifier and whose
    /// gaps are a clean multiple of the GCD - enough to reject a coincidental string match. Returns
    /// 0 (reject) otherwise.
    /// </summary>
    private static int StrideFromSiblings(byte[] d, int strOff, int nsIdx)
    {
        var pat = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(pat, nsIdx);
        var starts = new List<int>();
        for (int i = IndexOf(d, pat, 0); i >= 0; i = IndexOf(d, pat, i + 1))
        {
            int td = i - 4; // namespaceIndex is the 2nd field; type starts 4 bytes earlier
            if (td < 0) continue;
            int nameIdx = ReadI32(d, td);
            if (nameIdx > 0 && IsIdentifier(StringAt(d, strOff, nameIdx))) starts.Add(td);
        }
        // The pivot's namespace (Albion.Common.Photon) has many types; a coincidental string match
        // yields at most a few. Require a solid cluster at a plausible type-definition stride.
        if (starts.Count < 6) return 0;
        starts.Sort();
        int g = 0;
        for (int i = 1; i < starts.Count; i++) g = Gcd(g, starts[i] - starts[i - 1]);
        if (g < 48 || g > 400) return 0; // Il2CppTypeDefinition is dozens of bytes, never 12
        // every gap must be a multiple of g (contiguous table, uniform stride)
        for (int i = 1; i < starts.Count; i++)
            if ((starts[i] - starts[i - 1]) % g != 0) return 0;
        return g;
    }

    /// <summary>
    /// The byte offset of fieldStart inside a type-definition, and the field-table base. A type's
    /// fieldStart is an index into the field table; the field at that index is an enum's hidden
    /// <c>value__</c>. For each candidate offset we require that EVERY located enum lands its
    /// <c>value__</c> at its fieldStart simultaneously (a strong, near-unique constraint), then that
    /// the pivot's whole member run resolves to identifiers. With a single target it falls back to
    /// full-run validation alone.
    /// </summary>
    private static bool DiscoverFieldLayout(
        byte[] d, int strOff, int stride, IReadOnlyList<int> tds, IReadOnlySet<string> pivotMembers,
        out int fso, out int fieldsOff)
    {
        fso = 0; fieldsOff = 0;
        int valueIdx = FindCString(d, "value__");
        if (valueIdx < 0) return false;
        valueIdx -= strOff;

        // Every field entry named "value__" (each enum's backing field).
        var valPat = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(valPat, valueIdx);
        var valuePositions = new List<int>();
        var valueSet = new HashSet<int>();
        for (int i = IndexOf(d, valPat, 0); i >= 0; i = IndexOf(d, valPat, i + 1))
        { valuePositions.Add(i); valueSet.Add(i); }
        if (valuePositions.Count == 0) return false;

        int pivotTd = tds[0];
        for (int cand = 4; cand <= stride - 4; cand++)
        {
            int f0 = ReadI32(d, pivotTd + cand);
            int f1 = ReadI32(d, pivotTd + stride + cand);
            int count = f1 - f0;
            if (f0 < 0 || count < 2 || count > 20000) continue;

            // fieldsOff derived from each candidate value__ position for the pivot; accept when it
            // simultaneously puts value__ at every other located enum's fieldStart, and the pivot's
            // full member run is identifiers.
            foreach (int vpos in valuePositions)
            {
                long fo = (long)vpos - (long)f0 * FieldSize;
                if (fo <= 0 || fo + (long)f1 * FieldSize + FieldSize > d.Length) continue;
                int fob = (int)fo;

                bool aligned = true;
                for (int t = 1; t < tds.Count && aligned; t++)
                {
                    long other = fo + (long)ReadI32(d, tds[t] + cand) * FieldSize;
                    aligned = other >= 0 && other <= int.MaxValue && valueSet.Contains((int)other);
                }
                if (!aligned) continue;
                if (!RunValidates(d, strOff, fob, f0, count, pivotMembers)) continue;
                fso = cand; fieldsOff = fob; return true;
            }
        }
        return false;
    }

    /// <summary>
    /// value__ at f0, then f0+1..f0+count-1 and the next type's first field all identifiers, AND the
    /// run contains every required pivot-member name (the anchors) - which a coincidental run won't.
    /// </summary>
    private static bool RunValidates(
        byte[] d, int strOff, int fieldsOff, int f0, int count, IReadOnlySet<string> required)
    {
        if (StringAt(d, strOff, ReadI32(d, fieldsOff + f0 * FieldSize)) != "value__") return false;
        var names = new HashSet<string>();
        for (int k = 1; k <= count; k++)
        {
            var n = FieldName(d, strOff, fieldsOff, f0 + k);
            if (!IsIdentifier(n)) return false;
            if (k < count) names.Add(n);
        }
        return required.All(names.Contains);
    }

    /// <summary>Reads member names + their field-default dataIndex for one enum (values come later).</summary>
    private static Staged? StageEnum(
        byte[] d, int strOff, int fieldsOff, int td, int stride, int fso, string ns, string name)
    {
        int f0 = ReadI32(d, td + fso);
        int f1 = ReadI32(d, td + stride + fso);
        int count = f1 - f0;
        if (f0 < 0 || count < 2 || count > 20000) return null;

        // field-default-value entry for the first member (fieldIndex == f0+1), confirmed by the
        // next entry being fieldIndex f0+2 (members are contiguous, one default each).
        int firstMember = f0 + 1;
        int fdv = FindFdvRun(d, firstMember);
        if (fdv < 0) return null;

        var members = new List<(string, int)>(count - 1);
        for (int k = 1; k < count; k++)
        {
            int fieldIndex = f0 + k;
            var mName = FieldName(d, strOff, fieldsOff, fieldIndex);
            if (!IsIdentifier(mName)) return null;
            int entry = fdv + (k - 1) * FieldDefaultValueSize;
            if (ReadI32(d, entry) != fieldIndex) return null; // contiguity broken -> unsafe
            members.Add((mName, ReadI32(d, entry + 8)));
        }
        return new Staged(ns, name, members);
    }

    /// <summary>Locate the contiguous field-default-value entries for an enum by its first member's index.</summary>
    private static int FindFdvRun(byte[] d, int firstMember)
    {
        var pat = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(pat, firstMember);
        for (int i = IndexOf(d, pat, 0); i >= 0; i = IndexOf(d, pat, i + 1))
        {
            if (i + 2 * FieldDefaultValueSize <= d.Length &&
                ReadI32(d, i + FieldDefaultValueSize) == firstMember + 1)
                return i;
        }
        return -1;
    }

    /// <summary>Literal width = the constant spacing of consecutive members' dataIndex (1/2/4/8).</summary>
    private static int DiscoverWidth(List<Staged> staged)
    {
        foreach (var s in staged)
            for (int i = 1; i < s.Members.Count; i++)
            {
                int w = s.Members[i].DataIndex - s.Members[i - 1].DataIndex;
                if (w is 1 or 2 or 4 or 8) return w;
            }
        return 0;
    }

    /// <summary>
    /// Value-blob base: the offset where every anchor member reads back its known value at the
    /// chosen width. Solves off the first anchor then verifies the rest, so a coincidental hit for
    /// one anchor can't slip through.
    /// </summary>
    private static bool DiscoverValueBase(
        byte[] d, List<Staged> staged, IReadOnlyCollection<Anchor> anchors, int width, out int dataOff)
    {
        dataOff = 0;
        // Map each anchor to its member dataIndex.
        var byName = staged.ToDictionary(s => s.Name, s => s.Members.ToDictionary(m => m.Name, m => m.DataIndex));
        var pins = new List<(int DataIndex, int Value)>();
        foreach (var a in anchors)
            if (byName.TryGetValue(a.Enum, out var m) && m.TryGetValue(a.Member, out int di))
                pins.Add((di, a.Value));
        if (pins.Count == 0) return false;

        var (di0, val0) = pins[0];
        var pat = new byte[width];
        WriteSigned(pat, val0, width);
        for (int L = IndexOf(d, pat, 0); L >= 0; L = IndexOf(d, pat, L + 1))
        {
            long baseOff = (long)L - di0;
            if (baseOff <= 0) continue;
            bool ok = true;
            foreach (var (di, val) in pins)
            {
                long at = baseOff + di;
                if (at < 0 || at + width > d.Length || ReadSigned(d, (int)at, width) != val) { ok = false; break; }
            }
            if (ok) { dataOff = (int)baseOff; return true; }
        }
        return false;
    }

    // --- primitives ----------------------------------------------------------------------------

    private static string FieldName(byte[] d, int strOff, int fieldsOff, int fieldIndex) =>
        StringAt(d, strOff, ReadI32(d, fieldsOff + fieldIndex * FieldSize));

    private static int FindType(byte[] d, int strOff, string ns, string name)
    {
        int rawName = FindCString(d, name), rawNs = FindCString(d, ns);
        if (rawName < 0 || rawNs < 0) return -1;
        int nameIdx = rawName - strOff, nsIdx = rawNs - strOff;
        var pat = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(pat.AsSpan(0, 4), nameIdx);
        BinaryPrimitives.WriteInt32LittleEndian(pat.AsSpan(4, 4), nsIdx);
        return IndexOf(d, pat, 0);
    }

    private static bool IsIdentifier(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length > 256) return false;
        foreach (char c in s)
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        return true;
    }

    private static int Gcd(int a, int b) { while (b != 0) (a, b) = (b, a % b); return Math.Abs(a); }

    private static int ReadSigned(byte[] d, int off, int width) => width switch
    {
        1 => (sbyte) d[off],
        2 => BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(off, 2)),
        8 => (int) BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan(off, 8)),
        _ => BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(off, 4))
    };

    private static void WriteSigned(byte[] buf, int value, int width)
    {
        switch (width)
        {
            case 1: buf[0] = (byte)(sbyte)value; break;
            case 2: BinaryPrimitives.WriteInt16LittleEndian(buf, (short)value); break;
            case 8: BinaryPrimitives.WriteInt64LittleEndian(buf, value); break;
            default: BinaryPrimitives.WriteInt32LittleEndian(buf, value); break;
        }
    }

    private static uint ReadU32(byte[] d, int off) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(off, 4));
    private static int ReadI32(byte[] d, int off) => BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(off, 4));

    /// <summary>Offset of the first occurrence of the C-string <paramref name="s"/> (NUL-terminated), or -1.</summary>
    private static int FindCString(byte[] d, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        for (int i = IndexOf(d, bytes, 0); i >= 0; i = IndexOf(d, bytes, i + 1))
            if (i + bytes.Length < d.Length && d[i + bytes.Length] == 0 && (i == 0 || d[i - 1] == 0))
                return i;
        // Fall back to any occurrence terminated by NUL (some tables don't NUL-prefix the first entry).
        for (int i = IndexOf(d, bytes, 0); i >= 0; i = IndexOf(d, bytes, i + 1))
            if (i + bytes.Length < d.Length && d[i + bytes.Length] == 0)
                return i;
        return -1;
    }

    /// <summary>The NUL-terminated string at <c>strOff + idx</c>, or "" if out of range.</summary>
    private static string StringAt(byte[] d, int strOff, int idx)
    {
        long o = (long)strOff + idx;
        if (o < 0 || o >= d.Length) return "";
        int start = (int)o, end = start;
        while (end < d.Length && d[end] != 0) end++;
        return Encoding.UTF8.GetString(d, start, end - start);
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int from)
    {
        if (needle.Length == 0 || from < 0) return -1;
        int last = haystack.Length - needle.Length;
        byte first = needle[0];
        for (int i = from; i <= last; i++)
        {
            if (haystack[i] != first) continue;
            int k = 1;
            for (; k < needle.Length; k++) if (haystack[i + k] != needle[k]) break;
            if (k == needle.Length) return i;
        }
        return -1;
    }
}
