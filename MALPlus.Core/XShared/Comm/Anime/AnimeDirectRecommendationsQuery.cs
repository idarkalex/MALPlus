using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.Comm.Anime
{
    public class AnimeDirectRecommendationsQuery : Query
    {
        private static int BackgroundId;
        private static int BeginBackgroundId() => Interlocked.Increment(ref BackgroundId);
        private static readonly SemaphoreSlim MetadataEnrichmentGate = new SemaphoreSlim(1);
        private static readonly ConcurrentDictionary<string, Task> BackgroundEnrichment = new();
        private static readonly TimeSpan EnrichmentTimeout = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan BackgroundEnrichmentTimeout = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan MetadataRequestTimeout = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan DescriptionRequestTimeout = TimeSpan.FromSeconds(4);

        /// <summary>
        ///     Pause between enrichment rows.
        /// </summary>
        /// <remarks>
        ///     Tenrai answers 429 when the request rate crosses its limit, and its client answers one
        ///     by retrying internally: it waits up to two seconds and tries again, up to a few
        ///     attempts. The timeout this query hands it used to be three seconds, which cut that
        ///     ladder off mid-way and turned every throttled row into a thrown 429. Six seconds is
        ///     long enough for the client's own backoff to finish the job, and the gap here keeps a
        ///     thirty row list from walking into the limit all by itself.
        /// </remarks>
        private static readonly TimeSpan EnrichmentItemDelay = TimeSpan.FromMilliseconds(300);

        /// <summary>How long to sit still when the source is throttling before asking again.</summary>
        private static readonly TimeSpan ThrottlePause = TimeSpan.FromSeconds(8);

        /// <summary>
        /// Every recommendation past the first <see cref="MetadataEnrichmentBudget"/> needs its own
        /// Tenrai round trip (the MAL scraper carries neither the format nor the episode count), and
        /// TenraiClient serialises globally at ~500ms per request.
        /// <para>
        /// The gate is one deep and the loop awaits each item on its own, deliberately: enriching in
        /// parallel is what used to blow the <see cref="EnrichmentTimeout"/> and blank the tab, and
        /// the cost of being polite is that the last items arrive later. Both stop at the budget.
        /// </para>
        /// </summary>
        private const int MaxReturnedItems = 30;
        private const int MetadataEnrichmentBudget = 30;
        private readonly int _animeId;
        private readonly bool _animeMode;

        public AnimeDirectRecommendationsQuery(int id, bool anime = true)
        {
            Request =
                new Uri(
                    Uri.EscapeUriString($"https://myanimelist.net/{(anime ? "anime" : "manga")}/{id}/whatever/userrecs"));
            _animeId = id;
            _animeMode = anime;
        }

        public async Task<List<DirectRecommendationData>> GetDirectRecommendations(bool force = false)
        {
            var output = force
                ? new List<DirectRecommendationData>()
                : await DataCache.RetrieveDirectRecommendationData(_animeId, _animeMode) ??
                  new List<DirectRecommendationData>();
            // A cached list is not a finished list. This used to hand the cache straight back, which
            // is why a list saved before the enrichment existed came back with no format and no
            // episode count and kept coming back that way: nothing between here and the return
            // ever filled the blanks, and the cache TTL is not short. So the cached list is enriched
            // on the way out too, and re-saved so the badges only ever have to be earned once.
            if (output.Count != 0)
            {
                using var cachedCancellation = new CancellationTokenSource(EnrichmentTimeout);
                try
                {
                    await PopulateMetadataAsync(output, cachedCancellation.Token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception)
                {
                }
                await DataCache.SaveDirectRecommendationsData(_animeId, output, _animeMode);
                Console.WriteLine($"MALPLUS RecQuery anime={_animeId} cache yielded n={output.Count} type={output.Count(x => !string.IsNullOrEmpty(x.MediaType))} eps={output.Count(x => x.AllEpisodes > 0)}");
                StartBackgroundEnrichment(output);
                return output;
            }

            // The MAL userrecs page is the only source that carries the recommendation
            // comment (the review text the card shows), so it leads. Tenrai only fills
            // the metadata the scraper cannot see.
            output = await FetchDescriptionsFromMalAsync(CancellationToken.None);
            if (output != null && output.Count > 0)
            {
                using var cancellation = new CancellationTokenSource(EnrichmentTimeout);
                try
                {
                    await PopulateMetadataAsync(output, cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception)
                {
                }
                await DataCache.SaveDirectRecommendationsData(_animeId, output, _animeMode);
                StartBackgroundEnrichment(output);
                return output;
            }

            output = await FetchFromTenraiAsync();
            if (output != null && output.Count > 0)
            {
                try
                {
                    await EnrichRecommendationsAsync(output);
                }
                catch (Exception)
                {
                }
                await DataCache.SaveDirectRecommendationsData(_animeId, output, _animeMode);
            }
            return output ?? new List<DirectRecommendationData>();
        }

        private async Task<List<DirectRecommendationData>> FetchFromTenraiAsync()
        {
            try
            {
                var endpoint = _animeMode ? $"anime/{_animeId}/recommendations" : $"manga/{_animeId}/recommendations";
                var data = await TenraiClient.GetDataArrayAsync(endpoint);
                if (data.ValueKind != JsonValueKind.Array)
                    return null;

                var output = new List<DirectRecommendationData>();
                foreach (var item in data.EnumerateArray())
                {
                    try
                    {
                        var current = new DirectRecommendationData();
                        if (!item.TryGetProperty("entry", out var entry) || entry.ValueKind != JsonValueKind.Object)
                            continue;

                        var malId = entry.TryGetProperty("mal_id", out var idProp) && idProp.ValueKind == JsonValueKind.Number
                            ? idProp.GetInt32()
                            : 0;
                        if (malId <= 0)
                            continue;
                        current.Id = malId;
                        current.Title = WebUtility.HtmlDecode(
                            entry.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String
                                ? titleProp.GetString()
                                : "");

                        var entryUrl = entry.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
                            ? urlProp.GetString()
                            : "";
                        if (entryUrl.Contains("/manga/"))
                            current.Type = RelatedItemType.Manga;
                        else if (entryUrl.Contains("/anime/"))
                            current.Type = RelatedItemType.Anime;
                        else
                            current.Type = RelatedItemType.Unknown;
                        current.MediaType = NormalizeMediaType(GetString(entry, "media_type"));
                        if (string.IsNullOrEmpty(current.MediaType))
                        {
                            var entryType = GetString(entry, "type");
                            if (!string.Equals(entryType, "anime", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(entryType, "manga", StringComparison.OrdinalIgnoreCase))
                                current.MediaType = NormalizeMediaType(entryType);
                        }

                        if (entry.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Object &&
                            images.TryGetProperty("jpg", out var jpg) && jpg.ValueKind == JsonValueKind.Object &&
                            jpg.TryGetProperty("image_url", out var imgUrl) && imgUrl.ValueKind == JsonValueKind.String)
                            current.ImageUrl = MalImageUrl.Normalize(imgUrl.GetString());

                        output.Add(current);
                    }
                    catch (Exception)
                    {
                        // skip malformed recommendation
                    }
                }
                return output.Count > 0 ? output : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        ///     Keeps filling the blanks long after the tab is already on screen.
        /// </summary>
        /// <remarks>
        ///     Tenrai serialises globally at roughly half a second a call, so the eight seconds
        ///     <see cref="EnrichmentTimeout" /> allows is about sixteen rows and the rest of the list
        ///     arrived with no format and no count, which is what left the badges blank from roughly
        ///     the twentieth card onwards no matter how long the tab was left open. Blocking the tab
        ///     longer instead just moves the wait somewhere more visible.
        ///     <para>
        ///         So the tab shows what the scraper gave it immediately, and this keeps going in the
        ///         background. Each row raises its own property changed as its half lands, which is
        ///         what makes the chip and the counter fill in on the card already on screen, and the
        ///         list is re-saved as it goes so the work is paid for once.
        ///     </para>
        /// </remarks>
        private void StartBackgroundEnrichment(List<DirectRecommendationData> output)
        {
            if (output.All(item => item.Id > 0 &&
                                   !string.IsNullOrEmpty(item.MediaType) && item.AllEpisodes > 0))
                return;

            var key = $"{_animeId}|{_animeMode}|{BeginBackgroundId()}";
            BackgroundEnrichment.GetOrAdd(key, _ => EnrichInBackgroundAsync(output, key));
        }

        private async Task EnrichInBackgroundAsync(List<DirectRecommendationData> output, string key)
        {
            try
            {
                using var cancellation = new CancellationTokenSource(BackgroundEnrichmentTimeout);
                while (!cancellation.IsCancellationRequested)
                {
                    var before = output.Count(item => item.Id > 0 &&
                                                     (string.IsNullOrEmpty(item.MediaType) ||
                                                      item.AllEpisodes == 0));
                    await PopulateMetadataAsync(output, cancellation.Token);
                    await DataCache.SaveDirectRecommendationsData(_animeId, output, _animeMode);

                    var after = output.Count(item => item.Id > 0 &&
                                                    (string.IsNullOrEmpty(item.MediaType) ||
                                                     item.AllEpisodes == 0));
                    Console.WriteLine($"MALPLUS RecBg anime={_animeId} pending {before} -> {after}");
                    if (after == 0)
                        break;
                    // A pass that filled nothing is usually the source saying 429, not a row that
                    // will never resolve. Retrying straight away is what earns the next 429, so this
                    // waits it out and tries again until the background budget runs out.
                    if (after >= before)
                        await Task.Delay(ThrottlePause, cancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                // Background work that cannot reach the network is not worth surfacing: the rows keep
                // whatever the scraper gave them and the next open tries again.
            }
            finally
            {
                BackgroundEnrichment.TryRemove(key, out _);
            }
        }

        private async Task EnrichRecommendationsAsync(List<DirectRecommendationData> output)
        {
            if (output == null || output.Count == 0)
                return;

            using var cancellation = new CancellationTokenSource(EnrichmentTimeout);
            try
            {
                var work = Task.WhenAll(
                    EnrichDescriptionsAsync(output, cancellation.Token),
                    PopulateMetadataAsync(output, cancellation.Token));
                var completed = await Task.WhenAny(work, Task.Delay(EnrichmentTimeout));
                if (completed == work)
                    await work;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }
        }

        private async Task EnrichDescriptionsAsync(List<DirectRecommendationData> output,
            CancellationToken cancellationToken)
        {
            try
            {
                var scraped = await FetchDescriptionsFromMalAsync(cancellationToken);
                if (scraped == null || scraped.Count == 0)
                    return;
                cancellationToken.ThrowIfCancellationRequested();
                var byKey = scraped.Where(r => r.Id > 0)
                    .GroupBy(r => (r.Type, r.Id))
                    .ToDictionary(g => g.Key, g => g.First().Description);
                var byId = scraped.Where(r => r.Id > 0)
                    .GroupBy(r => r.Id)
                    .ToDictionary(g => g.Key, g => g.First().Description);
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var item in output)
                {
                    if (!byKey.TryGetValue((item.Type, item.Id), out var description))
                        byId.TryGetValue(item.Id, out description);
                    if (!string.IsNullOrEmpty(description))
                        item.Description = description;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }
        }

        private async Task PopulateMetadataAsync(List<DirectRecommendationData> output,
            CancellationToken cancellationToken)
        {
            // One at a time, and only the ones still missing either half. The gate is one deep on
            // purpose: enriching in parallel is what used to blow the enrichment budget and blank
            // the whole tab.
            var pending = output
                .Where(item => item.Id > 0 &&
                               (string.IsNullOrEmpty(item.MediaType) || item.AllEpisodes == 0))
                .Take(MetadataEnrichmentBudget)
                .ToList();

            foreach (var item in pending)
            {
                try
                {
                    await PopulateMetadataAsync(item, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    // One dead recommendation entry must not take the rest with it.
                }

                try
                {
                    await Task.Delay(EnrichmentItemDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task PopulateMetadataAsync(DirectRecommendationData item,
            CancellationToken cancellationToken)
        {
            await MetadataEnrichmentGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var animeMode = item.Type == RelatedItemType.Manga ? false : _animeMode;
                var cached = await DataCache.RetrieveAnimeSearchResultsData(item.Id.ToString(), animeMode);
                cancellationToken.ThrowIfCancellationRequested();

                if (cached != null)
                {
                    if (string.IsNullOrEmpty(item.MediaType) && !string.IsNullOrEmpty(cached.Type))
                        item.MediaType = NormalizeMediaType(cached.Type);
                    if (item.AllEpisodes == 0 && cached.AllEpisodes > 0)
                        item.AllEpisodes = cached.AllEpisodes;

                    // Only skip the round trip once the entry itself is whole. Asking the cache whether it
                    // knew anything at all was the bug: a search cache that carried the format but
                    // no episode count, or the reverse, ended the enrichment with the other half
                    // still blank. What matters is what the card now has, not what the cache had.
                    if (!string.IsNullOrEmpty(item.MediaType) && item.AllEpisodes > 0)
                        return;
                }

                var endpoint = animeMode ? $"anime/{item.Id}/full" : $"manga/{item.Id}/full";
                var data = await TenraiClient.GetDataAsync(endpoint, MetadataRequestTimeout);
                cancellationToken.ThrowIfCancellationRequested();
                if (data.ValueKind != JsonValueKind.Object)
                    return;

                if (string.IsNullOrEmpty(item.MediaType))
                    item.MediaType = NormalizeMediaType(GetString(data, "type"));

                // A manga counts chapters, in its own field, so the two halves are not one key.
                // These are the same names the details query reads out of this very payload,
                // AnimeGeneralDetailsQuery: "episodes" and "chapters". An earlier guess of
                // "total_episodes" matched nothing in the response and left every badge empty.
                if (item.AllEpisodes == 0)
                    item.AllEpisodes = animeMode
                        ? ReadInt(data, "episodes")
                        : ReadInt(data, "chapters");

                if (string.IsNullOrEmpty(item.MediaType) && item.AllEpisodes == 0)
                    Console.WriteLine($"MALPLUS RecEnrich id={item.Id} '{item.Title}' EMPTY type+eps from tenrai/{endpoint}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MALPLUS RecEnrich id={item.Id} '{item.Title}' FAILED {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                MetadataEnrichmentGate.Release();
            }
        }

        /// <summary>
        ///     Reads a whole number that may be missing, null, or not a number at all, as 0.
        /// </summary>
        private static int ReadInt(JsonElement element, string name)
            => element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
                ? prop.GetInt32()
                : 0;

        private async Task<List<DirectRecommendationData>> FetchDescriptionsFromMalAsync(
            CancellationToken cancellationToken)
        {
            var output = new List<DirectRecommendationData>();
            string raw;
            try
            {
                using var timeout = new CancellationTokenSource(DescriptionRequestTimeout);
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, timeout.Token);
                using var response = await _client.GetAsync(Request, cancellation.Token);
                if (!response.IsSuccessStatusCode)
                    return output;
                raw = await response.Content.ReadAsStringAsync(cancellation.Token);
            }
            catch (Exception)
            {
                return output;
            }

            if (string.IsNullOrEmpty(raw))
                return output;

            var doc = new HtmlDocument();
            doc.LoadHtml(raw);
            try
            {
                var recommNodes = doc.DocumentNode.Descendants("div")
                    .Where(
                        node =>
                            node.Attributes.Contains("class") &&
                            node.Attributes["class"].Value ==
                            "borderClass").Take(Settings.RecommsToPull).Take(MaxReturnedItems);

                foreach (var recommNode in recommNodes)
                {
                    try
                    {
                        var current = new DirectRecommendationData();

                        var tds = recommNode.Descendants("td").Take(2).ToList();
                         var img = tds[0].Descendants("img").First().Attributes["data-src"].Value;
                         if (!img.Contains("questionmark"))
                         {
                             img = Regex.Replace(img, @"\/r\/\d+x\d+", "");
                             var qPos = img.IndexOf('?');
                             if (qPos > 0) img = img.Substring(0, qPos);
                             current.ImageUrl = img;
                         }
                        current.Description = WebUtility.HtmlDecode(tds[1].Descendants("div").First(
                            node =>
                                node.Attributes.Contains("class") &&
                                node.Attributes["class"].Value ==
                                "borderClass bgColor1")
                            .Descendants("div")
                            .First().InnerText.Trim().Replace("&nbsp", "").Replace("read more", ""));
                        current.Description = current.Description.Substring(0, current.Description.Length - 1);
                        var titleNode = tds[1].ChildNodes[3].Descendants("a").First();
                        current.Title = titleNode.Descendants("strong").First().InnerText.Trim();
                        var link = titleNode.Attributes["href"].Value.Split('/');
                        current.Id = Convert.ToInt32(link[4]);
                        current.Type = link[3] == "anime"
                            ? RelatedItemType.Anime
                            : link[3] == "manga" ? RelatedItemType.Manga : RelatedItemType.Unknown;
                        output.Add(current);
                    }
                    catch (Exception)
                    {
                        //who knows...raw html is scary
                    }
                }
            }
            catch (Exception)
            {
                //something we wrong
            }

            return output;
        }

        private static string GetString(JsonElement element, string property)
        {
            return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : "";
        }

        private static string NormalizeMediaType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return type;
            return type switch
            {
                "tv" => "TV",
                "tv_special" => "TV Special",
                "movie" => "Movie",
                "ova" => "OVA",
                "ona" => "ONA",
                "special" => "Special",
                "music" => "Music",
                "cm" => "Commercial",
                "pv" => "PV",
                "manga" => "Manga",
                "novel" => "Novel",
                "light_novel" => "Light Novel",
                "one_shot" => "One-shot",
                "oneshot" => "One-shot",
                "doujinshi" => "Doujinshi",
                "doujin" => "Doujinshi",
                "manhwa" => "Manhwa",
                "manhua" => "Manhua",
                _ => type
            };
        }

    }
}
