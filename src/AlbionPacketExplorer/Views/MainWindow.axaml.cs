using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AlbionPacketExplorer.Services;
using AlbionPacketExplorer.ViewModels;
using SukiUI.Controls;
using SukiUI.Toasts;
using System.ComponentModel;
using System.Linq;
using Avalonia.VisualTree;

namespace AlbionPacketExplorer.Views;

public partial class MainWindow : SukiWindow, IFilePicker
{
    private Grid? _overviewMainGrid;
    private Grid? _overviewBottomGrid;
    private Grid? _focusGrid;
    private bool _summaryCollapsed;
    private bool _closing;

    public MainWindow(ISukiToastManager toastManager)
    {
        InitializeComponent();
        DataContext = new MainViewModel(this, toastManager);
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
        _focusGrid = this.FindControl<Grid>("FocusGrid");


        if (DataContext is MainViewModel vm)
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            vm.Aggregator.Clipboard = clipboard;
            vm.Aggregator.Toasts = vm.ToastManager;
            vm.PacketList.Clipboard = clipboard;
            vm.PacketList.Toasts = vm.ToastManager;
            vm.PacketDetail.Clipboard = clipboard;
            vm.PacketDetail.Toasts = vm.ToastManager;
            vm.PacketDetail.EditParamRequested += OnEditParamRequested;
            vm.PropertyChanged += OnMainViewModelPropertyChanged;
            BackgroundStyle = vm.BackgroundStyle;
        }

        ApplyLayout(LayoutStore.Load());

        if (DataContext is MainViewModel vmAuto)
            vmAuto.TriggerAutoStart();
    }

    private void ApplyLayout(LayoutState layout)
    {
        if (_overviewMainGrid != null)
            _overviewMainGrid.RowDefinitions[0] = new RowDefinition(layout.TopPanelHeight, GridUnitType.Pixel);
        if (_overviewBottomGrid != null)
            _overviewBottomGrid.ColumnDefinitions[0] = new ColumnDefinition(layout.LeftPanelWidth, GridUnitType.Pixel);
        if (_focusGrid != null)
        {
            _focusGrid.RowDefinitions[0] = new RowDefinition(layout.FocusTopHeight, GridUnitType.Pixel);
            _focusGrid.RowDefinitions[2] = new RowDefinition(layout.FocusMidHeight, GridUnitType.Pixel);
            _focusGrid.RowDefinitions[4] = new RowDefinition(1, GridUnitType.Star);
        }
    }

    public void ResetLayout()
    {
        ApplyLayout(LayoutState.Default);
        LayoutStore.Save(LayoutState.Default);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 && DataContext is MainViewModel vm)
        {
            vm.ToggleFocusModeCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnResetLayoutClicked(object? sender, RoutedEventArgs e) => ResetLayout();

    private double _summaryExpandedHeight = 160;

    private void OnSummaryHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        _summaryCollapsed = !_summaryCollapsed;
        var content = this.FindControl<Control>("FocusSummaryContent");
        var icon = this.FindControl<TextBlock>("SummaryCollapseIcon");
        if (icon != null) icon.Text = _summaryCollapsed ? "▶" : "▼";

        if (_focusGrid != null)
        {
            if (_summaryCollapsed)
            {
                var current = _focusGrid.RowDefinitions[0].ActualHeight;
                if (current > 40) _summaryExpandedHeight = current;
                if (content != null) content.IsVisible = false;
                _focusGrid.RowDefinitions[0] = new RowDefinition(GridLength.Auto);
                _focusGrid.RowDefinitions[1] = new RowDefinition(0, GridUnitType.Pixel);
            }
            else
            {
                if (content != null) content.IsVisible = true;
                _focusGrid.RowDefinitions[0] = new RowDefinition(_summaryExpandedHeight, GridUnitType.Pixel);
                _focusGrid.RowDefinitions[1] = new RowDefinition(4, GridUnitType.Pixel);
            }
        }
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.BackgroundStyle) && sender is MainViewModel vm)
        {
            BackgroundStyle = vm.BackgroundStyle;
            var host = this.GetVisualDescendants().OfType<SukiUI.Controls.SukiMainHost>().FirstOrDefault();
            if (host != null) host.BackgroundStyle = vm.BackgroundStyle;
            var bg = this.GetVisualDescendants().OfType<SukiUI.Controls.SukiBackground>().FirstOrDefault();
            if (bg != null) bg.Style = vm.BackgroundStyle;
        }
    }

    private void OnEditParamRequested(EditParamViewModel vm)
    {
        var win = new EditParamWindow(vm);
        win.Show(this);
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
        if (_closing) return;
        var prev = LayoutStore.Load();

        var topH     = _overviewMainGrid?.RowDefinitions[0].ActualHeight ?? 0;
        var leftW    = _overviewBottomGrid?.ColumnDefinitions[0].ActualWidth ?? 0;
        var focusTop = _summaryCollapsed ? _summaryExpandedHeight : (_focusGrid?.RowDefinitions[0].ActualHeight ?? 0);
        var focusMid = _focusGrid?.RowDefinitions[2].ActualHeight ?? 0;

        LayoutStore.Save(new LayoutState(
            topH     > 10 ? topH     : prev.TopPanelHeight,
            leftW    > 10 ? leftW    : prev.LeftPanelWidth,
            focusTop > 10 ? focusTop : prev.FocusTopHeight,
            focusMid > 10 ? focusMid : prev.FocusMidHeight));

        var vm2 = DataContext as MainViewModel;
        if (vm2?.MinimizeToTray == true)
        {
            e.Cancel = true;
            Hide();
        }
        else if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            _closing = true;
            e.Cancel = true;
            _ = ShutdownAsync(desktop, vm2);
        }
    }

    private async Task ShutdownAsync(
        Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop,
        MainViewModel? vm)
    {
        if (vm != null)
            await vm.AutoSaveLogsAsync();
        desktop.Shutdown();
    }
}
