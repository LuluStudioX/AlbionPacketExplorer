using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using AlbionPacketExplorer.Services;

namespace AlbionPacketExplorer.Controls;

/// <summary>
/// Overlay that renders the active <see cref="ToastService.Toasts"/> as a stack of
/// cards anchored bottom-right. Clicking a card dismisses it. Replaces SukiToastHost.
/// </summary>
public class ApxToastHost : TemplatedControl
{
    public static readonly StyledProperty<ToastService?> ManagerProperty =
        AvaloniaProperty.Register<ApxToastHost, ToastService?>(nameof(Manager));

    public ToastService? Manager
    {
        get => GetValue(ManagerProperty);
        set => SetValue(ManagerProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<ItemsControl>("PART_Items") is { } items)
            items.AddHandler(PointerReleasedEvent, OnToastClicked, handledEventsToo: true);
    }

    private void OnToastClicked(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Control { DataContext: Toast toast })
            Manager?.Dismiss(toast);
    }
}
