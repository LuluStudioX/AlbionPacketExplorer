using System.Collections.Generic;
using AlbionPacketExplorer.Services;
using Xunit;

namespace AlbionPacketExplorer.Tests;

/// <summary>
/// The core of cross-era capture resolution: a capture recorded when the bank overview lived at
/// operation codes 520/521 must still resolve after a patch moved those events to 516/517. The
/// remap is name-anchored, so the file keeps its raw wire codes and they are relabelled + aliased
/// to the app's current codes on open.
/// </summary>
public class ProtocolRemapTests
{
    private static Dictionary<string, Dictionary<string, int>> Enums(
        Dictionary<string, int> events, Dictionary<string, int> ops) =>
        new() { ["EventCodes"] = events, ["OperationCodes"] = ops };

    [Fact]
    public void Renumbered_operation_is_relabelled_and_aliased_by_name()
    {
        // Capture era: bank overview at 520/521. App now: same events shifted to 516/517.
        var era = Enums(
            new() { ["Foo"] = 10 },
            new() { ["GetGuildBankOverview"] = 520, ["GetGuildBankTabs"] = 521 });
        var appNow = Enums(
            new() { ["Foo"] = 10 },
            new() { ["GetGuildBankOverview"] = 516, ["GetGuildBankTabs"] = 517 });

        var (names, aliases) = ProtocolRemap.Build(era, appNow);

        // The capture's raw code shows the name it carried in its era.
        Assert.Equal("GetGuildBankOverview", names[("OP", 520)]);
        Assert.Equal("GetGuildBankTabs", names[("OP", 521)]);

        // And is aliased to the app's current code (both request + response) so its schema resolves.
        Assert.Equal(516, aliases[("REQUEST", 520)]);
        Assert.Equal(516, aliases[("RESPONSE", 520)]);
        Assert.Equal(517, aliases[("RESPONSE", 521)]);
    }

    [Fact]
    public void Unchanged_code_is_labelled_but_not_aliased()
    {
        var era = Enums(new() { ["Move"] = 3 }, new() { ["A"] = 1 });
        var appNow = Enums(new() { ["Move"] = 3 }, new() { ["A"] = 1 });

        var (names, aliases) = ProtocolRemap.Build(era, appNow);

        Assert.Equal("Move", names[("EVENT", 3)]);
        Assert.False(aliases.ContainsKey(("EVENT", 3)));   // same code -> no redirect
        Assert.Empty(aliases);
    }

    [Fact]
    public void Event_only_in_capture_era_is_still_labelled()
    {
        // An event the app no longer defines: keep its era name, but there is nowhere to alias it.
        var era = Enums(new() { ["RemovedEvent"] = 99 }, new());
        var appNow = Enums(new(), new());

        var (names, aliases) = ProtocolRemap.Build(era, appNow);

        Assert.Equal("RemovedEvent", names[("EVENT", 99)]);
        Assert.Empty(aliases);
    }

    [Fact]
    public void Identical_protocols_share_a_fingerprint()
    {
        var a = ProtocolSnapshotStore.AppBaseline();
        var b = ProtocolSnapshotStore.AppBaseline();
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.Equal(16, a.Fingerprint.Length); // 64-bit FNV as hex
    }

    [Fact]
    public void Fingerprint_changes_when_a_code_shifts()
    {
        var before = new Dictionary<string, Dictionary<string, int>> { ["OperationCodes"] = new() { ["A"] = 1 } };
        var after = new Dictionary<string, Dictionary<string, int>> { ["OperationCodes"] = new() { ["A"] = 2 } };
        Assert.NotEqual(ProtocolSnapshotStore.Fingerprint(before), ProtocolSnapshotStore.Fingerprint(after));
    }

    // --- era detection (drives merge normalization) ---

    private static ProtocolSnapshotStore.Snapshot Snap(string fp, Dictionary<string, int> events, Dictionary<string, int> ops)
        => new(fp, null, new() { ["EventCodes"] = events, ["OperationCodes"] = ops });

    [Fact]
    public void DetectEra_is_null_when_current_enums_explain_every_code()
    {
        var current = Snap("cur", new() { ["Move"] = 3 }, new() { ["Bank"] = 516 });
        var june = Snap("june", new() { ["Move"] = 3 }, new() { ["Bank"] = 520 });
        var observed = new[] { ("RESPONSE", 516), ("EVENT", 3) };
        // The log's codes all exist in current -> current era, nothing to normalize.
        Assert.Null(ProtocolSnapshotStore.DetectEra(new[] { current, june }, current, observed));
    }

    [Fact]
    public void DetectEra_picks_the_old_era_for_a_code_current_no_longer_defines()
    {
        var current = Snap("cur", new() { ["Move"] = 3 }, new() { ["Bank"] = 516 });
        var june = Snap("june", new() { ["Move"] = 3 }, new() { ["Bank"] = 520 });
        // Log uses op 520 (June bank), a hole in the current enums -> detect June and remap.
        var observed = new[] { ("RESPONSE", 520), ("EVENT", 3) };
        var era = ProtocolSnapshotStore.DetectEra(new[] { current, june }, current, observed);
        Assert.Equal("june", era?.Fingerprint);
    }

    [Fact]
    public void DetectEra_is_null_when_no_snapshot_covers_all_codes()
    {
        var current = Snap("cur", new() { ["Move"] = 3 }, new() { ["Bank"] = 516 });
        var june = Snap("june", new() { ["Move"] = 3 }, new() { ["Bank"] = 520 });
        // 999 exists in no snapshot -> too uncertain to remap, stay safe (null = canonical).
        var observed = new[] { ("RESPONSE", 999) };
        Assert.Null(ProtocolSnapshotStore.DetectEra(new[] { current, june }, current, observed));
    }
}
