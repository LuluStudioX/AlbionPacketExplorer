using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlbionPacketExplorer.Services;

public sealed class ParamSchema
{
    [JsonPropertyName("name")]    public string Name      { get; set; } = string.Empty;
    [JsonPropertyName("note")]    public string Note      { get; set; } = string.Empty;
    [JsonPropertyName("resolveAs")] public string ResolveAs { get; set; } = string.Empty;
}

public sealed class PacketTypeSchema
{
    [JsonPropertyName("name")]   public string Name   { get; set; } = string.Empty;
    [JsonPropertyName("params")] public Dictionary<string, ParamSchema> Params { get; set; } = new();
}

public sealed class PacketSchemaService
{
    // Routed through AppPaths so it lives in the relocatable data folder, never inside Velopack's
    // install directory (which would block install/repair/uninstall).
    private static string UserFilePath => AppPaths.UserSchema;

    private Dictionary<string, PacketTypeSchema> _base = new();
    private Dictionary<string, PacketTypeSchema> _user = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task LoadAsync()
    {
        _base = await LoadEmbeddedBaseAsync();
        _user = await LoadUserAsync();
    }

    private static async Task<Dictionary<string, PacketTypeSchema>> LoadEmbeddedBaseAsync()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("packet-schema.base.json"));
            if (resName == null) return new();

            await using var stream = asm.GetManifestResourceStream(resName)!;
            return await JsonSerializer.DeserializeAsync<Dictionary<string, PacketTypeSchema>>(stream, JsonOpts)
                   ?? new();
        }
        catch { return new(); }
    }

    private static async Task<Dictionary<string, PacketTypeSchema>> LoadUserAsync()
    {
        if (!File.Exists(UserFilePath)) return new();
        try
        {
            await using var stream = File.OpenRead(UserFilePath);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, PacketTypeSchema>>(stream, JsonOpts)
                   ?? new();
        }
        catch { return new(); }
    }

    public PacketTypeSchema? GetSchema(string kind, int code)
    {
        var key = $"{kind.ToUpperInvariant()}:{code}";
        _user.TryGetValue(key, out var userType);
        _base.TryGetValue(key, out var baseType);
        if (userType == null && baseType == null) return null;

        // Merge: user wins per param key
        var merged = new PacketTypeSchema
        {
            Name = userType?.Name ?? baseType?.Name ?? string.Empty
        };

        var allKeys = new HashSet<string>();
        if (baseType != null) foreach (var k in baseType.Params.Keys) allKeys.Add(k);
        if (userType != null) foreach (var k in userType.Params.Keys) allKeys.Add(k);

        foreach (var k in allKeys)
        {
            if (userType?.Params.TryGetValue(k, out var up) == true)
                merged.Params[k] = up;
            else if (baseType?.Params.TryGetValue(k, out var bp) == true)
                merged.Params[k] = bp;
        }

        return merged;
    }

    public ParamSchema? GetParam(string kind, int code, string key)
        => GetSchema(kind, code)?.Params.GetValueOrDefault(key);

    public async Task SaveUserParamAsync(string kind, int code, string key, string name, string note, string resolveAs = "")
    {
        var typeKey = $"{kind.ToUpperInvariant()}:{code}";
        if (!_user.TryGetValue(typeKey, out var userType))
        {
            userType = new PacketTypeSchema { Name = GetSchema(kind, code)?.Name ?? string.Empty };
            _user[typeKey] = userType;
        }

        userType.Params[key] = new ParamSchema { Name = name, Note = note, ResolveAs = resolveAs };
        await PersistUserFileAsync();
    }

    public async Task SaveUserEventNameAsync(string kind, int code, string name)
    {
        var typeKey = $"{kind.ToUpperInvariant()}:{code}";
        if (!_user.TryGetValue(typeKey, out var userType))
        {
            userType = new PacketTypeSchema();
            _user[typeKey] = userType;
        }
        userType.Name = name;
        await PersistUserFileAsync();
    }

    public async Task ClearUserParamAsync(string kind, int code, string key)
    {
        var typeKey = $"{kind.ToUpperInvariant()}:{code}";
        if (_user.TryGetValue(typeKey, out var userType))
        {
            userType.Params.Remove(key);
            if (userType.Params.Count == 0 && string.IsNullOrEmpty(userType.Name))
                _user.Remove(typeKey);
            await PersistUserFileAsync();
        }
    }

    public enum ParamSource { None, Base, User }

    public ParamSource GetParamSource(string kind, int code, string key)
    {
        var typeKey = $"{kind.ToUpperInvariant()}:{code}";
        if (_user.TryGetValue(typeKey, out var u) && u.Params.ContainsKey(key)) return ParamSource.User;
        if (_base.TryGetValue(typeKey, out var b) && b.Params.ContainsKey(key)) return ParamSource.Base;
        return ParamSource.None;
    }

    public IReadOnlyList<string> GetAllKnownParamNames()
        => _base.Values
            .SelectMany(t => t.Params.Values)
            .Select(p => p.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .OrderBy(n => n)
            .ToList();

    /// <summary>Every named packet code the schema knows (excludes the "Unused" enum placeholders).
    /// The capture-coverage gap = these minus the codes actually seen in a session.</summary>
    public IReadOnlyList<(string Kind, int Code, string Name)> GetKnownCodes()
    {
        var list = new List<(string, int, string)>();
        foreach (var (key, schema) in _base)
        {
            if (key.StartsWith('$')) continue;
            if (string.IsNullOrEmpty(schema.Name) || schema.Name == "Unused") continue;
            var sep = key.IndexOf(':');
            if (sep < 0 || !int.TryParse(key.AsSpan(sep + 1), out var code)) continue;
            list.Add((key[..sep], code, schema.Name));
        }
        return list;
    }

    public string ExportEventSchema(string kind, int code)
    {
        var schema = GetSchema(kind, code);
        var export = new
        {
            source = "AlbionPacketExplorer",
            kind = kind.ToUpperInvariant(),
            code,
            name = schema?.Name ?? string.Empty,
            exportedAt = DateTime.UtcNow.ToString("O"),
            @params = schema?.Params ?? new Dictionary<string, ParamSchema>()
        };
        return JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
    }

    private async Task PersistUserFileAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserFilePath)!);
            await using var stream = new FileStream(UserFilePath, FileMode.Create, FileAccess.Write);
            await JsonSerializer.SerializeAsync(stream, _user, JsonOpts);
        }
        catch { }
    }
}
