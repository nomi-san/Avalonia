using System;

namespace AndroidNativeOverlay;

/// <summary>
/// Static service for cross-view communication between the main Avalonia view
/// and the Android activity that manages the overlay and native GL layers.
/// </summary>
public static class OverlayService
{
    /// <summary>
    /// Raised when the overlay visibility should be toggled.
    /// </summary>
    public static event Action? ToggleOverlayRequested;

    /// <summary>
    /// Raised when the overlay should switch between interactive and click-through modes.
    /// </summary>
    public static event Action? ToggleClickThroughRequested;

    /// <summary>
    /// Raised when the overlay requests to be closed.
    /// </summary>
    public static event Action? CloseOverlayRequested;

    public static void RequestToggleOverlay() => ToggleOverlayRequested?.Invoke();

    public static void RequestToggleClickThrough() => ToggleClickThroughRequested?.Invoke();

    public static void RequestCloseOverlay() => CloseOverlayRequested?.Invoke();
}
