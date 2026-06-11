using System.Text.Json;
using AlbionPacketExplorer.Network;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Runtime overrides the Protocol Scanner writes so the app stays correct between releases after a
/// game patch. Two parts, both persisted and fed into the resolver / schema:
/// <list type="bullet">
/// <item>names: a code the compiled enums don't know (or name wrong after a shift) -> its real name.</item>
/// <item>aliases: a shifted wire code -> the code whose param schema it should borrow, so a moved
/// event still shows its real key meanings.</item>
/// </list>
/// The enums and the shipped schema are never modified at runtime; this is a lightweight overlay
/// that a real release later supersedes.
/// </summary>
public sealed class ProtocolOverrideStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static string FilePath => AppPaths.ProtocolOverrides;

    // names: "EVENT:683" / "OP:540" -> client name.  aliases: "EVENT:33" -> old code 32.
    private sealed record Data(Dictionary<string, string> Names, Dictionary<string, int> Aliases)
    {
        public Data() : this([], []) { }
    }

    private Data _data = new();

    public void Load()
    {
        _data = LoadFile();
        Push();
    }

    /// <summary>Folds detected changes into the overrides, persists, and applies them live.
    /// Returns the number of entries added or changed.</summary>
    public int Apply(IEnumerable<ProtocolChange> changes)
    {
        var applied = 0;
        foreach (var c in changes)
        {
            if (c.Type == ProtocolChangeType.Removed || c.ClientCode is not int newCode) continue;
            if (string.IsNullOrEmpty(c.Name)) continue;

            var domain = c.Enum == "EventCodes" ? "EVENT" : "OP";
            var key = $"{domain}:{newCode}";
            var touched = false;

            if (!_data.Names.TryGetValue(key, out var existingName) || existingName != c.Name)
            {
                _data.Names[key] = c.Name;
                touched = true;
            }

            // A shifted event borrows the params of the code it used to occupy.
            if (c.Type == ProtocolChangeType.Shifted && c.AppCode is int oldCode &&
                (!_data.Aliases.TryGetValue(key, out var existingOld) || existingOld != oldCode))
            {
                _data.Aliases[key] = oldCode;
                touched = true;
            }

            if (touched) applied++;
        }

        if (applied == 0) return 0;
        Save();
        Push();
        return applied;
    }

    private void Push()
    {
        // Names -> resolver (domain key matches PacketNameResolver: "EVENT" / "OP").
        var names = new Dictionary<(string Domain, int Code), string>();
        foreach (var (key, name) in _data.Names)
            if (TrySplit(key, out var domain, out var code))
                names[(domain, code)] = name;
        PacketNameResolver.SetOverrides(names);

        // Aliases -> schema, expanding "OP" into both REQUEST and RESPONSE kinds.
        var aliases = new Dictionary<(string Kind, int Code), int>();
        foreach (var (key, oldCode) in _data.Aliases)
        {
            if (!TrySplit(key, out var domain, out var code)) continue;
            if (domain == "EVENT")
                aliases[("EVENT", code)] = oldCode;
            else
            {
                aliases[("REQUEST", code)] = oldCode;
                aliases[("RESPONSE", code)] = oldCode;
            }
        }
        PacketSchemaService.SetCodeAliases(aliases);
    }

    private static bool TrySplit(string key, out string domain, out int code)
    {
        domain = ""; code = 0;
        var sep = key.IndexOf(':');
        if (sep <= 0 || !int.TryParse(key.AsSpan(sep + 1), out code)) return false;
        domain = key[..sep];
        return true;
    }

    private static Data LoadFile()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data();
        }
        catch { /* corrupt or old-format file: rebuilt on the next scan */ }
        return new Data();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_data, JsonOpts));
        }
        catch { /* best effort */ }
    }
}
