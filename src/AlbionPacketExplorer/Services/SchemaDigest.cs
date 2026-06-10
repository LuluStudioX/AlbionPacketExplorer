using System.Text.Json;
using System.Text.Json.Serialization;
using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Network;

namespace AlbionPacketExplorer.Services;

/// <summary>One observed value and how often it was seen (already sanitized for sharing).</summary>
public sealed class DigestTopValue
{
    [JsonPropertyName("v")] public string Value { get; set; } = "";
    [JsonPropertyName("n")] public int Count { get; set; }
}

/// <summary>Per-field statistics for one byte key of a packet code.</summary>
public sealed class DigestKey
{
    [JsonPropertyName("types")] public List<string> Types { get; set; } = [];
    [JsonPropertyName("present")] public int Present { get; set; }
    [JsonPropertyName("min")] public double? Min { get; set; }
    [JsonPropertyName("max")] public double? Max { get; set; }
    [JsonPropertyName("distinct")] public int Distinct { get; set; }
    [JsonPropertyName("capped")] public bool Capped { get; set; }
    [JsonPropertyName("top")] public List<DigestTopValue>? Top { get; set; }
    /// <summary>How many top-value samples were withheld by the privacy filter.</summary>
    [JsonPropertyName("redacted")] public int Redacted { get; set; }
}

public sealed class DigestCode
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("keys")] public Dictionary<string, DigestKey> Keys { get; set; } = [];
}

public sealed class SchemaDigest
{
    [JsonPropertyName("v")] public int Version { get; set; } = 1;
    [JsonPropertyName("app")] public string App { get; set; } = "";
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = "";
    [JsonPropertyName("schemaCommit")] public string SchemaCommit { get; set; } = "";
    /// <summary>UTC date only - day granularity keeps the payload free of session timing.</summary>
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = "";
    [JsonPropertyName("mode")] public string Mode { get; set; } = "";
    [JsonPropertyName("codes")] public List<DigestCode> Codes { get; set; } = [];
}

/// <summary>
/// Builds a shareable, privacy-filtered summary of the aggregated per-code field statistics.
/// The digest carries shape and distribution only (types, presence, ranges, distinct counts,
/// whitelisted samples) - never raw packets, free-text strings, GUIDs, or precise timestamps -
/// so a 20MB capture folds into a few-hundred-KB artifact that is safe to send.
/// </summary>
public static class SchemaDigestBuilder
{
    private const int TopValueCount = 5;

    // .NET DateTime ticks for modern dates / unix epoch milliseconds. Values in these ranges are
    // session timestamps: their exact values are withheld and ranges rounded to whole days.
    private static bool IsTicksLike(double v) => v is > 5e17 and < 8e17;
    private static bool IsUnixMsLike(double v) => v is > 1e12 and < 3e12;
    private const double TicksPerDay = 864_000_000_000d;
    private const double MsPerDay = 86_400_000d;

    public static SchemaDigest Build(
        IReadOnlyCollection<CodeStats> stats,
        PacketSchemaService? schema,
        bool unknownOnly,
        string appVersion)
    {
        var digest = new SchemaDigest
        {
            App = appVersion,
            SchemaVersion = schema?.SchemaVersion ?? "",
            SchemaCommit = schema?.SchemaCommit ?? "",
            CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Mode = unknownOnly ? "unknown" : "all"
        };

        var ordered = stats
            .Where(s => !unknownOnly || HasUnknownShape(s, schema))
            .OrderBy(s => s.Kind, StringComparer.Ordinal)
            .ThenBy(s => s.Code);

        foreach (var s in ordered)
        {
            var dc = new DigestCode
            {
                Kind = s.Kind,
                Code = s.Code,
                Name = NullIfEmpty(PacketNameResolver.Resolve(s.Kind, s.Code)),
                Count = s.Count
            };

            foreach (var (key, ks) in s.Keys.OrderBy(kv => ParseKey(kv.Key)))
                dc.Keys[key] = BuildKey(ks);

            digest.Codes.Add(dc);
        }

        return digest;
    }

