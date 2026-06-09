using System.Text;
using System.Text.Json;

namespace AlbionPacketExplorer.Models;

/// <summary>
/// Single source of truth for the params-object JSON shape
/// (<c>{ "0": { "type": .., "value": .. }, .. }</c>). Both the file loader and the lazy
/// <see cref="PackedParamStore"/> decoder read through <see cref="ReadParams"/>, and the live /
/// array paths re-serialize through <see cref="Write"/>. Keeping read+write here guarantees a
/// round-trip: <c>ReadParams(Write(set))</c> is value-equal to <c>set</c>, and the stored bytes
/// decode back to the same <see cref="ParamSet"/> the loader produced, so save/CSV/wire output
/// stays byte-identical across a load -&gt; save cycle.
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
            // Reuse a shared "0".."255" instance instead of allocating a fresh key string per packet.
            string name = ParamKeys.Intern(reader.GetString()!);
            reader.Read(); // advance to the param value (expected an object)

            string type = "";
            object? value = null;

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    string inner = reader.GetString()!;
                    reader.Read();
                    switch (inner)
                    {
                        case "type":
                            // Intern: a handful of distinct type names dedupe to single instances.
                            type = reader.TokenType == JsonTokenType.String ? string.Intern(reader.GetString() ?? "") : "";
                            break;
                        case "value":
                            value = ExtractValue(ref reader);
                            break;
                        default:
                            reader.Skip();
                            break;
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

    /// <summary>
    /// Serializes a <see cref="ParamSet"/> back to the canonical params-object UTF-8 bytes
    /// (<c>{ "0": { "type": .., "value": .. }, .. }</c>). Used by the live capture and JSON-array
    /// load paths, which have a parsed set in hand rather than the original line bytes.
    /// </summary>
    public static byte[] Write(ParamSet set)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (key, pv) in set)
            {
                writer.WritePropertyName(key);
                writer.WriteStartObject();
                writer.WriteString("type", pv.Type);
                writer.WritePropertyName("value");
                WriteValue(writer, pv.Value);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
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

    // Writes a value back in the same shape ExtractValue would re-read into an equal object graph.
    // Integers go out as numbers (Int64), doubles as numbers, the rest by JSON kind. Nested
    // List<object?> / Dictionary<string,object?> recurse. Anything unexpected stringifies, matching
    // the reader's TokenToString fallback round-trip.
    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case System.Collections.IDictionary dict:
                writer.WriteStartObject();
                foreach (System.Collections.DictionaryEntry kv in dict)
                {
                    writer.WritePropertyName(Convert.ToString(kv.Key, System.Globalization.CultureInfo.InvariantCulture) ?? "");
                    WriteValue(writer, kv.Value);
                }
                writer.WriteEndObject();
                break;
            case System.Collections.IEnumerable seq:
                writer.WriteStartArray();
                foreach (var item in seq)
                    WriteValue(writer, item);
                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
        }
    }
}
