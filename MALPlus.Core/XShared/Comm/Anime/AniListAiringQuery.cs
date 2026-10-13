using System;
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
    ///     Real next-airing dates from AniList.
    ///     <para>
    ///     MyAnimeList itself does not publish a next episode date: for One Piece it answers
    ///     <c>next_episode: null</c> while still reporting <c>currently_airing</c>, and its own page
    ///     only carries the weekly broadcast slot, which is a statement about when a slot is, not
    ///     about whether a chapter comes out in it. Extrapolating from that slot is how a countdown
    ///     ends up inventing a chapter that was never announced, and how a series that changed its
    ///     broadcast day keeps counting down to the old one.
    ///     </para>
    ///     <para>
    ///     AniList's <c>nextAiringEpisode</c> is the closest thing to a real schedule that is
    ///     publicly documented, and it is the same data the reference app ends up showing.
    ///     </para>
    /// </summary>
    public static class AniListAiringQuery
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        /// <summary>
        ///     AniList refuses outright anything without a browser User-Agent, and answers a plain
        ///     400 that does not say which half of the request was wrong.
        /// </summary>
        private const string ChromeUserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/120.0.0.0 Safari/537.36";

        /// <summary>
        ///     Bounds how many batches can be in flight together, so several callers do not pile onto
        ///     AniList at the same instant. The request budget itself is shared with the schedule
        ///     client: AniList counts the two together, so a limit kept in one of them is no limit at
        ///     all, and the API answered 429 while each client still thought it had room.
        /// </summary>
        private static readonly SemaphoreSlim RequestGate = new(4, 4);

        /// <summary>Last failure, so a caller can log why a list came back empty.</summary>
        public static string LastError { get; private set; } = "";

        private sealed class Entry
        {
            public int MalId { get; init; }
            public DateTime? NextAirUtc { get; init; }
            public int? NextEpisodeNumber { get; init; }
            public string Status { get; init; }
        }

        /// <summary>
        ///     Next airing dates for the given MyAnimeList ids, keyed by id. A series with no
        ///     declared next episode is simply absent from the result: "nothing scheduled" is not
        ///     the same as "airing at the weekly slot", and treating it as such is what this exists
        ///     to stop.
        /// </summary>
        public static async Task<Dictionary<int, DateTime>> GetNextAiringAsync(IEnumerable<int> malIds)
        {
            var result = new Dictionary<int, DateTime>();
            var ids = malIds?.Where(id => id > 0).Distinct().ToList();
            if (ids == null || ids.Count == 0)
                return result;

            LastError = "";
            foreach (var entry in await FetchBatchAsync(ids.ToArray()))
            {
                if (entry.NextAirUtc.HasValue)
                    result[entry.MalId] = entry.NextAirUtc.Value;
            }

            return result;
        }

        /// <summary>
        ///     Status alongside the date, for the gate that decides whether a countdown belongs on
        ///     screen at all. An entry that AniList has finished still carries a stale next episode
        ///     for a while.
        /// </summary>
        public static async Task<Dictionary<int, string>> GetStatusAsync(IEnumerable<int> malIds)
        {
            var result = new Dictionary<int, string>();
            var ids = malIds?.Where(id => id > 0).Distinct().ToList();
            if (ids == null || ids.Count == 0)
                return result;

            // Not requireDate: "is this finished" is exactly the question asked about a series that
            // has no announced episode, so filtering those out here answered nothing but silence.
            foreach (var entry in await FetchBatchAsync(ids.ToArray(), requireDate: false))
            {
                if (!string.IsNullOrEmpty(entry.Status))
                    result[entry.MalId] = entry.Status;
            }
            return result;
        }

        /// <summary>
        ///     One request for a whole batch of series, gated so several batches can be in flight
        ///     without walking into the request budget.
        /// </summary>
        private static async Task<List<Entry>> FetchBatchAsync(int[] malIds, bool requireDate = true)
        {
            var output = new List<Entry>();
            try
            {
                await RequestGate.WaitAsync();
                try
                {
                    await AniListRateLimiter.WaitAsync();
                    using var request = new HttpRequestMessage(HttpMethod.Post, "https://graphql.anilist.co")
                    {
                        Content = new StringContent(BuildBody(malIds), Encoding.UTF8, "application/json")
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
                    if (doc.RootElement.TryGetProperty("errors", out var errors) &&
                        errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                    {
                        LastError = errors[0].ToString();
                        LastError = LastError.Length > 200 ? LastError.Substring(0, 200) : LastError;
                        return output;
                    }

                    if (!doc.RootElement.TryGetProperty("data", out var data) ||
                        !data.TryGetProperty("Page", out var page) ||
                        !page.TryGetProperty("media", out var media) ||
                        media.ValueKind != JsonValueKind.Array)
                        return output;

                    // One MAL id can carry several AniList records, and the finished ones answer with
                    // no schedule. JoJo's Steel Ball Run is the case that mattered: its first stage
                    // came back bare and its second stage carried the episode, and keeping the
                    // first read as a series with no announced episode. A record without a date says
                    // nothing, so it is dropped rather than kept.
                    var seen = new HashSet<int>();
                    foreach (var item in media.EnumerateArray())
                    {
                        var parsed = ParseEntry(item);
                        if (parsed == null)
                            continue;
                        if (requireDate && !parsed.NextAirUtc.HasValue)
                            continue;
                        if (!seen.Add(parsed.MalId))
                            continue;
                        output.Add(parsed);
                    }
                }
                finally
                {
                    RequestGate.Release();
                }
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name;
            }

            return output;
        }

        private static Entry ParseEntry(JsonElement media)
        {
            if (!media.TryGetProperty("idMal", out var idMal) ||
                idMal.ValueKind != JsonValueKind.Number)
                return null;

            var id = idMal.GetInt32();
            if (id <= 0)
                return null;

            var status = media.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
                ? st.GetString()
                : null;

            DateTime? next = null;
            int? episode = null;
            if (media.TryGetProperty("nextAiringEpisode", out var nae) &&
                nae.ValueKind == JsonValueKind.Object)
            {
                if (nae.TryGetProperty("airingAt", out var at) && at.ValueKind == JsonValueKind.Number)
                    next = DateTimeOffset.FromUnixTimeSeconds(at.GetInt64()).UtcDateTime;
                if (nae.TryGetProperty("episode", out var ep) && ep.ValueKind == JsonValueKind.Number)
                    episode = ep.GetInt32();
            }

            return new Entry { MalId = id, NextAirUtc = next, NextEpisodeNumber = episode, Status = status };
        }

/// <summary>
        ///     Builds one GraphQL document asking for many series at once.
        ///     <para>
        ///     The ids go in as a single idMal_in list, and the answer is filtered above rather than
        ///     here. AniList answers the same request with one record per entry, and a series split
        ///     into stages gets one record per stage, so the caller has to keep the record that
        ///     carries the schedule instead of trusting whichever came back first.
        ///     </para>
        ///     <para>
        ///     Four openings here: the operation, Page, media and nextAiringEpisode, closed in that
        ///     order. One brace short answers a bare "Syntax Error: Expected Name, found EOF" with a
        ///     column and no explanation, and every series then looks like it has no announced
        ///     episode at all.
        ///     </para>
        /// </summary>
        private static string BuildBody(int[] malIds)
        {
            const string query =
                "query($ids:[Int],$perPage:Int)" +
                "{Page(perPage:$perPage)" +
                "{media(idMal_in:$ids,type:ANIME)" +
                "{idMal id status nextAiringEpisode{episode airingAt}}}}";

            var ids = string.Join(",", malIds);
            return "{\"query\":\"" + query + "\",\"variables\":{\"ids\":[" + ids +
                   "],\"perPage\":" + malIds.Length + "}}";
        }
    }
}
