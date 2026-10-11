using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;

namespace MALPlus.Views;

public partial class LogInPage : ContentPage
{
    /// <summary>
    ///     There is nothing behind this page. The app is unusable without a sign-in, so back must
    ///     never pop into an empty shell: it either stays here or, once signed in, leaves for the
    ///     start tab. Android's default would pop past the login route and land on the blank Shell
    ///     root, which is what "going back leaves the app empty" was.
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        try
        {
            // A sign-in already in progress owns the screen; going back should not yank the WebView
            // out from under it either.
            if (_autoMode)
                return true;

            if (!Credentials.Authenticated)
                return true;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    var route = (Settings.DefaultMenuTab ?? "anime") switch
                    {
                        "discover" => "//discover",
                        "manga" => "//manga",
                        _ => "//anime"
                    };
                    await Shell.Current.GoToAsync(route);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("MALPLUS login back nav failed: " + ex.Message);
                }
            });
        }
        catch (Exception ex)
        {
        }

        // Always handled: there is no page behind this one worth revealing.
        return true;
    }

    private bool _initialized;
    private bool _autoMode;
    private bool _autoSubmitted;
    private bool _loginPageReady;
    private string _autoUser = string.Empty;
    private string _autoPass = string.Empty;
    private CancellationTokenSource _autoCts;

    private LogInViewModel Vm => (LogInViewModel)BindingContext;

    public LogInPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.LogIn;

        // Signing out from anywhere has to land back here. The view model cannot navigate itself
        // (Core is platform independent), so it raises the request and this page, which is already
        // the login page, just needs to be there to receive it.
        Vm.LogOutRequested += OnLogOutRequested;
#if ANDROID
        AuthWebView.HandlerChanged += OnAuthWebViewHandlerChanged;
#endif
    }

    private async void OnLogOutRequested(object sender, EventArgs e)
    {
        // Already on the login page: nothing to navigate to, and popping here would leave the app.
        await Task.CompletedTask;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized)
            return;
        _initialized = true;
        try
        {
            Vm.Init();
        }
        catch (Exception ex)
        {
        }
        try
        {
#if ANDROID
            Android.Webkit.CookieManager.Instance.SetAcceptCookie(true);
#endif
            // Always, not only when unauthenticated. Tokens and cookies are independent: a sign-out
            // whose cookie deletion had not landed yet leaves Credentials.Authenticated false with
            // the previous MAL_SESSION still in the jar, and login.php then redirects straight to
            // the home page of that account instead of showing a form. Cleaning on every entry into
            // the login page is what makes "sign in as somebody else" actually mean somebody else.
            _ = LoadCleanLoginFormAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
        }
    }

    /// <summary>
    ///     The only sign-in button. The typed credentials are filled into MyAnimeList's own login
    ///     form inside the WebView, and everything after that (privacy notice, OAuth consent, token
    ///     exchange) is accepted on the user's behalf, so the app ends up holding a real session
    ///     without ever showing a MAL page.
    /// </summary>
    private async void OnAutoSignInClicked(object sender, EventArgs e)
    {
        try
        {
            var user = (UserEntry.Text ?? string.Empty).Trim();
            var pass = PassEntry.Text ?? string.Empty;
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                ShowStatus("Enter your MyAnimeList username and password.");
                return;
            }
            _autoUser = user;
            _autoPass = pass;
            _autoMode = true;
            _autoSubmitted = false;
            _oauthInFlight = false;
            LoginForm.IsEnabled = false;
            ShowBusy(true, "Connecting to MyAnimeList…");
#if ANDROID
            Android.Webkit.CookieManager.Instance.SetAcceptCookie(true);
#endif
            _autoCts?.Cancel();
            _autoCts = new CancellationTokenSource();
            _ = AutoTimeoutAsync(_autoCts.Token);
            if (_loginPageReady && AuthWebView.Source is UrlWebViewSource { Url: string readyUrl }
                && readyUrl.Contains("login.php"))
            {
                // The URL is not proof that the form is on screen. After a rejected sign-in MAL
                // leaves the WebView on its own error page and a later bounce back to login.php
                // keeps "login.php" in the URL with nothing to fill, so the autofill ran against a
                // page without fields and reported "could not fill the login form" for as long as
                // the user kept tapping. Ask the DOM, which is the only thing that knows.
                var onForm = await IsLoginFormOnScreenAsync();
                if (onForm)
                    _ = AutoFillAndSubmitAsync();
                else
                    _ = LoadCleanLoginFormAsync(_autoCts.Token);
            }
            else
            {
                // Always from a clean jar. A live session redirects login.php to the consent page,
                // where there is no form to fill, and the app then asks for consent and returns a
                // token for whatever account that cookie belonged to instead of the one typed.
                _ = LoadCleanLoginFormAsync(_autoCts.Token);
            }
        }
        catch (Exception ex)
        {
            ResetToForm("Could not start login. Try again.");
        }
    }

    /// <summary>
    ///     Reloads the form without touching the jar. Only for the case where the form is already
    ///     loaded and just needs a refresh; anything that starts a sign-in goes through
    ///     LoadCleanLoginFormAsync instead, which clears the MAL session first.
    /// </summary>
    private async Task LoadLoginFormAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(400, token);
            AuthWebView.Source = "https://myanimelist.net/login.php";
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
        }
    }

    private const string DesktopUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    private void OnAuthWebViewHandlerChanged(object sender, EventArgs e)
    {
        try
        {
#if ANDROID
            // MyAnimeList serves "Google reCAPTCHA is currently blocked" inside the
            // default Android WebView, because the stock WebView user agent is what its
            // bot detection keys on. Presenting a desktop Chrome is what unblocks it.
            var platform = AuthWebView?.Handler?.PlatformView as global::Android.Webkit.WebView;
            if (platform == null)
                return;
            var settings = platform.Settings;
            if (settings != null && settings.UserAgentString != DesktopUserAgent)
                settings.UserAgentString = DesktopUserAgent;
            if (_lastAppliedUserAgent == null)
                _lastAppliedUserAgent = settings?.UserAgentString;
            var cookies = global::Android.Webkit.CookieManager.Instance;
            cookies.SetAcceptCookie(true);
            cookies.SetAcceptThirdPartyCookies(platform, true);
            _webViewReady = true;

            // The user agent is only read when the page is first loaded, so a login.php that was
            // already requested with the stock agent keeps running with it and renders either a
            // reCAPTCHA wall or nothing at all. Reloading here is what makes the manual sign-in
            // path work: this used to leave a blank white WebView, which read as "the app is
            // broken" and left the user with no way in at all.
            if (_webViewReady && settings != null && _lastAppliedUserAgent != settings.UserAgentString)
            {
                _lastAppliedUserAgent = settings.UserAgentString;
                Probe("user agent changed, reloading the login form");
                var current = AuthWebView.Source;
                if (current is UrlWebViewSource { Url: string u } && u.Contains("login.php"))
                {
                    _loginPageReady = false;
                    _ = LoadLoginFormAsync(_autoCts?.Token ?? CancellationToken.None);
                }
            }
#endif
        }
        catch (Exception ex)
        {
        }
    }

    private string _lastAppliedUserAgent;
    private bool _webViewReady;

    /// <summary>
    /// MAL+ signs in with the username and password the user typed, so the MAL session in
    /// the WebView has to be gone first. With a live session, login.php redirects straight
    /// to the OAuth consent page: the login form never renders, AutoFillAndSubmitAsync
    /// finds no fields, and the app asks for consent and hands over a token for the
    /// previously logged-in account no matter what was typed. The credentials were never
    /// checked, which is exactly the "it signs in with anything" behaviour.
    /// </summary>
