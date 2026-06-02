using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
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
    private NativeRenderView? _renderView;
    private TextBlock _statusText;
    private TextBlock _mouseText;
    private TextBlock _keyText;
    private TextBlock _modeText;
    private Button _enableButton;
    private Button _relMouseButton;
    private bool _viewEnabled;

    public MainWindow()
    {
        Title = "X11 Native Sample - NativeControlHost + C++ Rendering";
        Width = 900;
        Height = 700;

        _statusText = new TextBlock
        {
            Text = "Status: Initializing...",
            FontSize = 16,
            Margin = new Thickness(10),
        };

        _mouseText = new TextBlock
        {
            Text = "View Mouse: N/A",
            FontSize = 13,
            Margin = new Thickness(10, 2),
            FontFamily = new FontFamily("Monospace"),
        };

        _keyText = new TextBlock
        {
            Text = "View Keyboard: N/A",
            FontSize = 13,
            Margin = new Thickness(10, 2),
            FontFamily = new FontFamily("Monospace"),
        };

        _modeText = new TextBlock
        {
            Text = "Relative Mouse: OFF",
            FontSize = 13,
            Margin = new Thickness(10, 2),
            Foreground = Brushes.DarkBlue,
        };

        _enableButton = new Button
        {
            Content = "Enable View",
            Margin = new Thickness(10, 5),
            Padding = new Thickness(20, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _enableButton.Click += OnEnableButtonClick;

        _relMouseButton = new Button
        {
            Content = "Toggle Relative Mouse",
            Margin = new Thickness(10, 5),
            Padding = new Thickness(20, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = false,
        };
        _relMouseButton.Click += OnRelMouseButtonClick;

        var infoText = new TextBlock
        {
            Text = "The native view below is a child X11 window created in C++ via NativeControlHost.\n" +
                   "It renders using X11 drawing (stub for Vulkan/GLX). Mouse absolute coordinates\n" +
                   "are bound to this view. Events are only active when enabled via the button.\n" +
                   "A crosshair shows mouse position inside the native view.",
            FontSize = 11,
            Margin = new Thickness(10, 10, 10, 5),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
        };

        // Create the native render view (NativeControlHost subclass)
        _renderView = new NativeRenderView
        {
            MinWidth = 640,
            MinHeight = 360,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(10),
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _enableButton, _relMouseButton },
        };

        Content = new DockPanel
        {
            Children =
            {
                new StackPanel
                {
                    Dock = Dock.Top,
                    Children =
                    {
                        _statusText,
                        buttonPanel,
                        _mouseText,
                        _keyText,
                        _modeText,
                        infoText,
                    }
                },
                _renderView,
            }
        };
        DockPanel.SetDock((Control)((DockPanel)Content).Children[0], Dock.Top);

        Opened += OnWindowOpened;
        Closed += OnWindowClosed;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        _patcher = new X11RuntimePatcher(this);

        DispatcherTimer.RunOnce(() =>
        {
            if (_patcher.Initialize())
            {
                _statusText.Text = "Status: Native library initialized ✓";
                _statusText.Foreground = Brushes.Green;

                // Pass display to the render view
                _renderView!.SetDisplay(_patcher.Display);

                // Subscribe to view events
                _renderView.ViewMouseEvent += OnViewMouseEvent;
                _renderView.ViewKeyEvent += OnViewKeyEvent;

                // Poll display
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                timer.Tick += (_, _) => UpdateDisplay();
                timer.Start();
            }
            else
            {
                _statusText.Text = "Status: Failed to initialize ✗";
                _statusText.Foreground = Brushes.Red;
            }
        }, TimeSpan.FromMilliseconds(200));
    }

    private void OnEnableButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_renderView == null) return;

        _viewEnabled = !_viewEnabled;
        _renderView.SetEnabled(_viewEnabled);

        _enableButton.Content = _viewEnabled ? "Disable View" : "Enable View";
        _relMouseButton.IsEnabled = _viewEnabled;

        if (!_viewEnabled)
        {
            _modeText.Text = "Relative Mouse: OFF";
            _modeText.Foreground = Brushes.DarkBlue;
        }
    }

    private void OnRelMouseButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_renderView == null || !_viewEnabled) return;

        bool newMode = !_renderView.IsRelativeMouseMode;
        _renderView.SetRelativeMouseMode(newMode);
        _modeText.Text = newMode ? "Relative Mouse: ON (pointer locked)" : "Relative Mouse: OFF";
        _modeText.Foreground = newMode ? Brushes.Red : Brushes.DarkBlue;
    }

    private void OnViewMouseEvent(NativeMouseEventType type, NativeMouseEvent ev)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _mouseText.Text = $"View Mouse: abs=({ev.AbsX:F1},{ev.AbsY:F1}) " +
                              $"rel=({ev.RelX:F3},{ev.RelY:F3}) " +
                              $"type={type} btn=0x{ev.Buttons:X} mod=0x{ev.Modifiers:X}";
        });
    }

    private void OnViewKeyEvent(NativeKeyEventType type, NativeKeyEvent ev)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _keyText.Text = $"View Keyboard: {type} keycode={ev.Keycode} keysym=0x{ev.Keysym:X} mod=0x{ev.Modifiers:X}";
        });
    }

    private void UpdateDisplay()
    {
        if (_renderView == null || !_viewEnabled) return;
        var (dx, dy) = _renderView.GetMouseDelta();
        if (dx != 0 || dy != 0)
        {
            // Delta is already shown in mouse event, but we can show accumulated here
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _renderView?.Dispose();
        _patcher?.Dispose();
    }
}
