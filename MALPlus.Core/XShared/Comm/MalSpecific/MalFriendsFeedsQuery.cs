using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using MALPlus.Models.Models;
using MALPlus.Models.Models.MalSpecific;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.Comm.MalSpecific
{
    public class MalFriendsFeedsQuery
    {
        private static readonly Regex LinkRegex = new(@"/(anime|manga)/(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly IEnumerable<MalUser> _friends;
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(2);

        class FeedQuery : Query
        {
            public FeedQuery(string type, string username)
            {
                Request =
                    new Uri(
                        Uri.EscapeUriString(
                            $"https://myanimelist.net/rss.php?type={type}&u={username}"));
            }
        }

        public MalFriendsFeedsQuery(IEnumerable<MalUser> friends)
        {
            _friends = friends;
        }

        public async Task<List<UserFeedEntryModel>> GetFeeds()
        {
            try
            {
                var friendList = _friends?.Where(f => !string.IsNullOrWhiteSpace(f?.Name)).ToList()
                                 ?? new List<MalUser>();
                Console.WriteLine($"MALPLUS Feeds: querying {friendList.Count} friends/pinned");
                var tasks = new List<Task<List<UserFeedEntryModel>>>();
                foreach (var friend in friendList)
                {
                    tasks.Add(GetUserFeed(friend, "rw", isManga: false));
                    tasks.Add(GetUserFeed(friend, "rm", isManga: true));
                }

                await Task.WhenAll(tasks);
                var allParsed = tasks
                    .Where(task => task.Result != null)
                    .SelectMany(task => task.Result)
                    .ToList();

                var maxAge = Math.Max(1, Settings.FeedsMaxEntryAge);
                var nowUtc = DateTime.UtcNow;
                var recent = allParsed
                    .Where(e => nowUtc.Subtract(e.Date.ToUniversalTime()).TotalDays <= maxAge)
                    .OrderByDescending(e => e.Date)
                    .ToList();

                // When recent activity within FeedsMaxEntryAge is sparse (< 10 items), fall back to
                // the latest updates across friends (preferring last 365 days, then any parsed RSS item)
                // so users with low-frequency friends still see Anime & Manga activity (like Manga tab).
                List<UserFeedEntryModel> combined;
                if (recent.Count >= 10)
                {
                    combined = recent;
                }
                else
                {
                    var withinYear = allParsed
                        .Where(e => nowUtc.Subtract(e.Date.ToUniversalTime()).TotalDays <= 365)
                        .OrderByDescending(e => e.Date)
                        .Take(40)
                        .ToList();
                    combined = withinYear.Count > 0
                        ? withinYear
                        : allParsed.OrderByDescending(e => e.Date).Take(40).ToList();
                }

                Console.WriteLine($"MALPLUS Feeds: raw={allParsed.Count}, recent({maxAge}d)={recent.Count}, shown={combined.Count}");
                await EnrichCoversAndScoresAsync(combined);
                return combined;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MALPLUS Feeds GetFeeds exception: {ex.GetType().Name}: {ex.Message}");
                return new List<UserFeedEntryModel>();
            }
        }

        private async Task<List<UserFeedEntryModel>> GetUserFeed(MalUser user, string rssType, bool isManga)
        {
            await _semaphoreSlim.WaitAsync();
            try
            {
                var resp = await new FeedQuery(rssType, user.Name).GetRequestResponse();
                if (string.IsNullOrWhiteSpace(resp))
                {
                    await Task.Delay(350);
                    resp = await new FeedQuery(rssType, user.Name).GetRequestResponse();
                }

                var output = new List<UserFeedEntryModel>();
                if (string.IsNullOrWhiteSpace(resp) || !resp.Contains("<rss", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"MALPLUS Feeds empty/non-rss for {user.Name} ({rssType}): len={(resp?.Length ?? 0)}");
                    return output;
                }

                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(resp);
                var nodes = xmlDoc.GetElementsByTagName("item");
                foreach (XmlNode node in nodes)
                {
                    var pubDateText = node["pubDate"]?.InnerText;
                    if (!DateTime.TryParse(pubDateText, CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces, out var parsedDate))
                        continue;

                    var rawHeader = WebUtility.HtmlDecode(node["title"]?.InnerText ?? "").Trim();
                    var link = (node["link"]?.InnerText ?? "").Trim();
                    var desc = WebUtility.HtmlDecode(node["description"]?.InnerText ?? "").Trim();

                    var match = LinkRegex.Match(link);
                    if (!match.Success || !int.TryParse(match.Groups[2].Value, out var id) || id <= 0)
                        continue;

                    var parsedIsManga = string.Equals(match.Groups[1].Value, "manga", StringComparison.OrdinalIgnoreCase) || isManga;

                    var title = rawHeader;
                    var mediaType = parsedIsManga ? "Manga" : "";
                    var dashPos = rawHeader.LastIndexOf(" - ", StringComparison.Ordinal);
                    if (dashPos > 0)
                    {
                        title = rawHeader.Substring(0, dashPos).Trim();
                        var suffix = rawHeader.Substring(dashPos + 3).Trim();
                        if (!string.IsNullOrEmpty(suffix))
                            mediaType = suffix;
                    }

                    output.Add(new UserFeedEntryModel
                    {
                        User = user,
                        Date = parsedDate.ToLocalTime(),
                        Header = rawHeader,
                        Title = title,
                        MediaType = mediaType,
                        Link = link,
                        Id = id,
                        IsManga = parsedIsManga,
                        Description = desc
                    });
                }

                await Task.Delay(150);
                return output;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MALPLUS Feeds parse failed for {user.Name} ({rssType}): {ex.Message}");
                return null;
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        private static async Task EnrichCoversAndScoresAsync(List<UserFeedEntryModel> items)
        {
            if (items == null || items.Count == 0)
                return;

            // Deduplicate per (IsManga, Id) so repeated updates to the same series share metadata
            var groups = items
                .Where(x => x.Id > 0)
                .GroupBy(x => (x.IsManga, x.Id))
                .ToList();

            var missingAnime = new List<int>();
            var missingManga = new List<int>();
            var metaMap = new Dictionary<(bool isManga, int id), (string img, double score, string type)>();

            foreach (var g in groups)
            {
                var cached = await DataCache.RetrieveAnimeSearchResultsData(g.Key.Id.ToString(CultureInfo.InvariantCulture), !g.Key.IsManga);
                if (cached != null && !string.IsNullOrEmpty(cached.ImgUrl))
                {
                    metaMap[g.Key] = (cached.ImgUrl, cached.GlobalScore, cached.Type);
                }
                else if (g.Key.IsManga)
                {
                    missingManga.Add(g.Key.Id);
                }
                else
                {
                    missingAnime.Add(g.Key.Id);
                }
            }

            await BatchFetchAsync(missingAnime, isManga: false, metaMap);
            await BatchFetchAsync(missingManga, isManga: true, metaMap);

            foreach (var item in items)
            {
                if (!metaMap.TryGetValue((item.IsManga, item.Id), out var meta))
                    continue;
                if (!string.IsNullOrEmpty(meta.img))
                    item.EntryImgUrl = meta.img;
                if (meta.score > 0)
                    item.Score = meta.score;
                if (string.IsNullOrEmpty(item.MediaType) && !string.IsNullOrEmpty(meta.type))
                    item.MediaType = meta.type;
            }
        }

        private static async Task BatchFetchAsync(List<int> ids, bool isManga,
            Dictionary<(bool isManga, int id), (string img, double score, string type)> metaMap)
        {
            if (ids == null || ids.Count == 0)
                return;

            var prefix = isManga ? "manga" : "anime";
            for (var i = 0; i < ids.Count; i += 25)
            {
                var chunk = ids.Skip(i).Take(25).ToList();
                var endpoint = chunk.Count > 1
                    ? $"{prefix}/{chunk[0]}/full?ids={string.Join(",", chunk.Skip(1))}"
                    : $"{prefix}/{chunk[0]}/full";

                try
                {
                    var raw = await TenraiClient.GetRawJsonAsync(endpoint);
                    if (string.IsNullOrEmpty(raw))
                        continue;

                    using var doc = JsonDocument.Parse(raw);
                    var elements = new List<JsonElement>();
                    if (doc.RootElement.TryGetProperty("data", out var dataEl))
                    {
                        if (dataEl.ValueKind == JsonValueKind.Object)
                            elements.Add(dataEl);
                        else if (dataEl.ValueKind == JsonValueKind.Array)
                            elements.AddRange(dataEl.EnumerateArray());
                    }
                    if (doc.RootElement.TryGetProperty("included", out var incEl) &&
                        incEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var inc in incEl.EnumerateArray())
                            if (inc.ValueKind == JsonValueKind.Object)
                                elements.Add(inc);
                    }

                    foreach (var el in elements)
                    {
                        if (!el.TryGetProperty("mal_id", out var idProp) || idProp.ValueKind != JsonValueKind.Number)
                            continue;
                        var malId = idProp.GetInt32();
                        if (malId <= 0)
                            continue;

                        string img = null;
                        if (el.TryGetProperty("images", out var images) &&
                            images.TryGetProperty("jpg", out var jpg) &&
                            jpg.TryGetProperty("image_url", out var urlProp) &&
                            urlProp.ValueKind == JsonValueKind.String)
                            img = urlProp.GetString();

                        double score = 0;
                        if (el.TryGetProperty("score", out var scProp) && scProp.ValueKind == JsonValueKind.Number)
                            score = scProp.GetDouble();

                        string type = null;
                        if (el.TryGetProperty("type", out var tyProp) && tyProp.ValueKind == JsonValueKind.String)
                            type = tyProp.GetString();

                        metaMap[(isManga, malId)] = (img, score, type);
                    }
                }
                catch
                {
                }
            }
        }
    }
}