/// <summary>
    ///     Drops the MyAnimeList web session from the WebView's cookie jar.
    ///     <para>
    ///     This is not optional. With a live MAL_SESSION cookie, login.php answers with a 302 to
    ///     the OAuth consent page instead of the login form, so the form never renders, the
    ///     automatic fill finds no fields, and the app ends up asking for consent and handing over
    ///     a token for WHATEVER account that cookie belongs to. That is how the app could end up
    ///     showing a session nobody signed into during this build: a cookie left in the jar by an
    ///     earlier attempt, not credentials the user typed.
    ///     </para>
    ///     <para>
    ///     RemoveSessionCookies alone is not enough on Android: MAL_SESSION is written with an
    ///     explicit expiry rather than as a session cookie, so it has to be expired by name, and
    ///     the deletion is asynchronous, so the caller has to pause before reloading the form or
    ///     the still-present cookie wins the race and redirects again.
    ///     </para>
    /// </summary>
    private static void ForgetMalSession()
    {
        // The narrow sweep, not the sign-out one. Before a login the only thing that has to go is
        // the session that makes login.php redirect to the consent page; clearing the whole jar
        // here took out cookies the form and the POST behind it need, and the automatic sign-in
        // started failing for no reason of its own. One implementation, two scopes: the login asks
        // for the blocker, the sign-out command asks for the whole session.
        MALPlus.XShared.Utils.Credentials.ClearMalLoginBlocker();
    }

    /// <summary>
    ///     Loads the login form from a clean jar. The pause is the point: SetCookie and
    ///     RemoveSessionCookies are asynchronous, and reloading before they land re-races the
    ///     redirect that made this whole path unreliable.
    /// </summary>
    private async Task LoadCleanLoginFormAsync(CancellationToken token)
    {
        try
        {
            ForgetMalSession();
            await Task.Delay(600, token);
            _loginPageReady = false;
            AuthWebView.Source = "https://myanimelist.net/login.php";
        }
        catch (TaskCanceledException)
        {
            // The reload below is the whole point of this method: without it the WebView keeps
            // showing whatever page it had, and after a sign-out that page belongs to the account
            // just signed out of. A cancellation therefore means the form is NOT loaded, and that
            // has to be visible rather than land in an empty catch.
            Probe("clean form load CANCELLED: the form was not reloaded");
        }
        catch (Exception ex)
        {
            Probe("clean form load failed: " + ex.GetType().Name);
        }
    }

    private async Task AutoTimeoutAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            if (_autoMode)
                FallbackToManual("Taking too long. Continue here.");
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void OnAuthNavigated(object sender, WebNavigatedEventArgs e)
    {
        try
        {
            var url = e.Url ?? string.Empty;
            _navigationInFlight = false;
            if (url.StartsWith("https://myanimelist.net") && !url.Contains("maloauth") &&
                !url.Contains("/v1/oauth2/"))
            {
                // Loaded pages are the reliable moment: the login response Set-Cookie is already
                // applied by the time the navigation completes.
                CaptureSessionCookies(url);
            }
            Probe("NAVIGATED " + url);
            if (url.Contains("login.php"))
                _loginPageReady = true;
            if (!_autoMode)
                return;
            // Hard guarantee: while we drive the sign-in ourselves, MyAnimeList's pages are
            // never on screen. Whatever it renders - the privacy wall, the consent screen,
            // a 400 - stays behind our own form.
            WebOverlay.IsVisible = false;
            if (!url.Contains("login.php"))
            {
                // Landing back on any /login URL after a submit is MAL bouncing us, and the page
                // itself says why: a captcha, a rate limit, or the credentials. Never call it a bad
                // password from the URL alone; the diagnosis below reads the actual text.
                if (_autoSubmitted && url.Contains("login"))
                {
                    _ = DiagnoseReturnToLoginAsync();
                    return;
                }
                _ = InspectAndFailIfErrorAsync();
                return;
            }
            if (_autoSubmitted)
            {
                // Only a page that came BACK to the form is a failed sign-in to diagnose. This
                // branch also fires for the pages a successful sign-in travels through, and running
                // the classifier on those is what produced "wrong username or password" for a
                // password that had been accepted.
                if (!IsLoginFormUrl(url) || _oauthInFlight)
                {
                    Probe("diagnose not run: " + url);
                    return;
                }

                _ = DiagnoseReturnToLoginAsync();
                return;
            }
            _ = AutoFillAndSubmitAsync();
        }
        catch (Exception ex)
        {
        }
    }

    /// <summary>
    /// MyAnimeList answers a rejected sign-in with its own error page ("400 Bad Request -
    /// please return to the previous screen"). Rendering it and then bouncing back to
    /// login.php is what made the user stare at MAL's site, so the page is inspected while
    /// it is still hidden and reported through our own message instead.
    /// </summary>
    private async Task InspectAndFailIfErrorAsync()
    {
        try
        {
            var js = "(function(){"
                + "var t=(document.title||'')+' '+(document.body?document.body.innerText:'');"
                + "if(/400\\s*bad request|bad request|403|forbidden|too many requests|access denied|error occurred/i.test(t))return 'error';"
                + "if(/we value your privacy/i.test(t))return 'wall';"
                + "return 'ok';})();";
            var result = await MainThread.InvokeOnMainThreadAsync(() => AuthWebView.EvaluateJavaScriptAsync(js));
            Probe("page check=" + result);
            if (result == null || !result.Contains("error"))
                return;
            if (!_autoMode)
                return;
            WebOverlay.IsVisible = false;
            FailAuto("Wrong username or password.");
        }
        catch (Exception ex)
        {
        }
    }

    private async Task AutoFillAndSubmitAsync()
    {
        try
        {
            await AutoFillAndSubmitAsync(_autoCts?.Token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            FallbackToManual("Could not fill the login form. Continue here.");
        }
    }

    /// <summary>
    ///     Whether the login form is actually on screen, as opposed to the URL still naming it.
    ///     <para>
    ///     A rejected sign-in leaves MyAnimeList's error page loaded, and the bounce back to
    ///     login.php keeps the word in the URL with no form behind it. Deciding from the URL meant
    ///     autofilling a page that had no fields, which is where the endless "could not fill the
    ///     login form" came from.
    ///     </para>
    /// </summary>
    private async Task<bool> IsLoginFormOnScreenAsync()
    {
        try
        {
            const string js =
                "(function(){return (document.querySelector('input[name=\"user_name\"]')"
                + "||document.querySelector('input[type=\"text\"]'))"
                + "&&(document.querySelector('input[name=\"password\"]')"
                + "||document.querySelector('input[type=\"password\"]')))?'yes':'no';})();";
            var result = await MainThread.InvokeOnMainThreadAsync(() => AuthWebView.EvaluateJavaScriptAsync(js));
            return result != null && result.Contains("yes");
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task AutoFillAndSubmitAsync(CancellationToken token)
    {
        try
        {
            var js = "(function(){"
                + "var wt=document.body&&/we value your privacy/i.test(document.body.innerText);"
                + "if(wt)return 'wall';"
                + "var u=document.querySelector('input[name=\"user_name\"]')||document.querySelector('input[type=\"text\"]');"
                + "var p=document.querySelector('input[name=\"password\"]')||document.querySelector('input[type=\"password\"]');"
                + "if(!u||!p)return 'nofields';"
                + "u.focus();u.value='" + JsEscape(_autoUser) + "';u.dispatchEvent(new Event('input',{bubbles:true}));"
                + "p.focus();p.value='" + JsEscape(_autoPass) + "';p.dispatchEvent(new Event('input',{bubbles:true}));"
                + "var f=u.form||p.form;"
                + "var b=document.querySelector('input[type=\"submit\"]')||document.querySelector('button[type=\"submit\"]')||document.querySelector('.btn-submit');"
                + "window.__malplusP=!!(f&&b);"
                + "if(b){b.click();}"
                + "return 'filled';})();";
            var result = await MainThread.InvokeOnMainThreadAsync(() => AuthWebView.EvaluateJavaScriptAsync(js));
            Probe("autofill result=" + result);
            if (result != null && result.Contains("wall"))
            {
                // MyAnimeList gates the login form behind its privacy notice. The user is
                // never supposed to see MAL's pages, so accept it ourselves and retry.
                if (await TryClickButtonAsync("privacy", "agree", "accept", "i agree", "allow", "accept all"))
                {
                    ShowBusy(true, "Accepting MyAnimeList's privacy notice…");
                    await Task.Delay(2500, token);
                    await AutoFillAndSubmitAsync(token);
                    return;
                }
                FallbackToManual("Could not pass MyAnimeList's privacy notice.");
            }
            else if (result != null && result.Contains("filled"))
            {
                // Submitting is the hard part. MyAnimeList's form needs its own submit
                // handler to run: a synthetic click is untrusted and gets ignored, and a
                // bare form.submit() skips the handler, so MAL answers the POST with a
                // 400 that has nothing to do with the password. Try all three, checking
                // after each whether the page actually moved on.
                _autoSubmitted = true;
                ShowBusy(true, "Verifying your credentials…");
                _autoCts?.Cancel();
                _autoCts = new CancellationTokenSource();
                _ = AutoTimeoutAsync(_autoCts.Token);
                // The token of the CTS we just cancelled is dead, so the submit loop has to
                // use the new one; reusing the old one aborted the attempt instantly and
                // reported "could not fill the login form" right after a successful submit.
                if (await SubmitWithFallbacksAsync(_autoCts.Token))
                    return;
                FailAuto("Could not submit the login form. Try again.");
            }
            else
            {
                // No fields means the WebView is not on the login form. While MyAnimeList is
                // bouncing through its own OAuth redirect that is expected, and it means the
                // credentials were already accepted: without this the sign in finished and then
                // announced "Could not fill the login form" on top of it.
                if (_oauthInFlight)
                {
                    Probe("no login fields, but OAuth is already in flight");
                    return;
                }

                FallbackToManual("Could not fill the login form.");
            }
        }
        catch (Exception ex)
        {
            FallbackToManual("Could not fill the login form.");
        }
    }

    private async Task<bool> SubmitWithFallbacksAsync(CancellationToken token)
    {
        var attempts = new (string Name, string Js)[]
        {
            ("submit-event",
                "(function(){var f=document.querySelector('form');if(!f)return 'noform';"
                + "return f.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}))?'dispatched':'blocked';})();"),
            ("prototype-submit",
                "(function(){var f=document.querySelector('form');if(!f)return 'noform';"
                + "HTMLFormElement.prototype.submit.call(f);return 'submitted';})();"),
        };
        foreach (var attempt in attempts)
        {
            // A submit that already worked does not get a second one. The loop used to fire the
            // fallback 40ms after a submit that had started a perfectly good navigation, and that
            // second POST landed on top of the first and bounced the sign-in back to the form.
            // The verdict check is the same idea one step later: the classifier can decide while
            // this loop is between attempts, and a decided attempt must not be re-sent.
            if (LeftLoginFormAlready() || _oauthInFlight || _autoVerdictReached)
            {
                Probe("submit " + attempt.Name + " skipped: attempt already resolved");
                return !_autoVerdictReached;
            }

            _leftLoginForm = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var result = await MainThread.InvokeOnMainThreadAsync(() => AuthWebView.EvaluateJavaScriptAsync(attempt.Js));
                Probe("submit " + attempt.Name + "=" + result);
            }
            catch (Exception ex)
            {
            }
            if (await WaitForLoginFormToGoAsync(token))
                return true;
        }
        return false;
    }

    // A submit that navigates is the common case, and OnAuthNavigating already sees that
    // navigation, so it completes the task and this returns at once. The poller stays as
    // the fallback for MyAnimeList replacing the DOM without navigating, which used to cost
    // up to twelve 500ms round trips per attempt before anything was noticed.
    private async Task<bool> WaitForLoginFormToGoAsync(CancellationToken token)
    {
        const string stillHere = "(function(){return document.querySelector('input[type=\"password\"]')?'here':'gone';})();";
        var left = _leftLoginForm?.Task ?? Task.CompletedTask;

        for (var waited = 0; waited < 12; waited++)
        {
            try
            {
                await Task.Delay(500, token);
            }
            catch (TaskCanceledException)
            {
                return false;
            }

            if (left.IsCompletedSuccessfully)
                return true;

            var here = await MainThread.InvokeOnMainThreadAsync(() => AuthWebView.EvaluateJavaScriptAsync(stillHere));
            if (here != null && here.Contains("gone"))
                return true;
        }
        return false;
    }

    private static bool IsLoginFormUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return false;
        // maloauth used to be counted as a login form url. It is not: it is where a SUCCESSFUL
        // sign-in lands, so counting it let the page be read as the form and its contents judged
        // as the answer to a submit that had already been taken.
        return url.Contains("login.php", StringComparison.OrdinalIgnoreCase) ||
               url.EndsWith("/login", StringComparison.OrdinalIgnoreCase);
    }

    private TaskCompletionSource<bool> _leftLoginForm;
    private bool _oauthInFlight;

    /// <summary>
    ///     True between a navigation starting and its page committing. The WebView can be asked for
    ///     its DOM during that window and will hand back the page it has not replaced yet, so
    ///     anything that reads the page has to wait it out.
    /// </summary>
    private volatile bool _navigationInFlight;

    /// <summary>
    ///     Set once the classifier has reached a verdict about the attempt. The submit loop keeps
    ///     its fallback attempts behind a fresh navigation check, and a verdict arriving in between
    ///     used to be overrun by the next submit, which then bounced the sign-in back to the form.
    /// </summary>
    private volatile bool _autoVerdictReached;

    /// <summary>
    ///     The submit already took the WebView off the login form, which is the success signal. The
    ///     task is completed from the navigation handler, so this is a read and not a guess.
    /// </summary>
    private bool LeftLoginFormAlready()
    {
        var left = _leftLoginForm;
        return left != null && left.Task.IsCompleted;
    }

    /// <summary>
    ///     Waits for the page in flight to commit, giving up rather than hanging when the navigation
    ///     never completes: a caller that is about to classify must always get an answer.
    /// </summary>
    private async Task WaitForNavigationToSettleAsync()
    {
        for (var i = 0; i < 25 && _navigationInFlight; i++)
            await Task.Delay(100);
    }

    private async Task DiagnoseReturnToLoginAsync()
    {
        try
        {
            // Nothing to diagnose once MyAnimeList has moved on. This check exists because the
            // classifier below reads whatever the DOM happens to hold, and a submit that succeeded
            // navigates to the OAuth page while the DOM is still the login form carrying the error
            // from an earlier attempt. Reading that turned a good sign-in into "wrong username or
            // password" in front of the user, which is what made it look like the app rejected a
            // password it had actually taken.
            if (_oauthInFlight || LeftLoginFormAlready())
            {
                Probe("diagnose skipped: sign-in already moved on");
                return;
            }

            // A navigation that started but has not landed yet is the same trap wearing a different
            // hat, so let it settle before reading anything.
            if (_navigationInFlight)
            {
                await WaitForNavigationToSettleAsync();
                if (_oauthInFlight || LeftLoginFormAlready())
                {
                    Probe("diagnose skipped after settling: sign-in already moved on");
                    return;
                }
            }

            var js = "(function(){"
                // Rate limiting and transient blocks come back as 400/403 too, and they have nothing
                // to do with the password. Only a page that actually names the credentials is a
                // rejection; everything else is reported as what it is, and an unrecognised page is
                // never silently turned into "wrong password", which is what this did before: a
                // captcha or a rate limit was reported to the user as a bad password.
                + "var t=document.body?document.body.innerText:'';"
                + "var e=document.querySelector('.error,.error-message,#error,.form-error,div[class*=\"error\"],div[class*=\"Error\"]');"
                + "var msg=(e?((e.innerText||e.value||'')+''):'')||((t.match(/[^\\n]*(incorrect|invalid|wrong|credentials|password|denied|too many|rate|captcha|robot|blocked|temporarily)[^\\n]*/i)||[''])[0]);"
                + "var head=(document.title||'')+' >> '+msg.trim().slice(0,200);"
                // Rendered captcha: the one case where the user genuinely has to do something.
                + "var els=document.querySelectorAll('iframe[src*=\"recaptcha\"],iframe[title*=\"challenge\"],div.g-recaptcha,#captcha,div[class*=\"captcha\"]');"
                + "for(var i=0;i<els.length;i++){"
                + "var r=els[i].getBoundingClientRect();"
                + "var st=getComputedStyle(els[i]);"
                + "if(r.width>40&&r.height>20&&st.display!=='none'&&st.visibility!=='hidden')return 'challenge|'+head;}"
                // Credentials named explicitly.
                + "if(/incorrect (?:username|password|credentials)|invalid (?:username|password|credentials)|wrong (?:username|password)|username or password/i.test(t))return 'denied|'+head;"
                + "if(/not a robot|verification required|confirm you'?re not a robot/i.test(t))return 'challenge|'+head;"
                + "if(/too many requests|rate limit|slow down|try again later|temporarily blocked/i.test(t))return 'throttled|'+head;"
                + "if(/400|bad request|403|forbidden|access denied|error occurred|internal server error|500|502|503/i.test(t))return 'server|'+head;"
                + "return 'unknown|'+head;})();";
            var result = await MainThread.InvokeOnMainThreadAsync(() => AuthWebView.EvaluateJavaScriptAsync(js));
            Probe("diagnose result=" + result);

            // Ordered from the most specific to the least. Anything unrecognised falls through to
            // the manual sign-in rather than to an accusation about the user's password.
            if (result == null)
            {
                _autoVerdictReached = true;
                FallbackToManual("MyAnimeList did not answer. Try signing in on their site.");
                return;
            }

            // The attempt is decided from here on. Marked before acting so the submit loop, which
            // may be between its two fallback attempts at this very moment, stops instead of
            // sending the credentials again behind the verdict.
            _autoVerdictReached = true;

            if (result.Contains("challenge"))
            {
                FallbackToManual("MyAnimeList is asking for verification. Complete it here.");
                return;
            }
            if (result.Contains("denied"))
            {
                // ResetToForm clears the OAuth flags too: a rejected password still travels back
                // through MyAnimeList's OAuth pages, so they must not be left set for the next
                // attempt to trip over.
                FailAuto("Wrong username or password.");
                return;
            }
            if (result.Contains("throttled"))
            {
                FallbackToManual("MyAnimeList is rate limiting this login. Wait a moment and try again.");
                return;
            }
            if (result.Contains("server"))
            {
                FallbackToManual("MyAnimeList returned an error, not a login problem. Try again shortly.");
                return;
            }

            FallbackToManual("Sign-in did not complete. You can finish it on the MyAnimeList site.");
        }
        catch (Exception ex)
        {
            // A WebView that cannot even be evaluated is not evidence about the credentials.
            Probe("diagnose failed: " + ex.GetType().Name);
            FallbackToManual("Could not read MyAnimeList's answer. Try signing in on their site.");
        }
    }

    private async Task AcceptConsentOrFallBackAsync()
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (!_autoMode)
                return;
            await Task.Delay(1200);
            if (await TryClickButtonAsync("consent", "allow", "authorize", "accept", "continue"))
                return;
        }
        if (!_autoMode)
            return;
        _autoCts?.Cancel();
        OverlayHint.Text = "Review and tap Allow to connect MAL+ to your account";
        OverlayHint.IsVisible = true;
        WebOverlay.IsVisible = true;
    }

    /// <summary>
    /// Clicks a button on the MyAnimeList page by its label. The whole point of the
    /// WebView here is to be invisible: the privacy notice and the OAuth consent screen
    /// both need a click, and the user must never be the one making it.
    /// </summary>
    private async Task<bool> TryClickButtonAsync(string what, params string[] labels)
    {
        try
        {
            var wanted = string.Join(",", labels.Select(l => "'" + JsEscape(l.ToLowerInvariant()) + "'"));
            var js = "(function(){"
                + "var want=[" + wanted + "];"
                + "var nodes=document.querySelectorAll('button,input[type=submit],input[type=button],a.btn,div.btn');"
                + "for(var i=0;i<nodes.length;i++){"
                + "var n=nodes[i];"
                + "if(!n||n.disabled)continue;"
                + "var t=((n.value||n.innerText||n.textContent||'')+'').trim().toLowerCase();"
                + "if(!t)continue;"
                + "for(var j=0;j<want.length;j++){"
                + "if(t===want[j]||t.indexOf(want[j])>=0){n.click();return 'clicked:'+t;}}}"
                + "return 'notfound';})();";
            var result = await MainThread.InvokeOnMainThreadAsync(() => AuthWebView.EvaluateJavaScriptAsync(js));
            Probe(what + " click=" + result);
            return result != null && result.StartsWith("clicked", StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    private static string JsEscape(string value)
    {
        return (value ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", string.Empty).Replace("\r", string.Empty);
    }

    /// <summary>
    ///     Closing the site overlay always lands back on the form with both routes offered. The
    ///     site button used to be a single control that hid itself while the overlay was open, so
    ///     coming back from a failed captcha left nothing to retry with.
    /// </summary>
    private void OnWebCloseClicked(object sender, EventArgs e)
    {
        ResetToForm(null);
        ManualLoginButton.IsVisible = true;
    }

    private void OnWebRetryClicked(object sender, EventArgs e)
    {
        try
        {
            WebEmptyState.IsVisible = false;
            ShowStatus("Loading MyAnimeList…");
            _loginPageReady = false;
            _lastAppliedUserAgent = null;
#if ANDROID
            var platform = AuthWebView?.Handler?.PlatformView as global::Android.Webkit.WebView;
            if (platform?.Settings != null)
                platform.Settings.UserAgentString = DesktopUserAgent;
#endif
            _ = LoadLoginFormAsync(_autoCts?.Token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
        }
    }

    /// <summary>
    ///     A WebView that rendered nothing is indistinguishable from a white rectangle. Measured
    ///     against the element box rather than the viewport, because the overlay stacks a header
    ///     and a hint above the page and a tall WebView can be "on screen" while its body is empty.
    /// </summary>
    private async Task WatchForBlankPageAsync()
    {
        try
        {
            await Task.Delay(2500);
            if (!WebOverlay.IsVisible)
                return;

            var js = "(function(){var b=document.body;return (b&&b.innerText&&b.innerText.trim().length>0)?'ok':'blank';})();";
            var result = await MainThread.InvokeOnMainThreadAsync(() => AuthWebView.EvaluateJavaScriptAsync(js));
            Probe("blank check=" + result);
            if (result != null && result.Contains("blank"))
            {
                WebEmptyState.IsVisible = true;
                ShowStatus("MyAnimeList did not load.");
            }
        }
        catch (Exception ex)
        {
            // A WebView with no handler yet throws here; nothing to report.
        }
    }

    private void ShowBusy(bool busy, string status)
    {
        BusySpinner.IsRunning = busy;
        BusySpinner.IsVisible = busy;
        ShowStatus(status);
    }

    private void ShowStatus(string status)
    {
        if (string.IsNullOrEmpty(status))
        {
            StatusLabel.IsVisible = false;
            return;
        }
        StatusLabel.Text = status;
        StatusLabel.IsVisible = true;
    }

    private void FailAuto(string message)
    {
        ResetToForm(message);
    }

    /// <summary>
    /// The automatic sign-in gave up. MyAnimeList's page is NOT shown: the app says what
    /// happened on our own screen and only reveals the WebView if the user explicitly asks
    /// for it. This used to flip the overlay on by itself, which is how MAL kept ending up
    /// in the foreground.
    /// </summary>
    private void FallbackToManual(string hint)
    {
        try
        {
            Probe("fallback: " + hint);
            _autoMode = false;
            _autoSubmitted = false;
            _autoCts?.Cancel();
            ShowBusy(false, null);
            LoginForm.IsEnabled = true;
            WebOverlay.IsVisible = false;
            // The button is always on the form now, so it is never toggled here.
            ShowStatus(string.IsNullOrEmpty(hint) ? "Sign-in needs one more step." : hint);
        }
        catch (Exception ex)
        {
        }
    }

    private void OnManualLoginClicked(object sender, EventArgs e)
    {
        try
        {
            _autoCts?.Cancel();
            // Anything the automatic pass was doing is abandoned: from here the user is in charge,
            // and a half-finished auto submit must not keep steering the page underneath them.
            _autoMode = false;
            _autoSubmitted = false;
            _autoUser = string.Empty;
            _autoPass = string.Empty;
            LoginForm.IsEnabled = true;
            ShowBusy(false, null);

            OverlayHint.Text = "Sign in with MyAnimeList, then tap Close";
            OverlayHint.IsVisible = true;
            WebEmptyState.IsVisible = false;
            _lastAppliedUserAgent = null;
            WebOverlay.IsVisible = true;
            // Same clean jar as the automatic path, so the site option cannot be handed a live
            // session either.
            //
            // CancellationToken.None, deliberately, and not _autoCts.Token. This method cancels
            // _autoCts on its way in, so that token is already dead: the delay at the top of
            // LoadCleanLoginFormAsync threw TaskCanceledException on the spot, the catch swallowed
            // it, and the reload of login.php below it never ran. The WebView was left showing the
            // page it already had, which after a sign-out is the previous account's home page, and
            // that is how a sign-in could end up completing as somebody else.
            _ = LoadCleanLoginFormAsync(CancellationToken.None);
            _ = WatchForBlankPageAsync();
        }
        catch (Exception ex)
        {
        }
    }

    private void ResetToForm(string message)
    {
        try
        {
            _autoCts?.Cancel();
            _autoMode = false;
            _autoSubmitted = false;
            _autoUser = string.Empty;
            _autoPass = string.Empty;
            // Every guard that remembers this attempt has to be cleared here, not just in the
            // branches that happened to reach it. _oauthInFlight in particular survives a rejected
            // password, because the rejection travels back through MyAnimeList's OAuth pages, and
            // left set it tells the next attempt it has already moved on: the diagnosis is skipped,
            // nothing decides anything, and the form sits on its busy message until the timeout.
            _oauthInFlight = false;
            _autoVerdictReached = false;
            _leftLoginForm = null;
            _navigationInFlight = false;
            try
            {
                Vm.PasswordInput = string.Empty;
                PassEntry.Text = string.Empty;
            }
            catch
            {
            }
            WebOverlay.IsVisible = false;
            OverlayHint.IsVisible = false;
            WebEmptyState.IsVisible = false;
            LoginForm.IsVisible = true;
            LoginForm.IsEnabled = true;
            // Both routes are restored on every reset, so no failure path can leave the form
            // without a way forward.
            ManualLoginButton.IsVisible = true;
            ShowBusy(false, message);
        }
        catch (Exception ex)
        {
        }
    }

    private static void Probe(string text)
    {
        try
        {
            File.AppendAllText(Path.Combine(FileSystem.CacheDirectory, "maui_login_probe.txt"),
                DateTime.UtcNow.ToString("HH:mm:ss") + " " + text + "\n");
        }
        catch
        {
        }
#if ANDROID
        try
        {
            // The probe file lives in the app's cache, which cannot be read without a
            // debuggable build, and a failing sign-in is exactly when the navigation
            // history matters. Logcat is readable from adb.
            Android.Util.Log.Info("MALPlusAuth", text);
        }
        catch
        {
        }
#endif
    }

    private string _cookies;
    private bool _reportedEmptyJar;

    /// <summary>
    /// Reads the MAL session cookies and keeps the longest blob seen. Only the length is logged,
    /// never the value: these cookies are the website session.
    /// </summary>
    private void CaptureSessionCookies(string url = "")
    {
#if ANDROID
        try
        {
            var jar = Android.Webkit.CookieManager.Instance.GetCookie("https://myanimelist.net");
            if (string.IsNullOrWhiteSpace(jar))
            {
                // The CookieManager applies the Set-Cookie of the login response asynchronously, so
                // on the very first navigation after the form post the jar can still be empty. The
                // capture is retried on every later navigation, so this is not fatal.
                if (!_reportedEmptyJar)
                {
                    _reportedEmptyJar = true;
                }
                return;
            }
            _reportedEmptyJar = false;
            if (_cookies != null && jar.Length <= _cookies.Length)
                return;
            _cookies = jar;
            var hasSession = jar.Contains("MAL_SESSION") || jar.Contains("p MAL_") ||
                             jar.Contains("mal_session") || jar.Contains("token=");
        }
        catch (Exception ex)
        {
        }
#endif
    }

    private void OnAuthNavigating(object sender, WebNavigatingEventArgs e)
    {
        try
        {
            var url = e.Url ?? string.Empty;
            Probe("NAVIGATING " + url);

            // From here until the page commits, the DOM belongs to the page being replaced.
            _navigationInFlight = true;

            // MyAnimeList accepting the credentials sends it straight into its own OAuth
            // redirect. Anything after that point is the sign in succeeding, not a form to fill.
            if (url.Contains("/v1/oauth2/authorize", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("maloauth", StringComparison.OrdinalIgnoreCase))
                _oauthInFlight = true;

            // A submit that navigates away from the login form is the success signal the
            // auto-login is blocked on, and this is the only place that knows it happened.
            if (!IsLoginFormUrl(url))
                _leftLoginForm?.TrySetResult(true);

            if (url.Contains("maloauth?state=signin&error="))
            {
                e.Cancel = true;
                var denied = _autoMode;
                ResetToForm(denied ? "Connection cancelled. Tap Sign in to retry." : null);
                if (!denied)
                    MainThread.BeginInvokeOnMainThread(() => WebOverlay.IsVisible = false);
                Vm.FailedSignIn();
                return;
            }
            if (url.Contains("maloauth"))
            {
                var match = System.Text.RegularExpressions.Regex.Matches(url, ".*maloauth\\?code=(.*)\\&.*");
                var hadAuto = _autoMode;
                _autoCts?.Cancel();
                _autoMode = false;
                _autoSubmitted = false;
                _autoUser = string.Empty;
                _autoPass = string.Empty;
                try
                {
                    Vm.PasswordInput = string.Empty;
                    PassEntry.Text = string.Empty;
                }
                catch
                {
                }
                MainThread.BeginInvokeOnMainThread(() => WebOverlay.IsVisible = false);
                if (match.Count > 0)
                {
                    e.Cancel = true;
                    Probe("oauth code captured");
                    // Last chance to pick the session cookies up: by now the whole login round trip
                    // is done, so the jar is guaranteed to hold the MAL session.
                    CaptureSessionCookies("maloauth callback");
                    ShowBusy(true, "Signing you in…");
                    Vm.SignIn(_cookies ?? string.Empty, match[0].Groups[1].Value);
                }
                else
                {
                    if (hadAuto)
                        ResetToForm("Could not complete the connection.");
                    else
                        ResetToForm(null);
                    Vm.FailedSignIn();
                }
                return;
            }
            // Keep the freshest session cookie on every MAL navigation, not only when the redirect
            // lands exactly on the site root: the community endpoints authenticate with these
            // cookies, and missing them is what left the forums dead until the next sign in.
            if (url.StartsWith("https://myanimelist.net") && !url.Contains("maloauth") &&
                !url.Contains("/v1/oauth2/"))
            {
                CaptureSessionCookies(url);
            }
            if (url == "https://myanimelist.net/" || url == "https://myanimelist.net/#"
                || url.StartsWith("https://myanimelist.net/#"))
            {
                // Only ever a consequence of the user pressing Sign in. Without this gate a
                // leftover MAL_SESSION cookie redirects the root here, the app answers by
                // starting the OAuth dance on its own, accepts the consent and hands back a token
                // for whichever account that cookie belonged to: signing in nobody typed anything.
                // Every sign-in has to start from _autoMode.
                if (!_autoMode)
                    return;

                // And never in the middle of the form itself. MyAnimeList bounces the submit
                // through the site root on its way to the OAuth page, so this used to fire while
                // AutoFillAndSubmitAsync was still working: the OAuth round trip started, the page
                // navigated back to login.php, the autofill found no fields mid-flight and the
                // whole attempt died with a message that said nothing useful. One sign-in, one
                // OAuth start.
                if (_oauthInFlight)
                    return;

                e.Cancel = true;
                CaptureSessionCookies();
                AuthWebView.Source = "https://myanimelist.net/v1/oauth2/authorize?response_type=code&"
                    + "client_id=" + Uri.EscapeDataString(MalOAuthSettings.ClientId) + "&"
                    + "state=signin&"
                    + $"code_challenge={Vm.PkceChallenge}&"
                    + "code_challenge_method=plain";
                return;
            }
            if (url.StartsWith("https://myanimelist.net/v1/oauth2/authorize")
                || url.StartsWith("https://myanimelist.net/dialog/"))
            {
                // Approving this is what hands MAL+ a session, so it may only happen after the
                // credentials the user typed were actually submitted and accepted.
                //
                // The URL alone does not prove that. A MAL_SESSION cookie left in the jar makes
                // login.php redirect straight to the OAuth page, the automatic pass never filled
                // anything, and the app used to wave the consent through for an account nobody
                // signed into during this session. _autoSubmitted is the record that a submit
                // happened; without it there is nothing to consent to.
                if (_autoMode && !_autoSubmitted)
                {
                    Probe("consent NOT auto-approved: no credentials were submitted");
                    _oauthInFlight = false;
                    FailAuto("MyAnimeList still has a session open. Sign out first, then sign in again.");
                    return;
                }

                if (_autoMode)
                {
                    // The credentials were accepted, so MyAnimeList is only asking to let
                    // MAL+ use the account. The user never sees this page: accept for them
                    // and keep our own login UI on screen. If the click does not land we
                    // fall back to showing MAL's page rather than hanging forever.
                    ShowBusy(true, "Connecting your account…");
                    _ = AcceptConsentOrFallBackAsync();
                }
                return;
            }
            if (url == "https://myanimelist.net/register.php"
                || url.Contains("consent") || url.Contains("quantcast") || url.Contains("sourcepoint")
                || url.Contains("privacy-mgmt") || url.Contains("trustarc") || url.Contains("cookielaw")
                || url.Contains("google") || url.Contains("facebook") || url.Contains("apple")
                || url.StartsWith("https://api.twitter") || url.StartsWith("https://myanimelist.net/sns/")
                || url.StartsWith("https://accounts.google") || url.StartsWith("https://accounts.youtube")
                || url.StartsWith("https://myanimelist.net/login.php"))
            {
                return;
            }
            e.Cancel = true;
            AuthWebView.Source = "https://myanimelist.net/login.php";
        }
        catch (Exception ex)
        {
        }
    }
}
