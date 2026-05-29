using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AlbionPacketExplorer.Services;
using AlbionPacketExplorer.ViewModels;
using SukiUI.Controls;

namespace AlbionPacketExplorer.Views;

public partial class MainWindow : SukiWindow, IFilePicker
{
    private Grid? _overviewMainGrid;
    private Grid? _overviewBottomGrid;
    private bool _summaryCollapsed;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(this);
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    public async Task<string?> PickJsonFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open packet_sniffer.json",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JSON files") { Patterns = ["*.json"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveJsonFileAsync(string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save packets as JSON",
            SuggestedFileName = suggestedName,
            DefaultExtension = "json",
            FileTypeChoices =
            [
                new FilePickerFileType("JSON files") { Patterns = ["*.json"] }
            ]
        });
        return file?.TryGetLocalPath();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _overviewMainGrid = this.FindControl<Grid>("OverviewGrid");
        _overviewBottomGrid = this.FindControl<Grid>("BottomGrid");

        if (DataContext is MainViewModel vm)
        {
            var clipboard = Clipboard;
            vm.Aggregator.Clipboard = clipboard;
            vm.PacketList.Clipboard = clipboard;
            vm.PacketDetail.Clipboard = clipboard;
        }

        var layout = LayoutStore.Load();
        if (_overviewMainGrid != null)
            _overviewMainGrid.RowDefinitions[0] = new RowDefinition(layout.TopPanelHeight, GridUnitType.Pixel);
        if (_overviewBottomGrid != null)
            _overviewBottomGrid.ColumnDefinitions[0] = new ColumnDefinition(layout.LeftPanelWidth, GridUnitType.Pixel);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 && DataContext is MainViewModel vm)
        {
            vm.ToggleFocusModeCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSummaryHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        _summaryCollapsed = !_summaryCollapsed;
        var content = this.FindControl<Control>("FocusSummaryContent");
        var icon = this.FindControl<TextBlock>("SummaryCollapseIcon");
        if (content != null) content.IsVisible = !_summaryCollapsed;
        if (icon != null) icon.Text = _summaryCollapsed ? "▶" : "▼";
    }

    private SettingsWindow? _settingsWindow;

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        var vm = (MainViewModel)DataContext!;
        _settingsWindow = new SettingsWindow(new SettingsViewModel(vm));
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(this);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_overviewMainGrid != null && _overviewBottomGrid != null)
            LayoutStore.Save(new LayoutState(
                _overviewMainGrid.RowDefinitions[0].ActualHeight,
                _overviewBottomGrid.ColumnDefinitions[0].ActualWidth));

        if (DataContext is MainViewModel { MinimizeToTray: true })
        {
            e.Cancel = true;
            Hide();
        }
    }
}
