using System.Text;
using System.Text.Json;

namespace AlbionPacketExplorer.Models;

/// <summary>
/// Single source of truth for PARSING the source params-object JSON shape
/// (<c>{ "0": { "type": .., "value": .. }, .. }</c>). The NDJSON and JSON-array load paths read the
/// input file through <see cref="ReadParams"/>, which yields a <see cref="ParamSet"/> with interned
/// keys/types; that set is then handed to <see cref="PackedParamStore.Append"/>, which owns the
/// arena's COMPACT-BINARY encoding (the arena is no longer JSON). Save/CSV/wire output is produced
/// separately by <c>PacketWire</c> from the decoded <see cref="ParamSet"/>, so a load -&gt; save cycle
/// stays byte-identical.
/// </summary>
public static class ParamCodec
{
    /// <summary>
    /// Reads a params object positioned at its <see cref="JsonTokenType.StartObject"/> (or skips a
    /// non-object) and returns a <see cref="ParamSet"/> with interned keys/types. This is the exact
    /// logic the NDJSON loader used; it is the only params parser in the app.
    /// </summary>
    public static ParamSet ReadParams(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return ParamSet.Empty;
        }

        var parameters = new List<KeyValuePair<string, ParamValue>>();

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            // Resolve the shared "0".."255" key straight from the UTF-8 token bytes - no string
            // allocation per key. Escaped or out-of-range names fall back to GetString+Intern.
            string name = !reader.HasValueSequence && TryGetByteKey(reader.ValueSpan, out var shared)
                ? shared
                : ParamKeys.Intern(reader.GetString()!);
            reader.Read(); // advance to the param value (expected an object)

            string type = "";
            object? value = null;

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    if (reader.ValueTextEquals("type"u8))
                    {
                        reader.Read();
                        type = reader.TokenType == JsonTokenType.String ? InternType(ref reader) : "";
                    }
                    else if (reader.ValueTextEquals("value"u8))
                    {
                        reader.Read();
                        value = ExtractValue(ref reader);
                    }
                    else
                    {
                        // Skip on a PropertyName consumes the name and its whole value.
                        reader.Skip();
                    }
                }
            }
            else
            {
                // Not an object: skip whatever it is, keep the type/value defaults.
                reader.Skip();
            }

            Upsert(parameters, name, new ParamValue(type, value));
        }

        return new ParamSet(parameters.ToArray());
    }

    // Parses a 1-3 digit property name (0..255) directly from its UTF-8 bytes and returns the shared
    // ParamKeys instance. Mirrors ParamKeys.Intern's canonicalization (leading zeros collapse).
    private static bool TryGetByteKey(ReadOnlySpan<byte> span, out string key)
    {
        key = "";
        if (span.Length is 0 or > 3) return false;
        int v = 0;
        foreach (byte b in span)
        {
            if (b < (byte)'0' || b > (byte)'9') return false;
            v = v * 10 + (b - '0');
        }
        if (v > 255) return false;
        key = ParamKeys.Get(v);
        return true;
    }

    // The distinct type names on the wire (~15). Matching the UTF-8 token against these returns the
    // shared instance without allocating the string first; anything new falls back to GetString+Intern.
    private static readonly string[] CommonTypes =
    [
        "Int64", "Int32", "Int16", "Byte", "Boolean", "Single", "Double", "String", "Null",
        "Byte[]", "Int16[]", "Int32[]", "Int64[]", "Single[]", "Double[]", "Boolean[]", "String[]"
    ];

    private static string InternType(ref Utf8JsonReader reader)
    {
        foreach (var t in CommonTypes)
            if (reader.ValueTextEquals(t))
                return t;
        return string.Intern(reader.GetString() ?? "");
    }

    // Mirrors the old Dictionary indexer's last-write-wins: a repeated key overwrites in place
    // (duplicate keys within one packet's params object are not expected, but the behavior matches).
    private static void Upsert(List<KeyValuePair<string, ParamValue>> entries, string key, ParamValue value)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Key == key)
            {
                entries[i] = new KeyValuePair<string, ParamValue>(key, value);
                return;
            }
        entries.Add(new KeyValuePair<string, ParamValue>(key, value));
    }

    // Mirrors the loader's ExtractValue exactly: Int64 first, then Double, String, Bool, Null,
    // Array (List<object?>), Object (Dictionary<string, object?>), else the raw token text.
    private static object? ExtractValue(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Number when reader.TryGetInt64(out var l) => l,
        JsonTokenType.Number when reader.TryGetDouble(out var d) => d,
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.Null => null,
        JsonTokenType.StartArray => ReadArray(ref reader),
        JsonTokenType.StartObject => ReadObject(ref reader),
        _ => TokenToString(ref reader)
    };

    private static List<object?> ReadArray(ref Utf8JsonReader reader)
    {
        var list = new List<object?>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            list.Add(ExtractValue(ref reader));
        return list;
    }

    private static Dictionary<string, object?> ReadObject(ref Utf8JsonReader reader)
    {
        var dict = new Dictionary<string, object?>();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string name = reader.GetString()!;
            reader.Read();
            dict[name] = ExtractValue(ref reader);
        }
        return dict;
    }

    // Fallback for token kinds the switch does not special-case (mirrors JsonElement.ToString()).
    private static string TokenToString(ref Utf8JsonReader reader)
    {
        if (reader.HasValueSequence)
            return Encoding.UTF8.GetString(System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence));
        return Encoding.UTF8.GetString(reader.ValueSpan);
    }
}
