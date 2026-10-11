using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using HttpClient = System.Net.Http.HttpClient;
using HttpStatusCode = System.Net.HttpStatusCode;

namespace MALPlus.XShared.Comm.MagicalRawQueries
{
    /// <summary>
    ///     Client wrapped with token.
    /// </summary>
    public class CsrfHttpClient : HttpClient
    {
        public CsrfHttpClient(HttpClientHandler handler) : base(handler)
        {
            Handler = handler;
        }

        public string Token { get; set; }
        public bool Disabled { get; set; }

        public HttpClientHandler Handler { get; }



        protected new void Dispose()
        {
            //it's not disposable
        }

        public void ExpiredDispose()
        {
            //base.Dispose();
        }

        public new Task<HttpResponseMessage> GetAsync(string uri)
        {
            if (!Disabled)
            {
                return base.GetAsync(uri);
            }
            return  new Task<HttpResponseMessage>(() => new HttpResponseMessage(HttpStatusCode.Forbidden));
        }

        public new Task<HttpResponseMessage> PostAsync(string uri,HttpContent content)
        {
            if (!Disabled)
            {
                return base.PostAsync(uri,content);
            }
            return new Task<HttpResponseMessage>(() => new HttpResponseMessage(HttpStatusCode.Forbidden));
        }

        public async Task GetToken()
        {

            var raw = await GetAsync("https://myanimelist.net/");

            if (!ParseTokenFromHtml(await raw.Content.ReadAsStringAsync()))
            {
                // Thrown here rather than allowed to leave ParseTokenFromHtml as an InvalidOperation
                // from a First() over a document with no csrf_token in it. The login page has none, so
                // this is the shape a failed session takes, and the message says so because the caller
                // acts on it.
                throw new WebException("No csrf_token found; the response is most likely the login page.");
            }
        }

        /// <summary>
        ///     Reads the CSRF token out of a MAL page.
        /// </summary>
        /// <returns>
        ///     False when the page carries no token, which is what the login page does.
        /// </returns>
        public bool ParseTokenFromHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return false;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            foreach (var head in doc.DocumentNode.Descendants("head"))
            {
                foreach (var node in head.ChildNodes)
                {
                    if (!node.Attributes.Contains("name"))
                        continue;
                    if (node.Attributes["name"].Value != "csrf_token")
                        continue;

                    // A token attribute that does not resolve is as good as no token, and reads as the
                    // same thing: a page that is not the signed in one.
                    var content = node.Attributes["content"];
                    if (content == null || string.IsNullOrWhiteSpace(content.Value))
                        continue;

                    Token = content.Value;
                    return true;
                }
            }

            return false;
        }

        protected override void Dispose(bool disposing)
        {
            //base.Dispose(disposing);
        }

        //private void ProcessCookies(HttpResponseMessage raw)
        //{
        //    bool changed = false;
        //    IEnumerable<string> values = new List<string>();
        //    if (raw.Headers.TryGetValues("set-cookie", out values))
        //    {
        //        foreach (var value in values)
        //        {
        //            if (value.Contains("MALHLOGSESSID"))
        //            {
        //                MalLogSessionIdCookie = value.Substring(value.IndexOf("MALHLOGSESSID"), value.IndexOf(';'));
        //                changed = true;
        //            }
        //            else if (value.Contains("MALSESSIONID"))
        //            {
        //                MalSessionIdCookie = value.Substring(value.IndexOf("MALSESSIONID"), value.IndexOf(';'));
        //                changed = true;
        //            }
        //        }
        //    }
        //    if (changed)
        //    {
        //        if (DefaultRequestHeaders.Contains("Cookie"))
        //            DefaultRequestHeaders.Remove("Cookie");

        //        if (MalSessionIdCookie == null)
        //            DefaultRequestHeaders.Add("Cookie", $"{MalLogSessionIdCookie};");
        //        else
        //            DefaultRequestHeaders.Add("Cookie", $"{MalSessionIdCookie}; {MalLogSessionIdCookie}");
        //    }
        //}
    }
}