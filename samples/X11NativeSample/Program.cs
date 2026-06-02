using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using static X11NativeSample.NativeX11Interop;

namespace X11NativeSample;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                EnableMultiTouch = true,
            })
            .UseSkia()
            .WithInterFont()
            .LogToTrace();
}

class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

class MainWindow : Window
{
    private X11RuntimePatcher? _patcher;
    private TextBlock _statusText;
    private TextBlock _mouseText;
    private TextBlock _deltaText;
    private TextBlock _modeText;
    private bool _relativeModeEnabled;

    public MainWindow()
    {
        Title = "X11 Native Sample - C++ Event Handling";
        Width = 800;
        Height = 600;

        _statusText = new TextBlock
        {
            Text = "Status: Waiting for window...",
            FontSize = 16,
            Margin = new Thickness(10),
        };

        _mouseText = new TextBlock
        {
            Text = "Mouse: N/A",
            FontSize = 14,
            Margin = new Thickness(10, 0),
        };

        _deltaText = new TextBlock
        {
            Text = "Delta: N/A",
            FontSize = 14,
            Margin = new Thickness(10, 0),
        };

        _modeText = new TextBlock
        {
            Text = "Mode: Absolute (press 'R' for relative)",
            FontSize = 14,
            Margin = new Thickness(10, 0),
            Foreground = Brushes.DarkBlue,
        };

        var infoText = new TextBlock
        {
            Text = "This sample uses a native C++ library (libx11_native) to handle X11 events.\n" +
                   "Mouse events and relative position are processed in C++ to avoid GC pressure.\n" +
                   "The C# side patches Avalonia.X11 at runtime via reflection.\n\n" +
                   "Press 'R' to toggle relative mouse mode.\n" +
                   "Press 'Escape' to exit relative mouse mode.",
            FontSize = 12,
            Margin = new Thickness(10, 20, 10, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
        };

        Content = new StackPanel
        {
            Children =
            {
                _statusText,
                _mouseText,
                _deltaText,
                _modeText,
                infoText,
            }
        };

        // Initialize after the window is opened
        Opened += OnWindowOpened;
        KeyDown += OnKeyDown;
        Closed += OnWindowClosed;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        _patcher = new X11RuntimePatcher(this);

        // Delay initialization to ensure the platform handle is ready
        DispatcherTimer.RunOnce(() =>
        {
            if (_patcher.Initialize())
            {
                _statusText.Text = "Status: Native library initialized ✓";
                _statusText.Foreground = Brushes.Green;

                // Subscribe to native mouse events
                _patcher.NativeMouseEvent += OnNativeMouseEvent;

                // Start a timer to poll mouse delta (for display purposes)
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                timer.Tick += (_, _) => UpdateMouseDisplay();
                timer.Start();
            }
            else
            {
                _statusText.Text = "Status: Failed to initialize native library ✗";
                _statusText.Foreground = Brushes.Red;
            }
        }, TimeSpan.FromMilliseconds(100));
    }

    private void OnNativeMouseEvent(NativeMouseEventType type, NativeMouseEvent ev)
    {
        // This is called from native code - update UI on dispatcher
        Dispatcher.UIThread.Post(() =>
        {
            _mouseText.Text = $"Mouse: abs=({ev.AbsX:F1}, {ev.AbsY:F1}) " +
                              $"rel=({ev.RelX:F3}, {ev.RelY:F3}) " +
                              $"type={type} buttons=0x{ev.Buttons:X}";
        });
    }

    private void UpdateMouseDisplay()
    {
        if (_patcher == null) return;

        var (relX, relY) = _patcher.GetRelativeMousePosition();
        var (deltaX, deltaY) = _patcher.GetMouseDelta();

        _deltaText.Text = $"Delta: ({deltaX:F1}, {deltaY:F1}) | RelPos: ({relX:F3}, {relY:F3})";
    }

    private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (_patcher == null) return;

        if (e.Key == Avalonia.Input.Key.R && !_relativeModeEnabled)
        {
            _relativeModeEnabled = true;
            _patcher.SetRelativeMouseMode(true);
            _modeText.Text = "Mode: RELATIVE (pointer locked, press 'Escape' to exit)";
            _modeText.Foreground = Brushes.Red;
        }
        else if (e.Key == Avalonia.Input.Key.Escape && _relativeModeEnabled)
        {
            _relativeModeEnabled = false;
            _patcher.SetRelativeMouseMode(false);
            _modeText.Text = "Mode: Absolute (press 'R' for relative)";
            _modeText.Foreground = Brushes.DarkBlue;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _patcher?.Dispose();
    }
}
