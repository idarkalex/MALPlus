using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MALPlus.XShared.Comm.Anime
{
    /// <summary>
    ///     One entry of an airing schedule: the episode number and the instant it aired.
    /// </summary>
    public class AniListEpisodeSlot
    {
        /// <summary>The real broadcast instant, UTC.</summary>
        public DateTime AiringUtc { get; set; }

        /// <summary>Number as MyAnimeList numbers it.</summary>
        public int Number { get; set; }

        /// <summary>
        ///     The broadcast calendar date in JST, which is the only date worth showing.
        ///     This is the whole reason the field exists. AniList stores a true UTC instant, so an
        ///     episode that airs Monday 00:00 in Tokyo is Sunday 15:00 UTC and reads as the PREVIOUS
        ///     day in UTC. Mushoku Tensei III episode 14 is exactly that case: AniList says
        ///     2026-09-27 in UTC and the broadcast date in Tokyo is 2026-09-28. Late night Japanese
        ///     slots (23:00 to 00:30 JST) are the worst case and they are extremely common.
        /// </summary>
        public DateTime AiringJstDate
        {
            get
            {
                var jst = AiringUtc.AddHours(9);
                return new DateTime(jst.Year, jst.Month, jst.Day, 0, 0, 0, DateTimeKind.Utc);
            }
        }
    }

    /// <summary>
    ///     What AniList knows about one entry's airing: whether it is still running, and the
    ///     schedule of the current cour.
    /// </summary>
    public class AniListSchedule
    {
        /// <summary>RELEASING, FINISHED, NOT_YET_RELEASED, HIATUS, ...</summary>
        public string Status { get; set; }

        public List<AniListEpisodeSlot> Slots { get; set; } = new List<AniListEpisodeSlot>();
        /// <summary>Next episode to air, when one is scheduled.</summary>
        public int? NextEpisodeNumber { get; set; }

        public DateTime? NextAiringUtc { get; set; }

        public bool IsReleasing => string.Equals(Status, "RELEASING", StringComparison.OrdinalIgnoreCase);

        public bool HasSchedule => Slots != null && Slots.Count > 0;

        /// <summary>
        ///     The most recent episode that has already aired, JST date. This is the value the
        ///     details hero shows as LAST AIRED, and it is the only part of the schedule that is
        ///     trustworthy: AniList's FUTURE dates are guesses and it got the One Piece return
        ///     wrong by months, but the dates it already published are the real broadcast days.
        /// </summary>
        public AniListEpisodeSlot LastAired(DateTime nowUtc)
        {
            if (Slots == null || Slots.Count == 0)
                return null;
            return Slots
                .Where(s => s.AiringUtc <= nowUtc)
                .OrderBy(s => s.Number)
                .LastOrDefault();
        }
    }

    /// <summary>
    ///     Airing truth from AniList, used for two things the rest of the app cannot do without it.
    ///
    ///     WHY THIS EXISTS. The other source, Tenrai, was measured and found to be incomplete
    ///     rather than wrong about numbers:
    ///   * One Piece episode rows carry the real MAL episode number (1173, 1174, 1177 line up
    ///     with AniList's numbers and dates exactly), but the LIST HAS GAPS: 1175, 1176, 1178,
    ///     1179 and 1180 are simply absent, so "last aired" stopped at 1177.
    ///   * For five of six currently airing titles tested, Tenrai returns an empty episode array
    ///     with HTTP 200, including ones AniList marks RELEASING (Yami Shibai 17, Romelia Senki).
    ///   * For FINISHED titles Tenrai is complete and correct (Attack on Titan 25 @ 2013, Frieren
    ///     28 @ 2024, Spy x Family 12 @ 2022), which is why it remains the title source.
    ///
    ///     WHAT IT DOES NOT HAVE. Episode titles. The AiringSchedule type has no title field at
    ///     all, so this can never replace Tenrai as the list, only supply the skeleton.
    ///
    ///     HOW TO TURN IT OFF. Set <see cref="Enabled" /> to false and every caller falls back to
    ///     the Tenrai only behaviour, which is worse but complete. Do that if Tenrai ever closes
    ///     its gaps; the fallback needs no other change.
    /// </summary>
    public static class AniListScheduleClient
    {
        /// <summary>Kill switch. See "HOW TO TURN IT OFF" above.</summary>
        public const bool Enabled = true;

        private const string Endpoint = "https://graphql.anilist.co";

        /// <summary>
        ///     AniList caps a schedule page at 25 entries no matter what perPage asks for, so a
        ///     cour needs ceil(entries / 25) requests. Measured page counts: One Piece's cour is 67
        ///     entries (3 pages), Sazae-san airs in 45 episode cours (2 pages) and the worst real
        ///     case is a daily anime over a three month cour at roughly 90 episodes (4 pages).
        ///     The cap is 6 because nothing legitimate reaches it: the number of pages is decided
        ///     by the length of a COUR, not by the length of the series, so a 1800 episode show
        ///     like Sazae-san needs fewer pages than One Piece. Capping at 3 would break every
        ///     4 page cour.
        /// </summary>
        public const int MaxPages = 6;

        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly ConcurrentDictionary<int, Task<AniListSchedule>> InFlight = new();
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(4, 4);

        /// <summary>
        ///     The request budget is shared with every other AniList call in the app through
        ///     <see cref="AniListRateLimiter" />, because the limit belongs to AniList rather than to
        ///     this class. Two private buckets meant two clients each spending a full allowance and
        ///     the API answering 429 to both.
        /// </summary>
        private static Task WaitForTokenAsync() => AniListRateLimiter.WaitAsync();

        /// <summary>
        ///     Schedule for one entry. In-flight requests are shared so returning to a grid does
        ///     not re-request every poster on it, and the stored schedule is read before the network
        ///     is touched.
        ///     <para>
        ///     That stored read was missing, and its absence is why restarting the app was as slow as
        ///     opening a series for the first time ever. The schedule was written to disk on every
        ///     fetch but never read back: DataCache has carried a six hour reader for it since the
        ///     file was introduced, with nothing calling it. Every process therefore re-asked AniList
        ///     for every entry it touched, spent the answer queueing behind the rate limiter, and
        ///     Bleach's episodes took thirty seconds to appear on a warm machine with a full disk
        ///     cache. Closing and reopening the app is the case that proved it.
        ///     </para>
        ///     <para>
        ///     A forced call skips both the in-flight sharing and the stored schedule, which is what
        ///     a pull-to-refresh means: the user asked, so the answer is asked again rather than
        ///     read back from disk.
        ///     </para>
        /// </summary>
        public static Task<AniListSchedule> GetScheduleAsync(int malId, bool force = false)
        {
            if (!Enabled || malId <= 0)
                return Task.FromResult<AniListSchedule>(null);
            if (force)
                return FetchAsync(malId);
            return InFlight.GetOrAdd(malId, id => LoadOrFetchAsync(id));
        }

        private static async Task<AniListSchedule> LoadOrFetchAsync(int malId)
        {
            try
            {
                var stored = await MALPlus.XShared.Utils.DataCache.RetrieveAiringSchedule(malId);
                if (stored != null)
                    return stored;
            }
            catch (Exception)
            {
                // A cache that cannot be read is a cache miss, not a failure.
            }
            return await FetchAsync(malId);
        }

        private static async Task<AniListSchedule> FetchAsync(int malId)
        {
            try
            {
                await Gate.WaitAsync();
                try
                {
                    var fetched = await FetchCoreAsync(malId);
                    if (fetched != null)
                        await MALPlus.XShared.Utils.DataCache.SaveAiringSchedule(malId, fetched);
                    return fetched;
                }
                finally
                {
                    Gate.Release();
                }
            }
            catch (Exception)
            {
                // A schedule is an improvement, never a requirement: a failure here has to leave
                // the caller with Tenrai only rather than an exception.
                return null;
            }
            finally
            {
                InFlight.TryRemove(malId, out _);
            }
        }

        /// <summary>
        ///     Drops the stored schedule for an entry so the next read goes to the network.
        /// </summary>
        public static async Task InvalidateAsync(int malId)
        {
            try
            {
                InFlight.TryRemove(malId, out _);
                await MALPlus.XShared.Utils.DataCache.ClearAiringSchedule(malId);
            }
            catch (Exception)
            {
            }
        }

        private static async Task<AniListSchedule> FetchCoreAsync(int malId)
        {
            var status = await GetStatusAsync(malId);
            var schedule = new AniListSchedule { Status = status?.Status, NextEpisodeNumber = status?.NextEpisodeNumber, NextAiringUtc = status?.NextAiringUtc };
            if (schedule == null)
                return null;

            // Page 1 gives the first episode number of the cour, which is what makes the jump to
            // the last page possible instead of walking every page in between.
            var first = await GetSchedulePageAsync(malId, 1);
            if (first == null)
                return schedule;
            schedule.Slots.AddRange(first);

            // A short page IS the end of the schedule, and an eleven episode cour came back that way
            // from page one. Treating "the page had rows" as "there may be more" made every single
            // series ask for a page two that does not exist, which is one wasted request per entry
            // across every airing poster the grid touches.
            var hasMore = first.Count >= PageSize;
            var page = 1;
            while (hasMore && page < MaxPages)
            {
                page++;
                var target = ResolveTargetPage(first, schedule.NextEpisodeNumber, page);
                var next = await GetSchedulePageAsync(malId, target);
                if (next == null || next.Count == 0)
                    break;
                if (!ReferenceEquals(next, first))
                    schedule.Slots.AddRange(next);
                // The page size is whatever AniList served, and it stops once a short page comes
                // back, so "fewer than a full page" is the end of the schedule.
                hasMore = next.Count >= PageSize;
            }

            schedule.Slots = schedule.Slots
                .GroupBy(s => s.Number)
                .Select(g => g.First())
                .OrderBy(s => s.Number)
                .ToList();
            return schedule;
        }

        private const int PageSize = 25;

        /// <summary>
        ///     Jump straight to the page holding the next episode instead of walking the middle of
        ///     the cour. The next episode number is right after everything aired, so the page that
        ///     holds it is the page that holds the last aired one.
        /// </summary>
        private static int ResolveTargetPage(List<AniListEpisodeSlot> first, int? nextEpisode, int fallback)
        {
            if (!nextEpisode.HasValue || first.Count == 0)
                return fallback;
            var start = first.Min(s => s.Number);
            var offset = nextEpisode.Value - start;
            if (offset <= 0)
                return fallback;
            var page = (int)(offset / PageSize);
            return Math.Max(fallback, page + 1);
        }

        private sealed class StatusResult
        {
            public string Status { get; set; }
            public int? NextEpisodeNumber { get; set; }
            public DateTime? NextAiringUtc { get; set; }
        }

        private static async Task<StatusResult> GetStatusAsync(int malId)
        {
            // Page rather than Media, for the same reason the countdown query uses it: one MAL id can
            // carry more than one AniList record, and JoJo's Steel Ball Run is split into a first
            // stage that finished and a second stage that is the one airing. Media answers with the
            // first, which reported a finished series with no schedule and left the details page
            // quoting an episode from months ago.
            const string query =
                "query($ids:[Int],$perPage:Int){Page(perPage:$perPage){media(idMal_in:$ids,type:ANIME)" +
                "{id idMal status nextAiringEpisode{episode airingAt}}}}";
            var variables = "{\"ids\":[" + malId + "],\"perPage\":10}";
            var body = await PostAsync(BuildRequest(query, malId, variables));
            if (!body.HasValue)
                return null;
            if (!body.Value.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("Page", out var page) ||
                !page.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array)
                return null;

            // A finished record answers with nothing scheduled, and the still-airing half of a split
            // series is the one worth reading. Preferring it is what puts the current season back on
            // the details page.
            JsonElement? chosen = null;
            var chosenIsReleasing = false;
            foreach (var item in media.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                if (chosen == null)
                    chosen = item;

                if (!item.TryGetProperty("status", out var statusEl) ||
                    statusEl.ValueKind != JsonValueKind.String)
                    continue;
                if (!string.Equals(statusEl.GetString(), "RELEASING", StringComparison.OrdinalIgnoreCase))
                    continue;

                chosen = item;
                chosenIsReleasing = true;
                break;
            }

            if (chosen == null || !chosen.Value.TryGetProperty("status", out var status2) ||
                status2.ValueKind != JsonValueKind.String)
                return null;

            var statusText = status2.GetString();
            int? nextEpisode = null;
            DateTime? nextAiring = null;
            if (chosen.Value.TryGetProperty("nextAiringEpisode", out var next) && next.ValueKind == JsonValueKind.Object)
            {
                if (next.TryGetProperty("episode", out var epEl) && epEl.ValueKind == JsonValueKind.Number)
                    nextEpisode = epEl.GetInt32();
                if (next.TryGetProperty("airingAt", out var atEl) && atEl.ValueKind == JsonValueKind.Number)
                    nextAiring = DateTimeOffset.FromUnixTimeSeconds(atEl.GetInt64()).UtcDateTime;
            }

            // A still-airing record with no schedule is a series between cours. Reporting it as
            // finished because that is what its next episode field happens to say is how a running
            // show ends up wearing a last aired date from its previous run.
            if (chosenIsReleasing && !nextAiring.HasValue)
                statusText = "RELEASING";

            return new StatusResult { Status = statusText, NextEpisodeNumber = nextEpisode, NextAiringUtc = nextAiring };
        }

        private static async Task<List<AniListEpisodeSlot>> GetSchedulePageAsync(int malId, int page)
        {
            const string query =
                "query($ids:[Int],$page:Int){Page(perPage:10)" +
                "{media(idMal_in:$ids,type:ANIME)" +
                "{status airingSchedule(notYetAired:false,perPage:50,page:$page)" +
                "{nodes{episode airingAt}}}}}";
            var variables = "{\"ids\":[" + malId + "],\"page\":" + page + "}";
            var body = await PostAsync(BuildRequest(query, malId, variables));
            if (!body.HasValue)
                return null;
            if (!body.Value.TryGetProperty("data", out var data) || !data.TryGetProperty("Page", out var pageEl) ||
                !pageEl.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array)
                return new List<AniListEpisodeSlot>();

            // A MAL id can hold several AniList records for a series that was split into stages, and
            // only one of them is the current run. JoJo's Steel Ball Run is the case that mattered:
            // its first stage answered as a finished series whose one episode was in March, and the
            // details page quoted that instead of the episode that aired last week. The record still
            // releasing wins; failing that, the one carrying the most aired episodes.
            JsonElement? best = null;
            var bestNodes = -1;
            var bestReleasing = false;
            foreach (var item in media.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                if (!item.TryGetProperty("airingSchedule", out var candidate) ||
                    candidate.ValueKind != JsonValueKind.Object ||
                    !candidate.TryGetProperty("nodes", out var probe) || probe.ValueKind != JsonValueKind.Array)
                    continue;

                var count = probe.GetArrayLength();
                var releasing = item.TryGetProperty("status", out var statusEl) &&
                                statusEl.ValueKind == JsonValueKind.String &&
                                string.Equals(statusEl.GetString(), "RELEASING", StringComparison.OrdinalIgnoreCase);

                if (best != null && !(releasing && !bestReleasing) && !(releasing == bestReleasing && count <= bestNodes))
                    continue;

                best = candidate;
                bestNodes = count;
                bestReleasing = releasing;
            }

            if (best == null || !best.Value.TryGetProperty("nodes", out var nodes) ||
                nodes.ValueKind != JsonValueKind.Array)
                return new List<AniListEpisodeSlot>();

            var result = new List<AniListEpisodeSlot>();
            foreach (var node in nodes.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object)
                    continue;
                if (!node.TryGetProperty("episode", out var epEl) || epEl.ValueKind != JsonValueKind.Number)
                    continue;
                if (!node.TryGetProperty("airingAt", out var atEl) || atEl.ValueKind != JsonValueKind.Number)
                    continue;
                result.Add(new AniListEpisodeSlot
                {
                    Number = epEl.GetInt32(),
                    AiringUtc = DateTimeOffset.FromUnixTimeSeconds(atEl.GetInt64()).UtcDateTime
                });
            }
            return result;
        }

        private static string BuildRequest(string query, int malId, string variables = null)
        {
            var vars = variables ?? ("{\"id\":" + malId + "}");
            var escaped = query.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
            return "{\"query\":\"" + escaped + "\",\"variables\":" + vars + "}";
        }

        private static async Task<JsonElement?> PostAsync(string json)
        {
            await WaitForTokenAsync();
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await Client.PostAsync(Endpoint, content);
            if (!response.IsSuccessStatusCode)
                return null;
            var text = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(text))
                return null;
            try
            {
                using var document = JsonDocument.Parse(text);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}