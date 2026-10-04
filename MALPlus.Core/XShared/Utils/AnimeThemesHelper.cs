using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace MALClient.XShared.Utils
{
    public static class AnimeThemesHelper
    {
        private static readonly HttpClient Client = new HttpClient();

        static AnimeThemesHelper()
        {
            Client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        private static DateTime? _rateLimitedUntil;

        private static async Task<string> GetStringWithBackoffAsync(string url)
        {
            if (_rateLimitedUntil.HasValue && DateTime.UtcNow < _rateLimitedUntil.Value)
                return null;

            try
            {
                return await Client.GetStringAsync(url);
            }
            catch (HttpRequestException ex)
            {
                if (ex.Message.Contains("429") || ex.Message.Contains("Too Many Requests"))
                {
                    _rateLimitedUntil = DateTime.UtcNow.AddMinutes(1);
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
                ///     Episode coverage, as inclusive ranges. This is what the match keys on.
                /// </summary>
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
                        if (videos.Any(v => v.Url == video.link))
                            continue;
videos.Add(new ThemeVideo
                            {
                                Type = theme.type,
                                Sequence = theme.sequence ?? 1,
                                Url = video.link,
                                AnimeSlug = Slugify(anime.name),
                                SongTitle = theme.song?.title,
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
                    return bestByEpisode ?? overlapping[0];
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
            return bestScore >= 0.7 ? best : null;
        }

        private static double ScoreSong(ThemeVideo v, string songQuery, string songName)
        {
            if (string.IsNullOrWhiteSpace(v?.SongTitle))
                return 0.0;
            return string.IsNullOrEmpty(songName)
                ? SongMatchScore(songQuery, v.SongTitle)
                : Math.Max(SongMatchScore(songName, v.SongTitle), SongMatchScore(songQuery, v.SongTitle));
        }

        /// <summary>
        /// One random theme straight from AnimeThemes, for the Discover "nostalgia gem" tile.
        /// Reuses the same CDN WebM link the OP/ED path plays.
        /// </summary>
        public static async Task<ThemeVideo> GetRandomThemeAsync()
        {
            try
            {
                const string url =
                    "https://api.animethemes.moe/animetheme?sort=random&page[size]=1" +
                    "&include=anime,animethemeentries.videos,song";
                var json = await GetStringWithBackoffAsync(url);
                if (string.IsNullOrEmpty(json))
                    return null;

                var root = JsonConvert.DeserializeObject<RandomThemeResponse>(json);
                var theme = root?.animethemes?.FirstOrDefault();
                if (theme == null)
                    return null;

                var link = theme.animethemeentries?
                    .SelectMany(entry => entry.videos ?? new List<InlineVideo>())
                    .Select(video => video.link)
                    .FirstOrDefault(l => !string.IsNullOrEmpty(l));
                if (string.IsNullOrEmpty(link))
                    return null;

                return new ThemeVideo
                {
                    Type = theme.type ?? "OP",
                    Sequence = theme.sequence ?? 1,
                    Url = link,
                    AnimeSlug = theme.anime?.slug,
                    SongTitle = theme.song?.title
                };
            }
            catch
            {
                return null;
            }
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
