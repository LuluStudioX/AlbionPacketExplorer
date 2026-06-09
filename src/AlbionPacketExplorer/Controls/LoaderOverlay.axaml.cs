using Avalonia;
using Avalonia.Controls;
using Avalonia.Labs.Gif;

namespace AlbionPacketExplorer.Controls;

/// <summary>
/// Full-bleed loading overlay: a centered animated GIF on a dim backdrop that fades in and out.
/// Place it as the last child of a Grid/Panel so it covers its siblings, and drive
/// <see cref="IsActive"/>. When inactive it is transparent and click-through.
/// </summary>
public partial class LoaderOverlay : UserControl
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<LoaderOverlay, bool>(nameof(IsActive));

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    static LoaderOverlay()
    {
        IsActiveProperty.Changed.AddClassHandler<LoaderOverlay>((o, _) => o.UpdateActive());
    }

    public LoaderOverlay()
    {
        InitializeComponent();
        // Source (IGifSource) has no string/uri converter, so build it from the embedded asset here.
        var gif = this.FindControl<GifImage>("Gif");
        if (gif is not null)
        {
            try { gif.Source = GifStreamSource.FromUriString("avares://AlbionPacketExplorer/Assets/loader.gif"); }
            catch { /* asset missing / designer: leave the overlay blank rather than crash */ }
        }
        UpdateActive();
    }

    // Fade is the inline Opacity transition; setting the target opacity animates it. Hit-testing
    // follows IsActive so the overlay only blocks input while shown.
    private void UpdateActive()
    {
        var backdrop = this.FindControl<Border>("Backdrop");
        if (backdrop is null) return;
        backdrop.Opacity = IsActive ? 1 : 0;
        backdrop.IsHitTestVisible = IsActive;
    }
}
