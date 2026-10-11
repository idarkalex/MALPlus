using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MALPlus.Models.Models.Anime;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;

namespace MALPlus.XShared.Comm.Anime
{
    public class AnimeSeasonalQuery : Query
    {
        /// <summary>
        ///     Budget for one whole season. Long enough for the pagination to finish, short enough that
        ///     a stalled source fails visibly instead of leaving the grid empty with no explanation.
        /// </summary>
        private static readonly TimeSpan SeasonsRequestTimeout = TimeSpan.FromSeconds(45);

        /// <summary>
        ///     Ceiling on how many pages one season may span. Generous for a real season and there to
        ///     stop a wrong page size from turning into a request for every page the source claims.
        /// </summary>
        private const int MaxSeasonPages = 20;

        private const int DefaultPageSize = 25;

        private readonly AnimeSeason _season;

        /// <summary>
        ///     Extra condition appended to the request, or null.
        /// </summary>
        /// <remarks>
        ///     Exists so a caller can ask for a filtered season without the panel that owns this query
        ///     having to know how. Null means an unfiltered read, which is what every existing caller
        ///     wants and why the parameter defaults to it.
        /// </remarks>
        private readonly string _extraQuery;

        private int _resolvedYear;
        private string _resolvedSeasonName;

        public AnimeSeasonalQuery(AnimeSeason season, string extraQuery = null)
        {
            _season = season;
            _extraQuery = string.IsNullOrWhiteSpace(extraQuery) ? null : extraQuery.Trim();
        }

        /// <summary>
        ///     The season endpoint, with whatever condition was asked for appended.
        /// </summary>
        private string SeasonEndpoint(int page) =>
            $"seasons/{_resolvedYear}/{_resolvedSeasonName}?page={page}" +
            (_extraQuery == null ? "" : "&" + _extraQuery);

        /// <summary>
        ///     The cache key for a season, in one place.
        /// </summary>
        /// <remarks>
        ///     The warmer prefetches finished seasons using this, and a mismatch would mean warming a
        ///     season into a slot nothing ever reads, so it is deliberately not spelled out twice.
        ///     <para>
        ///     The condition is part of the key because a filtered read is a different answer to the
        ///     same question: caching both under one key means the first one read is served for the
        ///     other forever, which is how a season's adult entries survive a filter that is on.
        /// </para>
        /// </remarks>
        public static string CacheTagFor(int year, Season season, string extraQuery = null)
        {
            var tag = $"{year}_{season switch
            {
                Season.Winter => "winter",
                Season.Spring => "spring",
                Season.Summer => "summer",
                _ => "fall"
            }}";
            // Derives a short stable suffix rather than embedding the condition itself, which would put
            // whatever the caller spelled into a filename.
            if (!string.IsNullOrWhiteSpace(extraQuery))
                tag += "_" + Canonical(extraQuery);
            return tag;
        }

        /// <summary>
        ///     A short, stable stand-in for a condition, for use inside a cache key.
        /// </summary>
        private static string Canonical(string condition) =>
            System.Convert.ToHexString(
                System.Security.Cryptography.SHA1.HashData(
                    System.Text.Encoding.UTF8.GetBytes(condition.Trim().ToLowerInvariant())))
                .Substring(0, 8)
                .ToLowerInvariant();

        /// <summary>
        ///     Called with the first page as soon as it lands, before the rest of the season is
        ///     requested.
        /// </summary>
        /// <param name="firstPage">
        ///     Whatever arrived first. Never null, possibly short.
        /// </param>
        /// <remarks>
        ///     A season is eight or nine requests, and holding the whole grid hostage to the last one
        ///     is what made it feel slow even when the network was fine: the first page carries the
        ///     best known entries, because the source returns them ordered, so it is the one worth
        ///     showing. This is how the caller paints something immediately and fills in behind it.
        /// </remarks>
        public Func<List<SeasonalAnimeData>, Task> FirstPageReady { get; set; }

        /// <summary>
        ///     Asks whether the caller has been superseded, so the retry loop stops instead of spending
        ///     its remaining attempts and its delays on a season nobody is waiting for.
        /// </summary>
        public Func<bool> RequestCancelled { get; set; }

