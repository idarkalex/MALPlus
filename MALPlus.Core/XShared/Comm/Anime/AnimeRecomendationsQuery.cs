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

        public AnimeRecomendationsQuery(bool anime = true)
        {
            _anime = anime;
            Request =
                new Uri(Uri.EscapeUriString($"https://myanimelist.net/recommendations.php?s=recentrecs&t={(anime ? "anime" : "manga")}"));
        }

public async Task<List<RecommendationData>> GetRecomendationsData()
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
