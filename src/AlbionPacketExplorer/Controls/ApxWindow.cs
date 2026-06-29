using System;
using Avalonia;
using Avalonia.Controls;

namespace AlbionPacketExplorer.Controls;

/// <summary>
/// Application window base. Uses the native OS title bar (reliable + cross-platform);
/// the client area is themed entirely through the Apx token system.
/// </summary>
public class ApxWindow : Window
{
    // Avalonia's CenterOwner is unreliable on multi-monitor desktops with negative screen
    // coordinates: a dialog can land on a different physical monitor than the owner (or partly
    // off any visible screen), which reads as "the popup never opened". After the window is sized,
    // re-center it on the OWNER'S screen and clamp it fully inside that screen's working area.
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (WindowStartupLocation != WindowStartupLocation.CenterOwner) return;
        if (Owner is not Window owner) return;

        var ownerCenter = new PixelPoint(
            owner.Position.X + (int)(owner.Bounds.Width * (owner.DesktopScaling) / 2),
            owner.Position.Y + (int)(owner.Bounds.Height * (owner.DesktopScaling) / 2));

        var screen = Screens.ScreenFromPoint(ownerCenter) ?? Screens.ScreenFromWindow(owner)
                     ?? Screens.Primary;
        if (screen is null) return;

        var area = screen.WorkingArea;
        var w = (int)(Bounds.Width * DesktopScaling);
        var h = (int)(Bounds.Height * DesktopScaling);

        // Center on the owner's screen, then clamp so the whole window stays inside the work area.
        var x = area.X + (area.Width - w) / 2;
        var y = area.Y + (area.Height - h) / 2;
        x = Math.Clamp(x, area.X, Math.Max(area.X, area.X + area.Width - w));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - h));

        Position = new PixelPoint(x, y);
    }

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
