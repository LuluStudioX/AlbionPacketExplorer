using AlbionPacketExplorer.Controls;
using AlbionPacketExplorer.ViewModels;

namespace AlbionPacketExplorer.Views;

public partial class ShareDigestWindow : ApxWindow
{
    public ShareDigestWindow(ShareDigestViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.CloseRequested += Close;
    }
}
