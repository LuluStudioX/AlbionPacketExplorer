using Avalonia.Controls;
using Avalonia.Interactivity;
using AlbionPacketExplorer.ViewModels;
using SukiUI.Controls;

namespace AlbionPacketExplorer.Views;

public partial class SettingsWindow : SukiWindow
{
    private StackPanel? _sectionDisplay;
    private StackPanel? _sectionAbout;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += (_, _) =>
        {
            _sectionDisplay = this.FindControl<StackPanel>("SectionDisplay");
            _sectionAbout   = this.FindControl<StackPanel>("SectionAbout");
        };
    }

    private void OnNavClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var tag = btn.Tag as string;

        if (_sectionDisplay != null) _sectionDisplay.IsVisible = tag == "Display";
        if (_sectionAbout   != null) _sectionAbout.IsVisible   = tag == "About";
    }
}
