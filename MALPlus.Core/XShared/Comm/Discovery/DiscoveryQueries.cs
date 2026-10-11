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
    }

/// <summary>
    ///     Trending for the Discover hub.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is AniList's own trending metric, and the one thing the app can ask a source for that
    ///         means "what people are actually on this week". Tenrai has no equivalent: /trending is a 404
    ///         and order_by=trending is a 400, and its order_by=popularity and order_by=rank both come
    ///         back in an order that is plainly not the requested one.
    ///     </para>
    ///     <para>
    ///         The part that made this look random was one missing word. Without a status filter,
    ///         TRENDING_DESC spans the whole catalogue, so a 1999 or a 2004 title with a sudden flurry of
    ///         activity outranked anything currently airing: Crayon Shin-chan, Conan and One Piece came
    ///         first and Doraemon with them. Restricting to status RELEASING is what makes it mean
    ///         trending now: the top of the list is this season's premieres and the long runners sit
    ///         where their popularity earns them.
    ///     </para>
    ///     <para>
    ///         Only entries AniList can map to a MAL id are kept, which is what lets a row open our own
    ///         details page. Measured, that was every entry in the page.
    ///     </para>
    /// </remarks>
    public static class AniListTrendingQuery
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        /// <summary>
        ///     AniList answers a request with no browser User-Agent as an outright refusal, and the
        ///     response is a plain 400 rather than anything that says what was wrong.
        /// </summary>
        private const string ChromeUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        /// <summary>Last failure, surfaced so the caller can log why the list came back empty.</summary>
        public static string LastError { get; private set; } = "";

        /// <summary>
        ///     The list of genres that marks an entry as too much for the strict setting.
        /// </summary>
        /// <remarks>
        ///     AniList has a flag for adult material but not for the softer tier, so Rx is excluded with
        ///     isAdult and R+ is excluded here by genre. Without this the strict setting let ecchi through
        ///     on this page, which is exactly what that setting promises not to.
        /// </remarks>
        private static readonly string[] StrictGenreBlocklist = { "Ecchi", "Hentai", "Erotica" };

        /// <summary>
        ///     The query, written out a line per level so the braces can be counted by eye.
        /// </summary>
        /// <remarks>
        ///     Two things here are not optional, and both are the kind a test only catches by sending the
        ///     body verbatim.
        ///     <para>
        ///         The page size is a declared variable: a variables object beside a query that never
        ///         mentions it is answered with a 400 that does not say which half is wrong.
        ///     </para>
        ///     <para>
        ///         The media argument list closes BEFORE the selection set opens. Run them together and
        ///         <c>isAdult:false,id idMal</c> reads id as one more argument, which reports "Expected :,
        ///         found Name idMal"; and a parenthesis where the selection set's brace belongs reports
        ///         "Expected Name, found )". The last line is media's brace plus Page's plus the
        ///         operation's, in that order.
        ///     </para>
        /// </remarks>
        private const string Query =
            "query($perPage:Int)" +
            "{Page(perPage:$perPage)" +
            "{media(type:ANIME,status:RELEASING,sort:TRENDING_DESC,isAdult:false)" +
            "{id idMal:idMal title{romaji}coverImage{large}genres averageScore}" +
            "}}";

        private static string Body(int perPage) =>
            "{\"query\":\"" + Query + "\",\"variables\":{\"perPage\":" + perPage + "}}";

        public static async Task<List<DiscoveryItem>> GetAsync(int limit = 10)
        {
            var output = new List<DiscoveryItem>();
            LastError = "";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://graphql.anilist.co")
                {
                    // isAdult is false in the query itself, so nothing explicit is wanted here, and the
                    // softer tier is decided by genre below instead.
                    Content = new StringContent(Body(50), Encoding.UTF8, "application/json")
                };
                request.Headers.TryAddWithoutValidation("User-Agent", ChromeUserAgent);
                request.Headers.TryAddWithoutValidation("Accept", "application/json");
                using var response = await Client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    LastError = "HTTP " + (int)response.StatusCode;
                    return output;
                }
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("Page", out var page) ||
                    !page.TryGetProperty("media", out var media) ||
                    media.ValueKind != JsonValueKind.Array)
                {
                    LastError = json.Length > 200 ? json.Substring(0, 200) : json;
                    return output;
                }

                var blockEcchi = XShared.Utils.Settings.ContentFilter == Models.Enums.ContentFilterMode.Safe;

                foreach (var entry in media.EnumerateArray())
                {
                    if (output.Count >= limit)
                        break;

                    // An AniList original has no MAL id, so there is nowhere for the row to go.
                    if (!entry.TryGetProperty("idMal", out var idMal) || idMal.ValueKind != JsonValueKind.Number)
                        continue;
                    var id = idMal.GetInt32();
                    if (id <= 0)
                        continue;

                    var genres = new List<string>();
                    if (entry.TryGetProperty("genres", out var genreArray) && genreArray.ValueKind == JsonValueKind.Array)
                        foreach (var genre in genreArray.EnumerateArray())
                            if (genre.ValueKind == JsonValueKind.String)
                                genres.Add(genre.GetString() ?? "");

                    if (blockEcchi && genres.Any(g => StrictGenreBlocklist.Contains(g, StringComparer.OrdinalIgnoreCase)))
                        continue;

                    var title = entry.TryGetProperty("title", out var t) && t.TryGetProperty("romaji", out var r)
                        ? r.GetString()
                        : null;
                    string cover = null;
                    if (entry.TryGetProperty("coverImage", out var coverImage) &&
                        coverImage.TryGetProperty("large", out var large) &&
                        large.ValueKind == JsonValueKind.String)
                        cover = large.GetString();

                    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(cover))
                        continue;

                    var score = entry.TryGetProperty("averageScore", out var sc) && sc.ValueKind == JsonValueKind.Number
                        ? sc.GetDouble()
                        : 0d;
                    // AniList scores out of 100 and MAL out of ten.
                    var malScore = score > 0 ? score / 10d : 0d;

                    output.Add(new DiscoveryItem
                    {
                        Id = id,
                        Title = title,
                        ImgUrl = cover,
                        IsManga = false,
                        Score = malScore,
                        // No subtitle. It used to say "Trending now" on every row, which told the
                        // reader the same thing the sheet's own heading says twenty times over; the
                        // type and the score on the poster carry the useful part.
                        Subtitle = ""
                    });
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
    /// </summary>
    public static class DiscoveryRandomQuery
    {
        private static readonly Random Rng = new Random();

        /// <summary>
        /// What the community is watching right now, without AniList: currently airing,
        /// most favourited first. Used as the OP/ED source and as the trending fallback.
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
            int minScore, int maxPopularityRank, int limit = 20)
        {
            var output = new List<DiscoveryItem>();
            try
            {
                var media = manga ? "manga" : "anime";
                var effectiveMinScore = minScore > 0 ? minScore : filter?.MinScore ?? 0;
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
                }
                if (effectiveMinScore > 0)
                    parts.Add("min_score=" + effectiveMinScore.ToString("0.0",
                        System.Globalization.CultureInfo.InvariantCulture));
                var baseQuery = media + "?" + string.Join("&", parts);

                var lastPage = await ResolveLastPageAsync(media, baseQuery, PageSize);
                if (lastPage < 1)
                    return output;

                var maxPage = Math.Max(1, Math.Min(lastPage, MaxOffset / PageSize));
                var page = maxPage == 1 ? 1 : Rng.Next(1, maxPage + 1);
                var raw = await TenraiClient.GetRawJsonAsync($"{baseQuery}&page={page}&limit={PageSize}");
                if (string.IsNullOrEmpty(raw))
                    return output;

                using var doc = JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    data.ValueKind != JsonValueKind.Array)
                    return output;

                foreach (var entry in data.EnumerateArray())
                {
                    if (output.Count >= limit)
                        break;
                    var item = ParseCatalogEntry(entry, manga);
                    if (item == null)
                        continue;
                    // Popularity ceilings cannot be filtered server side (Tenrai ignores
                    // min_members/max_members), so they are applied here.
                    if (maxPopularityRank > 0 && item.PopularityRank > 0 && item.PopularityRank < maxPopularityRank)
                        continue;
                    output.Add(item);
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

        private const string IdKey = "GemOfTheWeek_Id";
        private const string WeekKey = "GemOfTheWeek_Week";
        private const string TitleKey = "GemOfTheWeek_Title";
        private const string ImgKey = "GemOfTheWeek_Img";
        private const string TypeKey = "GemOfTheWeek_Type";
        private const string ScoreKey = "GemOfTheWeek_Score";
        private const string YearKey = "GemOfTheWeek_Year";

        private const int PageSize = 25;
        private const int MaxOffset = 20000;
        private const int PoolPages = 8;

        private static readonly Random Rng = new();

        /// <summary>Monday-based ISO-ish week key, so the pick rolls over on the same day worldwide.</summary>
        private static string CurrentWeek()
        {
            var today = DateTime.UtcNow.Date;
            var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            return monday.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static DiscoveryItem GetCachedGem()
        {
            var store = ViewModels.ResourceLocator.ApplicationDataService;
            if ((string)store[WeekKey] != CurrentWeek())
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
            store[WeekKey] = CurrentWeek();
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
