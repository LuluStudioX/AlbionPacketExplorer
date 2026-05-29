using AlbionPacketExplorer.Models;
using System.Text;

namespace AlbionPacketExplorer.Services;

public static class ConstructorExporter
{
    public static string Export(CodeStats stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// {stats.Kind} code={stats.Code} — {stats.Count} packets");

        foreach (var (key, ks) in stats.Keys.OrderBy(k => int.TryParse(k.Key, out var n) ? n : 999))
        {
            var typeStr = string.Join("/", ks.Types);
            sb.AppendLine($"// key {key}: {typeStr,-12} present {ks.PresencePct,5:F0}%");
        }

        sb.AppendLine($"public class {stats.Kind}Code{stats.Code}(Dictionary<byte, object> parameters)");
        sb.AppendLine("{");

        foreach (var (key, ks) in stats.Keys.Where(k => k.Key != "252" && k.Key != "253").OrderBy(k => int.TryParse(k.Key, out var n) ? n : 999))
        {
            var csType = GuessType(ks.Types);
            var propName = $"Key{key}";
            var defaultVal = DefaultFor(csType);
            sb.AppendLine($"    public {csType} {propName} {{ get; }} = parameters.TryGetValue({key}, out var v{key}) ? Convert.To{ConvertMethod(csType)}(v{key}) : {defaultVal};");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GuessType(HashSet<string> types)
    {
        if (types.Contains("Int64")) return "long";
        if (types.Contains("Int32")) return "int";
        if (types.Contains("Int16")) return "int";
        if (types.Contains("Byte")) return "byte";
        if (types.Contains("Boolean")) return "bool";
        if (types.Contains("Single")) return "float";
        if (types.Contains("String")) return "string";
        return "object";
    }

    private static string DefaultFor(string csType) => csType switch
    {
        "long" => "0L",
        "int" => "0",
        "byte" => "0",
        "bool" => "false",
        "float" => "0f",
        "string" => "string.Empty",
        _ => "null"
    };

    private static string ConvertMethod(string csType) => csType switch
    {
        "long" => "Int64",
        "int" => "Int32",
        "byte" => "ToByte",
        "bool" => "Boolean",
        "float" => "Single",
        "string" => "ToString",
        _ => "ToString"
    };
}
