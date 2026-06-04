using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace AndroidNativeOverlay.Android;

[Application]
public class Application : AvaloniaAndroidApplication<App>
{
    protected Application(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }
}
