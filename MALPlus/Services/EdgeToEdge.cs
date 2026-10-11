using Android.App;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;
using Microsoft.Maui.ApplicationModel;
using AndroidX.Core.Graphics;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Controls;

namespace MALPlus.Services;

/// <summary>
///     Edge-to-edge helpers for the Android window.
/// </summary>
/// <remarks>
///     <para>
///     The app draws under the system bars and into the display cutout. That is what a video
///     overlay needs, and <c>windowLayoutInDisplayCutoutMode</c> in the manifest is only the
///     permission: something still has to tell the window to lay itself out behind the bars, or
///     every WindowInsets the app reads comes back as zero and the notch keeps a black band.
///     </para>
///     <para>
///     The cost is that ordinary pages now sit under the status bar, so they add their own top
///     inset through <see cref="ApplyTop" />. Without it every header is half hidden behind the
///     clock.
///     </para>
/// </remarks>
public static class WindowInsetsHelper
{
    private const int Api35 = 35;

    /// <summary>
    ///     Lets the window extend behind the bars and into the cutout. Safe to call repeatedly.
    /// </summary>
    public static void Enable(Activity activity)
    {
        if (activity == null)
            return;
        var window = activity.Window;
        if (window == null)
            return;

        WindowCompat.SetDecorFitsSystemWindows(window, false);

        if ((int)Build.VERSION.SdkInt >= (int)BuildVersionCodes.P)
        {
            var attributes = window.Attributes;
            if (attributes != null)
            {
                attributes.LayoutInDisplayCutoutMode =
                    global::Android.Views.LayoutInDisplayCutoutMode.ShortEdges;
                window.Attributes = attributes;
            }
        }

        // Android 15+ actually clears the bars via the platform EdgeToEdge call.
        // The extension method lives in AndroidX.Core.SplashScreen or similar - skip for now
        // as the manifest + SetDecorFitsSystemWindows is enough for pre-15.
    }

    /// <summary>
    ///     The real inset of the status bar plus any display cutout, in pixels.
    /// </summary>
    public static (int Top, int Bottom, int Left, int Right) Insets()
    {
        try
        {
            var activity = Platform.CurrentActivity;
            var view = activity?.Window?.DecorView;
            if (view == null)
                return (0, 0, 0, 0);

            var insets = view.RootWindowInsets;
            if (insets == null)
                return (0, 0, 0, 0);

            var bars = insets.GetInsets(global::Android.Views.WindowInsets.Type.SystemBars());
            var cutout = insets.DisplayCutout;

            var top = Math.Max(bars.Top, cutout?.SafeInsetTop ?? 0);
            var bottom = Math.Max(bars.Bottom, cutout?.SafeInsetBottom ?? 0);
            var left = Math.Max(bars.Left, cutout?.SafeInsetLeft ?? 0);
            var right = Math.Max(bars.Right, cutout?.SafeInsetRight ?? 0);
            return (top, bottom, left, right);
        }
        catch (Exception)
        {
            return (0, 0, 0, 0);
        }
    }

    /// <summary>
    ///     Places the two video overlay buttons clear of the notch and of the bars, in the given
    ///     orientation. Works with MAUI views (Border, Button, etc.) by getting their native
    ///     Android view from the handler.
    /// </summary>
    public static void ApplyTop(Microsoft.Maui.Controls.View left, Microsoft.Maui.Controls.View right, int baseMarginPx = 0)
    {
        if (left == null && right == null)
            return;
        try
        {
            var density = DeviceDisplay.MainDisplayInfo.Density;
            var marginBase = (int)Math.Round((baseMarginPx > 0 ? baseMarginPx : 10) * density);
            var (top, _, leftInset, rightInset) = Insets();

            // Get native Android views from MAUI handlers
            var nativeLeft = left?.Handler?.PlatformView as global::Android.Views.View;
            var nativeRight = right?.Handler?.PlatformView as global::Android.Views.View;

            if (nativeLeft != null)
                nativeLeft.LayoutParameters = UpdateMargin(nativeLeft.LayoutParameters, leftInset + marginBase, top + marginBase);
            if (nativeRight != null)
                nativeRight.LayoutParameters = UpdateMargin(nativeRight.LayoutParameters, rightInset + marginBase, top + marginBase, isRight: true);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "edge insets failed: " + ex.GetType().Name);
        }
    }

    private static ViewGroup.MarginLayoutParams UpdateMargin(ViewGroup.LayoutParams lp, int marginLeftOrRight, int marginTop, bool isRight = false)
    {
        ViewGroup.MarginLayoutParams mlp = lp as ViewGroup.MarginLayoutParams ?? new ViewGroup.MarginLayoutParams(lp);
        if (isRight)
        {
            mlp.TopMargin = marginTop;
            mlp.RightMargin = marginLeftOrRight;
        }
        else
        {
            mlp.TopMargin = marginTop;
            mlp.LeftMargin = marginLeftOrRight;
        }
        return mlp;
    }
}