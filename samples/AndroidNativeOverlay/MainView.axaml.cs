using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AndroidNativeOverlay;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
    }

    private void OnToggleOverlay(object? sender, RoutedEventArgs e)
    {
        OverlayService.RequestToggleOverlay();
        StatusText.Text = "Overlay toggled. The Avalonia XAML panel floats above the native GL surface.";
    }
}