        public async Task<List<SeasonalAnimeData>> GetSeasonalAnime(bool force = false)
        {
            // Year and season are resolved BEFORE anything is read or written, and they are what both
            // the request and the cache key are built from. They used to disagree: the request read
            // Year and Season off the object while the cache was keyed on Name, which is a display
            // string that callers build however they like ("Airing", "Winter 2025", or null on the
            // objects composed elsewhere). Two seasons could therefore share one cache slot, a season
            // could be cached under another one's name, and picking one season returned another's
            // list. Resolving once and keying on the resolved pair removes that whole class of bug.
            var requestedYear = _season.Year != 0 ? _season.Year : DateTime.UtcNow.Year;
            var requestedSeason = _season.Year != 0 ? SeasonEnumToString(_season.Season) : GetCurrentSeason();
            _resolvedYear = requestedYear;
            _resolvedSeasonName = requestedSeason;
            // The condition travels with the key, so a filtered read never serves a cache written
            // without it and an unfiltered one never serves a cache written with one.
            var cacheTag = CacheTagFor(requestedYear,
                _season.Year != 0 ? _season.Season : CurrentSeasonOf(DateTime.UtcNow), _extraQuery);
            var started = DateTime.UtcNow;

            var output = new List<SeasonalAnimeData>();
            List<SeasonalAnimeData> stale = null;
            if (!force)
            {
                var cached = await DataCache.RetrieveSeasonalData(cacheTag);
                if (cached != null && cached.Count != 0 &&
                    cached.All(i => !string.IsNullOrEmpty(i.Title) && i.Id != 0))
                {
                    Console.WriteLine("MALPLUS season timing: " + requestedYear + " " + requestedSeason +
                                      " cache hit " + cached.Count + " rows in " +
                                      (DateTime.UtcNow - started).TotalMilliseconds.ToString("F0") + "ms");
                    return cached;
                }

                // An expired entry for a season that has already finished is still the right list.
                // Nothing about it changes, so when the refresh below fails there is nothing to be
                // gained by showing nothing, and a season the user browsed once becomes unavailable
                // again every time the seven days run out. Read through a separate entry point because
                // the ordinary one returns nothing once the entry is older than its seven days, which
                // is exactly the case that matters here.
                if (SeasonCacheWarmer.IsFinished(requestedYear, requestedSeason))
                {
                    var expired = await DataCache.RetrieveSeasonalDataIgnoringExpiry(cacheTag);
                    if (expired != null && expired.Count != 0 &&
                        expired.All(i => !string.IsNullOrEmpty(i.Title) && i.Id != 0))
                        stale = expired;
                }
            }

            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
var result = new List<SeasonalAnimeData>();
                // Outside the try so the retry delay can be cancelled with it. A season is one
                // paginated Tenrai endpoint and it can be slow; bounded, because the grid is waiting
                // on it, and reported rather than swallowed, because an empty result and a failed
                // request look identical on screen.
                using var timeout = new CancellationTokenSource(SeasonsRequestTimeout);
                try
                {
                    // Page 1 alone, because it is what says whether there is a page 2 at all. It also
                    // carries the page size, which is how the rest of the season is counted without
                    // walking it one round trip at a time: 187 rows at 25 a page is seven more.
                    var (firstItems, hasNext) = await TenraiClient.GetPaginatedAsync(
                                           SeasonEndpoint(1), timeout.Token);

                    // Painted before the rest is even requested. If the caller is slow to take it, or
                    // the rest fails outright, this still stands: a season's first page is a season.
                    if (FirstPageReady != null && firstItems.Count > 0)
                    {
                        var firstBatch = new List<SeasonalAnimeData>();
                        foreach (var entry in firstItems)
                            firstBatch.Add(ToSeasonalAnimeData(entry, firstBatch.Count + 1));
                        try
                        {
                            await FirstPageReady(firstBatch);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("MALPLUS season first page render failed: " + ex.GetType().Name);
                        }
                    }

                    var entries = new List<JsonElement>(firstItems);
                    if (hasNext)
                    {
                        // One request for what is left, rather than one per page. Sequential paging
                        // was measured at 27s for Spring 2026 and was the whole of the wait; a season is
                        // a list the user is looking at, not a document being streamed in order.
                        var rest = await TenraiClient.GetAllPagesParallelAsync(
                            page => SeasonEndpoint(page),
                            2, MaxSeasonPages, timeout.Token);
                        entries.AddRange(rest);
                    }

                    foreach (var entry in entries)
                        result.Add(ToSeasonalAnimeData(entry, result.Count + 1));

                    // Never an empty result: the cache has a seven day life and an empty entry would
                    // sit there being "valid" for all of it, so every season in the app would read
                    // back as an empty season until the entry expired.
                    if (result.Count != 0)
                        await DataCache.SaveSeasonalData(result, cacheTag);
                    Console.WriteLine("MALPLUS season timing: " + requestedYear + " " + requestedSeason +
                                      " network " + result.Count + " rows in " +
                                      (DateTime.UtcNow - started).TotalMilliseconds.ToString("F0") + "ms");
                    return result;
                }
                catch (Exception ex)
                {
                    // An empty season is a real answer and is returned above without a warning. An
                    // empty result HERE means the source never delivered, and silently returning it
                    // is what made a failed season indistinguishable from a season with no entries.
                    if (attempt == maxAttempts)
                    {
                        Console.WriteLine("MALPLUS season fetch failed for " + requestedYear + " " +
                                          requestedSeason + ": " + ex.GetType().Name + " " + ex.Message);
                        // A finished season does not change, so its expired entry beats nothing.
                        return stale ?? result;
                    }

                    // Superseded while it was failing. Retrying costs two more seconds of delay on
                    // every attempt and the answer is no longer wanted by anyone.
                    if (RequestCancelled != null && RequestCancelled())
                        return result;

                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), timeout.Token);
                }
            }

            return stale ?? output;
        }

        /// <summary>
        ///     One row of a Tenrai seasons endpoint, parsed.
        /// </summary>
        /// <remarks>
        ///     Pulled out of the fetch loop so the parallel path and any future caller parse a row the
        ///     same way. Index is passed in because it has to be the row's position in the WHOLE season,
        ///     which is what the list sorts by, and not its position inside whichever page it arrived in.
        /// </remarks>
        private static SeasonalAnimeData ToSeasonalAnimeData(JsonElement entry, int index)
        {
            var airDay = -1;
            var airStartDate = "";
            if (entry.TryGetProperty("broadcast", out var broadcast) &&
                broadcast.TryGetProperty("day", out var dayProp) &&
                dayProp.ValueKind == JsonValueKind.String)
            {
                airDay = DayOfWeekStringToInt(dayProp.GetString());
            }
            if (entry.TryGetProperty("aired", out var aired) &&
                aired.TryGetProperty("from", out var fromProp) &&
                fromProp.ValueKind == JsonValueKind.String)
            {
                var fromStr = fromProp.GetString();
                if (DateTime.TryParse(fromStr, out var dt))
                    airStartDate = dt.ToString("yyyy-MM-dd");
            }

            return new SeasonalAnimeData
            {
                Id = GetInt(entry, "mal_id"),
                Title = GetString(entry, "title"),
                Rating = GetString(entry, "rating"),
                ImgUrl = GetNestedString(entry, "images", "jpg", "image_url"),
                Episodes = GetInt(entry, "episodes").ToString(),
                Score = (float)GetDouble(entry, "score"),
                Genres = GetGenreNames(entry),
                Index = index,
                AirDay = airDay,
                AirStartDate = airStartDate,
                Type = (int) MalTypeParser.ParseAnimeType(GetString(entry, "type")),
            };
        }

        private static async Task<List<SeasonalAnimeData>> GetSeasonalFromOfficialMalApi(int year, string season)
        {
            var baseUrl =
                $"https://api.myanimelist.net/v2/anime/season/{year}/{season}" +
                "?sort=anime_num_list_users&limit=100&fields=id,title,main_picture,mean,media_type,num_episodes,genres,broadcast,start_date";

            var clients = new List<HttpClient>();
            if (!string.IsNullOrEmpty(Settings.RefreshToken))
            {
                try
                {
                    clients.Add(await ResourceLocator.MalHttpContextProvider.GetApiHttpContextAsync());
                }
                catch (Exception)
                {
                }
            }
            var anonClient = new HttpClient();
            anonClient.DefaultRequestHeaders.Add("X-MAL-CLIENT-ID", MalOAuthSettings.ClientId);
            clients.Add(anonClient);

            foreach (var client in clients)
            {
                var result = new List<SeasonalAnimeData>();
                var offset = 0;
                while (true)
                {
                    try
                    {
                        using var response = await client.GetAsync($"{baseUrl}&offset={offset}");
                        if (!response.IsSuccessStatusCode)
                            break;

                        var json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("data", out var dataArr) &&
                            dataArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in dataArr.EnumerateArray())
                            {
                                var entry = item;
                                if (item.TryGetProperty("node", out var node) &&
                                    node.ValueKind == JsonValueKind.Object)
                                    entry = node;
                                result.Add(ParseSeasonalEntry(entry, result.Count + 1));
                            }
                        }

                        var hasNext = root.TryGetProperty("paging", out var paging) &&
                                      paging.ValueKind == JsonValueKind.Object &&
                                      paging.TryGetProperty("next", out var next) &&
                                      next.ValueKind == JsonValueKind.String &&
                                      !string.IsNullOrEmpty(next.GetString());
                        if (!hasNext)
                            break;

                        offset += 100;
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }

                if (result.Count != 0)
                    return result;
            }

            return new List<SeasonalAnimeData>();
        }

        private static SeasonalAnimeData ParseSeasonalEntry(JsonElement entry, int index)
        {
            var airDay = -1;
            var airStartDate = "";
            if (entry.TryGetProperty("broadcast", out var broadcast) &&
                broadcast.TryGetProperty("day_of_the_week", out var dayProp) &&
                dayProp.ValueKind == JsonValueKind.String)
            {
                airDay = dayProp.GetString()?.ToLowerInvariant() switch
                {
                    "monday" => 1,
                    "tuesday" => 2,
                    "wednesday" => 3,
                    "thursday" => 4,
                    "friday" => 5,
                    "saturday" => 6,
                    "sunday" => 7,
                    _ => -1
                };
            }
            if (entry.TryGetProperty("start_date", out var startProp) &&
                startProp.ValueKind == JsonValueKind.String)
            {
                var startStr = startProp.GetString();
                if (DateTime.TryParse(startStr, out var dt))
                    airStartDate = dt.ToString("yyyy-MM-dd");
            }

            var imgUrl = GetNestedString(entry, "main_picture", "large");
            if (string.IsNullOrEmpty(imgUrl))
                imgUrl = GetNestedString(entry, "main_picture", "medium");

            return new SeasonalAnimeData
            {
                Title = GetString(entry, "title"),
                Id = GetInt(entry, "id"),
                ImgUrl = imgUrl,
                Episodes = GetInt(entry, "num_episodes").ToString(),
                Score = (float)GetDouble(entry, "mean"),
                Genres = GetGenreNames(entry),
                Index = index,
                AirDay = airDay,
                AirStartDate = airStartDate,
                Type = (int) MalTypeParser.ParseAnimeType(GetString(entry, "media_type")),
            };
        }

