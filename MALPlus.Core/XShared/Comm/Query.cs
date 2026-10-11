using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MALPlus.Models.Enums;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;

namespace MALPlus.XShared.Comm
{
    public abstract class Query
    {

        protected Uri Request;
        private bool _retry = true;
        public static ApiType CurrentApiType { get; set; } = Settings.SelectedApiType;

        protected static HttpClient _client;

        /// <summary>
        ///     Desktop Chrome on purpose. With a mobile User-Agent MAL serves a
        ///     different variant of several pages: /featured and /news lose the
        ///     article list entirely and a forum board answers with a
        ///     JavaScript-only shell, so those screens looked empty no matter how
        ///     they were parsed. Cookies, auth and the parser were never the cause.
        /// </summary>
        private const string DesktopUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        /// <summary>
        ///     Cookie-free client for pages MAL server-renders to signed-out
        ///     visitors.
        /// </summary>
        protected static readonly HttpClient _anonymousClient = CreateAnonymousClient();

        private static HttpClient CreateAnonymousClient()
        {
            var client = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = true,
                UseCookies = false
            })
            {
                Timeout = TimeSpan.FromSeconds(100)
            };
            // Desktop on purpose: with a mobile User-Agent MAL serves a different,
            // stripped variant of /featured and /news (no article list at all) and
            // the forum board comes back as a JavaScript-only shell, which is why
            // those pages looked empty no matter how they were parsed.
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DesktopUserAgent);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
            return client;
        }

        /// <summary>
        ///     Fetches a public MAL page without the session cookies.
        /// </summary>
        protected static async Task<string> GetAnonymousRequestResponse(Uri request)        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, request);
                var res = await _anonymousClient.SendAsync(message);
                var content = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode)
                    ResourceLocator.ConnectionInfoProvider.HasInternetConnection = true;
                return content;
            }
            catch (Exception)
            {
                ResourceLocator.ConnectionInfoProvider.HasInternetConnection = false;
            }
            return null;
        }

        static Query()
        {
            _client = new HttpClient(ResourceLocator.MalHttpContextProvider.GetHandler());
            // Several sources reject requests without a browser User-Agent outright.
            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                DesktopUserAgent);
            RefreshClientAuthHeader();
        }

        public static void RefreshClientAuthHeader()
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Credentials.UserName}:{Credentials.Password}")));
        }


        public virtual async Task<string> GetRequestResponse()
        {
            try
            {
                var res = await _client.GetAsync(Request);
                // Only MAL's own anti-bot page deserves this dialog: other sources
                // (reddit for wallpapers, ANN) 403 on their own and repeating the
                // MAL message there just spams modals over an unreachable host.
                if (res.StatusCode == HttpStatusCode.Forbidden
                    && Request.Host.EndsWith("myanimelist.net", StringComparison.OrdinalIgnoreCase)
                    && !Request.ToString()
                        .Contains("https://myanimelist.net/rss.php?type=rw&u=")) //workaround because I don't want to disturb the spaghetti gods sleeping around
                {
                    HandleMalBuggines();
                }

                await Task.Delay(150);
                var content = await res.Content.ReadAsStringAsync();

                // A body is not proof of a usable answer, and treating it as one is what made these
                // screens fail silently. MAL answers a request it refused to authenticate with HTTP 200
                // and the login page, so "no results" and "not signed in" looked identical and every
                // page parsed the login markup as an empty collection.
                if (!res.IsSuccessStatusCode)
                {
                    ResourceLocator.ConnectionInfoProvider.HasInternetConnection = false;
                    return null;
                }

                // Reported rather than served: the caller has to know the difference between MAL having
                // nothing and MAL not knowing who was asking.
                if (IsLoggedOutBody(content))
                {
                    ResourceLocator.SnackbarProvider.ShowText(SnackbarMessageOnFail);
                    return null;
                }

                ResourceLocator.ConnectionInfoProvider.HasInternetConnection = true;
                return content;
            }
            catch (Exception)
            {
                ResourceLocator.ConnectionInfoProvider.HasInternetConnection = false;

                if (Credentials.Authenticated)
                    ResourceLocator.SnackbarProvider.ShowText(SnackbarMessageOnFail);
            }
            return null;
        }

        public virtual string SnackbarMessageOnFail => "Operation failed, check your internet connection...";

        /// <summary>
        ///     Whether a body from MAL is actually the login page, which is what comes back for a
        ///     request the session could not authorise.
        /// </summary>
        /// <remarks>
        ///     The login page is the only body carrying both the "you must first login" notice and a
        ///     login form, and an empty inbox carries neither. The form is checked as well as the notice
        ///     so a reworded redesign still does not read as a signed-in reply.
        /// </remarks>
        private static bool IsLoggedOutBody(string content)
        {
            if (string.IsNullOrEmpty(content) || content.Length > 4096)
                return false;
            return content.Contains("You must first login to see this page")
                   || (content.Contains("badresult") && content.Contains("Login</h1>"));
        }

        private static readonly SemaphoreSlim _buggedMalMessageSemaphore = new SemaphoreSlim(1);
        private async void HandleMalBuggines()
        {
            ResourceLocator.DispatcherAdapter.Run(async () =>
            {
                await _buggedMalMessageSemaphore.WaitAsync();
                try
                {
                    await ResourceLocator.MessageDialogProvider.ShowMessageDialogAsync(
                        "There was an error connecting to MAL Api, it tends to behave in unpredictable ways unfortunately and there's nothing I can do about it. Please try again later.", "Whoops!");
                }
                finally
                {
                    _buggedMalMessageSemaphore.Release();
                }
            });
            //Couldn't handle it :(
        }
    }
}