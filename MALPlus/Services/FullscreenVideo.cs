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
    ///     Go landscape and hide the bars.
    /// </summary>
    /// <remarks>
    ///     Nothing is saved here, and nothing needs to be: <see cref="Exit" /> asks
    ///     <see cref="OrientationPolicy" /> what the app should be, which knows whether this is a
    ///     phone that has to stay portrait or a tablet that is free. The earlier version of this saved
    ///     the activity's orientation and there was nothing to save, because the activity never set
    ///     one: it inherited Unspecified from the manifest, so "restore what we found" restored
    ///     nothing and the app stayed landscape after the overlay closed.
    /// </remarks>
    public static void Enter()
    {
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity == null)
                return;

            // The lock stops applying before the rotation is asked for, and not after. MainActivity
            // re-applies the portrait lock from OnConfigurationChanged, and the rotation below is what
            // raises that: lock on, rotate to landscape, lock pulls it straight back to portrait,
            // WebView reads the second rotation as the video leaving full screen and the picture
            // closes itself the moment it opens.
            OrientationPolicy.Suspended = true;

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
    ///     Leave full screen and hand the orientation back to what the app should be.
    /// </summary>
    /// <remarks>
    ///     It used to hand it back to the sensor with Unspecified, which was right while nothing
    ///     locked the phone, and wrong the moment something did: Unspecified is the platform's own
    ///     "free to rotate", so a portrait locked phone that played one video came out of it unlocked
    ///     for the rest of the session. Asking the policy is what makes a phone portrait again and
    ///     still leaves a tablet free, which is why this does not just force Portrait.
    /// </remarks>
    public static void Exit()
    {
        // Released before the IsActive check, not inside it. Enter can suspend the portrait lock and
        // then return without ever reaching IsActive = true, and it can do the same by throwing: an
        // early return for a null window is the ordinary case, not an edge case. Gating the release on
        // IsActive left the lock suspended with no flag left to reset it, which is a phone that never
        // locks sideways again for the rest of the session.
        var wasSuspended = OrientationPolicy.Suspended;
        OrientationPolicy.Suspended = false;

        if (!IsActive)
        {
            // Nothing else to undo, but the orientation still has to come back if the lock was the
            // only thing that had moved it.
            if (wasSuspended)
                RestoreDesiredOrientation();
            return;
        }

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

            // The lock starts applying again BEFORE the orientation is handed back, so the rotation to
            // portrait it causes cannot be undone by a later configuration change.
            OrientationPolicy.Suspended = false;
            RestoreDesiredOrientation();

            // The overlays paint the bars themselves on close, so the colour is left to them; all
            // that matters here is that they are visible again.
            IsActive = false;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "Fullscreen exit failed: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Puts the activity back on whatever <see cref="OrientationPolicy" /> wants: portrait on a
    ///     phone, free on a tablet.
    /// </summary>
    /// <remarks>
    ///     Not Unspecified. That is the platform's own "free to rotate", so restoring it here would
    ///     quietly unlock the phone for the rest of the session, once for every video watched.
    /// </remarks>
    private static void RestoreDesiredOrientation()
    {
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity == null)
                return;
            activity.RequestedOrientation = OrientationPolicy.Desired(activity.Resources?.Configuration);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "orientation restore failed: " + ex.GetType().Name);
        }
    }
}
