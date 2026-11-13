using System.Text.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.Comm.Anime
{
    public class AnimeRecomendationsQuery : Query
    {
        private readonly bool _anime;
        private readonly bool _top;

        public AnimeRecomendationsQuery(bool anime = true, bool top = false)
        {
            _anime = anime;
            _top = top;
            Request =
                new Uri(Uri.EscapeUriString($"https://myanimelist.net/recommendations.php?{(_top ? "" : "s=recentrecs&")}t={(anime ? "anime" : "manga")}"));
        }

        public async Task<List<RecommendationData>> GetRecomendationsData()
        {
            // 1. Native Tenrai endpoint (structured JSON, fast)
            var tenraiData = await FetchFromTenraiAsync();
            if (tenraiData != null && tenraiData.Count > 0)
                return tenraiData;

            // 2. Fallback to HTML scraper
            return await FetchFromHtmlAsync();
        }

        private async Task<List<RecommendationData>> FetchFromTenraiAsync()
        {
            try
            {
                var output = new List<RecommendationData>();

                if (_top)
                {
                    // Fetch top 50 recommendations (pages 1 and 2, 25 items each)
                    for (int page = 1; page <= 2; page++)
                    {
                        var endpoint = $"top/recommendations?type={(_anime ? "anime" : "manga")}&page={page}";
                        var (items, hasNext) = await TenraiClient.GetPaginatedAsync(endpoint);
                        if (items == null || items.Count == 0)
                            break;

                        ParseTenraiItems(items, output);
                        if (!hasNext)
                            break;
                    }
                }
                else
                {
                    var endpoint = _anime ? "recommendations/anime?page=1" : "recommendations/manga?page=1";
                    var (items, _) = await TenraiClient.GetPaginatedAsync(endpoint);
                    if (items != null && items.Count > 0)
                    {
                        ParseTenraiItems(items, output);
                    }
                }

                if (output.Count > 0)
                {
                    await EnrichMetadataAsync(output);
                    return output;
                }

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MALPLUS recs Tenrai failed: {ex.Message}");
                return null;
            }
        }

        private void ParseTenraiItems(List<JsonElement> items, List<RecommendationData> output)
        {
            foreach (var item in items)
            {
                if (output.Count >= MaxRecommendations)
                    break;
                try
                {
                    if (!item.TryGetProperty("entry", out var entryArr) || entryArr.ValueKind != JsonValueKind.Array)
                        continue;

                    var entries = entryArr.EnumerateArray().ToList();
                    if (entries.Count < 2)
                        continue;

                    var dep = entries[0];
                    var rec = entries[1];

                    var depId = dep.TryGetProperty("mal_id", out var depIdProp) && depIdProp.ValueKind == JsonValueKind.Number ? depIdProp.GetInt32() : 0;
                    var depTitle = dep.TryGetProperty("title", out var depTitleProp) && depTitleProp.ValueKind == JsonValueKind.String ? depTitleProp.GetString() : "";
                    var depImg = GetImageUrl(dep);

                    var recId = rec.TryGetProperty("mal_id", out var recIdProp) && recIdProp.ValueKind == JsonValueKind.Number ? recIdProp.GetInt32() : 0;
                    var recTitle = rec.TryGetProperty("title", out var recTitleProp) && recTitleProp.ValueKind == JsonValueKind.String ? recTitleProp.GetString() : "";
                    var recImg = GetImageUrl(rec);

                    if (depId <= 0 || recId <= 0)
                        continue;

                    var content = item.TryGetProperty("content", out var cProp) && cProp.ValueKind == JsonValueKind.String ? cProp.GetString() : "";
                    var dateStr = item.TryGetProperty("date", out var dProp) && dProp.ValueKind == JsonValueKind.String ? dProp.GetString() : "";
                    var username = "";
                    if (item.TryGetProperty("user", out var uObj) && uObj.ValueKind == JsonValueKind.Object &&
                        uObj.TryGetProperty("username", out var unProp) && unProp.ValueKind == JsonValueKind.String)
                    {
                        username = unProp.GetString();
                    }

                    var votes = item.TryGetProperty("votes", out var vProp) && vProp.ValueKind == JsonValueKind.Number ? vProp.GetInt32() : 0;

                    output.Add(new RecommendationData
                    {
                        IsAnime = _anime,
                        DependentId = depId,
                        DependentTitle = depTitle,
                        DependentImageUrl = depImg,
                        RecommendationId = recId,
                        RecommendationTitle = recTitle,
                        RecommendationImageUrl = recImg,
                        RecommendedBy = username,
                        RecommendedWhen = FormatDate(dateStr),
                        Description = WebUtility.HtmlDecode(content).Trim(),
                        Votes = votes
                    });
                }
                catch
                {
                }
            }
        }

        private async Task EnrichMetadataAsync(List<RecommendationData> items)
        {
            try
            {
                var allIds = new HashSet<int>();
                foreach (var item in items)
                {
                    if (item.DependentId > 0) allIds.Add(item.DependentId);
                    if (item.RecommendationId > 0) allIds.Add(item.RecommendationId);
                }

                if (allIds.Count == 0) return;

                var idList = allIds.ToList();
                var metaDict = new Dictionary<int, (string Type, double Score, int Episodes)>();

                // Enrich in chunks of 25 items per batch request
                for (int i = 0; i < idList.Count; i += 25)
                {
                    var chunk = idList.Skip(i).Take(25).ToList();
                    var first = chunk[0];
                    var others = chunk.Skip(1).ToList();

                    var mediaPrefix = _anime ? "anime" : "manga";
                    var endpoint = others.Count > 0
                        ? $"{mediaPrefix}/{first}/full?ids={string.Join(",", others)}"
                        : $"{mediaPrefix}/{first}/full";

                    var raw = await TenraiClient.GetRawJsonAsync(endpoint);
                    if (string.IsNullOrEmpty(raw)) continue;

                    using var doc = JsonDocument.Parse(raw);
                    var entries = new List<JsonElement>();

                    if (doc.RootElement.TryGetProperty("data", out var dataEl))
                    {
                        if (dataEl.ValueKind == JsonValueKind.Object)
                            entries.Add(dataEl);
                        else if (dataEl.ValueKind == JsonValueKind.Array)
                            entries.AddRange(dataEl.EnumerateArray());
                    }

                    if (doc.RootElement.TryGetProperty("included", out var incEl) && incEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var inc in incEl.EnumerateArray())
                            if (inc.ValueKind == JsonValueKind.Object)
                                entries.Add(inc);
                    }

                    foreach (var entry in entries)
                    {
                        int malId = 0;
                        if (entry.TryGetProperty("mal_id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                            malId = idProp.GetInt32();
                        if (malId <= 0) continue;

                        string type = "";
                        if (entry.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
                            type = typeProp.GetString();

                        double score = 0;
                        if (entry.TryGetProperty("score", out var scoreProp) && scoreProp.ValueKind == JsonValueKind.Number)
                            score = scoreProp.GetDouble();

                        int eps = 0;
                        var epPropName = _anime ? "episodes" : "chapters";
                        if (entry.TryGetProperty(epPropName, out var epProp) && epProp.ValueKind == JsonValueKind.Number)
                            eps = epProp.GetInt32();

                        metaDict[malId] = (type, score, eps);
                    }
                }

                foreach (var item in items)
                {
                    if (metaDict.TryGetValue(item.DependentId, out var depMeta))
                    {
                        item.DependentType = depMeta.Type;
                        item.DependentScore = depMeta.Score;
                        item.DependentAllEpisodes = depMeta.Episodes;
                    }
                    if (metaDict.TryGetValue(item.RecommendationId, out var recMeta))
                    {
                        item.RecommendationType = recMeta.Type;
                        item.RecommendationScore = recMeta.Score;
                        item.RecommendationAllEpisodes = recMeta.Episodes;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS recs enrich failed: " + ex.Message);
            }
        }

        private static string FormatDate(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr))
                return string.Empty;

            if (DateTime.TryParse(dateStr, out var dt))
            {
                return dt.ToString("MMM d, yyyy");
            }
            return dateStr;
        }

        private static string GetImageUrl(JsonElement entry)
        {
            if (entry.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Object)
            {
                if (images.TryGetProperty("jpg", out var jpg) && jpg.ValueKind == JsonValueKind.Object &&
                    jpg.TryGetProperty("large_image_url", out var lProp) && lProp.ValueKind == JsonValueKind.String)
                    return lProp.GetString();
                if (images.TryGetProperty("jpg", out var jpg2) && jpg2.ValueKind == JsonValueKind.Object &&
                    jpg2.TryGetProperty("image_url", out var iProp) && iProp.ValueKind == JsonValueKind.String)
                    return iProp.GetString();
            }
            return "";
        }

        private async Task<List<RecommendationData>> FetchFromHtmlAsync()
        {
            var output = new List<RecommendationData>();
            var raw = await GetRequestResponse();
            if (string.IsNullOrEmpty(raw))
            {
                Console.WriteLine("MALPLUS recs: sin respuesta de MAL (GetRequestResponse devolvio vacio)");
                return null;
            }
            var doc = new HtmlDocument();
            doc.LoadHtml(raw);
            var recomNodes =
                doc.DocumentNode.Descendants("div")
                    .Where(
                        node =>
                            node.Attributes.Contains("class") &&
                            node.Attributes["class"].Value ==
                            "spaceit borderClass").Take(MaxRecommendations);
            Console.WriteLine($"MALPLUS recs: html={raw.Length} bloques={recomNodes.Count()}");

            foreach (var recomNode in recomNodes)
            {
                try
                {
                    var desc =
                        recomNode.ChildNodes.FirstOrDefault(
                            node =>
                                node.Name == "div" &&
                                node.Attributes.Contains("class") &&
                                node.Attributes["class"].Value ==
                                "spaceit recommendations-user-recs-text");
                    if (desc == null)
                        continue;

                    // Each block is a two cell table: the first cell is the "If you liked" entry the
                    // recommendation hangs off, the second is the entry being suggested. Reading them
                    // per cell rather than taking the first two titled anchors out of the whole block is
                    // what makes the covers available: they sit in the same cells as the titles, one img
                    // each, and the old shape never looked at them at all.
                    var cells = recomNode.Descendants("td").ToArray();
                    if (cells.Length < 2)
                        continue;

                    var dependent = ReadCell(cells[0]);
                    var recommended = ReadCell(cells[1]);
                    if (dependent == null || recommended == null)
                        continue;

                    var byline =
                        recomNode.ChildNodes.FirstOrDefault(
                            node =>
                                node.Name == "div" &&
                                node.Attributes.Contains("class") &&
                                node.Attributes["class"].Value.Contains("lightLink"));
                    ReadByline(byline, out var recommendedBy, out var recommendedWhen);

                                        output.Add(new RecommendationData
                    {
                        IsAnime = _anime,
                        DependentId = dependent.Value.Id,
                        RecommendationId = recommended.Value.Id,
                        DependentTitle = dependent.Value.Title,
                        RecommendationTitle = recommended.Value.Title,
                        DependentImageUrl = dependent.Value.ImageUrl,
                        RecommendationImageUrl = recommended.Value.ImageUrl,
                        RecommendedBy = recommendedBy,
                        RecommendedWhen = recommendedWhen,
                        Description = WebUtility.HtmlDecode(desc.InnerText).Trim()
                    });
                }
                catch (Exception)
                {
                    // One malformed block is not worth losing the other ninety-nine over.
                }
            }

            Console.WriteLine($"MALPLUS recs: parseados={output.Count}");
            return output;
        }

        /// <summary>
        ///     How many recommendations a single read may return.
        /// </summary>
        /// <remarks>
        ///     Was thirty, which was sized for the old page that put a dozen of them on screen at once.
        ///     The page now shows one at a time, so thirty was a third of what the feed actually offers.
        /// </remarks>
        private const int MaxRecommendations = 100;

        private static (int Id, string Title, string ImageUrl)? ReadCell(HtmlNode cell)
        {
            var link = cell.Descendants("a").FirstOrDefault(node => node.InnerText.Trim().Length > 0);
            if (link == null)
                return null;

            var href = link.GetAttributeValue("href", string.Empty);
            var tokens = href.Split('/');
            var ids = tokens.Select(s => int.TryParse(s, out var id) ? id : -1).ToList();
            var id = ids.FirstOrDefault(i => i > 0);
            if (id <= 0)
                return null;

            // The real URL is in data-src; src is the lazyload spacer.gif. Reading src gave every
            // cover the same 1x1 spacer and no picture at all. What the markup points at is the t
            // variant, a fifty pixel thumbnail, so it goes through the shared normaliser to get the
            // large one before it ever reaches a card.
            var image = cell.Descendants("img")
                .Select(node => node.GetAttributeValue("data-src", string.Empty))
                .FirstOrDefault(src => !string.IsNullOrWhiteSpace(src));

            return (id, WebUtility.HtmlDecode(link.InnerText.Trim()), MalImageUrl.Normalize(image));
        }

        private static void ReadByline(HtmlNode byline, out string user, out string when)
        {
            user = null;
            when = null;
            if (byline == null)
                return;

            var profile = byline.Descendants("a").FirstOrDefault();
            if (profile != null)
                user = WebUtility.HtmlDecode(profile.InnerText.Trim());

            // "Anime rec by someone - 2 hours ago". The time is whatever trails the dash, so it does not
            // have to be parsed, and the report link inside the same div never becomes part of it.
            var text = WebUtility.HtmlDecode(byline.InnerText);
            var dash = text.LastIndexOf('-');
            if (dash >= 0 && dash < text.Length - 1)
                when = text.Substring(dash + 1).Trim();
        }
    }
}
