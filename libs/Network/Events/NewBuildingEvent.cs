using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [45] NewBuilding
// 0=objectId 1=buildingGuid(byte[16]) 3=uniqueName 4=position(float[])
// 7=nutrition 8=lastActionTicks 9=housePlotGuid(byte[16])
// 11=islandOwnerName 13=laborerFirstName 14=laborerLastName 16=hasPremium 252=45
public sealed class NewBuildingEvent
{
    public long ObjectId { get; }
    public Guid BuildingGuid { get; }
    public string UniqueName { get; }
    public (float X, float Y)? Position { get; }
    public int Nutrition { get; }
    public DateTime? LastActionAt { get; }
    public Guid HousePlotGuid { get; }
    public string IslandOwnerName { get; }
    public string LaborerFirstName { get; }
    public string LaborerLastName { get; }
    public bool HasPremium { get; }

    public bool IsLaborerBuilding => UniqueName.Contains("LABOURER", StringComparison.OrdinalIgnoreCase);

    public NewBuildingEvent(Dictionary<byte, object> p)
    {
        UniqueName = IslandOwnerName = LaborerFirstName = LaborerLastName = string.Empty;

        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;

        if (p.TryGetValue(1, out var v1) && v1 is byte[] b1 && b1.Length == 16)
            BuildingGuid = new Guid(b1);

        if (p.TryGetValue(3, out var v3)) UniqueName = v3?.ToString() ?? string.Empty;

        if (p.TryGetValue(4, out var v4))
        {
            float px = 0, py = 0; bool ok = false;
            if (v4 is float[] fa && fa.Length >= 2) { px = fa[0]; py = fa[1]; ok = true; }
            else if (v4 is int[] ia && ia.Length >= 2) { px = ia[0]; py = ia[1]; ok = true; }
            if (ok) Position = (px, py);
        }

        if (p.TryGetValue(7, out var v7)) Nutrition = v7.ObjectToInt();

        if (p.TryGetValue(8, out var v8))
        {
            var ticks = v8.ObjectToLong();
            if (ticks is > 0) LastActionAt = new DateTime(ticks.Value, DateTimeKind.Utc);
        }

        if (p.TryGetValue(9, out var v9) && v9 is byte[] b9 && b9.Length == 16)
            HousePlotGuid = new Guid(b9);

        if (p.TryGetValue(11, out var v11)) IslandOwnerName = v11?.ToString() ?? string.Empty;
        if (p.TryGetValue(13, out var v13)) LaborerFirstName = v13?.ToString() ?? string.Empty;
        if (p.TryGetValue(14, out var v14)) LaborerLastName = v14?.ToString() ?? string.Empty;
        if (p.TryGetValue(16, out var v16)) HasPremium = v16.ObjectToBool();
    }
}
