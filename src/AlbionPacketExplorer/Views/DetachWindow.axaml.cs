using System;
using Avalonia.Controls;
using AlbionPacketExplorer.Controls;

namespace AlbionPacketExplorer.Views;

/// <summary>
/// Floating host for a panel detached from the main window. Owns chrome only (title + Dock button);
/// the body is supplied by the <see cref="DetachableHost"/>, which also drives reattach. Closing the
/// window or pressing Dock routes back through <see cref="_onDock"/>.
/// </summary>
public partial class DetachWindow : ApxWindow
{
    private readonly Action _onDock;

    // Parameterless ctor for the XAML designer / Avalonia loader.
    public DetachWindow() : this(string.Empty, static () => { }) { }

    public DetachWindow(string title, Action onDock)
    {
        InitializeComponent();
        _onDock = onDock;
        Title = title;
        TitleText.Text = title;
        DockButton.Click += (_, _) => _onDock();
    }

    public void SetBody(Control body) => BodyHost.Content = body;

    public Control? DetachBody()
    {
        var body = BodyHost.Content as Control;
        BodyHost.Content = null;
        return body;
    }

    // Size the floating window to roughly match the panel's footprint in the main window, leaving
    // room for the window chrome and the local title bar.
    public void SetInitialSize(double width, double height)
    {
        if (width > 120) Width = width + 16;
        if (height > 100) Height = height + 56;
    }
}
