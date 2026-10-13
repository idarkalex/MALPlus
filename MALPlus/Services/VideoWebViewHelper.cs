using System.Text.RegularExpressions;
using Android.Webkit;

namespace MALPlus.Services;

public static class VideoWebViewHelper
{
    // Never youtube.com: YouTube refuses to configure a player whose own origin is
    // youtube.com (error 152/153, onReady then onError in the same millisecond).
    // myanimelist.net is the v2 base and is known to work.
    private const string PlaybackBaseUrl = "https://myanimelist.net";

    private const string ChromeUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private static readonly HttpClient SearchClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    /// <summary>
    ///     Set by an open video overlay so the Android back gesture closes the player instead of
    ///     navigating away from the page underneath. Returns true when it consumed the back.
    ///     MAUI 7 has no Page.BackButtonPressed and a SwipeGestureRecognizer never sees the drag
    ///     because the WebView child consumes it, so MainActivity.OnBackPressed routes here.
    /// </summary>
    public static Func<bool> BackHandler { get; set; }

    public static void ConfigurePlatformView(global::Android.Views.View platformView)
    {
        if (platformView is not global::Android.Webkit.WebView wv) return;
        try
        {
            wv.Settings.JavaScriptEnabled = true;
            wv.Settings.DomStorageEnabled = true;
            wv.Settings.MediaPlaybackRequiresUserGesture = false;
            // YouTube needs third-party cookies to authenticate video streams (blocked by default on Android 13+)
            global::Android.Webkit.CookieManager.Instance.SetAcceptThirdPartyCookies(wv, true);
            wv.SetWebChromeClient(new LoggingWebChromeClient());
            // A client of our own, so a media element's error reaches the app. The stock client swallows
            // it and the overlay is left on a black splash with nothing able to answer, which is what made
            // a refusal look like a broken player instead of a file worth asking for again.
            wv.SetWebViewClient(new InlineVideoWebViewClient());
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "ConfigurePlatformView: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Must be called AFTER assigning <c>Source</c>. MAUI's WebView handler re-applies the
    ///     platform settings on every source update and puts
    ///     <c>MediaPlaybackRequiresUserGesture</c> back to true, which blocked the
    ///     <c>&lt;video&gt;</c> autoplay and left Chromium's grey placeholder up forever
    ///     (readyState 0, 0:00 / 0:00, no frames).
    /// </summary>
    public static void ApplyMediaSettings(global::Microsoft.Maui.Controls.WebView webView)
    {
        if (webView?.Handler?.PlatformView is not global::Android.Webkit.WebView wv) return;
        try
        {
            wv.Settings.MediaPlaybackRequiresUserGesture = false;
            wv.Settings.AllowFileAccessFromFileURLs = true;
            wv.Settings.AllowContentAccess = true;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "ApplyMediaSettings: " + ex.GetType().Name);
        }
    }

    /// <summary>
    ///     Silences and tears down the WebView's document.
    ///
    ///     Assigning Source = null on its own does NOT stop playback: the document keeps running
    ///     inside the WebView, which is why the audio survived closing the overlay, and a fast
    ///     close gesture raced the unload and won. So the media elements are paused first, the
    ///     page is pointed at about:blank to destroy the document rather than navigate it, and
    ///     loading is stopped. about:blank is also not a remote origin, so nothing is fetched on
    ///     the way out. Evaluating JS is asynchronous and nothing here waits on it: that is
    ///     deliberate, because blocking the close gesture on a round trip is what made the old
    ///     version feel unresponsive.
    /// </summary>
    public static void StopMediaAndClear(global::Microsoft.Maui.Controls.WebView webView)
    {
        if (webView?.Handler?.PlatformView is not global::Android.Webkit.WebView wv)
        {
            if (webView != null)
                webView.Source = null;
            return;
        }
        try
        {
            wv.EvaluateJavascript(
                "(function(){try{var m=document.querySelectorAll('video,audio');for(var i=0;i<m.length;i++){m[i].pause();m[i].src='';}}catch(e){}})()",
                null);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "StopMediaAndClear eval: " + ex.GetType().Name);
        }
        try
        {
            wv.StopLoading();
            wv.LoadUrl("about:blank");
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "StopMediaAndClear url: " + ex.GetType().Name);
        }
        webView.Source = null;
    }

    public static void Resume(global::Android.Views.View platformView)
    {
        if (platformView is not global::Android.Webkit.WebView wv) return;
        try { wv.OnResume(); } catch { }
    }

    /// <summary>
    ///     The tag the player page reports a failed or started file under, on its way to the console.
    /// </summary>
    /// <remarks>
    ///     The console is used rather than a JavaScript bridge because the chrome client that reads it is
    ///     already installed on every video WebView in the app, while AddJavaScriptInterface is not
    ///     surfaced by this binding at all. The line therefore reaches the app from whichever page is
    ///     playing, with no per page wiring for a new overlay to forget.
    /// </remarks>
    private const string ReportPrefix = "ATVIDEO:";

    /// <summary>
    ///     Raised when a theme reported that it could not play its file.
    /// </summary>
    public static event Action<string> PlaybackError;

    /// <summary>
    ///     Raised when a theme reported that it is playing, which is what ends a wait.
    /// </summary>
    public static event Action<string> PlaybackStarted;

    /// <summary>
    ///     Reports a theme that refused, so whoever is showing it can ask again.
    /// </summary>
    /// <remarks>
    ///     Raised by the watcher rather than by the event itself: a C# event can only be invoked from
    ///     inside the type that declares it, and the watcher is deliberately a separate type so it can be
    ///     shared by every overlay.
    /// </remarks>
    public static void ReportError(string url) => PlaybackError?.Invoke(url);

    /// <summary>
    ///     Reports a theme that is playing, which ends its retry accounting.
    /// </summary>
    public static void ReportStarted(string url) => PlaybackStarted?.Invoke(url);

    internal static void Report(string line)
    {
        try
        {
            if (string.IsNullOrEmpty(line) || !line.StartsWith(ReportPrefix, StringComparison.Ordinal))
                return;
            var payload = line.Substring(ReportPrefix.Length);
            var url = payload;
            var kind = 0;
            var dash = url.IndexOf('|');
            if (dash > 0)
            {
                kind = url[0] == 'e' ? 1 : 0;
                url = url.Substring(dash + 1);
            }

            if (kind == 1)
                PlaybackError?.Invoke(url);
            else
                PlaybackStarted?.Invoke(url);
        }
        catch
        {
        }
    }

    /// <summary>
    ///     When a theme was last asked for, used to spot one opened right after another.
    /// </summary>
    private static DateTime _lastRequestedUtc = DateTime.MinValue;

    /// <summary>
    ///     Smallest gap between two themes when they are being flipped through back to back.
    /// </summary>
    /// <remarks>
    ///     The origin lets a handful of requests through per IP and then refuses the rest until it
    ///     refills. Tapping down a list of themes spends them in a burst, because each one is a fresh
    ///     transfer on top of the one still being torn down, and the player is asking again before the
    ///     previous abort has finished landing.
    /// </remarks>
    private static readonly TimeSpan BurstGap = TimeSpan.FromMilliseconds(700);

    /// <summary>
    ///     The window in which a new theme counts as part of the same burst.
    /// </summary>
    /// <remarks>
    ///     Deliberately short, so the wait only ever applies to someone who is actually flipping. Anyone
    ///     who stops to watch, or who comes back minutes later, is never delayed by this.
    /// </remarks>
    private static readonly TimeSpan BurstWindow = TimeSpan.FromMilliseconds(900);

    /// <summary>
    ///     How long the caller must wait before asking for this theme, zero when it can go now.
    /// </summary>
    /// <remarks>
    ///     Not a fixed delay on every video: a single theme opened from a cold page must start at once,
    ///     and a delay there would be a delay on every entry into the app. This only returns a wait when
    ///     the previous theme was requested within <see cref="BurstWindow"/>, which is the case where
    ///     the origin is actually refusing.
    /// </remarks>
    public static TimeSpan BurstDelay()
    {
        var now = DateTime.UtcNow;
        var since = now - _lastRequestedUtc;
        _lastRequestedUtc = now;
        if (since > BurstWindow)
            return TimeSpan.Zero;
        var remaining = BurstGap - since;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    ///     Forgets the last request, so the next theme is never delayed by something from a previous
    ///     page or a previous minute.
    /// </summary>
    public static void ResetBurst() => _lastRequestedUtc = DateTime.MinValue;

    public static string ExtractYouTubeId(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var match = Regex.Match(url, @"(?:vnd\.youtube:|v=|/embed/|youtu\.be/)([A-Za-z0-9_\-]{6,})");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch { return null; }
    }

    private static bool IsSearchUrl(string url) =>
        !string.IsNullOrEmpty(url) && url.Contains("search_query=", StringComparison.OrdinalIgnoreCase);

    public static bool IsDirectMedia(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var lower = url.Split('?')[0].ToLowerInvariant();
        return lower.EndsWith(".webm") || lower.EndsWith(".mp4") || lower.EndsWith(".m4v")
               || lower.EndsWith(".ogg") || lower.EndsWith(".mp3") || lower.EndsWith(".m4a");
    }

    public static string ToEmbedUrl(string url, bool autoplay)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        try
        {
            var id = ExtractYouTubeId(url);
            if (!string.IsNullOrEmpty(id))
                return $"https://www.youtube.com/embed/{id}" + (autoplay ? "?autoplay=1" : "");
            return url;
        }
        catch { return url; }
    }

    /// <summary>
    /// Single entry point for every video overlay in the app. Returns the source to hand to
    /// a WebView, already routed to the right player (YouTube iframe or direct media element).
    /// </summary>
    public static HtmlWebViewSource BuildSource(string url, bool autoplay = true)
    {
        return new HtmlWebViewSource
        {
            Html = BuildPlayerHtml(url, autoplay),
            BaseUrl = PlaybackBaseUrl
        };
    }

    public static string BuildPlayerHtml(string url, bool autoplay = true)
    {
        if (string.IsNullOrWhiteSpace(url))
            return BuildPlayerShell("<p style='color:#d4e4f7;font-family:sans-serif;padding:24px'>No video</p>");

        if (IsDirectMedia(url))
            return BuildDirectMediaHtml(url, autoplay);

        if (IsSearchUrl(url))
        {
            // A search page is not a player. Show the results so the user can pick one
            // instead of handing the iframe a URL the YouTube player cannot boot.
            var match = Regex.Match(url, @"search_query=([^&]+)");
            var results = match.Success
                ? "https://www.youtube.com/results?search_query=" + match.Groups[1].Value
                : url;
            return BuildEmbedHtml(results);
        }

        return BuildEmbedHtml(ToEmbedUrl(url, autoplay));
    }

    public static string BuildEmbedHtml(string embedUrl)
    {
        if (!embedUrl.Contains("?")) embedUrl += "?enablejsapi=1";
        else embedUrl += "&enablejsapi=1";
        return BuildPlayerShell(
            "<iframe src='" + embedUrl + "' allow='autoplay;encrypted-media;fullscreen' allowfullscreen></iframe>",
            "<script>window.addEventListener('message',function(e){try{var d=e.data||{};if(typeof d==='string'){console.log('YTRAW '+d);}else if(d.event){console.log('YTEVT '+d.event+(d.info?JSON.stringify(d.info):''));}}catch(x){}});</script>");
    }

    /// <summary>
    ///     A direct media file, played by Chromium's own controls.
    /// </summary>
    /// <remarks>
    ///     This used to ship a hand written transport: a four pixel bar with no thumb, and a single
    ///     onclick to seek. That is why the player looked broken and was replaced by ExoPlayer — a touch
    ///     drag on a four pixel target either did nothing or jumped somewhere arbitrary, and the duration
    ///     read 0:00 until the metadata arrived because it was only written once v.duration existed.
    ///     <para>
    ///     None of that was a limit of the WebView. Native controls give the real thing: a seek bar with
    ///     a thumb that drags under the finger, the duration, volume and fullscreen, all handled by the
    ///     same engine that decodes the file. So this is now one element and no script, where before it
    ///     was an element plus two dozen lines that reimplemented it badly.
    ///     </para>
    /// </remarks>
    private static string BuildDirectMediaHtml(string url, bool autoplay)
    {
        var autoplayAttr = autoplay ? " autoplay" : "";
        return BuildPlayerShell(
            "<div id='splash'></div>" +
            "<div id='stage'><video id='video' src='" + url + "'" + autoplayAttr +
            " controls playsinline webkit-playsinline preload='auto'></video></div>",
            "<script>" +
            "var v=document.getElementById('video'),splash=document.getElementById('splash');" +
            "v.onplaying=function(){splash.style.opacity='0';};" +
            // Watchdog. The element's own error event is not enough: the origin refuses with a 503 and
            // an empty body, which Chromium does not always surface as an error on the element, so the
            // page sat on the splash with the app never told. If it has not started playing by now,
            // report it as failed so the app can ask again. A slow connection could trip this, which is
            // why the wait is generous and why a successful play cancels it.
            "var started=false,timer=setTimeout(function(){" +
            "if(!started){splash.style.opacity='0';console.log('ATVIDEO:e|'+v.currentSrc);}},8000);" +
            "v.addEventListener('playing',function(){started=true;clearTimeout(timer);},{once:true});" +
            "v.addEventListener('loadeddata',function(){started=true;clearTimeout(timer);},{once:true});" +
            // Chromium refuses to start a media element from an attribute alone when the document was
            // not opened by a gesture, and a rejected play() leaves the black splash up over the player
            // for good. Retrying gets it going without the reader having to tap twice.
            "function kick(){var p=v.play();if(p&&p.catch){p.catch(function(e){console.log('play-rejected '+e.name);setTimeout(function(){v.play().catch(function(){});},900);});}}" +
            "v.addEventListener('canplay',function(){kick();},{once:true});" +
            "v.addEventListener('loadedmetadata',function(){kick();},{once:true});" +
            // The element error still reports too, for when Chromium does raise it.
            "v.addEventListener('error',function(){splash.style.opacity='0';console.error('video-error code='+(v.error?v.error.code:-1)+' net='+v.networkState);" +
            "console.log('ATVIDEO:e|'+v.currentSrc);});" +
            "v.addEventListener('playing',function(){" +
            "console.log('ATVIDEO:o|'+v.currentSrc);},{once:true});" +
            "kick();" +
            // The splash is removed on a timer as well as on playback, so a file that never starts shows
            // the native controls over black rather than an unexplained black rectangle.
            "setTimeout(function(){splash.style.opacity='0';},6000);" +
            "</script>",
            directMedia: true);
    }

    /// <summary>Resolution spinner shown while we look for the OP/ED source.</summary>
    public static string BuildLoadingHtml()
    {
        return "<html><head><meta name='viewport' content='width=device-width,initial-scale=1'/><style>" +
               "@keyframes spin{to{transform:rotate(360deg)}}" +
               "body{margin:0;padding:0;background:#000;height:100%;overflow:hidden}" +
               "#s{position:absolute;top:50%;left:50%;width:44px;height:44px;margin:-22px 0 0 -22px;" +
               "border:4px solid rgba(255,255,255,0.2);border-top-color:#0066FF;border-radius:50%;animation:spin 1s linear infinite}" +
               "</style></head><body><div id='s'></div></body></html>";
    }

    private static string BuildPlayerShell(string body, string script = null, bool directMedia = false)
    {
        var videoStyle = directMedia
            // The stage fills the container and the element fills the stage. It used to stop 56px short
            // of the bottom "so Chromium's own control bar sits on its own strip", but the native
            // controls draw over the <video> element itself and never used that strip; with it, the
            // element was also sized by the file's intrinsic size (width/height auto, capped by
            // max-width/max-height), so a direct-media file rendered letterboxed with black down each
            // side instead of filling the box. object-fit:contain keeps the picture undistorted, so the
            // only letterboxing left is the unavoidable top/bottom one when the box is not the video's
            // aspect. The iframe branch below was already width/height 100% all along, which is why a
            // YouTube-sourced theme filled the box and an AnimeThemes one did not.
            ? "#stage{position:absolute;top:0;left:0;right:0;bottom:0;display:flex;align-items:center;justify-content:center;background:#000}" +
              "#video{width:100%;height:100%;max-width:100%;max-height:100%;object-fit:contain;background:#000}" +
              "#splash{position:absolute;top:0;left:0;width:100%;height:100%;background:#000;z-index:20;transition:opacity 0.3s;pointer-events:none}"
            : "iframe{position:absolute;top:0;left:0;width:100%;height:100%;border:none}";

        return "<html><head><meta name='viewport' content='width=device-width,initial-scale=1'/>" +
               "<style>*{box-sizing:border-box}" +
               "body{margin:0;padding:0;background:#000;overflow:hidden;font-family:sans-serif;user-select:none}" +
               videoStyle + "</style></head><body>" + body +
               (string.IsNullOrEmpty(script) ? "" : script) +
               "</body></html>";
    }

    /// <summary>
    /// Scrapes the first video id out of a YouTube search results page. Used as the
    /// secondary source when AnimeThemes has no usable match.
    /// </summary>
    public static async Task<string> SearchYouTubeVideoId(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        try
        {
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                "https://www.youtube.com/results?search_query=" + System.Net.WebUtility.UrlEncode(query));
            request.Headers.TryAddWithoutValidation("User-Agent", ChromeUserAgent);
            using var response = await SearchClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;
            var html = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(html)) return null;
            var match = Regex.Match(html, "\"videoId\":\"([A-Za-z0-9_\\-]{11})\"");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("MALPlus Video", "YouTube search failed: " + ex.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Keeps http(s) inside the overlay and rewrites the YouTube app hand-off schemes
    /// (vnd.youtube:, intent://) back into the embed player. Without this the WebView
    /// escapes to the YouTube app instead of playing in place.
    /// </summary>
    private sealed class InlineVideoWebViewClient : WebViewClient
    {
        public override bool ShouldOverrideUrlLoading(global::Android.Webkit.WebView view, IWebResourceRequest request)
        {
            var url = request?.Url?.ToString();
            if (string.IsNullOrEmpty(url)) return false;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return false;
            var id = ExtractYouTubeId(url);
            if (!string.IsNullOrEmpty(id))
            {
                view.LoadUrl($"https://www.youtube.com/embed/{id}?autoplay=1");
                return true;
            }
            return true;
        }
    }

    private sealed class LoggingWebChromeClient : WebChromeClient
    {
        public override bool OnConsoleMessage(ConsoleMessage consoleMessage)
        {
            var msg = consoleMessage?.Message();
            if (!string.IsNullOrWhiteSpace(msg))
            {
                global::Android.Util.Log.Info("MALPlus VideoOverlay", msg);
                Report(msg);
            }

            return base.OnConsoleMessage(consoleMessage);
        }

        // THE FULL SCREEN BUTTON THE PLAYER ALREADY DRAWS.
        //
        // Chromium draws a full screen control on its own because the html carries the `controls`
        // attribute, but WebView deliberately drops the request unless the host app implements these
        // two callbacks. AOSP WebChromeClient.java: "If this method is not overridden, WebView will
        // report to the web page it does not support fullscreen mode and will not honor the web page's
        // request to run in fullscreen mode." The Chromium WebView docs say the same thing. So the
        // button was always there and always did nothing, silently, and that was the whole bug.
        //
        // Implementing them here rather than per page is deliberate: this client is installed on
        // every video WebView in the app, so this is the one place that makes full screen work on the
        // details overlay, the videos page, the discovery overlay, the season themes page and the
        // random theme page, which has no button of its own at all.
        private global::Android.Views.View _fullscreenView;
        private ICustomViewCallback _fullscreenCallback;

        /// <summary>
        ///     The web content asked for full screen. WebView builds the surface and hands it over;
        ///     attaching it to the window is the app's half of the contract.
        /// </summary>
        public override void OnShowCustomView(global::Android.Views.View view, ICustomViewCallback callback)
        {
            base.OnShowCustomView(view, callback);

            try
            {
                if (view == null)
                    return;

                // A second request while one is already up: tell the page to stand down rather than
                // attaching a second surface on top of the first.
                //
                // OnCustomViewHidden is the only exit member the .NET Android binding exposes on
                // ICustomViewCallback; onRequestCustomViewExit and onCustomViewPositionChanged exist
                // in the Java interface and are simply not mapped, and asking for one is a compile
                // error. It is also the correct one: the surface is not going away, the page is being
                // told its request was refused, and WebView then raises OnHideCustomView by itself.
                if (_fullscreenView != null)
                {
                    callback?.OnCustomViewHidden();
                    return;
                }

                var decor = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?
                    .Window?.DecorView as global::Android.Views.ViewGroup;
                if (decor == null)
                    return;

                _fullscreenView = view;
                _fullscreenCallback = callback;

                decor.AddView(view, new global::Android.Views.ViewGroup.LayoutParams(
                    global::Android.Views.ViewGroup.LayoutParams.MatchParent,
                    global::Android.Views.ViewGroup.LayoutParams.MatchParent));

                // Landscape, bars hidden and the cutout released: the whole of what a video wants,
                // and already handled for the three overlays that used to carry a button of our own.
                FullscreenVideo.Enter();
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn("MALPlus Video", "fullscreen enter failed: " + ex.GetType().Name);
            }
        }

        /// <summary>
        ///     The web content left full screen, whoever asked. WebView calls this on its own when the
        ///     player closes it, so the teardown does not depend on us noticing first.
        /// </summary>
        public override void OnHideCustomView()
        {
            base.OnHideCustomView();

            try
            {
                if (_fullscreenView == null)
                    return;

                if (_fullscreenView.Parent is global::Android.Views.ViewGroup parent)
                    parent.RemoveView(_fullscreenView);

                _fullscreenView = null;
                _fullscreenCallback = null;

                FullscreenVideo.Exit();
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Warn("MALPlus Video", "fullscreen exit failed: " + ex.GetType().Name);
            }
        }
    }

    /// <summary>
    ///     The object the player page posts to. Exists so a media element's error or its first playback
    ///     crosses into the app, which is the only way a refused file can be asked for again by itself.
    /// </summary>
    /// <remarks>
    ///     Android.Java.Interop's annotation interface is required here and is not available in this
    ///     assembly, so the members are plain public methods taking string. That is exactly the shape the
    ///     WebView's JavaScript bridge expects, and it is why the page posts strings rather than an
    ///     object: anything it cannot marshal is silently dropped.
    /// </remarks>
    private sealed class PlaybackBridge
    {
        /// <summary>
        ///     Called from the page when its video element fails. The URL comes back with it so the
        ///     caller knows which attempt failed rather than assuming the one it started with is still
        ///     the one on screen.
        /// </summary>
        public void Post(string payload)
        {
            Report(payload);
        }
    }
}
