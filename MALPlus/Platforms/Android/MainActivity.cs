using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Microsoft.Maui;

namespace MALPlus;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true, LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(new[] { Microsoft.Maui.ApplicationModel.Platform.Intent.ActionAppAction },
    Categories = new[] { Android.Content.Intent.CategoryDefault })]
[IntentFilter(new[] { Android.Content.Intent.ActionView },
    Categories = new[] { Android.Content.Intent.CategoryDefault, Android.Content.Intent.CategoryBrowsable },
    DataScheme = "malplus")]
public class MainActivity : MauiAppCompatActivity
{
    public static Intent DeepLinkIntent { get; private set; }
    public static string PendingDeepLinkPath { get; private set; }

/// <summary>
    ///     The callback currently sitting in front of the Shell's, if any. Static because the pages
    ///     that open a video only ever have the activity to hand, never this object.
    /// </summary>
    private static VideoOverlayBackCallback _armedBackCallback;

    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Android.Util.Log.Info("MALPLUS", $"OnCreate intent data: {Intent?.Data}");
        if (Intent?.Data != null && Intent.Data.ToString().StartsWith("malplus://"))
        {
            DeepLinkIntent = Intent;
            PendingDeepLinkPath = Intent.Data.ToString().Replace("malplus://", "");
        }

        // An open video overlay has to be the first thing the back gesture meets, otherwise back
        // pops the navigation stack and leaves the player behind.
        //
        // This cannot live in an OnBackPressed override. Verified on device: with predictive back
        // the platform routes through OnBackPressedDispatcher and Activity.OnBackPressed is never
        // called at all, which is why pressing back used to close the whole details page instead of
        // just the video. Registering here does not work either: the dispatcher runs the most
        // recently added callback FIRST and the Shell adds its own after OnCreate returns, so one
        // added here sits behind the Shell's and never sees the press, which is also what was
        // measured. ArmVideoBackCallback adds it at the moment an overlay opens instead.
    }

    /// <summary>
    ///     Puts the video overlay in front of the back gesture, for as long as one is open.
    /// </summary>
    /// <remarks>
    ///     Re-adding is the whole point: OnBackPressedDispatcher runs the most recently added
    ///     callback first, so registering while the overlay is opening is the only moment at which
    ///     this callback is guaranteed to be ahead of the Shell's. The previous one is only disabled
    ///     rather than removed, because this binding of AndroidX.Activity exposes addCallback but
    ///     not removeCallback; the dispatcher skips disabled callbacks, so that is equivalent apart
    ///     from the dead entry, which costs nothing on a press.
    /// </remarks>
    public static void ArmVideoBackCallback()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity as MainActivity;
        if (activity == null)
            return;
        DisarmVideoBackCallback();
        _armedBackCallback = new VideoOverlayBackCallback(activity);
        activity.OnBackPressedDispatcher.AddCallback(activity, _armedBackCallback);
    }

    /// <summary>
    ///     Takes the overlay back out of the back gesture once it is closed, so the next press
    ///     navigates instead of being handed to a callback with nothing to do.
    /// </summary>
    public static void DisarmVideoBackCallback()
    {
        if (_armedBackCallback == null)
            return;
        _armedBackCallback.Enabled = false;
        _armedBackCallback = null;
    }

        /// <summary>
        ///     Gives the video overlay first refusal on the back gesture. If nothing claims the
        ///     press it stands itself down for that one press so the next callback, the Shell's,
        ///     does the navigating, and then re-enables itself so it can claim a later press.
        /// </summary>
        private sealed class VideoOverlayBackCallback : AndroidX.Activity.OnBackPressedCallback
        {
            private readonly MainActivity _owner;

            public VideoOverlayBackCallback(MainActivity owner) : base(enabled: true) => _owner = owner;

public override void HandleOnBackPressed()
            {
                try
                {
                    if (Services.VideoWebViewHelper.BackHandler?.Invoke() == true)
                    {
                            return;
                    }
                }
                catch (Exception ex)
                {
                    Android.Util.Log.Warn("MALPLUS", "Video back callback failed: " + ex.GetType().Name);
                }

                // Left disabled rather than re-enabled: the overlay is closed by now, and a stale
                // callback sitting on top of the dispatcher would keep intercepting later presses
                // just to forward them. Arming again makes a fresh one.
                Enabled = false;
                _armedBackCallback = null;
                _owner.OnBackPressedDispatcher.OnBackPressed();
            }
        }

        /// <summary>
        ///     Kept only as a fallback for platforms and manifest settings where the dispatcher is
        ///     not in charge and the system still calls this. On Android 13+ with predictive back it
        ///     never runs, and <see cref="VideoOverlayBackCallback"/> is what actually gets the
        ///     gesture.
        /// </summary>
        public override void OnBackPressed()
        {
            // An open video overlay gets first refusal: the back gesture must drop the player and
            // leave the user on the same page instead of popping the navigation stack.
            try
            {
                if (Services.VideoWebViewHelper.BackHandler?.Invoke() == true)
                    return;
            }
            catch (Exception ex)
            {
                Android.Util.Log.Warn("MALPLUS", "BackHandler failed: " + ex.GetType().Name);
            }

            base.OnBackPressed();
        }

        protected override void OnNewIntent(Intent intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        Android.Util.Log.Info("MALPLUS", $"OnNewIntent data: {intent?.Data}");
        if (intent?.Data != null && intent.Data.ToString().StartsWith("malplus://"))
        {
            DeepLinkIntent = intent;
            PendingDeepLinkPath = intent.Data.ToString().Replace("malplus://", "");
            HandleDeepLinkNavigation(intent.Data.ToString());
        }
    }

    public static async Task ApplyPendingDeepLinkAsync()
    {
        var path = PendingDeepLinkPath;
        if (string.IsNullOrWhiteSpace(path))
            return;
        Android.Util.Log.Info("MALPLUS", $"Applying pending deep link: {path}");
        var succeeded = await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await Shell.Current.GoToAsync(path);
                Android.Util.Log.Info("MALPLUS", "Pending deep link success");
                return true;
            }
            catch (Exception ex)
            {
                Android.Util.Log.Error("MALPLUS", $"Pending deep link failed: {ex}");
                return false;
            }
        });
        // Only clear on success: a link dropped while Shell was still building
        // would otherwise be lost with no way to retry.
        if (succeeded)
            PendingDeepLinkPath = null;
    }

    private void HandleDeepLinkNavigation(string uri)
    {
        if (uri.StartsWith("malplus://"))
        {
            var path = uri.Replace("malplus://", "");
            Android.Util.Log.Info("MALPLUS", $"Deep link path: {path}");
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    if (Shell.Current != null)
                    {
                        try
                        {
                            Android.Util.Log.Info("MALPLUS", $"GoToAsync: {path}");
                            await Shell.Current.GoToAsync(path);
                            Android.Util.Log.Info("MALPLUS", "GoToAsync success");
                            return;
                        }
                            catch (Exception ex)
                            {
                                Android.Util.Log.Error("MALPLUS", $"Deep link failed: {ex}");
                                return;
                            }
                    }
                    Android.Util.Log.Info("MALPLUS", $"Shell.Current null, retry {attempt + 1}/20");
                    await System.Threading.Tasks.Task.Delay(250);
                }
                Android.Util.Log.Info("MALPLUS", "Shell.Current never became ready (20 retries)");
            });
        }
    }
}
