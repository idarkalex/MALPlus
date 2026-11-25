namespace MALPlus.Services;

/// <summary>
///     Which way up the app is allowed to be, per device.
/// </summary>
/// <remarks>
///     <para>
///     A phone is held in one hand and every screen in here was drawn for that: a poster grid at three
///     across, a details hero 460 tall, a five row Discover grid. Letting it rotate put all of that
///     into landscape, where the grid cells become as wide as the screen and half as tall, and the
///     capsule buttons stretch. A tablet is a different object and none of that applies, so it keeps
///     rotating.
///     </para>
///     <para>
///     This is decided at runtime on purpose rather than with android:screenOrientation="portrait" in
///     the manifest. From API 36 the platform ignores an orientation lock, whether it came from the
///     manifest or from RequestedOrientation, on any display whose smallest width is 600dp or more,
///     and the opt out that used to cover that was removed in API 37, which is what this app targets.
///     A manifest portrait would therefore lock phones and silently do nothing on new tablets, and
///     the condition cannot even be written there: the manifest cannot ask about the device. Asking
///     at runtime works on both, and on a recent tablet the two mechanisms agree anyway because the
///     platform is ignoring us either way.
///     </para>
/// </remarks>
public static class OrientationPolicy
{
    /// <summary>
    ///     A window narrower or shorter than this is a phone in some posture, and stays portrait.
    ///     600dp is the platform's own breakpoint between compact and medium width, and it is the
    ///     smallest width rather than the width alone, so a landscape phone counts as compact too.
    /// </summary>
    private const int CompactDp = 600;

    /// <summary>
    ///     True while a video is in full screen, which is the one moment the app is allowed to leave
    ///     portrait on a phone.
    /// </summary>
    public static bool Suspended { get; internal set; }

    /// <summary>
    ///     True while any video overlay is active.
    /// </summary>
    public static bool IsVideoActive { get; set; }

    /// <summary>
    ///     Updates video active state, starts/stops physical orientation tracking, and re-applies policy.
    /// </summary>
    public static void SetVideoActive(bool active)
    {
        IsVideoActive = active;
#if ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (active)
        {
            if (_orientationListener == null && activity != null)
                _orientationListener = new VideoOrientationListener(activity);

            _orientationListener?.Reset();
            if (_orientationListener?.CanDetectOrientation() == true)
                _orientationListener.Enable();
        }
        else
        {
            _orientationListener?.Disable();
        }
        if (activity != null)
            Apply(activity);
#endif
    }

    /// <summary>
    ///     What the activity should be asked for, given its current configuration. Exposed because
    ///     <see cref="FullscreenVideo" /> has to hand the orientation back to this rather than to the
    ///     sensor: a full screen video that ended by setting Unspecified left the phone unlocked for
    ///     the rest of the session.
    /// </summary>
    /// <param name="configuration">The activity's current configuration.</param>
    /// <param name="videoFullscreen">
    ///     Landscape is requested when a video is full screen, on any device. A phone plays its videos
    ///     sideways because that is what a video is for, and the portrait lock is about how the rest
    ///     of the app is read rather than about this one surface.
    /// </param>
#if ANDROID
    public static global::Android.Content.PM.ScreenOrientation Desired(
        global::Android.Content.Res.Configuration configuration, bool videoFullscreen = false)
    {
        if (videoFullscreen || FullscreenVideo.IsActive)
            return global::Android.Content.PM.ScreenOrientation.SensorLandscape;

        if (configuration == null)
            return global::Android.Content.PM.ScreenOrientation.Portrait;

        return IsCompact(configuration)
            ? global::Android.Content.PM.ScreenOrientation.Portrait
            : global::Android.Content.PM.ScreenOrientation.FullUser;
    }

    /// <summary>
    ///     True when either edge of the window is under the compact breakpoint.
    /// </summary>
    /// <remarks>
    ///     The window, not the device. A tablet in split screen has a window under 600dp and should
    ///     behave like the phone-sized thing it currently is; the device's smallest width would let it
    ///     rotate inside a window too small to lay out.
    /// </remarks>
    public static bool IsCompact(global::Android.Content.Res.Configuration configuration) =>
        configuration.ScreenWidthDp < CompactDp || configuration.ScreenHeightDp < CompactDp;

    /// <summary>
    ///     Puts the activity in the orientation this policy asks for. A no-op when it already is,
    ///     because assigning RequestedOrientation starts a configuration change even when the value
    ///     does not move, and OnConfigurationChanged calls straight back into here.
    /// </summary>
    public static void Apply(global::Android.App.Activity activity)
    {
        if (activity == null)
            return;

        // A video holds the orientation while it plays. Re-applying the lock here would rotate a phone
        // back to portrait the moment it went landscape, and WebView takes that as the video leaving
        // full screen, so the picture would close itself the moment it opened.
        if (Suspended)
            return;

        try
        {
            var desired = Desired(activity.Resources?.Configuration);
            if (activity.RequestedOrientation == desired)
                return;
            activity.RequestedOrientation = desired;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus", "orientation policy failed: " + ex.GetType().Name);
        }
    }
#endif

#if ANDROID
    private static VideoOrientationListener _orientationListener;

    private sealed class VideoOrientationListener : global::Android.Views.OrientationEventListener
    {
        private bool? _wasLandscape;
        private long _lastTransitionTime;

        public VideoOrientationListener(global::Android.Content.Context context)
            : base(context, global::Android.Hardware.SensorDelay.Ui)
        {
        }

        public void Reset()
        {
            _wasLandscape = null;
            _lastTransitionTime = 0;
        }

        public override void OnOrientationChanged(int orientation)
        {
            if (orientation == OrientationUnknown || !IsVideoActive)
                return;

            // Landscape posture: right landscape 60°..120° or left landscape 240°..300°
            bool isLandscape = (orientation >= 60 && orientation <= 120) ||
                               (orientation >= 240 && orientation <= 300);

            // Portrait posture: upright 330°..360°/0°..30° or upside down 150°..210°
            bool isPortrait = (orientation >= 330 || orientation <= 30) ||
                              (orientation >= 150 && orientation <= 210);

            if (!isLandscape && !isPortrait)
                return; // Transitional angle, do not switch yet

            if (_wasLandscape.HasValue && _wasLandscape.Value == isLandscape)
                return; // Posture hasn't changed

            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_wasLandscape.HasValue &&
                (now - _lastTransitionTime) < (System.Diagnostics.Stopwatch.Frequency * 350 / 1000))
            {
                return; // Debounce rapid tilts
            }

            _wasLandscape = isLandscape;
            _lastTransitionTime = now;

            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!IsVideoActive)
                    return;

                if (isLandscape)
                {
                    if (!FullscreenVideo.IsActive)
                    {
                        if (VideoWebViewHelper.ActivePlatformWebView != null)
                        {
                            global::Android.Util.Log.Info("MALPlus Video", "Rotation triggering RequestWebViewFullscreen");
                            VideoWebViewHelper.RequestWebViewFullscreen();
                        }
                        else
                        {
                            global::Android.Util.Log.Info("MALPlus Video", "Rotation entering ExoPlayer fullscreen");
                            FullscreenVideo.Enter();
                        }
                    }
                }
                else
                {
                    if (FullscreenVideo.IsActive)
                    {
                        global::Android.Util.Log.Info("MALPlus Video", "Rotation triggered FullscreenVideo.Exit()");
                        if (!VideoWebViewHelper.TryExitCustomView())
                        {
                            FullscreenVideo.Exit();
                        }
                    }
                }
            });
        }
    }
#endif
}
