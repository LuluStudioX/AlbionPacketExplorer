using Avalonia.Controls;
using Avalonia.Controls.Selection;
using AlbionPacketExplorer.ViewModels;
using SukiUI.Controls;

namespace AlbionPacketExplorer.Views;

public partial class EditParamWindow : SukiWindow
{
    public EditParamWindow(EditParamViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.CloseRequested += Close;
    }

    private void OnSuggestionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItem is string name && DataContext is EditParamViewModel vm)
        {
            vm.AcceptSuggestionCommand.Execute(name);
            lb.SelectedItem = null;
        }
    }
}