    public static string ToJson(SchemaDigest digest, bool indented = false) =>
        JsonSerializer.Serialize(digest, indented ? JsonIndented : JsonCompact);

    private static readonly JsonSerializerOptions JsonCompact = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions JsonIndented = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    /// <summary>
    /// A code is worth sharing in "unknown only" mode when its name is unmapped or any seen
    /// payload key (echo keys excluded) has no schema param entry yet.
    /// </summary>
    private static bool HasUnknownShape(CodeStats s, PacketSchemaService? schema)
    {
        if (string.IsNullOrEmpty(PacketNameResolver.Resolve(s.Kind, s.Code))) return true;
        if (schema == null) return true;

        var type = schema.GetSchema(s.Kind, s.Code);
        if (type == null) return true;

        foreach (var key in s.Keys.Keys)
        {
            if (IsEchoKey(s.Kind, key)) continue;
            if (!type.Params.ContainsKey(key)) return true;
        }
        return false;
    }

    // Key 252 on EVENTs / 253 on REQUESTs+RESPONSEs is the code echo, not payload.
    private static bool IsEchoKey(string kind, string key) =>
        kind == "EVENT" ? key == "252" : key == "253";

    private static DigestKey BuildKey(KeyStats ks)
    {
        var dk = new DigestKey
        {
            Types = ks.Types.OrderBy(t => t, StringComparer.Ordinal).ToList(),
            Present = ks.PresenceCount,
            Distinct = ks.ValueCounts.Count,
            Capped = ks.DistinctCapped
        };

        var timeLike = false;
        if (ks.NumericMin is { } min && ks.NumericMax is { } max)
        {
            timeLike = (IsTicksLike(min) && IsTicksLike(max)) || (IsUnixMsLike(min) && IsUnixMsLike(max));
            dk.Min = timeLike ? RoundToDay(min) : min;
            dk.Max = timeLike ? RoundToDay(max) : max;
        }

        // Arrays/dicts are sampled as their formatted reprs (and Byte[16] = GUIDs), so any
        // array-typed field shares no samples at all - shape and presence carry the signal.
        var hasArray = ks.Types.Any(t => t.EndsWith("[]", StringComparison.Ordinal));

        var top = new List<DigestTopValue>();
        var redacted = 0;
        foreach (var (statKey, count) in ks.ValueCounts.OrderByDescending(kv => kv.Value))
        {
            if (top.Count >= TopValueCount) break;

            if (statKey.Numeric is { } num)
            {
                if (timeLike || IsTicksLike(num) || IsUnixMsLike(num)) { redacted++; continue; }
                top.Add(new DigestTopValue { Value = statKey.Display(), Count = count });
                continue;
            }

            var display = statKey.Display();
            if (display is "(null)" or "True" or "False")
            {
                top.Add(new DigestTopValue { Value = display, Count = count });
                continue;
            }

            if (hasArray || !IsSafeIdentifier(display)) { redacted++; continue; }
            top.Add(new DigestTopValue { Value = display, Count = count });
        }

        dk.Top = top.Count > 0 ? top : null;
        dk.Redacted = redacted;
        return dk;
    }

    /// <summary>
    /// Whitelist for shareable string samples: game identifiers (uniqueNames) are uppercase with
    /// underscores ("T4_FARM_CARROT_SEED", "@ITEMS_T4_..."). Player/guild names never match the
    /// required underscore + uppercase-only shape, so free text is withheld by construction.
    /// </summary>
    private static bool IsSafeIdentifier(string s)
    {
        if (s.Length is < 3 or > 64 || !s.Contains('_')) return false;
        foreach (var c in s)
        {
            var ok = c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '@' or ':' or '.' or '-';
            if (!ok) return false;
        }
        return true;
    }

    private static double RoundToDay(double v) =>
        IsTicksLike(v) ? Math.Floor(v / TicksPerDay) * TicksPerDay
                       : Math.Floor(v / MsPerDay) * MsPerDay;

    private static int ParseKey(string key) => int.TryParse(key, out var i) ? i : int.MaxValue;

    private static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
}
