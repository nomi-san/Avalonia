using Avalonia;

namespace FileTransferClipboard;

public class Program
{
    static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithDeveloperTools()
            .LogToTrace();
}
