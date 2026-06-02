using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AlbionPacketExplorer.Services;

public enum ToastSeverity { Info, Success, Warning, Error }

/// <summary>A single transient notification shown by <see cref="ApxToastHost"/>.</summary>
public sealed partial class Toast : ObservableObject
{
    public string Title { get; }
    public string Message { get; }
    public ToastSeverity Severity { get; }

    public Toast(string title, string message, ToastSeverity severity)
    {
        Title = title;
        Message = message;
        Severity = severity;
    }
}

/// <summary>
/// In-house replacement for SukiUI's toast manager. Holds the active toast queue and
/// auto-dismisses each after a delay. The host control (ApxToastHost) binds to
/// <see cref="Toasts"/>; view models call <see cref="Show"/>.
/// </summary>
public sealed class ToastService
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(3);

    public ObservableCollection<Toast> Toasts { get; } = [];

    public void Show(string title, string message, ToastSeverity severity = ToastSeverity.Info)
        => Show(title, message, severity, DefaultLifetime);

    public void Show(string title, string message, ToastSeverity severity, TimeSpan lifetime)
    {
        var toast = new Toast(title, message, severity);
        Dispatcher.UIThread.Post(() =>
        {
            Toasts.Add(toast);
            DispatcherTimer.RunOnce(() => Dismiss(toast), lifetime);
        });
    }

    public void Dismiss(Toast toast) => Dispatcher.UIThread.Post(() => Toasts.Remove(toast));
}
