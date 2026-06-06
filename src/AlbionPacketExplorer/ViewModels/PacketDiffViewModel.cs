using System.Collections.ObjectModel;
using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Network;
using AlbionPacketExplorer.Services;

namespace AlbionPacketExplorer.ViewModels;

/// <summary>
/// Backs the standalone diff window that compares any two packets picked from the list (e.g. two
/// EVENTs of the same code, to see which fields are optional or drift between captures). Left and
/// Right are the two selected packets in selection order.
/// </summary>
public sealed class PacketDiffViewModel
{
    public string LeftLabel { get; }
    public string RightLabel { get; }
    public string Legend => Loc.T("diff.legend");
    public ObservableCollection<ParamDiffRow> DiffRows { get; }

    public PacketDiffViewModel(PacketEntry left, PacketEntry right, PacketSchemaService schema)
    {
        LeftLabel = Label(left);
        RightLabel = Label(right);
        DiffRows = new ObservableCollection<ParamDiffRow>(PacketDiff.Build(left, right, schema));
    }

    private static string Label(PacketEntry p)
    {
        var name = PacketNameResolver.Resolve(p.Kind, p.Code);
        var head = string.IsNullOrEmpty(name) ? $"{p.Kind} {p.Code}" : $"{p.Kind} {p.Code}  {name}";
        return $"{head}   {p.Timestamp:HH:mm:ss.fff}";
    }
}