private static string GetCurrentSeason()
            {
                return DateTime.UtcNow.Month switch
                {
                    <= 3 => "winter",
                    <= 6 => "spring",
                    <= 9 => "summer",
                    _ => "fall"
                };
            }

        /// <summary>
        ///     The season a date falls in, as the enum.
        /// </summary>
        /// <remarks>
        ///     The same months <see cref="GetCurrentSeason" /> uses, which is what the cache key is
        ///     built from. Two spellings of the same rule is how the cache ends up written under one
        ///     key and read under another.
        /// </remarks>
        private static Season CurrentSeasonOf(DateTime moment)
            => moment.Month switch
            {
                <= 3 => Season.Winter,
                <= 6 => Season.Spring,
                <= 9 => Season.Summer,
                _ => Season.Fall
            };

        private static string SeasonEnumToString(Season season)
        {
            return season switch
            {
                Season.Winter => "winter",
                Season.Spring => "spring",
                Season.Summer => "summer",
                Season.Fall => "fall",
                _ => "fall"
            };
        }

        private static int DayOfWeekStringToInt(string day)
        {
            return day?.ToLowerInvariant() switch
            {
                "mondays" => 1,
                "tuesdays" => 2,
                "wednesdays" => 3,
                "thursdays" => 4,
                "fridays" => 5,
                "saturdays" => 6,
                "sundays" => 7,
                _ => -1
            };
        }

        private static string GetString(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : "";

        private static int GetInt(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;

        private static double GetDouble(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0;

        private static string GetNestedString(JsonElement el, params string[] props)
        {
            foreach (var prop in props.Take(props.Length - 1))
                if (!el.TryGetProperty(prop, out el)) return "";
            return GetString(el, props.Last());
        }

        private static List<string> GetGenreNames(JsonElement el)
        {
            if (!el.TryGetProperty("genres", out var genres) || genres.ValueKind != JsonValueKind.Array)
                return new List<string>();
            var list = new List<string>();
            foreach (var g in genres.EnumerateArray())
            {
                if (g.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    list.Add(name.GetString());
            }
            return list;
        }
    }
}