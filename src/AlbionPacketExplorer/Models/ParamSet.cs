using System.Collections;

namespace AlbionPacketExplorer.Models;

/// <summary>
/// A packed, read-only view over a packet's params, backed by a single
/// <see cref="KeyValuePair{TKey,TValue}"/> array instead of a <see cref="Dictionary{TKey,TValue}"/>.
/// A dictionary costs ~240 bytes of buckets/entries overhead per packet; params per packet are tiny
/// (~4-10), so a flat array plus linear scan is both smaller and faster than hashing for that N.
/// Keys are already interned by the construction sites. The surface mirrors
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> so every existing reader compiles unchanged.
/// </summary>
public readonly struct ParamSet : IReadOnlyDictionary<string, ParamValue>
{
    private readonly KeyValuePair<string, ParamValue>[]? _entries;

    public ParamSet(KeyValuePair<string, ParamValue>[] entries) => _entries = entries;

    public static ParamSet Empty => new(System.Array.Empty<KeyValuePair<string, ParamValue>>());

    private KeyValuePair<string, ParamValue>[] Entries =>
        _entries ?? System.Array.Empty<KeyValuePair<string, ParamValue>>();

    public int Count => Entries.Length;

    public ParamValue this[string key] =>
        TryGetValue(key, out var v) ? v : throw new KeyNotFoundException(key);

    public bool ContainsKey(string key)
    {
        foreach (var e in Entries)
            if (e.Key == key) return true;
        return false;
    }

    public bool TryGetValue(string key, out ParamValue value)
    {
        foreach (var e in Entries)
            if (e.Key == key)
            {
                value = e.Value;
                return true;
            }
        value = default;
        return false;
    }

    public IEnumerable<string> Keys
    {
        get
        {
            foreach (var e in Entries) yield return e.Key;
        }
    }

    public IEnumerable<ParamValue> Values
    {
        get
        {
            foreach (var e in Entries) yield return e.Value;
        }
    }

    public IEnumerator<KeyValuePair<string, ParamValue>> GetEnumerator()
    {
        foreach (var e in Entries) yield return e;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
