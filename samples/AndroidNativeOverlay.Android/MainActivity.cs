using System;
using System.Runtime.InteropServices;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Avalonia.Android;

namespace AndroidNativeOverlay.Android;

/// <summary>
/// Main activity that orchestrates the three-layer view hierarchy:
///   1. Base AvaloniaView (MainView) — bottom layer, main Avalonia content
///   2. Native SurfaceView (OpenGL ES via C++ NDK) — middle layer, native rendering
///   3. Overlay AvaloniaView (OverlayPanel) — top layer, Avalonia XAML overlay
///
/// This demonstrates embedding a native Android view over Avalonia and then
/// showing an Avalonia overlay/popup on top, similar to how child windows
/// work on desktop platforms (frameless overlay windows).
/// </summary>
[Activity(
    Label = "AndroidNativeOverlay",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/Icon",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AppCompatActivity, ISurfaceHolderCallback
{
    private FrameLayout? _rootLayout;
    private AvaloniaView? _mainAvaloniaView;
    private SurfaceView? _glSurfaceView;
    private AvaloniaView? _overlayAvaloniaView;
    private FrameLayout? _overlayContainer;
    private bool _overlayVisible;
    private bool _clickThrough;
    private bool _rendererInitialized;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _rootLayout = new FrameLayout(this);

        // ---- Layer 1: Base Avalonia view (main content) ----
        _mainAvaloniaView = new AvaloniaView(this) { Content = new MainView() };
        _rootLayout.AddView(_mainAvaloniaView,
            new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

        // ---- Layer 2: Native GL surface (centered, partial screen) ----
        _glSurfaceView = new SurfaceView(this);
        _glSurfaceView.Holder!.AddCallback(this);
        // Position the GL surface in the center portion of the screen
        var glParams = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent)
        {
            Gravity = GravityFlags.Center
        };
        glParams.SetMargins(48, 200, 48, 200);
        _rootLayout.AddView(_glSurfaceView, glParams);

        // ---- Layer 3: Overlay Avalonia view (floating panel on top) ----
        _overlayContainer = new FrameLayout(this);
        _overlayAvaloniaView = new AvaloniaView(this) { Content = new OverlayPanel() };
        _overlayContainer.AddView(_overlayAvaloniaView,
            new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));
        _overlayContainer.Visibility = ViewStates.Gone;
        _rootLayout.AddView(_overlayContainer,
            new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent));

        SetContentView(_rootLayout);

        // Wire up overlay service events
        OverlayService.ToggleOverlayRequested += OnToggleOverlay;
        OverlayService.CloseOverlayRequested += OnCloseOverlay;
        OverlayService.ToggleClickThroughRequested += OnToggleClickThrough;
    }

    protected override void OnDestroy()
    {
        OverlayService.ToggleOverlayRequested -= OnToggleOverlay;
        OverlayService.CloseOverlayRequested -= OnCloseOverlay;
        OverlayService.ToggleClickThroughRequested -= OnToggleClickThrough;

        if (_rendererInitialized)
        {
            NativeGLRenderer.Stop();
            NativeGLRenderer.Destroy();
            _rendererInitialized = false;
        }

        base.OnDestroy();
    }

    // ----- Overlay management -----

    private void OnToggleOverlay()
    {
        RunOnUiThread(() =>
        {
            _overlayVisible = !_overlayVisible;
            if (_overlayContainer != null)
                _overlayContainer.Visibility = _overlayVisible ? ViewStates.Visible : ViewStates.Gone;
        });
    }

    private void OnCloseOverlay()
    {
        RunOnUiThread(() =>
        {
            _overlayVisible = false;
            if (_overlayContainer != null)
                _overlayContainer.Visibility = ViewStates.Gone;
        });
    }

    private void OnToggleClickThrough()
    {
        RunOnUiThread(() =>
        {
            _clickThrough = !_clickThrough;
            if (_overlayContainer != null)
            {
                // In click-through mode, the overlay does not intercept touches
                _overlayContainer.SetOnTouchListener(
                    _clickThrough ? new PassthroughTouchListener() : null);
            }
        });
    }

    // ----- SurfaceHolder.Callback for native GL rendering -----

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        // Get the ANativeWindow pointer from the Java Surface object
        var surface = holder.Surface;
        if (surface?.Handle == null)
            return;

        var nativeWindow = ANativeWindow_fromSurface(
            JNIEnv.Handle, surface.Handle);

        if (nativeWindow == IntPtr.Zero)
            return;

        try
        {
            var frame = holder.SurfaceFrame;
            if (frame == null)
                return;

            int result = NativeGLRenderer.Init(nativeWindow, frame.Width(), frame.Height());
            if (result == 0)
            {
                _rendererInitialized = true;
                NativeGLRenderer.Start();
            }
        }
        finally
        {
            ANativeWindow_release(nativeWindow);
        }
    }

    public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format,
        int width, int height)
    {
        if (_rendererInitialized)
            NativeGLRenderer.Resize(width, height);
    }

    public void SurfaceDestroyed(ISurfaceHolder holder)
    {
        if (_rendererInitialized)
        {
            NativeGLRenderer.Stop();
            NativeGLRenderer.Destroy();
            _rendererInitialized = false;
        }
    }

    // ----- NDK imports for ANativeWindow -----

    [DllImport("android")]
    private static extern IntPtr ANativeWindow_fromSurface(IntPtr jniEnv, IntPtr surface);

    [DllImport("android")]
    private static extern void ANativeWindow_release(IntPtr window);

    /// <summary>
    /// Touch listener that always returns false, allowing touches to pass
    /// through to views below in the z-order.
    /// </summary>
    private class PassthroughTouchListener : Java.Lang.Object, View.IOnTouchListener
    {
        public bool OnTouch(View? v, MotionEvent? e) => false;
    }
}
