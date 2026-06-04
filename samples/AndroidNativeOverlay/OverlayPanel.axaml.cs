using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AndroidNativeOverlay;

public partial class OverlayPanel : UserControl
{
    public OverlayPanel()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        OverlayService.RequestCloseOverlay();
    }

    private void OnOpacityChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (OpacityLabel != null)
        {
            OpacityLabel.Text = $"Panel Opacity: {e.NewValue:F2}";
            Opacity = e.NewValue;
        }
    }

    private void OnClickThroughChanged(object? sender, RoutedEventArgs e)
    {
        OverlayService.RequestToggleClickThrough();
    }
}
