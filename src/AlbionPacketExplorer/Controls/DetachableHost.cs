using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using AlbionPacketExplorer.Views;
using CommunityToolkit.Mvvm.Input;

namespace AlbionPacketExplorer.Controls;

/// <summary>
/// Wraps a single panel (header + body) and lets the user pop it out into a floating
/// <see cref="DetachWindow"/>. The live body moves to the window; the original slot is meant to be
/// collapsed by the parent view (see <see cref="DetachedChanged"/>) so siblings reclaim the space.
/// Closing the window, or pressing its Dock button, returns the body to its original slot.
///
/// Reparenting is a pure visual-tree move: the body keeps its own explicitly-set DataContext, and the
/// floating window inherits this host's DataContext so any inherited bindings still resolve. State is
/// session-only; nothing is persisted.
/// </summary>
public class DetachableHost : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<DetachableHost, string?>(nameof(Title));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DirectProperty<DetachableHost, bool> IsDetachedProperty =
        AvaloniaProperty.RegisterDirect<DetachableHost, bool>(nameof(IsDetached), o => o.IsDetached);

    private bool _isDetached;
    public bool IsDetached
    {
        get => _isDetached;
        private set => SetAndRaise(IsDetachedProperty, ref _isDetached, value);
    }

    public ICommand DetachCommand { get; }

    /// <summary>Raised after the detached state changes (both on detach and reattach).</summary>
    public event EventHandler? DetachedChanged;

    private DetachWindow? _window;
    private Control? _body;
    private Window? _owner;
    private bool _ownerClosing;

    public DetachableHost()
    {
        DetachCommand = new RelayCommand(Detach);
    }

    private void Detach()
    {
        if (IsDetached) return;
        if (Content is not Control body) return;

        _owner = TopLevel.GetTopLevel(this) as Window;
        _body = body;
        Content = null;                       // unparent body so the window can adopt it

        _window = new DetachWindow(Title ?? string.Empty, Reattach) { DataContext = DataContext };
        _window.SetInitialSize(Bounds.Width, Bounds.Height);
        _window.SetBody(body);
        _window.Closed += OnWindowClosed;

        if (_owner is not null) _owner.Closing += OnOwnerClosing;
        IsDetached = true;
        DetachedChanged?.Invoke(this, EventArgs.Empty);

        if (_owner is not null) _window.Show(_owner);
        else _window.Show();
    }

    // App is shutting down: leave the body in the (auto-closing) floating window and never touch
    // the main window's visual tree, which is being torn down.
    private void OnOwnerClosing(object? sender, WindowClosingEventArgs e) => _ownerClosing = true;

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_ownerClosing) { Cleanup(); return; }
        Reattach();
    }

    private void Reattach()
    {
        if (!IsDetached) return;

        var body = _window?.DetachBody() ?? _body;
        if (_window is not null) _window.Closed -= OnWindowClosed;
        if (body is not null) Content = body;

        IsDetached = false;
        var window = _window;
        _window = null;
        _body = null;
        Cleanup();
        DetachedChanged?.Invoke(this, EventArgs.Empty);
        window?.Close();
    }

    private void Cleanup()
    {
        if (_owner is not null) { _owner.Closing -= OnOwnerClosing; _owner = null; }
    }
}
