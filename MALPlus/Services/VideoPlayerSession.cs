using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace MALPlus.Services;

/// <summary>
///     The lifecycle of one video surface: configure it, open something in it, and make sure that
///     closing it really closes it.
/// </summary>
/// <remarks>
///     <para>
///     This used to be the same eleven steps copied into five pages, and the copies drifted apart.
///     That is not hypothetical: the season themes page configured the WebView through its
///     <c>HandlerChanged</c> event and so got a working full screen button, while the random theme page
///     configured it once from <c>OnAppearing</c> and so usually configured it too early, when the
///     handler did not exist yet. Configuration returns silently on a null view, so the page carried
///     on with a WebView that had no chrome client, and the player's own full screen button had nobody
///     to answer it. Four of the five pages also did not stop the media when the page disappeared, so
///     a WebView went on decoding behind a popped page and the next visit was slower than the last.
///     </para>
///     <para>
///     Full screen needs no per page work at all. It is answered by the shared WebChromeClient inside
///     <see cref="VideoWebViewHelper" />, so every surface built through this class gets it the same
///     way a native one would.
///     </para>
/// </remarks>
public sealed class VideoPlayerSession : IDisposable
{
    private readonly Func<bool> _onCloseRequested;
    private readonly WebView _webView;
    private bool _attached;
    private bool _closed;
    private global::System.Diagnostics.Stopwatch _openedAt;
    private Action<string> _errorHandler;
    private Action<string> _startedHandler;

    // The playback events are static, so two live sessions would both answer for one file and both
    // decide to retry it. Whoever attaches last owns them and the previous owner is let go first.
    private static Action<string> _ownedError;
    private static Action<string> _ownedStarted;

    /// <param name="webView">The page's own WebView, declared in its XAML.</param>
    /// <param name="onCloseRequested">
    ///     Runs when the back gesture reaches an open video. It returns true when it handled the
    ///     press, which is what keeps the gesture from also popping the page.
    /// </param>
    public VideoPlayerSession(WebView webView, Func<bool> onCloseRequested)
    {
        _webView = webView;
        _onCloseRequested = onCloseRequested;
    }

    /// <summary>
    ///     Builds a session around a WebView it creates itself, for overlays that only need one when
    ///     something is actually played.
    /// </summary>
    /// <remarks>
    ///     Declaring a WebView in a page's XAML makes MAUI build a handler for it whether or not a
    ///     video was ever opened, and an Android WebView spawns a Chromium child process to do it.
    ///     Creating it on demand is what keeps an unplayed page cheap.
    ///     <para>
    ///     It goes in at index 0 rather than at the end. The overlay's close button is declared in the
    ///     XAML, so a child added last would be drawn over it and would take the taps: the button stayed
    ///     visible and did nothing, which reads as a disabled button rather than as a covered one.
    ///     </para>
    /// </remarks>
    public static VideoPlayerSession CreateInto(Grid container, Func<bool> onCloseRequested)
    {
        if (container == null)
            return null;

        var webView = new WebView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.Black
        };

#if ANDROID
        try
        {
            webView.ZIndex = 0;
        }
        catch
        {
        }
#endif

        container.Insert(0, webView);
        return new VideoPlayerSession(webView, onCloseRequested);
    }

    /// <summary>The WebView this session drives, for the pages that need to measure or inspect it.</summary>
    public WebView View => _webView;

    /// <summary>
    ///     Configures the WebView, now and again if its handler is replaced.
    /// </summary>
    /// <remarks>
    ///     Both, and the second one is the one that matters. MAUI attaches the handler at its own
    ///     moment, which for a page's own XAML WebView is usually after <c>OnAppearing</c> has already
    ///     run, and <see cref="VideoWebViewHelper.ConfigurePlatformView" /> does nothing at all when
    ///     handed a null view rather than complaining. Configuring once from OnAppearing is therefore a
    ///     race that loses silently; doing it here and from the event means the surface is configured
    ///     whichever way the timing falls.
    /// </remarks>
    public void Attach()
    {
        if (_webView == null || _attached)
            return;
        _attached = true;

#if ANDROID
        _webView.HandlerChanged += OnHandlerChanged;
#endif
        Configure();
    }

    private void OnHandlerChanged(object sender, EventArgs e) => Configure();

    private void Configure()
    {
#if ANDROID
        try
        {
            VideoWebViewHelper.ConfigurePlatformView(_webView?.Handler?.PlatformView as global::Android.Views.View);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "session configure: " + ex.GetType().Name);
        }
#endif
    }

    /// <summary>
    ///     Plays a URL, and puts the overlay's back gesture in front of the navigation one.
    /// </summary>
    public void Open(string url)
    {
        if (_webView == null || string.IsNullOrWhiteSpace(url))
            return;

        _closed = false;
        _openedAt = global::System.Diagnostics.Stopwatch.StartNew();

#if ANDROID
        try
        {
            VideoWebViewHelper.Resume(_webView.Handler?.PlatformView as global::Android.Views.View);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "session resume: " + ex.GetType().Name);
        }
