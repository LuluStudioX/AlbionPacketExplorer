using System.Collections.Generic;
using System.Linq;
using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Services;
using Xunit;

namespace AlbionPacketExplorer.Tests;

/// <summary>
/// Proves the runtime entity resolver: it learns entityId -> name from the naming events it observes
/// (NewCharacter = EVENT 29, param 0 = id, param 1 = name - verified against real captures), resolves
/// a raw id to that name, keeps domains separate, and clears on reset.
/// </summary>
public class EntityNameStoreTests
{
    // Builds a packet whose params are the given (key, type, value) triples, appended to the store
    // exactly as the loader would, so Observe decodes them through the real ParamSet path.
    private static PacketEntry Packet(PackedParamStore store, string kind, int code,
        params (string Key, string Type, object? Value)[] ps)
    {
        var entries = ps
            .Select(p => new KeyValuePair<string, ParamValue>(p.Key, new ParamValue(p.Type, p.Value)))
            .ToArray();
        var r = store.Append(new ParamSet(entries));
        return new PacketEntry(default, kind, code, store, r);
    }

    private static PacketEntry NewCharacter(PackedParamStore store, long id, string name)
        => Packet(store, "EVENT", 29, ("0", "Int64", id), ("1", "String", name));

    [Fact]
    public void Learns_and_resolves_a_character_id_to_its_name()
    {
        using var store = new PackedParamStore();
        var s = new EntityNameStore();
        s.Observe(NewCharacter(store, 1379287, "ParadoXxX"));

        // Resolves under the specific domain and under the any-domain "entity" tag, keyed by the id
        // stringified the same way the referencing param renders it.
        Assert.True(s.TryResolve("player", "1379287", out var byPlayer));
        Assert.Equal("ParadoXxX", byPlayer);
        Assert.True(s.TryResolve("entity", "1379287", out var byEntity));
        Assert.Equal("ParadoXxX", byEntity);
        Assert.Equal(1, s.Count);
    }

    [Fact]
    public void Unknown_id_and_non_naming_event_do_not_resolve()
    {
        using var store = new PackedParamStore();
        var s = new EntityNameStore();
        s.Observe(NewCharacter(store, 1379287, "ParadoXxX"));
        // A Move event (code 3) is not a naming event: nothing is learned from it.
        s.Observe(Packet(store, "EVENT", 3, ("0", "Int64", 999L)));

        Assert.False(s.TryResolve("player", "999", out _));
        Assert.False(s.TryResolve("entity", "424242", out _));
        Assert.Equal(1, s.Count);
    }

    [Fact]
    public void Same_kind_but_wrong_packet_kind_is_ignored()
    {
        using var store = new PackedParamStore();
        var s = new EntityNameStore();
        // A REQUEST that happens to reuse code 29 must not be harvested (naming events are EVENTs).
        s.Observe(Packet(store, "REQUEST", 29, ("0", "Int64", 5L), ("1", "String", "NotACharacter")));
        Assert.Equal(0, s.Count);
        Assert.False(s.TryResolve("player", "5", out _));
    }

    [Fact]
    public void Last_write_wins_for_a_recycled_id()
    {
        using var store = new PackedParamStore();
        var s = new EntityNameStore();
        s.Observe(NewCharacter(store, 42, "FirstOwner"));
        s.Observe(NewCharacter(store, 42, "SecondOwner"));

        Assert.True(s.TryResolve("player", "42", out var name));
        Assert.Equal("SecondOwner", name);
        Assert.Equal(1, s.Count);
    }

    [Fact]
    public void Reset_drops_everything_learned()
    {
        using var store = new PackedParamStore();
        var s = new EntityNameStore();
        s.Observe(NewCharacter(store, 1, "Alice"));
        Assert.Equal(1, s.Count);

        s.Reset();

        Assert.Equal(0, s.Count);
        Assert.False(s.TryResolve("player", "1", out _));
    }

    [Fact]
    public void Resolve_options_and_domain_membership_are_stable_before_any_observation()
    {
        var s = new EntityNameStore();
        var opts = s.ResolveAsOptions().ToList();
        Assert.Contains("entity", opts);
        Assert.Contains("player", opts);

        Assert.True(s.IsEntityResolve("entity"));
        Assert.True(s.IsEntityResolve("player"));
        Assert.False(s.IsEntityResolve("location"));
        Assert.False(s.IsEntityResolve("nope"));
    }

    [Fact]
    public void Empty_name_is_not_learned()
    {
        using var store = new PackedParamStore();
        var s = new EntityNameStore();
        s.Observe(NewCharacter(store, 7, ""));
        Assert.Equal(0, s.Count);
        Assert.False(s.TryResolve("player", "7", out _));
    }
}
