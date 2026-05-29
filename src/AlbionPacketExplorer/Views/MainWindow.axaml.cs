using Avalonia.Controls;
using Avalonia.Platform.Storage;
using AlbionPacketExplorer.Services;
using AlbionPacketExplorer.ViewModels;
using SukiUI.Controls;

namespace AlbionPacketExplorer.Views;

public partial class MainWindow : SukiWindow, IFilePicker
{
    private Grid? _mainGrid;
    private Grid? _bottomGrid;

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
        _mainGrid = this.FindControl<Grid>("MainGrid");
        _bottomGrid = this.FindControl<Grid>("BottomGrid");

        if (DataContext is MainViewModel vm)
        {
            var clipboard = Clipboard;
            vm.Aggregator.Clipboard = clipboard;
            vm.PacketList.Clipboard = clipboard;
            vm.PacketDetail.Clipboard = clipboard;
        }

        var layout = LayoutStore.Load();

        if (_mainGrid != null)
            _mainGrid.RowDefinitions[0] = new RowDefinition(layout.TopPanelHeight, GridUnitType.Pixel);

        if (_bottomGrid != null)
            _bottomGrid.ColumnDefinitions[0] = new ColumnDefinition(layout.LeftPanelWidth, GridUnitType.Pixel);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_mainGrid == null || _bottomGrid == null) return;

        LayoutStore.Save(new LayoutState(
            _mainGrid.RowDefinitions[0].ActualHeight,
            _bottomGrid.ColumnDefinitions[0].ActualWidth));
    }
}
