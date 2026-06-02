using System;
using Avalonia.Controls;

namespace AlbionPacketExplorer.Controls;

/// <summary>
/// Application window base. Uses the native OS title bar (reliable + cross-platform);
/// the client area is themed entirely through the Apx token system.
/// </summary>
public class ApxWindow : Window
{
    /// <summary>
    /// Sizes the window to the requested width/height, clamped to a fraction of the
    /// screen the window opens on, and never below the given minimums. Use for popups
    /// whose ideal size depends on their content.
    /// </summary>
    protected void SizeToScreen(
        double desiredWidth, double desiredHeight,
        double minWidth = 320, double minHeight = 200,
        double maxScreenFraction = 0.85)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var maxWidth = area.Width / scale * maxScreenFraction;
        var maxHeight = area.Height / scale * maxScreenFraction;

        Width = Math.Clamp(desiredWidth, minWidth, maxWidth);
        Height = Math.Clamp(desiredHeight, minHeight, maxHeight);
    }
}
