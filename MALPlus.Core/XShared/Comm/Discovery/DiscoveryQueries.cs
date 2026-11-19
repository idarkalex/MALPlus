using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MALPlus.XShared.Comm.Discovery
{
public class DiscoveryItem
        {
            public int Id { get; set; }
            public string Title { get; set; }
            public string ImgUrl { get; set; }
            public string Type { get; set; }
            public string Subtitle { get; set; }
            public bool IsManga { get; set; }
            public int PopularityRank { get; set; }

            /// <summary>Community score, 0 when the source omits it. Drives the gem's minimum.</summary>
            public double Score { get; set; }

            /// <summary>Members on the library list, 0 when unknown. Drives the gem's ceiling.</summary>
            public int Members { get; set; }

            /// <summary>Year the entry started airing/publishing, 0 when unknown.</summary>
            public int Year { get; set; }
            public int AllEpisodes { get; set; }
        /// <summary>Direct media url, only set for the OP/ED discovery entries.</summary>
        public string ThemeUrl { get; set; }
    }

    public class RandomFilter
    {
        public List<int> GenreIds { get; set; } = new List<int>();

        /// <summary>
        ///     The media formats the reader is willing to be shown, as MAL spells them.
        /// </summary>
        /// <remarks>
        ///     A list rather than the single value it used to be, because picking several is a normal
        ///     thing to want: "TV or OVA", "specials too". Tenrai accepts them comma separated, verified:
        ///     type=tv,ova comes back a real mix rather than the first value alone, which is the usual
        ///     way this API quietly ignores a filter.
        ///     <para>
        ///         Empty means no format filter at all. The old single Format property was read here so
        ///         that a filter saved before this change still narrows things down instead of quietly
        ///         becoming "everything".
        ///     </para>
        /// </remarks>
        public List<string> Formats { get; set; } = new List<string>();

        public string Format { get; set; } = "";

        /// <summary>Every format the picker offers, in the order it shows them.</summary>
        public static readonly string[] AnimeFormats =
            { "", "tv", "movie", "ova", "special", "ona", "music" };

        public static readonly string[] MangaFormats =
            { "", "manga", "novel", "lightnovel", "oneshot", "doujinshi", "manhwa", "manhua" };

        /// <summary>
        ///     What actually goes in the query: the list when there is one, otherwise the single value a
        ///     filter stored before the multi-select existed.
        /// </summary>
        public string FormatsQuery()
        {
            if (Formats != null && Formats.Count > 0)
                return string.Join(",", Formats);
            return Format ?? "";
        }
        public int MinYear { get; set; }
        public int MaxYear { get; set; }
        public double MinScore { get; set; } = 0;
        public double MaxScore { get; set; } = 0;
    }

    /// <summary>
    ///     Trending anime for the Discover hub, powered exclusively by Tenrai.
    ///     Ranked by net members gained over the past 7 days (members_gained_7d).
    /// </summary>
    public static class DiscoveryTrendingQuery
    {
        public static string LastError { get; private set; } = "";

        public static async Task<List<DiscoveryItem>> GetAsync(int limit = 10)
        {
            var output = new List<DiscoveryItem>();
            LastError = "";
            try
            {
                var url = ContentFilter.Query(
                    $"anime?status=airing&order_by=members_gained_7d&sort=desc&page=1&limit={limit}");

                var raw = await TenraiClient.GetRawJsonAsync(url);
                if (string.IsNullOrEmpty(raw))
                {
                    LastError = "Tenrai returned empty response.";
                    return output;
                }

                using var doc = JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    data.ValueKind != JsonValueKind.Array)
                {
                    LastError = "Invalid Tenrai JSON envelope.";
                    return output;
                }

                int rank = 1;
                foreach (var entry in data.EnumerateArray())
                {
                    var item = DiscoveryRandomQuery.ParseCatalogEntry(entry, false);
                    if (item == null)
                        continue;

                    // Justify the trend with rank and formatted members so users know why it's trending:
                    var memberStr = item.Members >= 1_000_000
                        ? $"{item.Members / 1_000_000.0:F1}M"
                        : item.Members >= 1_000
                            ? $"{item.Members / 1_000.0:F1}K"
                            : item.Members.ToString();

                    item.Subtitle = $"#{rank} in weekly member growth · {memberStr} members";
                    output.Add(item);
                    rank++;

                    if (output.Count >= limit)
                        break;
                }
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name;
            }

            return output;
        }
    }

    /// <summary>
    /// Compatibility alias redirecting to native Tenrai trending.
    /// </summary>
    public static class AniListTrendingQuery
    {
        public static string LastError => DiscoveryTrendingQuery.LastError;
        public static Task<List<DiscoveryItem>> GetAsync(int limit = 10) =>
            DiscoveryTrendingQuery.GetAsync(limit);
    }

    /// <summary>
    /// </summary>
    public static class DiscoveryRandomQuery
    {
        private static readonly Random Rng = new Random();

        /// <summary>
        /// What the community is watching right now: currently airing,
        /// most favourited first. Used as the OP/ED source.
        /// </summary>
        public static async Task<List<DiscoveryItem>> GetTopAiringAsync(int limit = 10)
        {
            var output = new List<DiscoveryItem>();
            try
            {
                var raw = await TenraiClient.GetRawJsonAsync(
                    ContentFilter.Query("anime?status=airing&order_by=favorites&sort=desc&page=1&limit=" + limit));
                if (string.IsNullOrEmpty(raw))
                    return output;
                using var doc = JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    data.ValueKind != JsonValueKind.Array)
                    return output;
                foreach (var entry in data.EnumerateArray())
                {
                    var item = ParseCatalogEntry(entry, false);
                    if (item != null)
                        output.Add(item);
                }
            }
            catch
            {
            }
            return output;
        }

        //Tenrai recomputes pagination from the page size: anime?sfw reports 27341 pages with
        //limit=1 but only ~1094 with limit=25, and asking for a page past the real last one is a
        //400 rather than an empty page. The page count therefore has to be resolved with the very
        //same limit the fetch uses, or the random page is out of range almost every time.
        private const int PageSize = 25;

        //last_visible_page is also not a usable bound on its own. manga?sfw advertises 2369 pages
        //of 25, yet page 1000 (offset 25,000) is served and page 2000 (offset 50,000) is a 400:
        //there is a hard offset cap that the pagination metadata does not mention. The random pick
        //is confined to an offset known to be served, which is still a 20k entry pool.
        private const int MaxOffset = 20000;

        public static async Task<List<DiscoveryItem>> GetAsync(bool manga, RandomFilter filter,
            int minScore, int maxPopularityRank, int limit = 20, IEnumerable<int> excludeIds = null)
        {
            var output = new List<DiscoveryItem>();
            try
            {
                var media = manga ? "manga" : "anime";
                var effectiveMinScore = minScore > 0 ? minScore : filter?.MinScore ?? 0;
                var effectiveMaxScore = filter?.MaxScore ?? 0;
                var parts = new List<string>
                {
                    // Always written, one way or the other. A list with no parts at all would build the
                    // bare endpoint "anime?", and the filter is decided in one place for a reason.
                    ContentFilter.UsesSourceFilter ? "sfw" : "sfw=false"
                };
                if (filter != null)
                {
                    if (filter.GenreIds.Count > 0)
                        parts.Add("genres=" + string.Join(",", filter.GenreIds));
                    var formats = filter.FormatsQuery();
                    if (!string.IsNullOrEmpty(formats))
                        parts.Add("type=" + formats);
                    if (filter.MinYear > 0)
                        parts.Add($"start_date={filter.MinYear:D4}-01-01");
                    if (filter.MaxYear > 0)
                        parts.Add($"end_date={filter.MaxYear:D4}-12-31");
                }
                if (effectiveMinScore > 0)
                    parts.Add("min_score=" + effectiveMinScore.ToString("0.0",
                        System.Globalization.CultureInfo.InvariantCulture));
                if (effectiveMaxScore > 0)
                    parts.Add("max_score=" + effectiveMaxScore.ToString("0.0",
                        System.Globalization.CultureInfo.InvariantCulture));
                if (excludeIds != null)
                {
                    var ids = excludeIds.Take(1000).ToList();
                    if (ids.Count > 0)
                        parts.Add("exclude_ids=" + string.Join(",", ids));
                }

                // If asking for a single random pick without popularity bounds,
                // Tenrai's upgraded /random/{anime|manga} endpoint directly returns a matching entry in 1 request
                // (supports start_date, end_date, genres, min_score, and formats).
                if (limit == 1 && maxPopularityRank <= 0)
                {
                    var randomQuery = $"random/{media}?" + string.Join("&", parts);
                    var randomRaw = await TenraiClient.GetRawJsonAsync(randomQuery);
                    if (!string.IsNullOrEmpty(randomRaw))
                    {
                        using var rdoc = JsonDocument.Parse(randomRaw);
                        if (rdoc.RootElement.TryGetProperty("data", out var rdata) &&
                            rdata.ValueKind == JsonValueKind.Object)
                        {
                            var ritem = ParseCatalogEntry(rdata, manga);
                            if (ritem != null)
                            {
                                output.Add(ritem);
                                return output;
                            }
                        }
                    }
                }

                var baseQuery = media + "?" + string.Join("&", parts);

                var lastPage = await ResolveLastPageAsync(media, baseQuery, PageSize);
                if (lastPage < 1)
                    return output;

                var maxPage = Math.Max(1, Math.Min(lastPage, MaxOffset / PageSize));

                // Try up to 3 random pages if needed, to guard against small pools or sparse pages
                for (var attempt = 0; attempt < 3 && output.Count < limit; attempt++)
                {
                    var page = maxPage == 1 ? 1 : Rng.Next(1, maxPage + 1);
                    var raw = await TenraiClient.GetRawJsonAsync($"{baseQuery}&page={page}&limit={PageSize}");
                    if (string.IsNullOrEmpty(raw))
                        continue;

                    using var doc = JsonDocument.Parse(raw);
                    if (!doc.RootElement.TryGetProperty("data", out var data) ||
                        data.ValueKind != JsonValueKind.Array)
                        continue;

                    var batch = new List<DiscoveryItem>();
                    foreach (var entry in data.EnumerateArray())
                    {
                        var item = ParseCatalogEntry(entry, manga);
                        if (item == null)
                            continue;
                        // Popularity ceilings cannot be filtered server side (Tenrai ignores
                        // min_members/max_members), so they are applied here.
                        if (maxPopularityRank > 0 && item.PopularityRank > 0 && item.PopularityRank < maxPopularityRank)
                            continue;
                        // Client-side safety bounds
                        if (effectiveMinScore > 0 && item.Score > 0 && item.Score < effectiveMinScore)
                            continue;
                        if (effectiveMaxScore > 0 && item.Score > effectiveMaxScore)
                            continue;
                        if (filter?.MinYear > 0 && item.Year > 0 && item.Year < filter.MinYear)
                            continue;
                        if (filter?.MaxYear > 0 && item.Year > 0 && item.Year > filter.MaxYear)
                            continue;
                        batch.Add(item);
                    }

                    if (batch.Count == 0)
                        continue;

                    if (limit == 1)
                    {
                        // Pick a random candidate from the page rather than always the first one
                        output.Add(batch[Rng.Next(batch.Count)]);
                        return output;
                    }

                    foreach (var item in batch)
                    {
                        if (output.Count >= limit)
                            break;
                        output.Add(item);
                    }
                }
            }
            catch
            {
            }
            return output;
        }

        private static async Task<int> ResolveLastPageAsync(string media, string baseQuery, int pageSize)
        {
            try
            {
                var raw = await TenraiClient.GetRawJsonAsync($"{baseQuery}&page=1&limit={pageSize}");
                if (string.IsNullOrEmpty(raw))
                    return 0;
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("pagination", out var pagination) &&
                    pagination.TryGetProperty("last_visible_page", out var last) &&
                    last.ValueKind == JsonValueKind.Number)
                    return last.GetInt32();
            }
            catch
            {
            }
            return 0;
        }

        internal static DiscoveryItem ParseCatalogEntry(JsonElement entry, bool manga)
        {
            try
            {
                if (!entry.TryGetProperty("mal_id", out var idProp) || idProp.ValueKind != JsonValueKind.Number)
                    return null;
                var id = idProp.GetInt32();
                if (id <= 0)
                    return null;
                string title = entry.TryGetProperty("title", out var t) ? t.GetString() : null;
                string type = entry.TryGetProperty("type", out var ty) ? ty.GetString() : null;
                string img = null;
                if (entry.TryGetProperty("images", out var images) &&
                    images.TryGetProperty("jpg", out var jpg) &&
                    jpg.TryGetProperty("image_url", out var imgProp) && imgProp.ValueKind == JsonValueKind.String)
                    img = imgProp.GetString();
                int popularity = 0;
                if (entry.TryGetProperty("popularity", out var pop) && pop.ValueKind == JsonValueKind.Number)
                    popularity = pop.GetInt32();
                double score = 0;
                if (entry.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number)
                    score = sc.GetDouble();
                // Tenrai names it num_list_users; members is the same figure MAL shows as "Members".
                int members = 0;
                if (entry.TryGetProperty("members", out var mem) && mem.ValueKind == JsonValueKind.Number)
                    members = mem.GetInt32();
                else if (entry.TryGetProperty("num_list_users", out var mem2) &&
                         mem2.ValueKind == JsonValueKind.Number)
                    members = mem2.GetInt32();
                int year = 0;
                if (entry.TryGetProperty("year", out var yr) && yr.ValueKind == JsonValueKind.Number)
                    year = yr.GetInt32();
                if (year <= 0)
                {
                    var datePropName = manga ? "published" : "aired";
                    if (entry.TryGetProperty(datePropName, out var dateObj) && dateObj.ValueKind == JsonValueKind.Object)
                    {
                        if (dateObj.TryGetProperty("prop", out var propObj) && propObj.ValueKind == JsonValueKind.Object &&
                            propObj.TryGetProperty("from", out var fromObj) && fromObj.ValueKind == JsonValueKind.Object &&
                            fromObj.TryGetProperty("year", out var fromYear) && fromYear.ValueKind == JsonValueKind.Number)
                        {
                            year = fromYear.GetInt32();
                        }
                        else if (dateObj.TryGetProperty("from", out var fromStr) && fromStr.ValueKind == JsonValueKind.String)
                        {
                            var s = fromStr.GetString();
                            if (!string.IsNullOrEmpty(s) && s.Length >= 4 && int.TryParse(s.Substring(0, 4), out var y))
                                year = y;
                        }
                    }
                }
                int episodes = 0;
                if (entry.TryGetProperty("episodes", out var ep) && ep.ValueKind == JsonValueKind.Number)
                    episodes = ep.GetInt32();

                  return new DiscoveryItem
                  {
                      Id = id,
                      Title = title,
                      ImgUrl = img,
                      Type = type,
                      IsManga = manga,
                      PopularityRank = popularity,
                      Score = score,
                      Members = members,
                      Year = year,
                      AllEpisodes = episodes,
                      Subtitle = BuildSubtitle(type, score, popularity)
                  };
            }
            catch
            {
                return null;
            }
        }

        private static string BuildSubtitle(string type, double score, int popularity)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(type))
                parts.Add(type);
            if (score > 0)
                parts.Add("★ " + score.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            if (popularity > 0)
                parts.Add($"#{popularity}");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    ///     One hidden gem per week: highly rated, genuinely underseen, and not something the user
    ///     already has on their list.
    ///
    ///     Tenrai ignores min_members/max_members and has no "not in my list" filter, so both are
    ///     applied here against a pooled sample. The pick is stored by id with the ISO week it was
    ///     made in, so the tile shows the same title all week instead of reshuffling on every tap.
    /// </summary>
    public static class DiscoveryGemQuery
    {
        private const double MinScore = 7.5;
        private const int MinPopularityRank = 1500;
        private const int MaxPopularityRank = 15000;
        private const int MaxMembers = 60000;
        private const int MinYear = 2010;

        private const string IdKey = "GemOfTheDay_Id";
        private const string DayKey = "GemOfTheDay_Day";
        private const string TitleKey = "GemOfTheDay_Title";
        private const string ImgKey = "GemOfTheDay_Img";
        private const string TypeKey = "GemOfTheDay_Type";
        private const string ScoreKey = "GemOfTheDay_Score";
        private const string YearKey = "GemOfTheDay_Year";

        private const int PageSize = 25;
        private const int MaxOffset = 20000;
        private const int PoolPages = 8;

        private static readonly Random Rng = new();

        /// <summary>UTC-based day key (yyyy-MM-dd), so the pick rolls over every day worldwide.</summary>
        private static string CurrentDay()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static DiscoveryItem GetCachedGem()
        {
            var store = ViewModels.ResourceLocator.ApplicationDataService;
            if ((string)store[DayKey] != CurrentDay())
                return null;
            var id = (int)(store[IdKey] ?? 0);
            if (id <= 0)
                return null;
            return new DiscoveryItem
            {
                Id = id,
                Title = (string)store[TitleKey] ?? "",
                ImgUrl = (string)store[ImgKey] ?? "",
                Type = (string)store[TypeKey] ?? "",
                Score = (int)(store[ScoreKey] ?? 0) / 10.0,
                Year = (int)(store[YearKey] ?? 0),
                Subtitle = BuildGemSubtitle((string)store[TypeKey], (int)(store[ScoreKey] ?? 0) / 10.0,
                    (int)(store[YearKey] ?? 0))
            };
        }

        private static string BuildGemSubtitle(string type, double score, int year)
        {
            var parts = new List<string>();
            if (score > 0)
                parts.Add("★ " + score.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(type))
                parts.Add(type);
            if (year > 0)
                parts.Add(year.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return string.Join(" · ", parts);
        }

        /// <summary>
        ///     Returns the stored gem when it is still this week's, otherwise draws a new one and
        ///     persists it. Returns null when nothing in the sampled pool qualifies.
        /// </summary>
        public static async Task<DiscoveryItem> GetAsync(HashSet<int> alreadyInLibrary)
        {
            var cached = GetCachedGem();
            if (cached != null)
                return cached;

            var pool = new List<DiscoveryItem>();

            var lastPage = await ResolveLastPageAsync("anime", ContentFilter.Query("anime?min_score=" +
                MinScore.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)), PageSize);
            var maxPage = Math.Max(1, Math.Min(lastPage, MaxOffset / PageSize));

            for (var attempt = 0; attempt < PoolPages && pool.Count < 40; attempt++)
            {
                var page = maxPage == 1 ? 1 : Rng.Next(1, maxPage + 1);
                var raw = await TenraiClient.GetRawJsonAsync(
                    ContentFilter.Query("anime?min_score=" + MinScore.ToString("0.0",
                        System.Globalization.CultureInfo.InvariantCulture) + $"&page={page}&limit={PageSize}"));
                if (string.IsNullOrEmpty(raw))
                    continue;
                List<DiscoveryItem> batch;
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    batch = new List<DiscoveryItem>();
                    if (!doc.RootElement.TryGetProperty("data", out var data) ||
                        data.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var entry in data.EnumerateArray())
                    {
                        var item = DiscoveryRandomQuery.ParseCatalogEntry(entry, false);
                        if (item != null)
                            batch.Add(item);
                    }
                }
                catch
                {
                    continue;
                }

                foreach (var item in batch)
                {
                    if (item.Score < MinScore)
                        continue;
                    if (item.PopularityRank < MinPopularityRank || item.PopularityRank > MaxPopularityRank)
                        continue;
                    if (item.Members <= 0 || item.Members > MaxMembers)
                        continue;
                    if (item.Year > 0 && item.Year < MinYear)
                        continue;

                    var normType = item.Type?.Trim().ToLowerInvariant();
                    if (normType != "tv" && normType != "movie" && normType != "ova")
                        continue;

                    if (alreadyInLibrary != null && alreadyInLibrary.Contains(item.Id))
                        continue;

                    pool.Add(item);
                }
            }

            if (pool.Count == 0)
                return null;

            var gem = pool[Rng.Next(0, pool.Count)];
            var store = ViewModels.ResourceLocator.ApplicationDataService;
            store[IdKey] = gem.Id;
            store[TitleKey] = gem.Title;
            store[ImgKey] = gem.ImgUrl;
            store[TypeKey] = gem.Type;
            store[ScoreKey] = (int)Math.Round(gem.Score * 10);
            store[YearKey] = gem.Year;
            store[DayKey] = CurrentDay();
            return gem;
        }

        private static async Task<int> ResolveLastPageAsync(string media, string query, int pageSize)
        {
            try
            {
                var raw = await TenraiClient.GetRawJsonAsync($"{query}&limit={pageSize}");
                if (string.IsNullOrEmpty(raw))
                    return 0;
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("pagination", out var pagination) &&
                    pagination.TryGetProperty("last_visible_page", out var last) &&
                    last.ValueKind == JsonValueKind.Number)
                    return last.GetInt32();
            }
            catch
            {
            }
            return 0;
        }
    }
}
