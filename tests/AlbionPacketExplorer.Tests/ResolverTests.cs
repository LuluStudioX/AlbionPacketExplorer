using System.Reflection;
using System.Text.Json;
using AlbionPacketExplorer.Services;
using Xunit;

namespace AlbionPacketExplorer.Tests;

/// <summary>
/// Proves the param-value resolvers work against the real shipped assets (embedded resolve-enums.json,
/// loc-strings.json, packet-schema.base.json) - the same data the running app loads. This is the
/// "does the feature actually resolve" check without driving the UI.
/// </summary>
public class ResolveEnumStoreTests
{
    private static ResolveEnumStore Loaded()
    {
        var s = new ResolveEnumStore();
        s.Load();
        return s;
    }

    [Fact]
    public void Loads_many_enums()
    {
        var s = Loaded();
        Assert.True(s.EnumNames.Count >= 150, $"expected >=150 enums, got {s.EnumNames.Count}");
    }

    [Theory]
    [InlineData("enum:AttackType", 1, "Melee")]
    [InlineData("enum:AttackType", 2, "Ranged")]
    [InlineData("enum:ActionComponentType", 1, "CraftItem")]
    [InlineData("enum:ActionComponentType", 2, "RepairItem")]
    [InlineData("enum:ActionOnBuildingErrorCode", 15, "ItemOvercharged")]
    [InlineData("enum:GuildRole", 0, "NONE")]
    [InlineData("enum:ClusterQualities", 6, "Q6")]
    [InlineData("enum:RcGeneric", -500, "InternalServerError")]   // negative value
    public void Resolves_known_members(string resolveAs, long value, string expected)
    {
        var s = Loaded();
        Assert.True(s.TryResolve(resolveAs, value, out var member), $"{resolveAs} {value} did not resolve");
        Assert.Equal(expected, member);
    }

    [Fact]
    public void Unknown_value_does_not_resolve()
    {
        var s = Loaded();
        Assert.False(s.TryResolve("enum:AttackType", 9999, out _));
    }

    [Fact]
    public void Non_enum_resolveAs_is_ignored()
    {
        var s = Loaded();
        Assert.False(s.IsEnumResolve("itemIndex"));
        Assert.False(s.IsEnumResolve(""));
        Assert.True(s.IsEnumResolve("enum:AttackType"));
    }

    [Fact]
    public void Options_list_is_prefixed_and_nonempty()
    {
        var s = Loaded();
        var opts = s.ResolveAsOptions().ToList();
        Assert.NotEmpty(opts);
        Assert.All(opts, o => Assert.StartsWith("enum:", o));
        Assert.Contains("enum:AttackType", opts);
    }
}

public class LocStringStoreTests
{
    private static LocStringStore Loaded()
    {
        var s = new LocStringStore();
        s.Load();
        return s;
    }

    [Fact]
    public void Loads_many_keys()
    {
        Assert.True(Loaded().IsLoaded);
    }

    [Fact]
    public void Resolves_a_known_loc_key()
    {
        var s = Loaded();
        // @PARTYFINDER_JOINREQUEST_DECLINED is in the shipped loc-strings.json
        Assert.True(s.TryResolve("@PARTYFINDER_JOINREQUEST_DECLINED", out var text));
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Theory]
    [InlineData("owner")]          // no leading @ -> not a loc key
    [InlineData("T8_BAG")]
    [InlineData("")]
    [InlineData("@NOT_A_REAL_KEY_XYZ")]
    public void Non_loc_or_unknown_does_not_resolve(string value)
    {
        Assert.False(Loaded().TryResolve(value, out _));
    }
}

public class DomainStringStoreTests
{
    private static DomainStringStore Loaded()
    {
        var s = new DomainStringStore();
        s.Load();
        return s;
    }

    [Fact]
    public void Has_accessrights_set()
    {
        var s = Loaded();
        Assert.Contains("str:accessrights", s.ResolveAsOptions());
        Assert.True(s.IsStringResolve("str:accessrights"));
        Assert.False(s.IsStringResolve("enum:AttackType"));
        Assert.False(s.IsStringResolve(""));
    }

    [Theory]
    [InlineData("owner", "Owner")]
    [InlineData("noaccess", "No access")]
    [InlineData("builder", "Builder")]
    public void Resolves_access_roles(string value, string expected)
    {
        Assert.True(Loaded().TryResolve("str:accessrights", value, out var m));
        Assert.Equal(expected, m);
    }

    [Theory]
    [InlineData("str:accessrights", "not_a_role")]
    [InlineData("str:accessrights", "")]
    [InlineData("str:unknownset", "owner")]
    public void Unknown_does_not_resolve(string resolveAs, string value)
        => Assert.False(Loaded().TryResolve(resolveAs, value, out _));
}

/// <summary>Proves the curated box->enum mappings actually shipped in the embedded base schema.</summary>
public class SchemaResolveAsTests
{
    private static JsonDocument BaseSchema()
    {
        var asm = typeof(PacketSchemaService).Assembly;
        var res = asm.GetManifestResourceNames().First(n => n.EndsWith("packet-schema.base.json"));
        using var stream = asm.GetManifestResourceStream(res)!;
        return JsonDocument.Parse(stream);
    }

    private static string ResolveAsOf(string typeKey, string paramKey)
    {
        using var doc = BaseSchema();
        return doc.RootElement.GetProperty(typeKey).GetProperty("params")
            .GetProperty(paramKey).GetProperty("resolveAs").GetString() ?? "";
    }

    [Theory]
    [InlineData("REQUEST:55", "2", "enum:ActionComponentType")]   // the original anchor
    [InlineData("EVENT:13", "3", "enum:AttackType")]               // curated
    [InlineData("EVENT:104", "7", "enum:GuildRole")]               // curated
    [InlineData("EVENT:210", "1", "enum:AccessRightsContainers")]  // curated (accessrights)
    [InlineData("EVENT:140", "29", "enum:ClusterQualities")]       // curated
    [InlineData("EVENT:210", "5", "str:accessrights")]             // string value-set
    public void Schema_has_curated_resolveAs(string typeKey, string paramKey, string expected)
        => Assert.Equal(expected, ResolveAsOf(typeKey, paramKey));

    [Fact]
    public void At_least_50_enum_resolveAs_in_schema()
    {
        using var doc = BaseSchema();
        int count = 0;
        foreach (var t in doc.RootElement.EnumerateObject())
        {
            if (!t.Value.TryGetProperty("params", out var ps)) continue;
            foreach (var p in ps.EnumerateObject())
                if (p.Value.TryGetProperty("resolveAs", out var r) &&
                    (r.GetString() ?? "").StartsWith("enum:"))
                    count++;
        }
        Assert.True(count >= 50, $"expected >=50 enum: resolveAs in schema, got {count}");
    }
}
