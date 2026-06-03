using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AlbionPacketExplorer.Controls;

/// <summary>
/// A click-to-record key gesture box. Click it, press a key combination, and it captures the
/// gesture as a string (e.g. "Ctrl+B", "F5"). Two-way bindable via <see cref="Gesture"/>.
/// Modifier-only presses are ignored until a non-modifier key completes the combo.
/// </summary>
public class GestureCaptureBox : Button
{
    public static readonly StyledProperty<string> GestureProperty =
        AvaloniaProperty.Register<GestureCaptureBox, string>(
            nameof(Gesture), defaultValue: "F5",
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public string Gesture
    {
        get => GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    private bool _recording;

    public GestureCaptureBox()
    {
        UpdateContent();
        LostFocus += (_, _) =>
        {
            if (_recording)
            {
                _recording = false;
                UpdateContent();
            }
        };
    }

    protected override void OnClick()
    {
        base.OnClick();
        _recording = true;
        Content = "Press a key…";
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_recording)
        {
            base.OnKeyDown(e);
            return;
        }

        // Wait for a real key; ignore lone modifier presses.
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            _recording = false;
            UpdateContent();
            e.Handled = true;
            return;
        }

        var gesture = new KeyGesture(e.Key, e.KeyModifiers);
        Gesture = gesture.ToString();
        _recording = false;
        UpdateContent();
        e.Handled = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GestureProperty && !_recording)
            UpdateContent();
    }

    private void UpdateContent() =>
        Content = string.IsNullOrWhiteSpace(Gesture) ? "F5" : Gesture;
}
