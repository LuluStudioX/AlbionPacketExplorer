using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AlbionPacketExplorer.Controls;
using AlbionPacketExplorer.Services;
using AlbionPacketExplorer.ViewModels;
using System.ComponentModel;
using System.Linq;

namespace AlbionPacketExplorer.Views;

public partial class MainWindow : ApxWindow, IFilePicker
{
    private Grid? _overviewMainGrid;
    private Grid? _overviewBottomGrid;
    private Grid? _focusGrid;
    private bool _summaryCollapsed;
    private bool _closing;

    public MainWindow(ToastService toastManager)
    {
        InitializeComponent();
        DataContext = new MainViewModel(this, toastManager);
        Loaded += OnLoaded;
        Opened += OnOpened;
        Closing += OnClosing;
        SizeChanged += (_, _) => ClampPanelSizes();
    }

    // Center using the real physical FrameSize, which is only known once the window has
    // opened — computing it from DIP * scale beforehand was always a few px off.
    private void OnOpened(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Normal) return;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        var area = screen.WorkingArea;
        var frame = PixelSize.FromSize(FrameSize ?? ClientSize, screen.Scaling);
        var x = area.X + (area.Width - frame.Width) / 2;
        var y = area.Y + (area.Height - frame.Height) / 2;
        Position = new PixelPoint(x, y);
    }

    // Keep the fixed-size summary row and left column from exceeding what the current
    // window can show, so the star-sized panels (packet list / detail) never get pushed
    // off-screen when the window shrinks. Mutates the existing definitions in place so the
    // GridSplitter bindings stay intact (recreating them breaks resizing).
    private void ClampPanelSizes()
    {
        if (_overviewMainGrid is { } mg && mg.Bounds.Height > 0)
        {
            var max = mg.Bounds.Height - MinPanelSize - mg.RowDefinitions[1].ActualHeight;
            var def = mg.RowDefinitions[0];
            if (max > MinPanelSize && def.Height.IsAbsolute && def.ActualHeight > max)
                def.Height = new GridLength(max, GridUnitType.Pixel);
        }
        // BottomGrid columns are star-sized, so they cannot overflow and need no clamping.
    }

    // Re-fit the window to its screen when it returns from maximized, so un-maximizing
    // on a smaller monitor never spills onto the next one.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty &&
            change.GetNewValue<WindowState>() == WindowState.Normal && IsLoaded)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(ClampToCurrentScreen);
        }
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
            vm.PacketDetail.ViewFullValueRequested += OnViewFullValueRequested;
        }

        var layout = LayoutStore.Load();
        RestoreWindowBounds(layout);
        ApplyLayout(layout);

        if (DataContext is MainViewModel vmAuto)
            vmAuto.TriggerAutoStart();
    }

    // Restore the saved window size/position, but only if those bounds still land on a
    // connected screen — otherwise the window could open off-screen (e.g. a monitor that
    // was unplugged). Falls back to centering on the primary screen.
    private void RestoreWindowBounds(LayoutState layout)
    {
        if (!layout.HasWindowBounds)
        {
            CenterOnPrimary();
            return;
        }

        var width = layout.WindowWidth!.Value;
        var height = layout.WindowHeight!.Value;
        var x = (int)layout.WindowX!.Value;
        var y = (int)layout.WindowY!.Value;

        var bounds = new PixelRect(x, y, (int)width, (int)height);
        var screen = Screens.All.FirstOrDefault(s => s.WorkingArea.Intersects(bounds));

        if (screen is null)
        {
            Width = width;
            Height = height;
            CenterOnPrimary();
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = width;
        Height = height;
        // Restore the saved size on the screen it was last on, but centered there rather
        // than at the exact old coordinates.
        CenterOn(screen);

        if (layout.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private void CenterOnPrimary()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen is not null) CenterOn(screen);
    }

    // Center the window on the given screen, clamping its size to fit with a margin.
    private void CenterOn(Avalonia.Platform.Screen screen)
    {
        const int margin = 12;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;

        var maxWpx = area.Width - margin * 2;
        var maxHpx = area.Height - margin * 2;
        var pxW = (int)Math.Ceiling((Width > 0 ? Width : 1400) * scale);
        var pxH = (int)Math.Ceiling((Height > 0 ? Height : 900) * scale);
        if (pxW > maxWpx) { Width = maxWpx / scale; pxW = (int)Math.Ceiling(Width * scale); }
        if (pxH > maxHpx) { Height = maxHpx / scale; pxH = (int)Math.Ceiling(Height * scale); }

        var x = area.X + (area.Width - pxW) / 2;
        var y = area.Y + (area.Height - pxH) / 2;
        Position = new PixelPoint(x, y);
    }

    // When the window leaves the maximized state, make sure its restored size still fits
    // the screen it sits on — otherwise un-maximizing on a smaller monitor bleeds onto the
    // neighbouring one.
    private void ClampToCurrentScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        ClampWindowToScreen(screen);
    }

    // Shared clamp working in physical pixels against the screen working area. Leaves an
    // 8px margin so OS borders / fractional-DPI rounding never reach the screen edge.
    private void ClampWindowToScreen(Avalonia.Platform.Screen screen)
    {
        const int margin = 12;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;

        var maxWpx = area.Width - margin * 2;
        var maxHpx = area.Height - margin * 2;

        var pxW = (int)Math.Ceiling(Width * scale);
        var pxH = (int)Math.Ceiling(Height * scale);

        if (pxW > maxWpx) { Width = maxWpx / scale; pxW = (int)Math.Ceiling(Width * scale); }
        if (pxH > maxHpx) { Height = maxHpx / scale; pxH = (int)Math.Ceiling(Height * scale); }

        var nx = Math.Clamp(Position.X, area.X + margin, Math.Max(area.X + margin, area.X + area.Width - margin - pxW));
        var ny = Math.Clamp(Position.Y, area.Y + margin, Math.Max(area.Y + margin, area.Y + area.Height - margin - pxH));
        if (nx != Position.X || ny != Position.Y) Position = new PixelPoint(nx, ny);
    }

    // Each resizable panel keeps at least this many px so a splitter can never be
    // collapsed to the point it becomes ungrabbable.
    private const double MinPanelSize = 60;
    private const double MinColumnWidth = 180;

    private void ApplyLayout(LayoutState layout)
    {
        if (_overviewMainGrid != null)
        {
            _overviewMainGrid.RowDefinitions[0] =
                new RowDefinition(layout.TopPanelHeight, GridUnitType.Pixel) { MinHeight = MinPanelSize };
            _overviewMainGrid.RowDefinitions[2].MinHeight = MinPanelSize;
        }
        if (_overviewBottomGrid != null)
        {
            // Star-sized so the two panels always sum to the available width — never
            // overflowing the window the way fixed-pixel columns did.
            var frac = Math.Clamp(layout.LeftPanelFraction, 0.2, 0.8);
            _overviewBottomGrid.ColumnDefinitions[0] =
                new ColumnDefinition(frac, GridUnitType.Star) { MinWidth = MinColumnWidth };
            _overviewBottomGrid.ColumnDefinitions[2] =
                new ColumnDefinition(1 - frac, GridUnitType.Star) { MinWidth = MinColumnWidth };
        }
        if (_focusGrid != null)
        {
            _focusGrid.RowDefinitions[0] =
                new RowDefinition(layout.FocusTopHeight, GridUnitType.Pixel) { MinHeight = MinPanelSize };
            _focusGrid.RowDefinitions[2] =
                new RowDefinition(layout.FocusMidHeight, GridUnitType.Pixel) { MinHeight = MinPanelSize };
            _focusGrid.RowDefinitions[4] = new RowDefinition(1, GridUnitType.Star) { MinHeight = MinPanelSize };
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
                _focusGrid.RowDefinitions[0] =
                    new RowDefinition(_summaryExpandedHeight, GridUnitType.Pixel) { MinHeight = MinPanelSize };
                _focusGrid.RowDefinitions[1] = new RowDefinition(2, GridUnitType.Pixel);
            }
        }
    }

    private void OnEditParamRequested(EditParamViewModel vm)
    {
        var win = new EditParamWindow(vm);
        win.Show(this);
    }

    private void OnViewFullValueRequested(ParamRow row, Services.GameDataService gameData)
    {
        var win = new ExpandedValueWindow(row, gameData);
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
        var rightW   = _overviewBottomGrid?.ColumnDefinitions[2].ActualWidth ?? 0;
        var focusTop = _summaryCollapsed ? _summaryExpandedHeight : (_focusGrid?.RowDefinitions[0].ActualHeight ?? 0);
        var focusMid = _focusGrid?.RowDefinitions[2].ActualHeight ?? 0;

        var leftFraction = (leftW + rightW) > 10 ? leftW / (leftW + rightW) : prev.LeftPanelFraction;

        // Only capture the normal (restored) bounds; when maximized keep the previous
        // normal bounds and just remember the maximized flag.
        var maximized = WindowState == WindowState.Maximized;
        var winX = maximized ? prev.WindowX : Position.X;
        var winY = maximized ? prev.WindowY : Position.Y;
        var winW = maximized ? prev.WindowWidth : Width;
        var winH = maximized ? prev.WindowHeight : Height;

        LayoutStore.Save(prev with
        {
            TopPanelHeight = topH     > 10 ? topH     : prev.TopPanelHeight,
            LeftPanelWidth = leftW    > 10 ? leftW    : prev.LeftPanelWidth,
            LeftPanelFraction = leftFraction,
            FocusTopHeight = focusTop > 10 ? focusTop : prev.FocusTopHeight,
            FocusMidHeight = focusMid > 10 ? focusMid : prev.FocusMidHeight,
            WindowX = winX,
            WindowY = winY,
            WindowWidth = winW,
            WindowHeight = winH,
            WindowMaximized = maximized,
        });

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
