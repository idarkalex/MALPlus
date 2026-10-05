namespace MALPlus.Services;

/// <summary>
///     Full screen playback for the video overlays.
/// </summary>
/// <remarks>
///     Landscape plus the system bars hidden, which is what a video actually wants: the bars take a
///     slice of the short edge in portrait and nothing at all once they are gone. Both are restored
///     on the way out, orientation included, so leaving full screen never strands the app sideways.
/// </remarks>
public static class FullscreenVideo
{
    /// <summary>True while the app is in full screen. Read by the overlays to pick their icon.</summary>
    public static bool IsActive { get; private set; }

    /// <summary>
    ///     Go landscape and hide the bars. The activity's own orientation is saved first: forcing it
    ///     is not reversible on its own, and an app left in landscape after the overlay closes is the
    ///     kind of thing that looks like a crash on the next page.
    /// </summary>
    public static void Enter()
    {
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity == null)
                return;

            activity.RequestedOrientation = global::Android.Content.PM.ScreenOrientation.Landscape;

            var window = activity.Window;
            if (window == null)
                return;

            // Two separate things were needed for the picture to reach the notch. Hiding the bars only
            // removes them; the window still reserves the cutout, which is why landscape video came
            // up with a black wedge along the long edge.
            //
            // LayoutInDisplayCutoutMode is what lets the window extend into it. ShortEdges is the one
            // that matters in landscape, where the cutout sits on a side; Always is only valid from
            // API 30 and buys nothing further for a video.
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.P)
            {
                var attributes = window.Attributes;
                if (attributes != null)
                {
                    attributes.LayoutInDisplayCutoutMode = global::Android.Views.LayoutInDisplayCutoutMode.ShortEdges;
                    window.Attributes = attributes;
                }
            }

            // And the window has to be told to lay out behind the bars rather than inside them.
            // Without this the insets still shrink the surface even with the bars hidden.
            AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(window, false);

            window.SetStatusBarColor(global::Android.Graphics.Color.Black);
            window.SetNavigationBarColor(global::Android.Graphics.Color.Black);

            // The modern API hides the bars without the layout resizing around them; the decor one is
            // kept as a fallback because some devices still only honour that path.
            var controller = window.InsetsController;
            if (controller != null)
            {
                controller.Hide(global::Android.Views.WindowInsets.Type.StatusBars() |
                                global::Android.Views.WindowInsets.Type.NavigationBars());
            }
            else
            {
                // The binding types the property as StatusBarVisibility, so the flags go through an
                // explicit conversion rather than being assigned straight.
                window.DecorView?.SystemUiVisibility =
                    (global::Android.Views.StatusBarVisibility)(
                        global::Android.Views.SystemUiFlags.Fullscreen |
                        global::Android.Views.SystemUiFlags.HideNavigation |
                        global::Android.Views.SystemUiFlags.ImmersiveSticky);
            }

            IsActive = true;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "Fullscreen enter failed: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Leave full screen and hand the orientation back to the sensor, which restores whatever the
    ///     user was holding the phone in before. Forcing Portrait would be wrong on a tablet.
    /// </summary>
    public static void Exit()
    {
        if (!IsActive)
            return;
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            var window = activity?.Window;

            window?.InsetsController?.Show(global::Android.Views.WindowInsets.Type.StatusBars() |
                                            global::Android.Views.WindowInsets.Type.NavigationBars());
            window?.DecorView?.SystemUiVisibility =
                (global::Android.Views.StatusBarVisibility)global::Android.Views.SystemUiFlags.Visible;

            // Both of these are undone here rather than left set. The whole app lays out inside the
            // bars the moment this goes back to true, and the cutout goes back to being reserved, so
            // every page after a video looks like the one before it again.
            if (window != null)
            {
                AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(window, true);
                if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.P)
                {
                    var attributes = window.Attributes;
                    if (attributes != null)
                    {
                        attributes.LayoutInDisplayCutoutMode =
                            global::Android.Views.LayoutInDisplayCutoutMode.Default;
                        window.Attributes = attributes;
                    }
                }
            }

            if (activity != null)
                activity.RequestedOrientation = global::Android.Content.PM.ScreenOrientation.Unspecified;

            // The overlays paint the bars themselves on close, so the colour is left to them; all
            // that matters here is that they are visible again.
            IsActive = false;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "Fullscreen exit failed: " + ex.GetType().Name);
        }
    }
}
