namespace AlbionPacketExplorer.Services;

/// <summary>
/// Builds the name + schema-alias overlay that lets a capture recorded under one protocol era be
/// read correctly under the app's current enums. The stable identity of an event is its enum member
/// NAME, not its wire number: a game patch renumbers codes but keeps names, so
/// <c>oldWireCode -&gt; NAME (capture era) -&gt; currentCode (app now)</c> is a deterministic remap.
///
/// <para>The output mirrors what <see cref="ProtocolOverrideStore"/> pushes for the live view, so it
/// feeds the same sinks: names -&gt; <see cref="AlbionPacketExplorer.Network.PacketNameResolver"/>,
/// aliases -&gt; <see cref="PacketSchemaService"/>. Only the direction differs: overrides map a newer
/// client onto the app's older enums; this maps an older capture onto the app's newer enums.</para>
/// </summary>
public static class ProtocolRemap
{
    // enum name (as stored in a snapshot) -> resolver/schema domain. "OP" is expanded into the
    // REQUEST + RESPONSE kinds for the schema, matching ProtocolOverrideStore.
    private static readonly (string Enum, string Domain)[] Enums =
    [
        ("EventCodes", "EVENT"),
        ("OperationCodes", "OP"),
    ];

    /// <summary>
    /// Given the capture's era enum maps and the app's current enum maps (both name -&gt; wire code),
    /// returns:
    /// <list type="bullet">
    /// <item>names: (domain, wireCode) -&gt; member name, so the capture's raw code shows the name it
    /// carried in its era, even if the app no longer defines that number.</item>
    /// <item>aliases: (kind, wireCode) -&gt; current code, so the capture's raw code borrows the param
    /// schema of wherever the same-named event lives now.</item>
    /// </list>
    /// When era and app agree on a code no alias is emitted; identical maps yield no aliases at all.
    /// </summary>
    public static (Dictionary<(string Domain, int Code), string> Names,
                   Dictionary<(string Kind, int Code), int> Aliases)
        Build(
            Dictionary<string, Dictionary<string, int>> era,
            Dictionary<string, Dictionary<string, int>> appNow)
    {
        var names = new Dictionary<(string, int), string>();
        var aliases = new Dictionary<(string, int), int>();

        foreach (var (enumName, domain) in Enums)
        {
            if (!era.TryGetValue(enumName, out var eraMap)) continue;
            appNow.TryGetValue(enumName, out var appMap);

            foreach (var (name, wireCode) in eraMap)
            {
                // Label the wire code with the name it carried in the capture's era.
                names[(domain, wireCode)] = name;

                // If the same-named event now lives at a different code, redirect the wire code to the
                // current code so its schema (keyed by the current code) resolves.
                if (appMap != null && appMap.TryGetValue(name, out var appCode) && appCode != wireCode)
                {
                    if (domain == "EVENT")
                        aliases[("EVENT", wireCode)] = appCode;
                    else
                    {
                        aliases[("REQUEST", wireCode)] = appCode;
                        aliases[("RESPONSE", wireCode)] = appCode;
                    }
                }
            }
        }

        return (names, aliases);
    }
}
