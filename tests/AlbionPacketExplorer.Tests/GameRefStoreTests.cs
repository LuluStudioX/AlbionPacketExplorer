using System.Linq;
using AlbionPacketExplorer.Services;
using Xunit;

namespace AlbionPacketExplorer.Tests;

/// <summary>
/// Proves the reference resolvers work against the real embedded maps (clusters/mob/spell/building),
/// the same data the running app loads - the "does locationId actually resolve to a map name" check.
/// </summary>
public class GameRefStoreTests
{
    private static GameRefStore Loaded()
    {
        var s = new GameRefStore();
        s.Load();
        return s;
    }

    [Fact]
    public void Registers_all_four_domains()
    {
        var opts = Loaded().ResolveAsOptions().ToList();
        Assert.Contains("location", opts);
        Assert.Contains("mob", opts);
        Assert.Contains("spell", opts);
        Assert.Contains("building", opts);
    }

    [Theory]
    [InlineData("1359", "Rivercopse Fount")]
    [InlineData("5002", "Brecilien Bank")]
    [InlineData("0006", "Bank of Thetford")]
    [InlineData("1001", "Bank of Lymhurst")]
    public void Resolves_cluster_index_to_map_name(string code, string expected)
    {
        Assert.True(Loaded().TryResolve("location", code, out var name), $"location {code} did not resolve");
        Assert.Equal(expected, name);
    }

    [Fact]
    public void Resolves_a_building_uniquename()
    {
        Assert.True(Loaded().TryResolve("building", "T1_BANK", out var name));
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void Unknown_code_or_domain_does_not_resolve()
    {
        var s = Loaded();
        Assert.False(s.TryResolve("location", "999999", out _));
        Assert.False(s.IsRefResolve("nope"));
        Assert.False(s.TryResolve("nope", "1359", out _));
        Assert.True(s.IsRefResolve("location"));
    }
}
