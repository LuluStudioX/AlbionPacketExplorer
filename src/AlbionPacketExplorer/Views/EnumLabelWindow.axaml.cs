using AlbionPacketExplorer.Controls;
using AlbionPacketExplorer.ViewModels;

namespace AlbionPacketExplorer.Views;

public partial class EnumLabelWindow : ApxWindow
{
    public EnumLabelWindow(EnumLabelViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.CloseRequested += Close;
    }
}
