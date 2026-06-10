using System.Collections.Specialized;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using AlbionPacketExplorer.Controls;
using AlbionPacketExplorer.Services;
using AlbionPacketExplorer.ViewModels;

namespace AlbionPacketExplorer.Views;

public partial class DiagnosticsWindow : ApxWindow
{
    private readonly DiagnosticsViewModel _vm;

    public DiagnosticsWindow(DiagnosticsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        vm.Clipboard = Clipboard;
        vm.RequestOpenLogsFolder = OpenLogsFolder;

        // Keep the newest log line in view as lines stream in.
        vm.Log.CollectionChanged += OnLogChanged;
        Closed += (_, _) =>
        {
            vm.Log.CollectionChanged -= OnLogChanged;
            vm.Dispose();
        };
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        var scroller = this.FindControl<ScrollViewer>("LogScroller");
        if (scroller is null) return;
        Dispatcher.UIThread.Post(() =>
            scroller.Offset = scroller.Offset.WithY(scroller.Extent.Height),
            DispatcherPriority.Background);
    }

    private void OpenLogsFolder()
    {
        try
        {
            var dir = AppPaths.LogsDir;
            Directory.CreateDirectory(dir);
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            // A file:// URI to the folder opens the OS file manager on all three platforms;
            // new Uri(absolutePath) yields the right file URI on Windows, Linux and macOS.
            if (launcher is not null)
                _ = launcher.LaunchUriAsync(new Uri(dir));
        }
        catch { /* opening the folder is best-effort */ }
    }
}