#endif

        _webView.Source = VideoWebViewHelper.BuildSource(url);
        VideoWebViewHelper.ApplyMediaSettings(_webView);

        // Registered here rather than once at startup: the back dispatcher runs the most recently
        // added callback first, and the Shell adds its own after MainActivity.OnCreate, so one added
        // back then never sees the press.
        VideoWebViewHelper.BackHandler = HandleBack;
        MainActivity.ArmVideoBackCallback();

        SetSystemBars(true);
    }

    private bool HandleBack() => _onCloseRequested != null && _onCloseRequested();

    /// <summary>
    ///     Ends playback and lets go of everything <see cref="Open" /> took.
    /// </summary>
    /// <remarks>
    ///     Called both by the close button and from OnDisappearing, and the second one is not optional:
    ///     a page can be left by the bottom navigation or a deep link without ever reaching its close
    ///     button, and a WebView left running behind it keeps a video decoding and the audio playing
    ///     for as long as the process lives.
    ///     <para>
    ///     Setting Source to null is not enough on its own. That unloads the document and leaves the
    ///     media element running, so the overlay went away and the audio carried on.
    ///     </para>
    /// </remarks>
    public void Close()
    {
        if (_webView == null)
            return;
        _closed = true;
        _openedAt = null;

        // Leaving full screen belongs to closing: an overlay that went away while the activity was
        // still landscape left the whole app sideways.
        FullscreenVideo.Exit();

        VideoWebViewHelper.BackHandler = null;
        MainActivity.DisarmVideoBackCallback();

        VideoWebViewHelper.StopMediaAndClear(_webView);
        SetSystemBars(false);
    }

    /// <summary>True while something is loaded, which is what "the overlay is open" means here.</summary>
    public bool IsPlaying => _webView != null && !_closed;

    /// <summary>
    ///     Routes a refused file to the page, which owns the retry policy.
    /// </summary>
    /// <remarks>
    ///     Only the newest subscriber is kept. These are static events, so a page that is still in the
    ///     navigation stack underneath would otherwise get told about a file it is not showing and
    ///     would queue a retry for it.
    /// </remarks>
    public void OnPlaybackError(Action<string> handler)
    {
        if (_errorHandler != null)
            VideoWebViewHelper.PlaybackError -= _errorHandler;
        _errorHandler = handler;

        if (_ownedError != null)
            VideoWebViewHelper.PlaybackError -= _ownedError;
        _ownedError = handler;
        if (handler != null)
            VideoWebViewHelper.PlaybackError += handler;
    }

    /// <inheritdoc cref="OnPlaybackError" />
    public void OnPlaybackStarted(Action<string> handler)
    {
        if (_startedHandler != null)
            VideoWebViewHelper.PlaybackStarted -= _startedHandler;
        _startedHandler = handler;

        if (_ownedStarted != null)
            VideoWebViewHelper.PlaybackStarted -= _ownedStarted;

        if (handler == null)
        {
            _ownedStarted = null;
            return;
        }

        // Wrapped rather than subscribed as it is, so the time from Open to the first decoded frame
        // can be reported. The watchdog inside the player html reports a refusal at eight seconds and
        // the page then queues a retry, so a slow open and a refused one look identical from outside;
        // this is what tells them apart.
        void Wrapped(string url)
        {
            if (_openedAt != null)
            {
                global::Android.Util.Log.Info("MALPlusVideoOpen", $"{_openedAt.ElapsedMilliseconds}ms {url}");
                _openedAt = null;
            }
            handler(url);
        }

        _ownedStarted = Wrapped;
        VideoWebViewHelper.PlaybackStarted += Wrapped;
    }

    /// <summary>
    ///     Colours the system bars for the overlay: black while a video is up, the page's own shade
    ///     afterwards.
    /// </summary>
    /// <remarks>
    ///     This was a private copy in two pages doing exactly this, and the shade it restores is the
    ///     Discover background every page in the app sits on.
    /// </remarks>
    private static void SetSystemBars(bool video)
    {
#if ANDROID
        try
        {
            var window = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window;
            if (window == null)
                return;
            window.SetStatusBarColor(video
                ? global::Android.Graphics.Color.Black
                : global::Android.Graphics.Color.ParseColor("#051522"));
            window.SetNavigationBarColor(global::Android.Graphics.Color.Black);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "system bars: " + ex.GetType().Name);
        }
#endif
    }

    /// <summary>
    ///     Closes the session for good and drops the static subscriptions with it.
    /// </summary>
    public void Dispose()
    {
        if (_closed)
            return;
        Close();

#if ANDROID
        if (_webView != null)
            _webView.HandlerChanged -= OnHandlerChanged;
#endif

        OnPlaybackError(null);
        OnPlaybackStarted(null);
        _attached = false;
    }
}
