using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace MALPlus.XShared.Utils
{
    public static class AnimeThemesHelper
    {
        private static readonly HttpClient Client = new HttpClient();

        /// <summary>
        ///     Where AnimeThemes maps a MAL id onto its own entries.
        /// </summary>
        /// <remarks>
        ///     GraphQL rather than REST, because the REST side has no filter for an external reference:
        ///     asking for one answers 422. This endpoint answers 200 with the slugs that share the MAL
        ///     id, and a MAL id can own more than one of them, which is why it returns a list.
        ///     <para>
        ///     No token is needed despite what the docs suggest: a plain POST with a JSON body works.
        /// </para>
        /// </remarks>
        private const string GraphQlEndpoint = "https://graphql.animethemes.moe/";

        private static readonly HttpClient GraphQlClient = new HttpClient();

        static AnimeThemesHelper()
        {
            Client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            GraphQlClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        public static async Task<JsonDocument> ExecuteGraphQlAsync(string query)
        {
            try
            {
                var cleanQuery = query.Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
                var body = "{\"query\":\"" + cleanQuery + "\"}";
                using var cts = new CancellationTokenSource(RequestTimeout);
                var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                var response = await GraphQlClient.PostAsync(GraphQlEndpoint, content, cts.Token);
                if (!response.IsSuccessStatusCode)
                    return null;
                var json = await response.Content.ReadAsStringAsync();
                return JsonDocument.Parse(json);
            }
            catch (Exception ex)
            {
                AppendThemeDebug($"GraphQL execution exception: {ex.Message}");
                return null;
            }
        }

        private static DateTime? _rateLimitedUntil;

        /// <summary>
        ///     How long one refused request silences every other one.
        /// </summary>
        /// <remarks>
        ///     Short, and deliberately so. It used to be a minute, and a minute is far longer than it takes
        ///     a reader to move from the thing that caused it to the next thing they wanted. The random
        ///     theme reported "no theme found" for that whole minute, which read as AnimeThemes being
        ///     empty rather than as the app having asked it to stand still.
        /// </remarks>
        private static readonly TimeSpan RateLimitCooldown = TimeSpan.FromSeconds(10);

        /// <summary>
        ///     How long one request may take before it is written off.
        /// </summary>
        /// <remarks>
        ///     The client had no timeout of its own, so a request that never answered held the caller for
        ///     the default hundred seconds. On a page that means a spinner that never stops and a list
        ///     that never appears, with nothing to say which of the two went wrong.
        /// </remarks>
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(25);

        private static async Task<string> GetStringWithBackoffAsync(string url)
        {
            if (_rateLimitedUntil.HasValue && DateTime.UtcNow < _rateLimitedUntil.Value)
                return null;

            try
            {
                using var cts = new CancellationTokenSource(RequestTimeout);
                return await Client.GetStringAsync(url, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Nothing came back inside the budget. Reported rather than thrown, because a caller
                // that gets an exception here shows an error where "no theme" is the honest answer.
                return null;
            }
            catch (HttpRequestException ex)
            {
                if (ex.Message.Contains("429") || ex.Message.Contains("Too Many Requests"))
                {
                    _rateLimitedUntil = DateTime.UtcNow.Add(RateLimitCooldown);
                    return null;
                }
                if (ex.Message.Contains("404") || ex.Message.Contains("Not Found"))
                {
                    // 404 is expected for slug guesses that don't exist - silent return
                    return null;
                }
                throw;
            }
        }

        public class ThemeVideo
        {
            public string Type { get; set; }
            public int Sequence { get; set; }
            public string Url { get; set; }
            public string AnimeSlug { get; set; }
            public string SongTitle { get; set; }

            /// <summary>
            ///     The MAL id of the entry this theme belongs to, when the source knows it.
            /// </summary>
            /// <remarks>
            ///     Carried so a theme can be matched to the right row without anybody comparing names.
            ///     Zero means AnimeThemes held no reference for this entry, which happens for the
            ///     hand-built ones and means the theme cannot be placed on a card.
            /// </remarks>
            public int MalId { get; set; }

            /// <summary>
            ///     Episode coverage, as inclusive ranges. This is what the match keys on.
            /// </summary>
            /// <summary>
            ///     Size in bytes, as AnimeThemes reports it. Zero when unknown.
            /// </summary>
            /// <remarks>
            ///     Free: it arrives in the same document as the link, so picking the lightest of several
            ///     encodes costs nothing extra. One Piece's first opening exists three times, at 16.8, 30.4
            ///     and 50.2 MB, and the order the API returns them in has nothing to do with size, so
            ///     without this the biggest one is as likely to be played as the smallest.
            /// </remarks>
            public long Size { get; set; }

            public List<EpisodeRange> Episodes { get; set; } = new List<EpisodeRange>();
        }

            /// <summary>
            ///     A class and not a ValueTuple on purpose: this list is round tripped through the
            ///     cache with plain System.Text.Json options, and a tuple serialises as an anonymous
            ///     Item1/Item2 blob that is easy to lose on the way back.
            /// </summary>
            public class EpisodeRange
            {
                public int From { get; set; }
                public int To { get; set; }

                public EpisodeRange()
                {
                }

                public EpisodeRange(int from, int to)
                {
                    From = from;
                    To = to;
                }
            }

        /// <summary>
        ///     Parses AnimeThemes' episode coverage into inclusive ranges.
        ///     The format is loose on purpose at their end and this has to survive all of it:
        ///     "1163", "1123-1138", "1157-1162, 1164-", "1-47, 1000". An open ended "1164-" means
        ///     "from 1164 onwards", which is capped generously because the field only exists to
        ///     decide whether two entries describe the same theme.
        /// </summary>
        internal static List<EpisodeRange> ParseEpisodeRanges(string raw)
        {
            var result = new List<EpisodeRange>();
            if (string.IsNullOrWhiteSpace(raw))
                return result;
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var token = part.Trim().Replace("–", "-").Replace("—", "-");
                if (token.Length == 0)
                    continue;
                var bounds = token.Split('-');
                if (!int.TryParse(bounds[0].Trim(), out var from))
                    continue;
                var to = from;
                if (bounds.Length > 1)
                {
                    var tail = bounds[1].Trim();
                    if (tail.Length == 0)
                        to = from + 5000;
                    else if (!int.TryParse(tail, out to))
                        continue;
                }
                if (to < from) { var swap = from; from = to; to = swap; }
                result.Add(new EpisodeRange(from, to));
            }
            return result;
        }

        /// <summary>
        ///     Parses the episode numbers out of a Tenrai theme line, e.g.
        ///     28: "Never Was Alone in This World (????????????)" by Maki Otsuki (eps 1163)
        ///     or a range such as "(eps 1139-)". The Japanese in those lines arrives as literal
        ///     question marks, which is why the title can never be the reliable key and this is.
        /// </summary>
        internal static List<EpisodeRange> ExtractEpisodeRanges(string opEdText)
        {
            var result = new List<EpisodeRange>();
            if (string.IsNullOrEmpty(opEdText))
                return result;
            var match = Regex.Match(opEdText, @"\(\s*eps?\s*\.?\s*([0-9]+)\s*(?:-\s*([0-9]+)?\s*)?\)",
                RegexOptions.IgnoreCase);
            if (!match.Success)
                return result;
            if (!int.TryParse(match.Groups[1].Value, out var from))
                return result;
            var to = from;
            var tailGroup = match.Groups[2];
            if (tailGroup.Success)
            {
                var tail = tailGroup.Value.Trim();
                to = tail.Length == 0 ? from + 5000 : (int.TryParse(tail, out var parsed) ? parsed : from);
            }
            if (to < from) { var swap = from; from = to; to = swap; }
            result.Add(new EpisodeRange(from, to));
            return result;
        }

        internal static bool RangesOverlap(List<EpisodeRange> a, List<EpisodeRange> b)
        {
            if (a == null || b == null || a.Count == 0 || b.Count == 0)
                return false;
            foreach (var x in a)
            foreach (var y in b)
                if (x.From <= y.To && y.From <= x.To)
                    return true;
            return false;
        }

        private static string NormalizeSong(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            var sb = new System.Text.StringBuilder();
            foreach (var c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        ///     The shared way of reducing two names to something that can be compared.
        /// </summary>
        /// <remarks>
        ///     The same function the song matching has always used, offered for the season join so a
        ///     season of titles is matched with the rule the app already matches songs by rather than a
        ///     second scheme of its own. It keeps letters and digits and drops everything else, which
        ///     is why "Tensei Shitara Ken Deshita 2nd Season" survives as one key.
        /// </remarks>
        public static string NormalizeForMatch(string s) => NormalizeSong(s);

        /// <summary>
        ///     Reads a URL with the source's own backoff, for a caller that wants the raw document.
        /// </summary>
        public static Task<string> GetRawAsync(string url) => GetStringWithBackoffAsync(url);

        /// <summary>
        ///     The name of an anime AnimeThemes holds, from its slug.
        /// </summary>
        /// <remarks>
        ///     The random theme only carries the slug, and a slug is not a thing to show anybody. One
        ///     GraphQL request turns it into the name, which is then enough to reach MAL, which is the only side
        ///     of the arrangement with artwork to show.
        /// </remarks>
        public static async Task<string> GetAnimeNameAsync(string slug)
        {
            if (string.IsNullOrEmpty(slug))
                return null;
            try
            {
                var query = "{ anime(slug: \"" + slug.Replace("\"", "") + "\") { title { romaji english } } }";
                using var doc = await ExecuteGraphQlAsync(query);
                if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("anime", out var anime) || anime.ValueKind != JsonValueKind.Object)
                    return null;

                if (anime.TryGetProperty("title", out var titleObj) && titleObj.ValueKind == JsonValueKind.Object)
                {
                    if (titleObj.TryGetProperty("romaji", out var romajiProp) && romajiProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(romajiProp.GetString()))
                        return romajiProp.GetString();
                    if (titleObj.TryGetProperty("english", out var engProp) && engProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(engProp.GetString()))
                        return engProp.GetString();
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public class AnimeSlugMetadata
        {
            public string Name { get; set; }
            public int Year { get; set; }
            public string Synopsis { get; set; }
            public string MediaFormat { get; set; }
            public int MalId { get; set; }
        }

        public static async Task<AnimeSlugMetadata> GetAnimeMetadataBySlugAsync(string slug)
        {
            if (string.IsNullOrEmpty(slug))
                return null;
            try
            {
                var query = "{ anime(slug: \"" + slug.Replace("\"", "") + "\") { slug title { romaji english } year synopsis formatLocalized resources { nodes { site externalId } } } }";
                using var doc = await ExecuteGraphQlAsync(query);
                if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("anime", out var anime) || anime.ValueKind != JsonValueKind.Object)
                    return null;

                var meta = new AnimeSlugMetadata();
                if (anime.TryGetProperty("title", out var titleObj) && titleObj.ValueKind == JsonValueKind.Object)
                {
                    if (titleObj.TryGetProperty("romaji", out var romaji) && romaji.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(romaji.GetString()))
                        meta.Name = romaji.GetString();
                    else if (titleObj.TryGetProperty("english", out var eng) && eng.ValueKind == JsonValueKind.String)
                        meta.Name = eng.GetString();
                }
                if (anime.TryGetProperty("year", out var yProp) && yProp.ValueKind == JsonValueKind.Number)
                    meta.Year = yProp.GetInt32();
                if (anime.TryGetProperty("synopsis", out var synProp) && synProp.ValueKind == JsonValueKind.String)
                    meta.Synopsis = synProp.GetString();
                if (anime.TryGetProperty("formatLocalized", out var fmtProp) && fmtProp.ValueKind == JsonValueKind.String)
                    meta.MediaFormat = fmtProp.GetString();

                if (anime.TryGetProperty("resources", out var resObj) && resObj.ValueKind == JsonValueKind.Object &&
                    resObj.TryGetProperty("nodes", out var nodesArr) && nodesArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var node in nodesArr.EnumerateArray())
                    {
                        var site = node.TryGetProperty("site", out var siteProp) && siteProp.ValueKind == JsonValueKind.String
                            ? siteProp.GetString() : string.Empty;
                        if (string.Equals(site, "MAL", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(site, "MyAnimeList", StringComparison.OrdinalIgnoreCase))
                        {
                            if (node.TryGetProperty("externalId", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                            {
                                meta.MalId = idProp.GetInt32();
                                break;
                            }
                        }
                    }
                }

                return meta;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Fuzzy score (0..1) of how much of the query words appear in the theme title.
        /// Extra words in the title never penalize, so a high but not perfect coincidence
        /// still matches (user requirement: no 100% needed).
        /// </summary>
        private static double SongMatchScore(string query, string songTitle)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(songTitle))
                return 0.0;
            var title = NormalizeSong(songTitle);
            if (title.Length == 0)
                return 0.0;
            var tokens = Regex.Matches(query, @"[A-Za-z0-9]{2,}")
                .Cast<Match>()
                .Select(m => m.Value.ToLowerInvariant())
                .Distinct()
                .ToList();
            if (tokens.Count == 0)
                return 0.0;
            if (tokens.All(t => title.Contains(t)))
                return 1.0;
            return tokens.Count(t => title.Contains(t)) / (double)tokens.Count;
        }

        private static string Slugify(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            var sb = new System.Text.StringBuilder();
            foreach (var c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        ///     Cache file name for a resolved anime.
        ///
        ///     The v2 in the name is not cosmetic. v1 entries were written before ThemeVideo
        ///     carried episode coverage, so every one of them deserialises with an empty Episodes
        ///     list, and the episode first match then finds no overlap anywhere and silently falls
        ///     back to the fuzzy title score, which cannot match across languages. The result was
        ///     that the fix appeared to do nothing for up to seven days: the tab loaded a healthy
        ///     looking cache written by the old code. There is no in-place migration for this
        ///     cache, so the version lives in the filename and bumping it is how a new field
        ///     reaches an existing install.
        /// </summary>
        private static string ThemeCacheFile(string cacheKey)
        {
            return "animethemes_v2_" + CacheFileName(cacheKey);
        }

        /// <summary>
        ///     The cache file for one MAL entry's themes.
        /// </summary>
        /// <remarks>
        ///     Keyed on the id and not on a name, and that is the whole point: a name is what differs
        ///     between MAL and AnimeThemes, so a cache keyed on one of them answers a question that
        ///     was never asked. The version in the name is bumped from v2, so the entries written under
        ///     the old title keys are simply not read rather than served as though they were current.
        /// </remarks>
        private static string ThemeCacheFileForMalId(int malId)
        {
            // v4 because Size arrived in v3 and a cached entry written before it deserialises with zero,
            // which is exactly the value that makes Lightest decline to choose. There is no in-place
            // migration for this cache, so the version lives in the filename and bumping it is how the
            // new field reaches an existing install.
            return "animethemes_mal_" + malId + "_v4";
        }

        /// <summary>
        ///     Themes already resolved, by MAL id, for the life of the process.
        /// </summary>
        private static readonly Dictionary<int, List<ThemeVideo>> ThemesByMalId =
            new Dictionary<int, List<ThemeVideo>>();

        private static string CacheFileName(string key)
        {
            return Regex.Replace(key ?? "", @"[^a-z0-9]", "_");
        }

        /// <summary>
        /// Rejects fuzzy false positives: "Enen no Shouboutai..." must never match
        /// "ShingekiNoKyojinS3Part2" just because the search API returned it.
        /// </summary>
        private static bool SlugMatches(string videoSlug, string querySlug)
        {
            if (string.IsNullOrEmpty(videoSlug) || string.IsNullOrEmpty(querySlug))
                return true;
            if (videoSlug.Contains(querySlug) || querySlug.Contains(videoSlug))
                return true;
            var prefix = 0;
            var max = Math.Min(videoSlug.Length, querySlug.Length);
            while (prefix < max && videoSlug[prefix] == querySlug[prefix])
                prefix++;
            return prefix >= 6;
        }

        private static readonly Dictionary<string, List<ThemeVideo>> Cache =
            new Dictionary<string, List<ThemeVideo>>();

        private static readonly object DebugFileLock = new object();
        private static string _debugFilePath;

        private static void AppendThemeDebug(string line)
        {
            // The per-line File.AppendAllText used to ship enabled: 40-80 serialized
            // open/write/close syscalls under a process-wide lock on EVERY details open,
            // all for a file nobody reads. Keep it behind an explicit opt-in.
            if (!ThemeDebugEnabled || line == null)
                return;
            try
            {
                lock (DebugFileLock)
                {
                    if (_debugFilePath == null)
                    {
                        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                        var dir = System.IO.Path.Combine(baseDir, "ThemeDebug");
                        System.IO.Directory.CreateDirectory(dir);
                        _debugFilePath = System.IO.Path.Combine(dir, "themes_debug.txt");
                    }
                    System.IO.File.AppendAllText(_debugFilePath,
                        $"[{DateTime.Now:HH:mm:ss.fff}] {line}{Environment.NewLine}");
                }
            }
            catch (Exception)
            {
            }
        }

        public static bool ThemeDebugEnabled { get; set; }

        /// <summary>
        ///     The themes of one MAL entry, and the only way anything in the app asks for them.
        /// </summary>
        /// <remarks>
        ///     Everything resolves by MAL id here, and that is deliberate. The name is not a reliable
        ///     key: AnimeThemes calls Psyren "PSYЯEN", with a Cyrillic letter, and a search by the name
        ///     MAL uses finds nothing at all. The id is the same on both sides and needs no interpreting.
        ///     <para>
        ///     One MAL id can own several AnimeThemes entries — JoJo's Steel Ball Run has one for
        ///     "Part 7" and a second for "2nd &amp; 3rd STAGE" — so every slug the id maps to is read and
        ///     the results are merged, with duplicates collapsed on slot, song and video.
        ///     </para>
        ///     <para>
        ///     Cached twice: in memory for the life of the process, and on disk under the id for seven
        ///     days. Keying the disk copy on the id rather than a name is what makes it mean the same
        ///     thing it did when it was written.
        ///     </para>
        /// </remarks>
        public static async Task<List<ThemeVideo>> GetThemesAsync(int malId)
        {
            if (malId <= 0)
                return new List<ThemeVideo>();

            if (ThemesByMalId.TryGetValue(malId, out var remembered))
                return remembered;

            var onDisk = await DataCache.RetrieveData<List<ThemeVideo>>(
                ThemeCacheFileForMalId(malId), "AnimeThemes", 7);
            if (onDisk != null && onDisk.Count > 0)
            {
                ThemesByMalId[malId] = onDisk;
                return onDisk;
            }

            // Direct GraphQL lookup: single roundtrip, carries OPs/EDs + videos directly,
            // and remains fully operational even when the legacy REST API (api.animethemes.moe) is 503.
            var merged = await FetchThemesFromGraphQlByMalIdAsync(malId);
            if (merged == null || merged.Count == 0)
            {
                var slugs = await GetSlugsByMalIdAsync(malId);
                if (slugs.Count == 0)
                {
                    ThemesByMalId[malId] = new List<ThemeVideo>();
                    return ThemesByMalId[malId];
                }

                merged = new List<ThemeVideo>();
                if (slugs.Count == 1)
                {
                    merged = await FetchBySlugAsync(slugs[0]);
                }
                else
                {
                    var batches = await Task.WhenAll(slugs.Select(slug => FetchBySlugAsync(slug)));
                    foreach (var batch in batches)
                        if (batch != null)
                            merged.AddRange(batch);
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                merged = merged
                    .Where(theme => seen.Add(theme.Type + " " + theme.Sequence + "|" +
                                             (theme.SongTitle ?? "") + "|" + theme.Url))
                    .ToList();
            }

            foreach (var theme in merged)
                theme.MalId = malId;

            ThemesByMalId[malId] = merged;
            try
            {
                await DataCache.SaveData(merged, ThemeCacheFileForMalId(malId), "AnimeThemes");
            }
            catch
            {
            }
            return merged;
        }

        /// <summary>
        ///     Fills the id cache from themes somebody already read in one go, so the per-entry
        ///     resolution finds them without asking the source again.
        /// </summary>
        /// <remarks>
        ///     What makes the season tab cheap: it reads every entry of the season with its references
        ///     in a single request, so the rows afterwards hit the cache rather than each spending two
        ///     requests of their own. It goes through the same function the other callers use.
        /// </remarks>
        public static void PrimeThemesByMalId(IEnumerable<KeyValuePair<int, List<ThemeVideo>>> entries)
        {
            foreach (var pair in entries)
            {
                if (pair.Key <= 0 || pair.Value == null || pair.Value.Count == 0)
                    continue;
                ThemesByMalId[pair.Key] = pair.Value;
            }
        }

        /// <summary>
        ///     Fetches all themes and videos for a MAL entry directly via GraphQL in a single request.
        /// </summary>
        public static async Task<List<ThemeVideo>> FetchThemesFromGraphQlByMalIdAsync(int malId)
        {
            var result = new List<ThemeVideo>();
            if (malId <= 0)
                return result;

            try
            {
                var query = "{ findAnimeByExternalSite(site: MAL, id: " + malId + ") { slug animethemes { type sequence song { title { romaji native } } animethemeentries { episodes videos { nodes { basename link size } } } } } }";
                var body = "{\"query\":\"" + query.Replace("\"", "\\\"") + "\"}";
                using var cts = new CancellationTokenSource(RequestTimeout);

                var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                var response = await GraphQlClient.PostAsync(GraphQlEndpoint, content, cts.Token);
                if (!response.IsSuccessStatusCode)
                    return result;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("findAnimeByExternalSite", out var matches) ||
                    matches.ValueKind != JsonValueKind.Array)
                    return result;

                foreach (var animeMatch in matches.EnumerateArray())
                {
                    var slug = animeMatch.TryGetProperty("slug", out var sProp) && sProp.ValueKind == JsonValueKind.String
                        ? sProp.GetString() : string.Empty;

                    if (!animeMatch.TryGetProperty("animethemes", out var themesArr) || themesArr.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var themeObj in themesArr.EnumerateArray())
                    {
                        var type = themeObj.TryGetProperty("type", out var tProp) && tProp.ValueKind == JsonValueKind.String
                            ? tProp.GetString() : "OP";
                        var sequence = themeObj.TryGetProperty("sequence", out var seqProp) && seqProp.ValueKind == JsonValueKind.Number
                            ? seqProp.GetInt32() : 1;

                        string songTitle = null;
                        if (themeObj.TryGetProperty("song", out var songObj) && songObj.ValueKind == JsonValueKind.Object &&
                            songObj.TryGetProperty("title", out var titleObj) && titleObj.ValueKind == JsonValueKind.Object)
                        {
                            if (titleObj.TryGetProperty("romaji", out var romajiProp) && romajiProp.ValueKind == JsonValueKind.String)
                                songTitle = romajiProp.GetString();
                            else if (titleObj.TryGetProperty("native", out var nativeProp) && nativeProp.ValueKind == JsonValueKind.String)
                                songTitle = nativeProp.GetString();
                        }

                        if (!themeObj.TryGetProperty("animethemeentries", out var entriesArr) || entriesArr.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (var entryObj in entriesArr.EnumerateArray())
                        {
                            var epStr = entryObj.TryGetProperty("episodes", out var epProp) && epProp.ValueKind == JsonValueKind.String
                                ? epProp.GetString() : null;
                            var epRanges = ParseEpisodeRanges(epStr);

                            if (!entryObj.TryGetProperty("videos", out var videosObj) || videosObj.ValueKind != JsonValueKind.Object ||
                                !videosObj.TryGetProperty("nodes", out var nodesArr) || nodesArr.ValueKind != JsonValueKind.Array)
                                continue;

                            foreach (var node in nodesArr.EnumerateArray())
                            {
                                var link = node.TryGetProperty("link", out var linkProp) && linkProp.ValueKind == JsonValueKind.String
                                    ? linkProp.GetString() : null;
                                if (string.IsNullOrEmpty(link))
                                    continue;

                                long size = node.TryGetProperty("size", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number
                                    ? sizeProp.GetInt64() : 0;

                                result.Add(new ThemeVideo
                                {
                                    Type = type,
                                    Sequence = sequence,
                                    Url = link,
                                    AnimeSlug = slug,
                                    SongTitle = songTitle,
                                    MalId = malId,
                                    Size = size,
                                    Episodes = epRanges
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendThemeDebug($"GraphQL error for malId={malId}: {ex.Message}");
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return result
                .Where(theme => seen.Add(theme.Type + " " + theme.Sequence + "|" +
                                         (theme.SongTitle ?? "") + "|" + theme.Url))
                .ToList();
        }

        /// <summary>
        ///     The AnimeThemes slugs one MAL entry maps to, which can be more than one.
        /// </summary>
        public static async Task<List<string>> GetSlugsByMalIdAsync(int malId)
        {
            var slugs = new List<string>();
            if (malId <= 0)
                return slugs;

            try
            {
                var query =
                    "{ findAnimeByExternalSite(site: MAL, id: " + malId + ") { slug } }";
                var body = "{\"query\":\"" + query.Replace("\"", "\\\"") + "\"}";
                using var cts = new CancellationTokenSource(RequestTimeout);

                var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                var response = await GraphQlClient.PostAsync(GraphQlEndpoint, content, cts.Token);
                if (!response.IsSuccessStatusCode)
                    return slugs;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("findAnimeByExternalSite", out var matches) ||
                    matches.ValueKind != JsonValueKind.Array)
                    return slugs;

                foreach (var match in matches.EnumerateArray())
                {
                    if (match.TryGetProperty("slug", out var slugProp) &&
                        slugProp.ValueKind == JsonValueKind.String)
                    {
                        var slug = slugProp.GetString();
                        if (!string.IsNullOrEmpty(slug))
                            slugs.Add(slug);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }

            return slugs;
        }

        /// <summary>
        ///     The themes of one AnimeThemes entry, read by its slug via GraphQL.
        /// </summary>
        public static async Task<List<ThemeVideo>> FetchBySlugAsync(string slug, int malId = 0)
        {
            var result = new List<ThemeVideo>();
            if (string.IsNullOrEmpty(slug))
                return result;

            try
            {
                var query = "{ anime(slug: \"" + slug.Replace("\"", "") + "\") { slug resources { nodes { site externalId } } animethemes { type sequence song { title { romaji native } } animethemeentries { episodes videos { nodes { basename link size } } } } } }";
                using var doc = await ExecuteGraphQlAsync(query);
                if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("anime", out var anime) || anime.ValueKind != JsonValueKind.Object)
                    return result;

                if (malId <= 0 && anime.TryGetProperty("resources", out var resObj) && resObj.ValueKind == JsonValueKind.Object &&
                    resObj.TryGetProperty("nodes", out var nodesArr) && nodesArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var node in nodesArr.EnumerateArray())
                    {
                        var site = node.TryGetProperty("site", out var sProp) && sProp.ValueKind == JsonValueKind.String ? sProp.GetString() : "";
                        if (string.Equals(site, "MAL", StringComparison.OrdinalIgnoreCase) || string.Equals(site, "MyAnimeList", StringComparison.OrdinalIgnoreCase))
                        {
                            if (node.TryGetProperty("externalId", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                            {
                                malId = idProp.GetInt32();
                                break;
                            }
                        }
                    }
                }

                if (!anime.TryGetProperty("animethemes", out var themesArr) || themesArr.ValueKind != JsonValueKind.Array)
                    return result;

                foreach (var themeObj in themesArr.EnumerateArray())
                {
                    var type = themeObj.TryGetProperty("type", out var tProp) && tProp.ValueKind == JsonValueKind.String ? tProp.GetString() : "OP";
                    var sequence = themeObj.TryGetProperty("sequence", out var seqProp) && seqProp.ValueKind == JsonValueKind.Number ? seqProp.GetInt32() : 1;

                    string songTitle = null;
                    if (themeObj.TryGetProperty("song", out var songObj) && songObj.ValueKind == JsonValueKind.Object &&
                        songObj.TryGetProperty("title", out var titleObj) && titleObj.ValueKind == JsonValueKind.Object)
                    {
                        if (titleObj.TryGetProperty("romaji", out var rProp) && rProp.ValueKind == JsonValueKind.String)
                            songTitle = rProp.GetString();
                        else if (titleObj.TryGetProperty("native", out var nProp) && nProp.ValueKind == JsonValueKind.String)
                            songTitle = nProp.GetString();
                    }

                    if (!themeObj.TryGetProperty("animethemeentries", out var entriesArr) || entriesArr.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var entryObj in entriesArr.EnumerateArray())
                    {
                        var epStr = entryObj.TryGetProperty("episodes", out var epProp) && epProp.ValueKind == JsonValueKind.String ? epProp.GetString() : null;
                        var epRanges = ParseEpisodeRanges(epStr);

                        if (!entryObj.TryGetProperty("videos", out var vidObj) || vidObj.ValueKind != JsonValueKind.Object ||
                            !vidObj.TryGetProperty("nodes", out var vidNodes) || vidNodes.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (var vid in vidNodes.EnumerateArray())
                        {
                            var link = vid.TryGetProperty("link", out var lProp) && lProp.ValueKind == JsonValueKind.String ? lProp.GetString() : null;
                            if (string.IsNullOrEmpty(link))
                                continue;
                            long size = vid.TryGetProperty("size", out var szProp) && szProp.ValueKind == JsonValueKind.Number ? szProp.GetInt64() : 0;

                            result.Add(new ThemeVideo
                            {
                                Type = type,
                                Sequence = sequence,
                                Url = link,
                                AnimeSlug = slug,
                                SongTitle = songTitle,
                                MalId = malId,
                                Size = size,
                                Episodes = epRanges
                            });
                        }
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        /// <summary>
        ///     Themes found by name. NOT the way anything asks for them any more.
        /// </summary>
        /// <remarks>
        ///     Kept only as a red de seguridad for when the id route cannot reach the source at all,
        ///     because a name is a bad key and this is the code that proved it: it searches for the name
        ///     MAL uses, and AnimeThemes calls Psyren "PSYЯEN", so it finds nothing. A resolved-by-id
        ///     answer beats a guessed-by-name one every time, which is why <see cref="GetThemesAsync"/>
        ///     exists and this is not on its path.
        /// </remarks>
        public static async Task<List<ThemeVideo>> SearchAsync(string animeTitle, string englishTitle = null)
        {
            if (string.IsNullOrEmpty(animeTitle) && string.IsNullOrEmpty(englishTitle))
                return new List<ThemeVideo>();

            // Build search variants: English title FIRST (matches AnimeThemes DB), then romaji
            var variants = new List<string>();
            if (!string.IsNullOrEmpty(englishTitle))
            {
                variants.Add(englishTitle.Trim());
                var colonIdx = englishTitle.IndexOf(':');
                if (colonIdx > 0)
                    variants.Add(englishTitle.Substring(0, colonIdx).Trim());
            }
            if (!string.IsNullOrEmpty(animeTitle))
            {
                variants.Add(animeTitle.Trim());
                var colonIdx = animeTitle.IndexOf(':');
                if (colonIdx > 0)
                    variants.Add(animeTitle.Substring(0, colonIdx).Trim());
                var partMatch = Regex.Match(animeTitle, @"^(.+?)(?:\s+(?:Part|Season|S\d|2nd|3rd|4th).*)?$", RegexOptions.IgnoreCase);
                if (partMatch.Success && partMatch.Groups[1].Value.Trim() != animeTitle.Trim())
                    variants.Add(partMatch.Groups[1].Value.Trim());
            }

            // PHASE 1 â€” structured /anime resolution for EVERY variant. The legacy search
            // is season-ambiguous (e.g. "KonoSuba" returns Season 1 videos), so a structured
            // hit from a later variant (romaji "Kono Subarashii Sekai ni Shukufuku wo! 3")
            // must never be masked by legacy garbage cached from an earlier one. Only cache
            // structured results.
            foreach (var variant in variants)
            {
                var cacheKey = variant.ToLowerInvariant();
                AppendThemeDebug($"PHASE1 variant='{variant}'");
                if (Cache.TryGetValue(cacheKey, out var cached) && cached.Count > 0)
                    return cached;

                // Persisted cache: the AnimeThemes server intermittently serves EMPTY theme
                // relations (Konosuba S3 is a real case) for minutes at a time â€” an in-memory
                // cache cannot survive that. Once a structured resolve succeeds, store it so
                // future sessions/taps stay immune to the flap.
                try
                {
                    var diskList = await DataCache.RetrieveData<List<ThemeVideo>>(
                        ThemeCacheFile(cacheKey), "AnimeThemes", 7);
                    AppendThemeDebug($"  disk cache key='{ThemeCacheFile(cacheKey)}' hit={diskList?.Count ?? 0}");
                    if (diskList != null && diskList.Count > 0)
                    {
                        foreach (var v in variants)
                            Cache[v.ToLowerInvariant()] = diskList;
                        return diskList;
                    }
                }
                catch (Exception)
                {
                }

                try
                {
                    var videos = await SearchViaAnimeApi(variant,
                        string.Equals(variant, animeTitle?.Trim(), StringComparison.OrdinalIgnoreCase));
                    AppendThemeDebug($"  SearchViaAnimeApi='{variant}' result={videos?.Count ?? 0}");
                    if (videos.Count > 0)
                    {
                        foreach (var v in variants)
                        {
                            var vKey = v.ToLowerInvariant();
                            Cache[vKey] = videos;
                            try
                            {
                                await DataCache.SaveData(videos, ThemeCacheFile(vKey), "AnimeThemes");
                            }
                            catch (Exception)
                            {
                            }
                        }
                        return videos;
                    }
                }
                catch (Exception ex)
                {
                    if (ex is not OperationCanceledException)
                    {
                    }
                }
            }

            // PHASE 2 â€” legacy fuzzy endpoint, only when no variant resolved a structured anime
            foreach (var variant in variants)
            {
                var cacheKey = variant.ToLowerInvariant();
                AppendThemeDebug($"PHASE2 variant='{variant}'");
                if (Cache.TryGetValue(cacheKey, out var cached) && cached.Count > 0)
                    return cached;
                try
                {
                    var videos = await SearchViaLegacySearch(variant);
                    AppendThemeDebug($"  SearchViaLegacySearch='{variant}' result={videos?.Count ?? 0}");
                    if (videos.Count > 0)
                    {
                        // NEVER cache legacy results: they are season-ambiguous (e.g. "KonoSuba"
                        // returns Season 1 videos) and a transient API hiccup would poison every
                        // subsequent tap within the session via the phase-1 cache lookup.
                        return videos;
                    }
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException)
                    {
                        return new List<ThemeVideo>();
                    }
                }
            }

            return new List<ThemeVideo>();
        }

        private static async Task<List<ThemeVideo>> SearchViaLegacySearch(string variant)
        {
            var query = Uri.EscapeDataString(variant);
            var url = $"https://api.animethemes.moe/search?q={query}";
            AppendThemeDebug($"GET {url}");
            var json = await GetStringWithBackoffAsync(url);
            if (string.IsNullOrEmpty(json))
                return new List<ThemeVideo>(); // 404 or rate limited
            AppendThemeDebug($"  resp len={json?.Length ?? 0} head={(json?.Length > 200 ? json.Substring(0, 200) : json)}");
            var response = JsonConvert.DeserializeObject<SearchResponse>(json);

            var videos = new List<ThemeVideo>();
            if (response?.search?.videos == null)
                return videos;
            var querySlug = Slugify(variant);
            foreach (var video in response.search.videos)
            {
                var match = Regex.Match(video.basename ?? "",
                    @"-((OP|ED)(\d*))\.webm$", RegexOptions.IgnoreCase);
                if (!match.Success)
                    continue;
                var animeSlug = video.basename.Substring(0, match.Index);
                if (!SlugMatches(Slugify(animeSlug), querySlug))
                    continue;
                var type = match.Groups[2].Value.ToUpper();
                var seqStr = match.Groups[3].Value;
                var seq = string.IsNullOrEmpty(seqStr) ? 1 : int.Parse(seqStr);
                videos.Add(new ThemeVideo
                {
                    Type = type,
                    Sequence = seq,
                    Url = video.link,
                    AnimeSlug = animeSlug
                });
            }
            return videos;
        }

        /// <summary>
        /// Structured /anime API: resolves the exact series (sequels included) and
        /// returns every theme with its real sequence number. The /anime endpoint returns
        /// a FLAT document under the top-level "anime" key (anime â†’ animethemes â†’
        /// song / animethemeentries â†’ videos), NOT a JSON:API sideloaded "included" array.
        /// </summary>
        private static async Task<List<ThemeVideo>> SearchViaAnimeApi(string variant, bool guessKeyedSlug = false)
        {
            // The CDN/query cache for the /anime LIST route is poisoned for stable periods
            // (Konosuba S3: filter[name]= returns empty/anime=[] or even cross-aliased bodies
            // from /search). The KEYED route /anime/{slug} is a DIFFERENT path with its own
            // cache key and stays healthy. Kageetai slugs are just the lowercased title with
            // spacesâ†’underscores, so we can GUESS the slug and hit the healthy route first
            // for the romaji title.
            if (guessKeyedSlug)
            {
                var slugVideos = await TryKeyedSlugGuess(variant);
                if (slugVideos.Count > 0)
                    return slugVideos;
            }

            // The AnimeThemes CDN edge caches EMPTY theme relations (or empty results) per
            // IP/POP for a long time â€” KonoSuba S3 is a textbook case: healthy from a PC on
            // another edge, permanently EMPTY from the phone's edge. A random query param
            // busts the cache key and forces a fresh origin fetch.
            var videos = await FetchByVariant(variant, null);
            if (videos.Count > 0)
                return videos;

            videos = await FetchByVariant(variant, Guid.NewGuid().ToString("N"));
            if (videos.Count > 0)
                return videos;

            return await FetchByVariant(variant, Guid.NewGuid().ToString("N"));
        }

        private static async Task<List<ThemeVideo>> TryKeyedSlugGuess(string variant)
        {
            var guessSlug = GuessSlug(variant);
            if (string.IsNullOrEmpty(guessSlug))
                return new List<ThemeVideo>();
            var keyedUrl =
                $"https://api.animethemes.moe/anime/{guessSlug}?include=animethemes.animethemeentries.videos,animethemes.song";
            try
            {
                AppendThemeDebug($"GET {keyedUrl}");
                var keyedJson = await GetStringWithBackoffAsync(keyedUrl);
                if (string.IsNullOrEmpty(keyedJson))
                    return new List<ThemeVideo>(); // 404 or rate limited - silent fallback
                AppendThemeDebug($"  resp len={keyedJson?.Length ?? 0} head={(keyedJson?.Length > 160 ? keyedJson.Substring(0, 160) : keyedJson)}");
                var keyed = JsonConvert.DeserializeObject<AnimeKeyedApiResponse>(keyedJson);
                var videos = BuildVideos(keyed?.anime);
                AppendThemeDebug($"  keyed guess slug='{guessSlug}' result={videos.Count} themes={keyed?.anime?.animethemes?.Count}");
                if (videos.Count > 0)
                return videos;
            }
            catch (Exception ex)
            {
                AppendThemeDebug($"  keyed guess slug='{guessSlug}' EXCEPTION {ex.Message}");
                return new List<ThemeVideo>();
            }

            return new List<ThemeVideo>();
        }
        private static string GuessSlug(string title)
        {
            if (string.IsNullOrEmpty(title)) return "";
            var cleaned = new List<string>();
            foreach (var word in title.ToLowerInvariant().Split(' '))
            {
                var sb = new System.Text.StringBuilder();
                foreach (var c in word)
                    if (char.IsLetterOrDigit(c))
                        sb.Append(c);
                if (sb.Length > 0)
                    cleaned.Add(sb.ToString());
            }
            return string.Join("_", cleaned);
        }

        private static async Task<List<ThemeVideo>> FetchByVariant(string variant, string cacheBuster)
        {
            var query = Uri.EscapeDataString(variant);
            var buster = string.IsNullOrEmpty(cacheBuster) ? "" : "&cb=" + cacheBuster;
            var url = $"https://api.animethemes.moe/anime?filter[name]={query}&include=animethemes.animethemeentries.videos,animethemes.song&page[size]=8{buster}";
            AppendThemeDebug($"GET {url}");
            var json = await GetStringWithBackoffAsync(url);
            if (string.IsNullOrEmpty(json))
                return new List<ThemeVideo>(); // 404 or rate limited
            AppendThemeDebug($"  resp len={json?.Length ?? 0} head={(json?.Length > 160 ? json.Substring(0, 160) : json)}");
            var response = JsonConvert.DeserializeObject<AnimeApiResponse>(json);
            AppendThemeDebug($"  parse anime={response?.anime?.Count ?? 0} exactThemes={(response?.anime?.FirstOrDefault()?.animethemes?.Count) ?? 0}");
            if (response?.anime == null || response.anime.Count == 0)
                return new List<ThemeVideo>();

            var exact = response.anime.FirstOrDefault(a =>
                            string.Equals(a.name?.Trim(), variant.Trim(), StringComparison.OrdinalIgnoreCase))
                     ?? response.anime.FirstOrDefault(a =>
                            a.name?.IndexOf(variant.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                     ?? response.anime.FirstOrDefault();
            AppendThemeDebug($"  anime count={response.anime.Count} exact='{exact?.name}' slug='{exact?.slug}' themes={exact?.animethemes?.Count}");

            var videos = BuildVideos(exact);
            if (videos.Count > 0 || string.IsNullOrEmpty(exact?.slug))
                return videos;

            // The /anime LIST endpoint (filter[name]=) may ship an empty animetheme relation for
            // valid records (Konosuba S3) while the themes exist. The KEYED route /anime/{slug}
            // is more reliable â€” rescue with it, retrying with a fresh cache-buster each attempt.
            var keyedBase =
                $"https://api.animethemes.moe/anime/{Uri.EscapeDataString(exact.slug)}?include=animethemes.animethemeentries.videos,animethemes.song";
            for (var attempt = 0; attempt < 2 && videos.Count == 0; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(500);
                try
                {
                    var keyedUrl = keyedBase + "&cb=" + Guid.NewGuid().ToString("N");
                    AppendThemeDebug($"GET {keyedUrl}");
                    var keyedJson = await GetStringWithBackoffAsync(keyedUrl);
                    if (string.IsNullOrEmpty(keyedJson))
                        continue; // 404 or rate limited, try next attempt
                    AppendThemeDebug($"  resp len={keyedJson?.Length ?? 0} head={(keyedJson?.Length > 160 ? keyedJson.Substring(0, 160) : keyedJson)}");
                    var keyed = JsonConvert.DeserializeObject<AnimeKeyedApiResponse>(keyedJson);
                    videos = BuildVideos(keyed?.anime);
                    AppendThemeDebug($"  keyed attempt={attempt + 1} themes={keyed?.anime?.animethemes?.Count} result={videos.Count}");
                }
                catch (Exception ex)
                {
                    AppendThemeDebug($"  keyed attempt={attempt + 1} EXCEPTION {ex.Message}");
                }
            }
            return videos;
        }

        private static List<ThemeVideo> BuildVideos(InlineAnime anime)
        {
            var videos = new List<ThemeVideo>();
            if (anime?.animethemes == null)
                return videos;
            foreach (var theme in anime.animethemes)
            {
                foreach (var entry in theme.animethemeentries ?? new List<InlineThemeEntry>())
                {
                    foreach (var video in entry.videos ?? new List<InlineVideo>())
                    {
                        if (string.IsNullOrEmpty(video.link))
                            continue;
                        // Same video AND same kind. The URL alone was too broad: one video used by both
                        // an opening and an ending was kept only once, whichever came first, and an
                        // ending that shared its file with an opening silently became the opening.
                        if (videos.Any(v => v.Url == video.link &&
                                           string.Equals(v.Type, theme.type, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        videos.Add(new ThemeVideo
                        {
                            Type = theme.type,
                                Sequence = theme.sequence ?? 1,
                                Url = video.link,
                                AnimeSlug = Slugify(anime.name),
                                SongTitle = theme.song?.title,
                                Size = video.size,
                                Episodes = ParseEpisodeRanges(entry.episodes)
                            });
                    }
                }
            }
            return videos;
        }

        private class AnimeApiResponse
        {
            [JsonProperty("anime")] public List<InlineAnime> anime { get; set; }
        }

        private class AnimeKeyedApiResponse
        {
            [JsonProperty("anime")] public InlineAnime anime { get; set; }
        }

        private class InlineAnime
        {
            [JsonProperty("slug")] public string slug { get; set; }
            [JsonProperty("name")] public string name { get; set; }
            [JsonProperty("animethemes")] public List<InlineTheme> animethemes { get; set; }
        }

        private class InlineTheme
        {
            [JsonProperty("type")] public string type { get; set; }
            [JsonProperty("sequence")] public int? sequence { get; set; }
            [JsonProperty("song")] public InlineSong song { get; set; }
            [JsonProperty("anime")] public InlineThemeAnime anime { get; set; }
            [JsonProperty("animethemeentries")] public List<InlineThemeEntry> animethemeentries { get; set; }
        }

        private class InlineThemeAnime
        {
            [JsonProperty("slug")] public string slug { get; set; }
            [JsonProperty("name")] public string name { get; set; }
        }

        private class InlineSong
        {
            [JsonProperty("title")] public string title { get; set; }
        }

private class InlineThemeEntry
             {
                 [JsonProperty("videos")] public List<InlineVideo> videos { get; set; }
                 /// <summary>
                 ///     Raw episode coverage, e.g. "1163", "1123-1138" or "1157-1162, 1164-".
                 ///     AnimeThemes ships this for 74 of 75 entries on One Piece and it is the only
                 ///     join key between the two sources that actually holds: their titles are in
                 ///     different languages and there is no alternate-title field to translate with.
                 /// </summary>
                 [JsonProperty("episodes")] public string episodes { get; set; }
             }

        private class InlineVideo
        {
            [JsonProperty("link")] public string link { get; set; }
            [JsonProperty("size")] public long size { get; set; }
        }

        /// <summary>
        ///     Picks the theme for an OP/ED row.
        ///
        ///     Episode coverage is the primary key, and the reason is that the two sources name the
        ///     same theme in different languages. One Piece's last ending is
        ///     "Never Was Alone in This World" from Tenrai, with the Japanese mangled to literal
        ///     question marks, and AnimeThemes lists that exact episode as "Sekai wa Hitori ja
        ///     Nakatta". AnimeThemes has no alternate-title field, so there is nothing to translate
        ///     with and a fuzzy title score cannot rescue it: the row fell through to YouTube for
        ///     every Japanese-titled theme of a show whose list is entirely in English. Both sides
        ///     carry the episode numbers though, and they agree, so the join is done on those and
        ///     the title only breaks ties between candidates that overlap.
        ///
        ///     Sequence is still ignored on purpose: it is not aligned between the sources (Tenrai
        ///     calls "Sono Mirai" number 27 where AnimeThemes calls it 26).
        /// </summary>
        public static ThemeVideo FindMatch(List<ThemeVideo> videos, bool isOp, int sequence, string songQuery = null, string songName = null, string opEdText = null)
        {
            if (videos == null || videos.Count == 0)
                return null;

            // Only themes of the kind that was asked about. This is the whole fix for picking the last
            // opening when the last ending was tapped: the kind was already an argument, and it was
            // never consulted, so when a line carried no episode range the scoring ran over openings
            // and endings together and the better title won regardless of which one it belonged to.
            //
            // An empty set of the right kind returns null rather than falling back to the other kind.
            // Playing an opening for an ending is worse than not playing.
            var wantedType = isOp ? "OP" : "ED";
            videos = videos
                .Where(v => string.Equals(v.Type, wantedType, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (videos.Count == 0)
                return null;

            var wanted = ExtractEpisodeRanges(opEdText);
            if (wanted.Count > 0)
            {
                var overlapping = videos.Where(v => RangesOverlap(wanted, v.Episodes)).ToList();
                if (overlapping.Count == 1)
                    return overlapping[0];
                if (overlapping.Count > 1)
                {
                    // Several versions cover the same episodes. Score the title to choose between
                    // them, and fall back to the first rather than giving up when no title matches.
                    ThemeVideo bestByEpisode = null;
                    var bestEpisodeScore = 0.0;
                    foreach (var v in overlapping)
                    {
                        var score = ScoreSong(v, songQuery, songName);
                        if (score > bestEpisodeScore)
                        {
                            bestEpisodeScore = score;
                            bestByEpisode = v;
                        }
                    }
                    return Lightest(bestByEpisode ?? overlapping[0], overlapping);
                }
            }

            ThemeVideo best = null;
            var bestScore = 0.0;
            foreach (var v in videos)
            {
                // Prefer the bare song name (artist tokens the title lacks would unfairly
                // drag the score down); fall back to the full artist+song otherwise.
                var score = ScoreSong(v, songQuery, songName);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = v;
                }
            }
            return bestScore >= 0.7 ? Lightest(best, videos) : null;
        }

        /// <summary>
        ///     Swaps the chosen encode for the smallest one that plays the same slot, keeping the winner
        ///     when the candidates are not interchangeable.
        /// </summary>
        /// <remarks>
        ///     AnimeThemes files one opening several times: One Piece's first exists as an NCDVD480 at
        ///     16.8 MB, a plain one at 30.4 and a v2 at 50.2. They are the same ninety seconds, so the
        ///     biggest is three times the transfer for nothing, and the order they arrive in is not
        ///     sorted by size, so "take the first" is how the 50 MB one got played.
        ///     <para>
        ///     Only applies inside the same theme slot, and never trades away the chosen song: a candidate
        ///     only replaces the winner when it is the same type and sequence, so a differently titled
        ///     version of the same episodes cannot slip in and defeat the match that just happened.
        ///     </para>
        /// </remarks>
        private static ThemeVideo Lightest(ThemeVideo chosen, List<ThemeVideo> candidates)
        {
            if (chosen == null || candidates == null || candidates.Count <= 1 || chosen.Size <= 0)
                return chosen;

            ThemeVideo best = chosen;
            foreach (var v in candidates)
            {
                if (v == null || v.Size <= 0 || v.Size >= best.Size)
                    continue;
                if (!string.Equals(v.Type, chosen.Type, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (v.Sequence != chosen.Sequence)
                    continue;
                if (!string.Equals(NormalizeSong(v.SongTitle), NormalizeSong(chosen.SongTitle),
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                best = v;
            }

            return best;
        }

        private static double ScoreSong(ThemeVideo v, string songQuery, string songName)
        {
            if (string.IsNullOrWhiteSpace(v?.SongTitle))
                return 0.0;
            return string.IsNullOrEmpty(songName)
                ? SongMatchScore(songQuery, v.SongTitle)
                : Math.Max(SongMatchScore(songName, v.SongTitle), SongMatchScore(songQuery, v.SongTitle));
        }

        public static async Task<ThemeVideo> GetRandomThemeAsync()
        {
            var query = "{ animethemeShuffle(first: 1) { type sequence anime { slug title { romaji english } resources { nodes { site externalId } } } song { title { romaji native } } animethemeentries { episodes videos { nodes { basename link size } } } } }";

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var doc = await ExecuteGraphQlAsync(query);
                    if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) ||
                        !data.TryGetProperty("animethemeShuffle", out var shuffleArr) ||
                        shuffleArr.ValueKind != JsonValueKind.Array)
                    {
                        if (attempt >= 3) return null;
                        await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt));
                        continue;
                    }

                    var theme = shuffleArr.EnumerateArray().FirstOrDefault();
                    if (theme.ValueKind != JsonValueKind.Object)
                    {
                        if (attempt >= 3) return null;
                        await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt));
                        continue;
                    }

                    var type = theme.TryGetProperty("type", out var tProp) && tProp.ValueKind == JsonValueKind.String ? tProp.GetString() : "OP";
                    var sequence = theme.TryGetProperty("sequence", out var sProp) && sProp.ValueKind == JsonValueKind.Number ? sProp.GetInt32() : 1;

                    string slug = null;
                    var malId = 0;
                    if (theme.TryGetProperty("anime", out var animeObj) && animeObj.ValueKind == JsonValueKind.Object)
                    {
                        if (animeObj.TryGetProperty("slug", out var slugProp) && slugProp.ValueKind == JsonValueKind.String)
                            slug = slugProp.GetString();

                        if (animeObj.TryGetProperty("resources", out var resObj) && resObj.ValueKind == JsonValueKind.Object &&
                            resObj.TryGetProperty("nodes", out var nodesArr) && nodesArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var node in nodesArr.EnumerateArray())
                            {
                                var site = node.TryGetProperty("site", out var siteProp) && siteProp.ValueKind == JsonValueKind.String ? siteProp.GetString() : "";
                                if (string.Equals(site, "MAL", StringComparison.OrdinalIgnoreCase) || string.Equals(site, "MyAnimeList", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (node.TryGetProperty("externalId", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                                    {
                                        malId = idProp.GetInt32();
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    string songTitle = null;
                    if (theme.TryGetProperty("song", out var songObj) && songObj.ValueKind == JsonValueKind.Object &&
                        songObj.TryGetProperty("title", out var titleObj) && titleObj.ValueKind == JsonValueKind.Object)
                    {
                        if (titleObj.TryGetProperty("romaji", out var rProp) && rProp.ValueKind == JsonValueKind.String)
                            songTitle = rProp.GetString();
                        else if (titleObj.TryGetProperty("native", out var nProp) && nProp.ValueKind == JsonValueKind.String)
                            songTitle = nProp.GetString();
                    }

                    string link = null;
                    long size = 0;
                    if (theme.TryGetProperty("animethemeentries", out var entriesArr) && entriesArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var entryObj in entriesArr.EnumerateArray())
                        {
                            if (entryObj.TryGetProperty("videos", out var vidObj) && vidObj.ValueKind == JsonValueKind.Object &&
                                vidObj.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var node in nodes.EnumerateArray())
                                {
                                    var l = node.TryGetProperty("link", out var lProp) && lProp.ValueKind == JsonValueKind.String ? lProp.GetString() : null;
                                    if (!string.IsNullOrEmpty(l))
                                    {
                                        link = l;
                                        if (node.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number)
                                            size = sz.GetInt64();
                                        break;
                                    }
                                }
                            }
                            if (!string.IsNullOrEmpty(link))
                                break;
                        }
                    }

                    if (string.IsNullOrEmpty(link))
                    {
                        if (attempt >= 3) return null;
                        await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt));
                        continue;
                    }

                    return new ThemeVideo
                    {
                        Type = type,
                        Sequence = sequence,
                        Url = link,
                        AnimeSlug = slug,
                        SongTitle = songTitle,
                        MalId = malId,
                        Size = size
                    };
                }
                catch
                {
                    if (attempt >= 3)
                        return null;
                    await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt));
                }
            }

            return null;
        }
        private class RandomThemeResponse
        {
            // The animetheme index returns its collection under "animethemes", NOT "data".
            [JsonProperty("animethemes")] public List<InlineTheme> animethemes { get; set; }
        }

        public static int ParseSequence(string opEdText)
        {
            if (string.IsNullOrEmpty(opEdText)) return 1;
            var match = Regex.Match(opEdText, @"^(\d+):");
            return match.Success ? int.Parse(match.Groups[1].Value) : 1;
        }

        /// <summary>
        /// Tenrai hands us the raw theme line, e.g.
        /// 1: "Sparkle" by RADWIMPS (Japanese) (eps 1-6)
        /// Pulls out just the song name (kana in parens discarded).
        /// </summary>
        public static string ExtractOpEdSong(string opEdText)
        {
            if (string.IsNullOrEmpty(opEdText)) return "";
            var match = Regex.Match(opEdText, "\"\\s*([^\"]+?)\\s*(?:\\([^)]*\\))?\\s*\"");
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }

        /// <summary>
        /// Search query for the YouTube fallback: "song artist" with the Japanese
        /// glyphs stripped, since they only pollute the query.
        /// </summary>
        public static string BuildOpEdSearchQuery(string opEdText)
        {
            var song = ExtractOpEdSong(opEdText);
            var artistMatch = Regex.Match(opEdText ?? "", @"by\s+(.+?)(?:\s*\(eps|\s*$)");
            var artist = artistMatch.Success ? artistMatch.Groups[1].Value.Trim() : "";
            var query = $"{song} {artist}".Trim();
            query = Regex.Replace(query, @"[\u3000-\u9FFF\uFF00-\uFFEF]+", " ").Trim();
            return Regex.Replace(query, @"\s+", " ");
        }

        private class SearchResponse
        {
            [JsonProperty("search")]
            public SearchData search { get; set; }
        }

        private class SearchData
        {
            [JsonProperty("videos")]
            public List<VideoItem> videos { get; set; }
        }

        private class VideoItem
        {
            [JsonProperty("link")]
            public string link { get; set; }
            [JsonProperty("basename")]
            public string basename { get; set; }
        }
    }
}
