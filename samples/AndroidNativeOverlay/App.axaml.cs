using Avalonia;
using Avalonia.Markup.Xaml;

namespace AndroidNativeOverlay;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
