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

        public bool HasSchedule => Slots.Count > 0;

        /// <summary>
        ///     The most recent episode that has already aired, JST date. This is the value the
        ///     details hero shows as LAST AIRED, and it is the only part of the schedule that is
        ///     trustworthy: AniList's FUTURE dates are guesses and it got the One Piece return
        ///     wrong by months, but the dates it already published are the real broadcast days.
        /// </summary>
        public AniListEpisodeSlot LastAired(DateTime nowUtc)
        {
            if (Slots.Count == 0)
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
        ///     AniList allows 30 requests per minute per IP. The bucket holds a burst of eight so a
        ///     screenful of posters and one details page can be in flight together: with a burst of
        ///     five the details page waited behind the grid's warming and the Episodes tab visibly
        ///     stalled on WaitForTokenAsync. Eight still leaves the long-run average at 30/min.
        /// </summary>
        private const int BucketCapacity = 8;
        private static readonly TimeSpan RefillInterval = TimeSpan.FromSeconds(2);
        private static int _tokens = BucketCapacity;
        private static DateTime _lastRefill = DateTime.UtcNow;
        private static readonly object BucketLock = new object();

        private static bool TryTakeToken()
        {
            lock (BucketLock)
            {
                var now = DateTime.UtcNow;
                var elapsed = now - _lastRefill;
                var refill = (int)(elapsed.TotalSeconds / RefillInterval.TotalSeconds);
                if (refill > 0)
                {
                    _tokens = Math.Min(BucketCapacity, _tokens + refill);
                    _lastRefill = now;
                }
                if (_tokens <= 0)
                    return false;
                _tokens--;
                return true;
            }
        }

        private static async Task WaitForTokenAsync()
        {
            while (!TryTakeToken())
                await Task.Delay(RefillInterval);
        }

        /// <summary>
        ///     Schedule for one entry. In-flight requests are shared so returning to a grid does
        ///     not re-request every poster on it.
        /// </summary>
        public static Task<AniListSchedule> GetScheduleAsync(int malId)
        {
            if (!Enabled || malId <= 0)
                return Task.FromResult<AniListSchedule>(null);
            return InFlight.GetOrAdd(malId, id => FetchAsync(id));
        }

        private static async Task<AniListSchedule> FetchAsync(int malId)
        {
            try
            {
                var cached = await MALPlus.XShared.Utils.DataCache.RetrieveAiringSchedule(malId);
                if (cached != null)
                    return cached;

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

            var hasMore = first.Count > 0;
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
            const string query =
                "query($id:Int){Media(idMal:$id,type:ANIME){status nextAiringEpisode{episode airingAt}}}";
            var body = await PostAsync(BuildRequest(query, malId));
            if (!body.HasValue)
                return null;
            if (!body.Value.TryGetProperty("data", out var data) || !data.TryGetProperty("Media", out var media) ||
                media.ValueKind != JsonValueKind.Object)
                return null;

            string status = null;
            int? nextEpisode = null;
            DateTime? nextAiring = null;
            if (media.TryGetProperty("status", out var statusEl) && statusEl.ValueKind == JsonValueKind.String)
                status = statusEl.GetString();
            if (media.TryGetProperty("nextAiringEpisode", out var next) && next.ValueKind == JsonValueKind.Object)
            {
                if (next.TryGetProperty("episode", out var epEl) && epEl.ValueKind == JsonValueKind.Number)
                    nextEpisode = epEl.GetInt32();
                if (next.TryGetProperty("airingAt", out var atEl) && atEl.ValueKind == JsonValueKind.Number)
                    nextAiring = DateTimeOffset.FromUnixTimeSeconds(atEl.GetInt64()).UtcDateTime;
            }
            return new StatusResult { Status = status, NextEpisodeNumber = nextEpisode, NextAiringUtc = nextAiring };
        }

        private static async Task<List<AniListEpisodeSlot>> GetSchedulePageAsync(int malId, int page)
        {
            const string query =
                "query($id:Int,$page:Int){Media(idMal:$id,type:ANIME){airingSchedule(notYetAired:false,perPage:50,page:$page){nodes{episode airingAt}}}}";
            var variables = "{\"id\":" + malId + ",\"page\":" + page + "}";
            var body = await PostAsync(BuildRequest(query, malId, variables));
            if (!body.HasValue)
                return null;
            if (!body.Value.TryGetProperty("data", out var data) || !data.TryGetProperty("Media", out var media) ||
                media.ValueKind != JsonValueKind.Object ||
                !media.TryGetProperty("airingSchedule", out var schedule) ||
                schedule.ValueKind != JsonValueKind.Object ||
                !schedule.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
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