using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AlbionPacketExplorer.Views;
using Microsoft.Extensions.DependencyInjection;
using AlbionPacketExplorer.Services;

namespace AlbionPacketExplorer;

public class App : Application
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        if (OperatingSystem.IsWindows())
            SetCurrentProcessExplicitAppUserModelID("AlbionPacketExplorer.App");

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Nothing logged unhandled exceptions, so a throw in a command handler (e.g. opening the
        // Share window) was swallowed by the dispatcher and read to the user as "the button does
        // nothing". Record them to a crash log so these failures are diagnosable.
        InstallCrashLogging();

        // Apply persisted culture AND theme before any window renders, so the saved accent / dark
        // mode are in effect on the first paint (no flash of the XAML-default accent on startup).
        var saved = AppSettingsStore.Load();
        LocalizationService.Instance.SetCulture(saved.Culture);
        ThemeService.Instance.Initialize(saved.IsDarkMode, saved.AccentTheme);

        var services = new ServiceCollection();
        services.AddSingleton<ToastService>();
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(Services.GetRequiredService<ToastService>());
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Last-resort exception sinks. Avalonia/dispatcher swallows exceptions thrown from UI event
    // handlers; AppDomain + TaskScheduler catch the rest. All three append to a single crash log.
    private static void InstallCrashLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrash("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Append one exception to <c>logs/crash.log</c>. Never throws.</summary>
    public static void WriteCrash(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] {ex}\n\n";
            File.AppendAllText(Path.Combine(AppPaths.LogsDir, "crash.log"), line);
        }
        catch { /* logging must never crash the crash logger */ }
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
