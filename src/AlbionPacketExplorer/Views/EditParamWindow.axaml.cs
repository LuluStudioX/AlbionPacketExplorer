using Avalonia.Controls;
using AlbionPacketExplorer.ViewModels;

namespace AlbionPacketExplorer.Views;

public partial class EditParamWindow : Window
{
    public EditParamWindow(EditParamViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
