using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AlbionPacketExplorer.Views;

namespace AlbionPacketExplorer;

public class App : Application
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    public override void Initialize()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            SetCurrentProcessExplicitAppUserModelID("AlbionPacketExplorer.App");

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrayIconClicked(object? sender, EventArgs e) => ShowMainWindow();

    private void OnTrayShowClicked(object? sender, EventArgs e) => ShowMainWindow();

    private void OnTrayExitClicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private static void ShowMainWindow()
    {
        if (Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        var window = desktop.MainWindow;
        if (window == null) return;
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
