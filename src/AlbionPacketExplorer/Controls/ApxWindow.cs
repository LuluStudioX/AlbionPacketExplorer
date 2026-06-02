using Avalonia.Controls;

namespace AlbionPacketExplorer.Controls;

/// <summary>
/// Application window base. Uses the native OS title bar (reliable + cross-platform);
/// the client area is themed entirely through the Apx token system. A future custom
/// borderless chrome can replace this, but the native bar avoids the brittle
/// window-ControlTheme resolution issues under Avalonia 12.
/// </summary>
public class ApxWindow : Window
{
}
