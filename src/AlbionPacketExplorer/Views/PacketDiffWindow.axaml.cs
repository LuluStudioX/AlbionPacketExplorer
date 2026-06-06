using AlbionPacketExplorer.Controls;
using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Services;
using AlbionPacketExplorer.ViewModels;

namespace AlbionPacketExplorer.Views;

public partial class PacketDiffWindow : ApxWindow
{
    public PacketDiffWindow(PacketEntry left, PacketEntry right, PacketSchemaService schema)
    {
        InitializeComponent();
        DataContext = new PacketDiffViewModel(left, right, schema);
    }
}
