using System.Net;
using System.Net.Http;
using MALPlus.XShared.Comm.MagicalRawQueries;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;

namespace MALPlus.Services;

public class MauiMalHttpContextProvider : MALPlus.XShared.BL.MalHttpContextProviderBase
{
    public override HttpClientHandler GetHandler()
    {
        return new HttpClientHandler();
    }

        protected override async Task<CsrfHttpClient> ObtainContext()
        {
            var httpHandler = ResourceLocator.MalHttpContextProvider.GetHandler();
            _httpClient = new CsrfHttpClient(httpHandler) { BaseAddress = new Uri(MalBaseUrl) };

            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("authority", "myanimelist.net");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Host", "myanimelist.net");
            _httpClient.DefaultRequestHeaders.Add("X-Requested-With", new[] { "XMLHttpRequest" });
            _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
            _httpClient.Handler.CookieContainer.Add(new Cookie("anime_update_advanced", "0", "/", "myanimelist.net"));

            // The website cookies, not the password. These used to be the same field, which meant a password
            // was being handed to the cookie jar on every sign-in made through the password form.
            var existingCookies = Credentials.WebSessionCookies;
            var success = false;

            if (!string.IsNullOrEmpty(existingCookies))
            {
                SetCookies(existingCookies);

                try
                {
                    // The body decides, not the status code. editprofile answers a request it cannot
                    // authorise with a redirect to the login page, and HttpClient follows the redirect,
                    // so the caller sees a 200 carrying the login page and used to take that as proof of
                    // a live session. Everything after that then failed on a page nobody could read.
                    var req = await _httpClient.GetAsync("https://myanimelist.net/editprofile.php?go=myoptions");
                    var body = await req.Content.ReadAsStringAsync();
                    success = req.IsSuccessStatusCode && !IsLoginPage(body);

                    if (!success)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "MALPLUS session check: status=" + (int)req.StatusCode +
                            " bodyLen=" + body.Length +
                            " looksLikeLogin=" + IsLoginPage(body));
                    }
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine("MALPLUS session check failed: " + e.GetType().Name);
                    success = false;
                }
            }

            if (!success)
            {
                ResourceLocator.DispatcherAdapter.Run(async () =>
                {
                    await ResourceLocator.MessageDialogProvider.ShowMessageDialogAsync(
                        "Failed to sign in. Please sign in again, MAL seems to have invalidated your current session :(",
                        "Error.");
                });

                throw new WebException($"Unable to authorize,");
            }

            await _httpClient.GetToken();

            return _httpClient;
        }

        /// <summary>
        ///     Whether a MAL response is the login page, which is what a request without a working
        ///     session is answered with.
        /// </summary>
        private static bool IsLoginPage(string body)
        {
            if (string.IsNullOrEmpty(body))
                return false;
            // The notice only appears on a refused request; the form name is the other half of the
            // same page, so a reworded notice still does not pass as a signed-in reply.
            return body.Contains("You must first login to see this page")
                   || body.Contains("<h1 class=\"h1\">Login</h1>")
                   || (body.Contains("login_name") && body.Contains("login_password"));
        }
}
