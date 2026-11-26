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
            public int DurationMinutes { get; set; }
            public string AirStartDate { get; set; }
            public string Status { get; set; }
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

                    if (string.IsNullOrEmpty(item.Status))
                        item.Status = "Currently Airing";

                    // Justify the trend with rank and clean momentum indicator (Option A):
                    item.Subtitle = rank == 1
                        ? "#1 Trending · Top weekly growth"
                        : $"#{rank} Trending · High weekly growth";
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
                string airStartDate = null;
                var datePropName = manga ? "published" : "aired";
                if (entry.TryGetProperty(datePropName, out var dateObj) && dateObj.ValueKind == JsonValueKind.Object)
                {
                    if (dateObj.TryGetProperty("from", out var fromStr) && fromStr.ValueKind == JsonValueKind.String)
                        airStartDate = fromStr.GetString();

                    if (year <= 0)
                    {
                        if (dateObj.TryGetProperty("prop", out var propObj) && propObj.ValueKind == JsonValueKind.Object &&
                            propObj.TryGetProperty("from", out var fromObj) && fromObj.ValueKind == JsonValueKind.Object &&
                            fromObj.TryGetProperty("year", out var fromYear) && fromYear.ValueKind == JsonValueKind.Number)
                        {
                            year = fromYear.GetInt32();
                        }
                        else if (!string.IsNullOrEmpty(airStartDate) && airStartDate.Length >= 4 && int.TryParse(airStartDate.Substring(0, 4), out var y))
                        {
                            year = y;
                        }
                    }
                }
                string status = entry.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() : null;
                int episodes = 0;
                if (entry.TryGetProperty("episodes", out var ep) && ep.ValueKind == JsonValueKind.Number)
                    episodes = ep.GetInt32();
                int durationMinutes = 0;
                if (!manga && entry.TryGetProperty("duration", out var dur) && dur.ValueKind == JsonValueKind.String)
                    durationMinutes = ParseDurationMinutes(dur.GetString());

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
                      DurationMinutes = durationMinutes,
                      AirStartDate = airStartDate,
                      Status = status,
                      Subtitle = BuildSubtitle(type, score, popularity)
                  };
            }
            catch
            {
                return null;
            }
        }

        private static int ParseDurationMinutes(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0;

            var total = 0;
            var hasSeconds = false;
            var tokens = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < tokens.Length - 1; i++)
            {
                if (!int.TryParse(tokens[i], out var value) || value <= 0)
                    continue;
                var unit = tokens[i + 1].ToLowerInvariant();
                if (unit.StartsWith("hr", StringComparison.Ordinal))
                    total += value * 60;
                else if (unit.StartsWith("min", StringComparison.Ordinal))
                    total += value;
                else if (unit.StartsWith("sec", StringComparison.Ordinal))
                    hasSeconds = true;
            }
            return total == 0 && hasSeconds ? 1 : total;
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
    ///     One hidden gem per day: critically acclaimed (`>= 7.75-7.85`), genuinely underseen
    ///     (`<= 130k-160k` members, excluding massive mainstream hits), not already in the user's
    ///     library or recent gem history.
    ///
    ///     Uses Tenrai's native `/random/anime` endpoint in tiered passes (standalone IPs first,
    ///     then general hidden gems, then score-weighted catalog sampling as fallback).
    /// </summary>
    public static class DiscoveryGemQuery
    {
        private const int GemSchemaVersion = 3;
        private const double Tier1MinScore = 7.85;
        private const double Tier2MinScore = 7.75;
        private const int Tier1MinMembers = 4000;
        private const int Tier1MaxMembers = 130000;
        private const int Tier2MinMembers = 3000;
        private const int Tier2MaxMembers = 160000;
        private const int Tier1MinYear = 1998;
        private const int Tier2MinYear = 1990;

        private const string VerKey = "GemOfTheDay_Ver";
        private const string IdKey = "GemOfTheDay_Id";
        private const string DayKey = "GemOfTheDay_Day";
        private const string TitleKey = "GemOfTheDay_Title";
        private const string ImgKey = "GemOfTheDay_Img";
        private const string TypeKey = "GemOfTheDay_Type";
        private const string ScoreKey = "GemOfTheDay_Score";
        private const string YearKey = "GemOfTheDay_Year";
        private const string HistoryKey = "GemOfTheDay_History";

        private const int PageSize = 25;
        private const int MaxOffset = 15000;
        private const int CatalogPoolPages = 4;
        private const int MaxHistoryIds = 30;

        private static readonly Random Rng = new();

        /// <summary>UTC-based day key (yyyy-MM-dd), so the pick rolls over every day worldwide.</summary>
        private static string CurrentDay()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static DiscoveryItem GetCachedGem()
        {
            var store = ViewModels.ResourceLocator.ApplicationDataService;
            if ((int)(store[VerKey] ?? 0) != GemSchemaVersion)
                return null;
            if ((string)store[DayKey] != CurrentDay())
                return null;
            var id = (int)(store[IdKey] ?? 0);
            if (id <= 0)
                return null;
            var rawScore = (int)(store[ScoreKey] ?? 0);
            var score = rawScore > 100 ? rawScore / 100.0 : rawScore / 10.0;
            var type = (string)store[TypeKey] ?? "";
            var year = (int)(store[YearKey] ?? 0);
            return new DiscoveryItem
            {
                Id = id,
                Title = (string)store[TitleKey] ?? "",
                ImgUrl = (string)store[ImgKey] ?? "",
                Type = type,
                Score = score,
                Year = year,
                Subtitle = BuildGemSubtitle(type, score, year)
            };
        }

        private static string BuildGemSubtitle(string type, double score, int year)
        {
            var parts = new List<string>();
            if (score > 0)
                parts.Add("★ " + score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(type))
                parts.Add(type);
            if (year > 0)
                parts.Add(year.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return string.Join(" · ", parts);
        }

        private static List<int> LoadHistory()
        {
            var raw = ViewModels.ResourceLocator.ApplicationDataService[HistoryKey] as string;
            var ids = new List<int>();
            if (string.IsNullOrWhiteSpace(raw))
                return ids;
            foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token, out var id) && id > 0 && !ids.Contains(id))
                    ids.Add(id);
            }
            return ids;
        }

        private static void RecordHistory(int gemId)
        {
            if (gemId <= 0)
                return;
            var ids = LoadHistory();
            ids.Remove(gemId);
            ids.Add(gemId);
            if (ids.Count > MaxHistoryIds)
                ids.RemoveRange(0, ids.Count - MaxHistoryIds);
            ViewModels.ResourceLocator.ApplicationDataService[HistoryKey] = string.Join(",", ids);
        }

        private static void SaveCachedGem(DiscoveryItem gem)
        {
            gem.Subtitle = BuildGemSubtitle(gem.Type, gem.Score, gem.Year);
            var store = ViewModels.ResourceLocator.ApplicationDataService;
            store[VerKey] = GemSchemaVersion;
            store[IdKey] = gem.Id;
            store[TitleKey] = gem.Title;
            store[ImgKey] = gem.ImgUrl;
            store[TypeKey] = gem.Type;
            store[ScoreKey] = (int)Math.Round(gem.Score * 100);
            store[YearKey] = gem.Year;
            store[DayKey] = CurrentDay();
            RecordHistory(gem.Id);
        }

        /// <summary>
        ///     Returns today's cached gem when valid, otherwise draws a new one using Tenrai's
        ///     filtered random endpoint and persists it. Returns null if no candidate qualifies.
        /// </summary>
        public static async Task<DiscoveryItem> GetAsync(HashSet<int> alreadyInLibrary)
        {
            var cached = GetCachedGem();
            if (cached != null)
                return cached;

            var excludeSet = alreadyInLibrary != null ? new HashSet<int>(alreadyInLibrary) : new HashSet<int>();
            foreach (var histId in LoadHistory())
                excludeSet.Add(histId);

            // Tier 1: Standalone hidden gem (has_relations=false avoids random Season 3+ sequels)
            var gem = await TryRandomEndpointAsync(
                minScore: Tier1MinScore,
                minScoredBy: 1500,
                minMembers: Tier1MinMembers,
                maxMembers: Tier1MaxMembers,
                minYear: Tier1MinYear,
                types: "tv,movie,ova",
                standaloneOnly: true,
                excludeSet: excludeSet,
                attempts: 4);

            // Tier 2: Hidden gem allowing franchise entries / ONAs / 1990+ classics
            gem ??= await TryRandomEndpointAsync(
                minScore: Tier2MinScore,
                minScoredBy: 1200,
                minMembers: Tier2MinMembers,
                maxMembers: Tier2MaxMembers,
                minYear: Tier2MinYear,
                types: "tv,movie,ova,ona",
                standaloneOnly: false,
                excludeSet: excludeSet,
                attempts: 4);

            // Tier 3: Score-weighted catalog sampling fallback
            gem ??= await TryWeightedCatalogFallbackAsync(excludeSet);

            if (gem == null)
                return null;

            SaveCachedGem(gem);
            return gem;
        }

        private static string BuildCommonParams(double minScore, int minScoredBy, int minMembers,
            int maxMembers, int minYear, string types, bool standaloneOnly, HashSet<int> excludeSet)
        {
            var parts = new List<string>
            {
                ContentFilter.UsesSourceFilter ? "sfw" : "sfw=false",
                "status=finished_airing",
                "type=" + types,
                "min_score=" + minScore.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                $"min_scored_by={minScoredBy}",
                $"min_members={minMembers}",
                $"max_members={maxMembers}",
                $"start_date={minYear:D4}-01-01"
            };
            if (standaloneOnly)
                parts.Add("has_relations=false");
            if (excludeSet != null && excludeSet.Count > 0)
                parts.Add("exclude_ids=" + string.Join(",", excludeSet.Take(1000)));
            return string.Join("&", parts);
        }

        private static bool IsValidGem(DiscoveryItem item, HashSet<int> excludeSet,
            double minScore, int maxMembers, int minYear)
        {
            if (item == null || item.Id <= 0)
                return false;
            if (excludeSet != null && excludeSet.Contains(item.Id))
                return false;
            if (item.Score < minScore)
                return false;
            if (item.Members > 0 && item.Members > maxMembers)
                return false;
            if (item.Year > 0 && item.Year < minYear)
                return false;

            var normType = item.Type?.Trim().ToLowerInvariant();
            if (normType != "tv" && normType != "movie" && normType != "ova" && normType != "ona")
                return false;

            // Avoid short festival films/trailers for movies (< 40m) and short-form clips/episodes (< 12m)
            if (normType == "movie" && item.DurationMinutes > 0 && item.DurationMinutes < 40)
                return false;
            if (normType != "movie" && item.DurationMinutes > 0 && item.DurationMinutes < 12)
                return false;

            // Avoid 1-3 episode promo clips/specials disguised as TV/ONA
            if ((normType == "tv" || normType == "ona") && item.AllEpisodes > 0 && item.AllEpisodes < 4)
                return false;

            return true;
        }

        private static async Task<DiscoveryItem> TryRandomEndpointAsync(double minScore, int minScoredBy,
            int minMembers, int maxMembers, int minYear, string types, bool standaloneOnly,
            HashSet<int> excludeSet, int attempts)
        {
            var query = "random/anime?" + BuildCommonParams(
                minScore, minScoredBy, minMembers, maxMembers, minYear, types, standaloneOnly, excludeSet);

            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    var raw = await TenraiClient.GetRawJsonAsync(query);
                    if (string.IsNullOrEmpty(raw))
                        continue;

                    using var doc = JsonDocument.Parse(raw);
                    if (!doc.RootElement.TryGetProperty("data", out var data) ||
                        data.ValueKind != JsonValueKind.Object)
                        continue;

                    var item = DiscoveryRandomQuery.ParseCatalogEntry(data, false);
                    if (IsValidGem(item, excludeSet, minScore, maxMembers, minYear))
                        return item;
                }
                catch
                {
                }
            }

            return null;
        }

        private static async Task<DiscoveryItem> TryWeightedCatalogFallbackAsync(HashSet<int> excludeSet)
        {
            var baseQuery = "anime?" + BuildCommonParams(
                Tier2MinScore, 1000, Tier2MinMembers, Tier2MaxMembers, Tier2MinYear,
                "tv,movie,ova", standaloneOnly: false, excludeSet: null);

            var lastPage = await ResolveLastPageAsync(baseQuery, PageSize);
            if (lastPage < 1)
                return null;

            var maxPage = Math.Max(1, Math.Min(lastPage, MaxOffset / PageSize));
            var pool = new List<DiscoveryItem>();

            for (var attempt = 0; attempt < CatalogPoolPages && pool.Count < 30; attempt++)
            {
                var page = maxPage == 1 ? 1 : Rng.Next(1, maxPage + 1);
                var raw = await TenraiClient.GetRawJsonAsync($"{baseQuery}&page={page}&limit={PageSize}");
                if (string.IsNullOrEmpty(raw))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (!doc.RootElement.TryGetProperty("data", out var data) ||
                        data.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var entry in data.EnumerateArray())
                    {
                        var item = DiscoveryRandomQuery.ParseCatalogEntry(entry, false);
                        if (IsValidGem(item, excludeSet, Tier2MinScore, Tier2MaxMembers, Tier2MinYear))
                            pool.Add(item);
                    }
                }
                catch
                {
                }
            }

            if (pool.Count == 0)
                return null;

            // Bias selection toward higher-rated gems so 8.1+ entries clearly beat 7.7s
            double totalWeight = 0;
            var weights = new double[pool.Count];
            for (var i = 0; i < pool.Count; i++)
            {
                var delta = Math.Max(0.15, pool[i].Score - 7.45);
                weights[i] = Math.Pow(delta, 2.2);
                totalWeight += weights[i];
            }

            var roll = Rng.NextDouble() * totalWeight;
            double acc = 0;
            for (var i = 0; i < pool.Count; i++)
            {
                acc += weights[i];
                if (roll <= acc)
                    return pool[i];
            }

            return pool[pool.Count - 1];
        }

        private static async Task<int> ResolveLastPageAsync(string query, int pageSize)
        {
            try
            {
                var raw = await TenraiClient.GetRawJsonAsync($"{query}&page=1&limit={pageSize}");
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
