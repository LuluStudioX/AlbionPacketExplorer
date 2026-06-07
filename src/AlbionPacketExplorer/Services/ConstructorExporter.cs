using System.Text;
using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Network;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Generates a SAT-style event/operation class scaffold for a code from its observed fields and
/// (when available) the curated schema. The result is meant to be pasted into SAT and adjusted,
/// using the same Object-to-T helper style SAT's constructors use.
/// </summary>
public static class ConstructorExporter
{
    public static string Export(CodeStats stats, PacketSchemaService? schema = null)
    {
        var sb = new StringBuilder();
        var name = PacketNameResolver.Resolve(stats.Kind, stats.Code);
        var className = ClassName(stats.Kind, stats.Code, name);

        sb.AppendLine($"// {stats.Kind} {stats.Code}{(string.IsNullOrEmpty(name) ? "" : $" {name}")} — {stats.Count} packets observed");

        var keys = stats.Keys
            .Where(k => k.Key != "252" && k.Key != "253")
            .OrderBy(k => int.TryParse(k.Key, out var n) ? n : int.MaxValue)
            .ToList();

        foreach (var (key, ks) in keys)
        {
            var note = schema?.GetParam(stats.Kind, stats.Code, key)?.Note ?? "";
            var range = ks.HasNumericRange ? $" range {ks.MinMaxDisplay}" : "";
            var guess = string.IsNullOrEmpty(ks.Heuristic) ? "" : $" ~{ks.Heuristic}";
            sb.AppendLine($"//   key {key,-3} {ks.TypesDisplay,-14} {ks.PresencePct,3:F0}% present  distinct {ks.DistinctDisplay}{range}{guess}{(string.IsNullOrEmpty(note) ? "" : $"  // {note}")}");
        }

        sb.AppendLine($"public class {className}");
        sb.AppendLine("{");

        // Properties
        foreach (var (key, ks) in keys)
        {
            var ps = schema?.GetParam(stats.Kind, stats.Code, key);
            var prop = PropName(ps?.Name, key);
            var (csType, _) = MapType(ks);
            sb.AppendLine($"    public {csType} {prop} {{ get; }}");
        }

        sb.AppendLine();
        sb.AppendLine($"    public {className}(Dictionary<byte, object> parameters)");
        sb.AppendLine("    {");

        foreach (var (key, ks) in keys)
        {
            var ps = schema?.GetParam(stats.Kind, stats.Code, key);
            var prop = PropName(ps?.Name, key);
            var (_, read) = MapType(ks);
            sb.AppendLine($"        if (parameters.TryGetValue({key}, out var v{key})) {prop} = v{key}{read};");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string ClassName(string kind, int code, string name)
    {
        if (string.IsNullOrEmpty(name)) return $"{Cap(kind.ToLowerInvariant())}Code{code}";
        return kind switch
        {
            "EVENT"    => name.EndsWith("Event") ? name : name + "Event",
            "RESPONSE" => name.EndsWith("Response") ? name : name + "Response",
            _          => name, // REQUEST/operation: keep as-is
        };
    }

    // (C# property type, value-read suffix using SAT-style ObjectToT helpers).
    private static (string Type, string Read) MapType(KeyStats ks)
    {
        if (ks.Types.Contains("Byte[]")) return ("Guid?", ".ObjectToGuid()");
        if (ks.Types.Any(t => t.EndsWith("[]"))) return ("object", " /* TODO: array */");
        if (ks.Types.Contains("String")) return ("string", " as string ?? string.Empty");
        if (ks.Types.Contains("Boolean")) return ("bool", ".ObjectToBool()");
        if (ks.Types.Contains("Single") || ks.Types.Contains("Double")) return ("double", ".ObjectToDouble()");
        if (ks.Types.Contains("Int64")) return ("long", ".ObjectToLong() ?? 0");
        if (ks.Types.Contains("Int16")) return ("short", ".ObjectToShort()");
        if (ks.Types.Contains("Byte")) return ("byte", ".ObjectToByte()");
        return ("int", ".ObjectToInt()"); // Int32 and default
    }

    private static string PropName(string? schemaName, string key)
    {
        if (string.IsNullOrWhiteSpace(schemaName)) return $"Key{key}";
        var cleaned = new string(schemaName.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (cleaned.Length == 0) return $"Key{key}";
        return char.ToUpperInvariant(cleaned[0]) + cleaned[1..];
    }

    private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
