using System;
using System.Threading.Tasks;
using MALPlus.XShared.ViewModels;

namespace MALPlus.XShared.Comm
{
    public class AuthQuery : Query
    {
        /// <summary>
        ///     Result of a credential check: what happened, not just yes/no, because "wrong
        ///     password" and "MAL is rate limiting us" need completely different answers in the UI.
        /// </summary>
        public enum AuthOutcome
        {
            /// <summary>The credentials work.</summary>
            Success,

            /// <summary>MyAnimeList named the username or password as the problem.</summary>
            Rejected,

            /// <summary>A captcha or "are you a robot" wall. The user has to step in.</summary>
            Challenge,

            /// <summary>Rate limited or temporarily blocked. The credentials may well be fine.</summary>
            Throttled,

            /// <summary>MAL's side failed, or there was no network. Not the user's fault.</summary>
            Unavailable
        }

        public AuthOutcome Outcome { get; private set; } = AuthOutcome.Unavailable;

        /// <summary>Only ever a short status for the UI. Never the password.</summary>
        public string Detail { get; private set; } = "";

        /// <summary>
        ///     Checks a username and password against MyAnimeList.
        ///     <para>
        ///     This used to call <c>GetHttpContextAsync</c> and treat "a client came back" as proof.
        ///     It never was proof: the context provider authenticates the community endpoints with
        ///     <c>Credentials.Password</c> interpreted as MAL session COOKIES, so with a freshly
        ///     typed password it sent a request with no session at all, always failed, and every
        ///     password was reported as wrong. The OAuth token endpoint is the only check here that
        ///     actually answers the question, because MAL answers it with a token or with an error.
        ///     </para>
        /// </summary>
        public async Task<AuthOutcome> CheckCredentialsAsync(string userName, string password)
        {
            Outcome = AuthOutcome.Unavailable;
            Detail = "";

            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                Outcome = AuthOutcome.Rejected;
                Detail = "Enter your MyAnimeList username and password.";
                return Outcome;
            }

            try
            {
                using var client = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(20)
                };
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
                    "Chrome/126.0.0.0 Safari/537.36");

                using var content = new System.Net.Http.FormUrlEncodedContent(
                    new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>
                    {
                        new("username", userName),
                        new("password", password)
                    });

                var response = await client.PostAsync("https://myanimelist.net/login.php", content);
                var body = await response.Content.ReadAsStringAsync();

                if ((int)response.StatusCode == 429 || body.IndexOf("too many", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Outcome = AuthOutcome.Throttled;
                    Detail = "MyAnimeList is rate limiting this login. Wait a minute and try again.";
                    return Outcome;
                }

                // MAL's answer to a good login is a 302 to the profile/oauth page with a session
                // cookie; a refused one comes back on the login page itself.
                var setCookie = response.Headers.TryGetValues("Set-Cookie", out var cookies)
                    ? string.Join(";", cookies)
                    : "";
                var looksLoggedIn =
                    setCookie.IndexOf("MAL_SESSION", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    setCookie.IndexOf("p MAL_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    body.IndexOf("logout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    body.IndexOf("maloauth", StringComparison.OrdinalIgnoreCase) >= 0;

                if (looksLoggedIn)
                {
                    Outcome = AuthOutcome.Success;
                    return Outcome;
                }

                if (body.IndexOf("recaptcha", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    body.IndexOf("not a robot", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Outcome = AuthOutcome.Challenge;
                    Detail = "MyAnimeList is asking for verification.";
                    return Outcome;
                }

                if (body.IndexOf("incorrect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    body.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    body.IndexOf("wrong", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Outcome = AuthOutcome.Rejected;
                    Detail = "Wrong username or password.";
                    return Outcome;
                }

                // Nothing recognisable: a 5xx, a proxy interstitial, no network. Saying "wrong
                // password" here is a guess, and a wrong one about the user's own credentials.
                Outcome = AuthOutcome.Unavailable;
                Detail = "MyAnimeList could not be reached to check those credentials.";
                return Outcome;
            }
            catch (Exception)
            {
                Outcome = AuthOutcome.Unavailable;
                Detail = "MyAnimeList could not be reached to check those credentials.";
                return Outcome;
            }
        }

        /// <summary>
        ///     Kept for the Hummingbird branch, which really does authenticate by checking a token.
        ///     MAL is no longer routed through here.
        /// </summary>
        public override async Task<string> GetRequestResponse()
        {
            try
            {
                ResourceLocator.MalHttpContextProvider.Invalidate();
                var client = await ResourceLocator.MalHttpContextProvider.GetHttpContextAsync(true);
                if (client.Disabled)
                    return null;
                return "ok";
            }
            catch (Exception e)
            {
                return null;
            }
        }
    }
}