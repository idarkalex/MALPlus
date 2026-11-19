using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MALPlus.Models.Models.MalSpecific;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.Comm.Articles
{
    public class MalArticleQuery : Query
    {
        private readonly string _title;
        private readonly MalNewsType _type;

        public MalArticleQuery(string url, string title, MalNewsType type)
        {
            _type = type;
            _title = title;
            Request =
                new Uri(Uri.EscapeUriString(url));
        }

        public async Task<string> GetArticleHtml()
        {
            var possibleData = await DataCache.RetrieveArticleContentData(_title, _type);
            if (possibleData != null)
                return possibleData;

            // 1. Modern Tenrai API: /news/{id}?format=html or /articles/{id}?format=html
            try
            {
                var match = Regex.Match(Request.AbsolutePath, @"/(?:news|featured|articles)/(\d+)");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var id))
                {
                    var endpoint = _type == MalNewsType.News ? $"news/{id}?format=html" : $"articles/{id}?format=html";
                    var json = await TenraiClient.GetRawJsonAsync(endpoint);
                    if (!string.IsNullOrEmpty(json))
                    {
                        using var docJson = JsonDocument.Parse(json);
                        if (docJson.RootElement.TryGetProperty("data", out var data) &&
                            data.TryGetProperty("content", out var content) &&
                            content.TryGetProperty("html", out var htmlElem) &&
                            htmlElem.ValueKind == JsonValueKind.String)
                        {
                            var html = htmlElem.GetString();
                            if (!string.IsNullOrWhiteSpace(html))
                            {
                                DataCache.SaveArticleContentData(_title, html, _type);
                                return html;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fall back cleanly to web scrape if Tenrai fails or entry is not cached
            }

            // 2. Legacy fallback: HTML scraping from MyAnimeList
            var raw = await GetRequestResponse();
            if (string.IsNullOrEmpty(raw))
                return null;
            var doc = new HtmlDocument();
            doc.LoadHtml(raw);
            string htmlData;
            try
            {
                htmlData = doc.FirstOfDescendantsWithClass("div", "news-container").OuterHtml;
                DataCache.SaveArticleContentData(_title, htmlData, _type);
            }
            catch (Exception)
            {
                htmlData = "Something went wrong";
            }

            return htmlData;
        }
    }
}
