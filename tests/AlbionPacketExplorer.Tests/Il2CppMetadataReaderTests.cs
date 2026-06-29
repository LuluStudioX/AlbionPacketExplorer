using AlbionPacketExplorer.Network;
using Xunit;

namespace AlbionPacketExplorer.Tests;

/// <summary>
/// Pins the IL2CPP enum reader against a real Albion <c>global-metadata.dat</c>. Guards the fix that
/// reads each member's ACTUAL literal from the field default-value blob instead of assuming a dense
/// <c>base + ordinal</c> sequence: a regression that reintroduced extrapolation would shift codes and
/// fail these anchors. The metadata file is a large local-only asset, so the test returns early
/// (passes) when it's absent rather than hard-failing CI.
/// </summary>
public class Il2CppMetadataReaderTests
{
    // Local game-copy metadata; not committed. Override via APX_METADATA_PATH if it lives elsewhere.
    private static string? MetadataPath()
    {
        var env = Environment.GetEnvironmentVariable("APX_METADATA_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        const string local = @"C:\github\ao-sage\assets-raw\il2cpp_gamecopy\global-metadata.dat";
        return File.Exists(local) ? local : null;
    }

    private static IReadOnlyDictionary<string, Il2CppMetadataReader.EnumDump>? Dumps() =>
        Il2CppMetadataReader.ReadEnums(MetadataPath()!,
        [
            ("Albion.Common.Photon", "EventCodes"),
            ("Albion.Common.Photon", "OperationCodes"),
        ]);

    [Fact]
    public void Reads_known_protocol_codes_from_real_metadata()
    {
        var path = MetadataPath();
        if (path is null) return; // local-only asset absent: skip rather than fail CI.

        var dumps = Dumps();
        Assert.NotNull(dumps);
        Assert.Contains("EventCodes", dumps!.Keys);
        Assert.Contains("OperationCodes", dumps.Keys);

        var ev = dumps["EventCodes"].Members.ToDictionary(m => m.Name, m => m.Value);
        var op = dumps["OperationCodes"].Members.ToDictionary(m => m.Name, m => m.Value);

        // ProtocolScanService anchors - stable codes that have never moved.
        Assert.Equal(1, ev["Leave"]);
        Assert.Equal(3, ev["Move"]);
        Assert.Equal(32, ev["NewSimpleItem"]);
        Assert.Equal(45, ev["NewBuilding"]);
        Assert.Equal(22, op["Move"]);
    }

    [Fact]
    public void Parsed_values_match_the_apps_compiled_enums()
    {
        var path = MetadataPath();
        if (path is null) return; // local-only asset absent: skip rather than fail CI.

        var dumps = Dumps()!;
        AssertMatchesCompiled<EventCodes>(dumps["EventCodes"]);
        AssertMatchesCompiled<OperationCodes>(dumps["OperationCodes"]);
    }

    private static void AssertMatchesCompiled<TEnum>(Il2CppMetadataReader.EnumDump dump)
        where TEnum : struct, Enum
    {
        var compiled = Enum.GetValues<TEnum>().ToDictionary(e => e.ToString(), e => Convert.ToInt32(e));
        foreach (var m in dump.Members)
            if (compiled.TryGetValue(m.Name, out var expected))
                Assert.Equal(expected, m.Value);
    }
}
