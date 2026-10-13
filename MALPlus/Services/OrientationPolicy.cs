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
    /// <remarks>
    ///     Without this the two features cancel each other out. A landscape video rotates the window,
    ///     which raises a configuration change, which re-applies the portrait lock, which rotates it
    ///     straight back, which WebView reports as the video leaving full screen: the picture closes
    ///     itself the instant it opens. The lock and the full screen video were each correct on their
    ///     own and fought each other through this callback.
    /// </remarks>
    public static bool Suspended { get; internal set; }

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
    public static global::Android.Content.PM.ScreenOrientation Desired(
        global::Android.Content.Res.Configuration configuration, bool videoFullscreen = false)
    {
        if (videoFullscreen)
            return global::Android.Content.PM.ScreenOrientation.Landscape;

        if (configuration == null)
            return global::Android.Content.PM.ScreenOrientation.UserPortrait;

        return IsCompact(configuration)
            ? global::Android.Content.PM.ScreenOrientation.UserPortrait
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
}
