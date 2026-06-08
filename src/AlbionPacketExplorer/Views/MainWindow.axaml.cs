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
    private Grid? _workspaceGrid;   // [sidebar | splitter | content]
    private Grid? _contentGrid;     // [table | splitter | detail]
    private Control? _sidebarPanel;
    private Grid? _sidebarGrid;     // [status | by-code]
    private DetachableHost? _statusHost, _byCodeHost, _listHost, _detailHost;
    private GridSplitter? _contentSplitter;
    private double _savedContentTopHeight = 300;
    private bool _closing;

    public MainWindow(ToastService toastManager)
    {
        InitializeComponent();
        DataContext = new MainViewModel(this, toastManager);
        Loaded += OnLoaded;
        Opened += OnOpened;
        Closing += OnClosing;
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
            Title = "Open packet capture (JSON)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("JSON files") { Patterns = ["*.json"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickOpenFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open packet capture",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Captures (JSON or raw)") { Patterns = ["*.json", "*.b64", "*.raw"] },
                new FilePickerFileType("JSON") { Patterns = ["*.json"] },
                new FilePickerFileType("Raw packets") { Patterns = ["*.b64", "*.raw"] },
                new FilePickerFileType("All files") { Patterns = ["*"] }
            ]
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string suggestedName, string extension, string typeName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Save packets as {typeName}",
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = [$"*.{extension}"] }]
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
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
        _workspaceGrid = this.FindControl<Grid>("WorkspaceGrid");
        _contentGrid = this.FindControl<Grid>("ContentGrid");
        _sidebarPanel = this.FindControl<Control>("SidebarPanel");
        _sidebarGrid = _sidebarPanel as Grid;
        _contentSplitter = this.FindControl<GridSplitter>("ContentSplitter");

        _statusHost = this.FindControl<DetachableHost>("StatusHost");
        _byCodeHost = this.FindControl<DetachableHost>("ByCodeHost");
        _listHost = this.FindControl<DetachableHost>("ListHost");
        _detailHost = this.FindControl<DetachableHost>("DetailHost");
        if (_statusHost is not null) _statusHost.DetachedChanged += OnSidebarDetachChanged;
        if (_byCodeHost is not null) _byCodeHost.DetachedChanged += OnSidebarDetachChanged;
        if (_listHost is not null) _listHost.DetachedChanged += OnContentDetachChanged;
        if (_detailHost is not null) _detailHost.DetachedChanged += OnContentDetachChanged;

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
            vm.PacketDetail.LabelValueRequested += OnLabelValueRequested;
            vm.PacketDetail.ViewFullValueRequested += OnViewFullValueRequested;
            vm.PacketList.DiffRequested += OnDiffRequested;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.ShortcutsChanged += ApplyShortcuts;
            vm.OpenSettingsSectionRequested += OpenSettings;
            vm.UpdateAvailableRequested += OnUpdateAvailableRequested;
            ApplyEffectiveSidebar();
            ApplyShortcuts();
        }

        var layout = LayoutStore.Load();
        RestoreWindowBounds(layout);
        ApplyLayout(layout);
        _savedContentTopHeight = layout.TopPanelHeight > MinPanelSize ? layout.TopPanelHeight : 300;

        if (DataContext is MainViewModel vmAuto)
            vmAuto.TriggerAutoStart();
    }

    private bool BothSidebarDetached =>
        _statusHost?.IsDetached == true && _byCodeHost?.IsDetached == true;

    // A sidebar panel (Status / By Code) was detached or docked back. Collapse a detached panel's
    // row to zero so the other reclaims the sidebar; if both are detached, collapse the whole
    // sidebar column too so the content area fills the width.
    private void OnSidebarDetachChanged(object? sender, EventArgs e)
    {
        if (_sidebarGrid is not null)
        {
            var rows = _sidebarGrid.RowDefinitions;
            rows[0].Height = _statusHost?.IsDetached == true ? new GridLength(0) : GridLength.Auto;
            rows[1].Height = _byCodeHost?.IsDetached == true
                ? new GridLength(0)
                : new GridLength(1, GridUnitType.Star);
        }
        ApplyEffectiveSidebar();
    }

    // The sidebar column is shown only when the user hasn't toggled it off AND at least one of its
    // panels is still docked. With both detached there is nothing to show, so reclaim the width.
    private void ApplyEffectiveSidebar()
    {
        var wantsSidebar = (DataContext as MainViewModel)?.SidebarVisible != false;
        ApplySidebarVisibility(wantsSidebar && !BothSidebarDetached);
    }

    // A content panel (Packet List / Packet Detail) was detached or docked back. Give the whole
    // content area to whichever remains; hide the splitter unless both are docked.
    private void OnContentDetachChanged(object? sender, EventArgs e)
    {
        if (_contentGrid is null) return;
        var rows = _contentGrid.RowDefinitions;

        // While both are docked the splitter is live; remember the table height before collapsing
        // so it can be restored when the panel docks back.
        if (_contentSplitter?.IsVisible == true && rows[0].ActualHeight > MinPanelSize)
            _savedContentTopHeight = rows[0].ActualHeight;

        var listDet = _listHost?.IsDetached == true;
        var detailDet = _detailHost?.IsDetached == true;
        var bothDocked = !listDet && !detailDet;

        if (_contentSplitter is not null) _contentSplitter.IsVisible = bothDocked;
        rows[1].Height = new GridLength(bothDocked ? 2 : 0);

        if (bothDocked)
        {
            rows[0].MinHeight = MinPanelSize;
            rows[0].Height = new GridLength(_savedContentTopHeight, GridUnitType.Pixel);
            rows[2].MinHeight = MinPanelSize;
            rows[2].Height = new GridLength(1, GridUnitType.Star);
            return;
        }

        rows[0].MinHeight = listDet ? 0 : MinPanelSize;
        rows[0].Height = listDet ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        rows[2].MinHeight = detailDet ? 0 : MinPanelSize;
        rows[2].Height = detailDet ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SidebarVisible))
            ApplyEffectiveSidebar();
    }

    // Collapse the sidebar column (and its splitter) to zero width when hidden, instead of
    // just toggling visibility, so the table reclaims the space.
    private void ApplySidebarVisibility(bool visible)
    {
        if (_workspaceGrid == null || _sidebarPanel == null) return;
        _sidebarPanel.IsVisible = visible;
        if (visible)
        {
            var saved = LayoutStore.Load();
            _workspaceGrid.ColumnDefinitions[0].Width =
                new GridLength(saved.LeftPanelWidth > MinColumnWidth ? saved.LeftPanelWidth : 260, GridUnitType.Pixel);
            _workspaceGrid.ColumnDefinitions[0].MinWidth = MinColumnWidth;
            _workspaceGrid.ColumnDefinitions[1].Width = new GridLength(2, GridUnitType.Pixel);
        }
        else
        {
            _workspaceGrid.ColumnDefinitions[0].MinWidth = 0;
            _workspaceGrid.ColumnDefinitions[0].Width = new GridLength(0, GridUnitType.Pixel);
            _workspaceGrid.ColumnDefinitions[1].Width = new GridLength(0, GridUnitType.Pixel);
        }
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
        // Sidebar = fixed-width first column (only when visible); the content column is star.
        if (_workspaceGrid != null && (DataContext as MainViewModel)?.SidebarVisible != false)
        {
            var w = layout.LeftPanelWidth > MinColumnWidth ? layout.LeftPanelWidth : 260;
            _workspaceGrid.ColumnDefinitions[0] =
                new ColumnDefinition(w, GridUnitType.Pixel) { MinWidth = MinColumnWidth };
        }
        // Content = table row over detail row, split by a GridSplitter; table fixed, detail star.
        if (_contentGrid != null)
        {
            _contentGrid.RowDefinitions[0] =
                new RowDefinition(layout.TopPanelHeight, GridUnitType.Pixel) { MinHeight = MinPanelSize };
            _contentGrid.RowDefinitions[2] =
                new RowDefinition(1, GridUnitType.Star) { MinHeight = MinPanelSize };
        }
    }

    public void ResetLayout()
    {
        ApplyLayout(LayoutState.Default);
        LayoutStore.Save(LayoutState.Default);
        // ApplyLayout rebuilds the row definitions, so re-collapse any currently-detached panels.
        OnSidebarDetachChanged(this, EventArgs.Empty);
        OnContentDetachChanged(this, EventArgs.Empty);
    }

    // Rebuild the configurable KeyBindings from the user's gestures. Invalid or empty
    // gestures fall back to a sensible default so each shortcut always works.
    private void ApplyShortcuts()
    {
        if (DataContext is not MainViewModel vm) return;

        KeyBindings.Clear();
        AddBinding(vm.SidebarToggleGesture, "F5", vm.ToggleSidebarCommand);
        AddBinding(vm.AutoSelectNewestGesture, "Ctrl+L", vm.ToggleAutoSelectNewestCommand);
        AddBinding(vm.ToggleRowExpandGesture, "Space", vm.ToggleRowExpandCommand);
    }

    private void AddBinding(string? gestureText, string fallback, System.Windows.Input.ICommand command)
    {
        KeyGesture gesture;
        try { gesture = KeyGesture.Parse(string.IsNullOrWhiteSpace(gestureText) ? fallback : gestureText); }
        catch { gesture = KeyGesture.Parse(fallback); }
        KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command });
    }

    private void OnResetLayoutClicked(object? sender, RoutedEventArgs e) => ResetLayout();

    private void OnEditParamRequested(EditParamViewModel vm)
    {
        var win = new EditParamWindow(vm);
        win.Show(this);
    }

    private void OnLabelValueRequested(EnumLabelViewModel vm)
    {
        var win = new EnumLabelWindow(vm);
        win.Show(this);
    }

    private void OnViewFullValueRequested(ParamRow row, Services.GameDataService gameData)
    {
        var win = new ExpandedValueWindow(row, gameData);
        win.Show(this);
    }

    private async void OnUpdateAvailableRequested(string version, string? notes)
    {
        if (DataContext is not MainViewModel vm) return;

        var dialogVm = new UpdateAvailableViewModel(version, notes);
        var win = new UpdateAvailableWindow(dialogVm);
        await win.ShowDialog(this);

        switch (dialogVm.Choice)
        {
            case UpdateChoice.UpdateNow:
                if (vm.ApplyUpdateCommand.CanExecute(null)) vm.ApplyUpdateCommand.Execute(null);
                break;
            case UpdateChoice.Skip:
                vm.SkipUpdateVersion(version);
                break;
            case UpdateChoice.NotNow:
                break;
        }
    }

    private void OnDiffRequested(Models.PacketEntry left, Models.PacketEntry right)
    {
        if (DataContext is not MainViewModel vm) return;
        var win = new PacketDiffWindow(left, right, vm.Schema);
        win.Show(this);
    }

    private SettingsWindow? _settingsWindow;

    private void OnSettingsClicked(object? sender, RoutedEventArgs e) => OpenSettings(null);

    private void OpenSettings(string? section)
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Activate();
            return;
        }

        var vm = (MainViewModel)DataContext!;
        _settingsWindow = new SettingsWindow(new SettingsViewModel(vm), section);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(this);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closing) return;
        var prev = LayoutStore.Load();

        // Only persist the sidebar width while it is visible (a hidden sidebar is 0-wide).
        var sidebarVisible = (DataContext as MainViewModel)?.SidebarVisible != false;
        var sidebarW = sidebarVisible ? (_workspaceGrid?.ColumnDefinitions[0].ActualWidth ?? 0) : 0;

        // A detached content panel collapses its row, so the live row height is meaningless then;
        // fall back to the last good split height instead of persisting the collapsed value.
        var contentDetached = _listHost?.IsDetached == true || _detailHost?.IsDetached == true;
        var tableH = contentDetached
            ? _savedContentTopHeight
            : (_contentGrid?.RowDefinitions[0].ActualHeight ?? 0);

        // Only capture the normal (restored) bounds; when maximized keep the previous
        // normal bounds and just remember the maximized flag.
        var maximized = WindowState == WindowState.Maximized;
        var winX = maximized ? prev.WindowX : Position.X;
        var winY = maximized ? prev.WindowY : Position.Y;
        var winW = maximized ? prev.WindowWidth : Width;
        var winH = maximized ? prev.WindowHeight : Height;

        LayoutStore.Save(prev with
        {
            TopPanelHeight = tableH   > 10 ? tableH   : prev.TopPanelHeight,
            LeftPanelWidth = sidebarW > 10 ? sidebarW : prev.LeftPanelWidth,
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
