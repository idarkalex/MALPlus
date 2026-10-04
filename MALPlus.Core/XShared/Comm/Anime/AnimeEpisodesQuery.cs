using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AnimeEpisode = MALClient.Models.Models.Anime.AnimeEpisode;

namespace MALClient.XShared.Comm.Anime
{
    public class AnimeEpisodesQuery : Query
    {
        private readonly Dictionary<int, (List<AnimeEpisode> data, DateTime fetchedAt, int lastPage)> _cache = new Dictionary<int, (List<AnimeEpisode>, DateTime, int)>();
        private static bool IsAiringForTtl(int animeId)
        {
            if (MALClient.XShared.Utils.DataCache.TryRetrieveDataForId(animeId, out var vd) && !string.IsNullOrEmpty(vd.LastKnownStatus))
                return MALClient.XShared.Utils.AirTimeUtils.IsCurrentlyAiringStatus(vd.LastKnownStatus);
            return false;
        }

        /// <param name="onFirstBatch">
        ///     Invoked with the first page as soon as it lands, while the remaining pages are
        ///     still being fetched. A long-runner has a dozen pages, so waiting for all of them
        ///     meant the tab stayed empty for seconds; this lets the caller show the newest
        ///     episodes immediately and fill the rest in as it arrives.
        /// </param>
        public async Task<List<AnimeEpisode>> GetEpisodes(int animeId, bool force = false,
            CancellationToken cancellationToken = default, Action<List<AnimeEpisode>> onFirstBatch = null)
        {
            if (!force && _cache.TryGetValue(animeId, out var cachedFull))
            {
                var ttl = IsAiringForTtl(animeId) ? TimeSpan.FromHours(1) : TimeSpan.FromDays(7);
                if (DateTime.UtcNow - cachedFull.fetchedAt < ttl)
                    return cachedFull.data;
            }

            try
            {
                var result = new List<AnimeEpisode>();
                int page = 1;
                while (true)
                {
                    var attempt = 0;
                    try
                    {
                        var (items, hasNext) = await TenraiClient.GetPaginatedAsync($"anime/{animeId}/episodes?page={page}", cancellationToken);
                        foreach (var ep in items)
                        {
                            result.Add(new AnimeEpisode
                            {
                                EpisodeId = GetInt(ep, "mal_id"),
                                Filler = GetBool(ep, "filler"),
                                ForumUrl = GetString(ep, "forum_url"),
                                Recap = GetBool(ep, "recap"),
                                Title = GetString(ep, "title"),
                                TitleJapanese = GetString(ep, "title_japanese"),
                                TitleRomanji = GetString(ep, "title_romanji"),
                                VideoUrl = GetString(ep, "url"),
                                AiredDate = GetDateTime(ep, "aired"),
                            });
                        }

                        if (!hasNext)
                            break;

                        if (onFirstBatch != null && result.Count > 0)
                        {
                            try { onFirstBatch(result.ToList()); }
                            catch { }
                            onFirstBatch = null;
                        }

                        page++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        attempt++;
                        if (attempt >= 3)
                            return result;
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }
                }

                _cache[animeId] = (result, DateTime.UtcNow, 1);
                NotifyOverrideIfResumed(animeId, result);
                return result;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Efficiently fetches only the most recent page of episodes (the ones with the
        /// latest airdates) so airing cadence/staleness can be computed without pulling
        /// the whole episode list for long-running series.
        /// </summary>
        public async Task<List<AnimeEpisode>> GetLastEpisodesAsync(int animeId)
        {
            if (_cache.TryGetValue(animeId, out var cached) && cached.data != null)
            {
                    var ttl = IsAiringForTtl(animeId) ? TimeSpan.FromHours(1) : TimeSpan.FromDays(7);
                if (DateTime.UtcNow - cached.fetchedAt < ttl)
                    return cached.data;
            }

            try
            {
                int lastPage = 1;
                List<JsonElement> items = null;
                var json = await TenraiClient.GetRawJsonAsync($"anime/{animeId}/episodes?page=1");
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("data", out var data))
                    {
                        items = new List<JsonElement>();
                        foreach (var it in data.EnumerateArray())
                            items.Add(it.Clone());
                    }
                    if (root.TryGetProperty("pagination", out var pag) &&
                        pag.TryGetProperty("last_visible_page", out var lvp) &&
                        lvp.ValueKind == JsonValueKind.Number)
                        lastPage = lvp.GetInt32();
                }

                if (lastPage > 1)
                {
                    var (lastItems, _) = await TenraiClient.GetPaginatedAsync($"anime/{animeId}/episodes?page={lastPage}");
                    items = lastItems;
                }

                var result = new List<AnimeEpisode>();
                if (items != null)
                {
                    foreach (var ep in items)
                    {
                        result.Add(new AnimeEpisode
                        {
                            EpisodeId = GetInt(ep, "mal_id"),
                            Filler = GetBool(ep, "filler"),
                            ForumUrl = GetString(ep, "forum_url"),
                            Recap = GetBool(ep, "recap"),
                            Title = GetString(ep, "title"),
                            TitleJapanese = GetString(ep, "title_japanese"),
                            TitleRomanji = GetString(ep, "title_romanji"),
                            VideoUrl = GetString(ep, "url"),
                            AiredDate = GetDateTime(ep, "aired"),
                        });
                    }
                }

                _cache[animeId] = (result, DateTime.UtcNow, lastPage);
                NotifyOverrideIfResumed(animeId, result);
                return result;
            }
            catch
            {
                return null;
            }
        }

        //Every real episode fetch funnels through here, so a series that was flagged as
        //not airing by hand gets its flag dropped the moment a newer episode shows up.
        //No-op for every anime that has no override.
        private static void NotifyOverrideIfResumed(int animeId, List<AnimeEpisode> episodes)
        {
            try
            {
                if (episodes == null || episodes.Count == 0) return;
                if (!MALClient.XShared.Utils.AiringOverrides.IsOverriddenAsNotAiring(animeId)) return;
                var latest = episodes.Where(ep => ep.AiredDate.HasValue)
                    .Select(ep => ep.AiredDate.Value)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();
                if (latest == default) return;
                MALClient.XShared.Utils.AiringOverrides.ResumeIfAiring(animeId, latest);
            }
            catch { }
        }

        private static string GetString(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : "";

        private static int GetInt(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;

        private static bool GetBool(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.True;

        private static DateTime? GetDateTime(JsonElement el, string prop)
        {
            if (el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(p.GetString(), out var result))
                    return result;
            }
            return null;
        }

        /// <summary>
        ///     The episode list, with AniList supplying the skeleton when it has a schedule.
        ///
        ///     WHY. Tenrai's list is incomplete rather than wrong: the numbers it carries ARE the
        ///     real MyAnimeList episode numbers (its rows 1173, 1174 and 1177 for One Piece line
        ///     up with AniList's numbers AND dates exactly), but the list has gaps. One Piece is
        ///     missing 1175, 1176, 1178, 1179 and 1180, which is why "last aired" stalled at 1177
        ///     while the real episode was 1180. For five of six currently airing titles Tenrai
        ///     returns an EMPTY episode array with HTTP 200, including ones AniList marks
        ///     RELEASING. For finished titles Tenrai is complete and correct, which is why it is
        ///     still the fallback and still the title source.
        ///
        ///     AniList cannot replace it because its schedule has no title field at all, so this is
        ///     a merge and not a swap: AniList decides how many episodes there are and when each
        ///     one aired, Tenrai decides what they are called. Episodes Tenrai does not know about
        ///     still appear, without a title.
        ///
        ///     Dates. Tenrai's `aired` is the JST broadcast date stored as midnight UTC, so its
        ///     DATE PART IS ALREADY the Japanese calendar day and must not be shifted. AniList's
        ///     airingAt is a true UTC instant, so it has to be taken to JST before its date is used,
        ///     otherwise an episode airing Monday 00:00 in Tokyo reads as Sunday. Mushoku Tensei
        ///     III episode 14 is the reference case: AniList says 2026-09-27 UTC, the broadcast
        ///     date in Tokyo is 2026-09-28, and Tenrai already says 2026-09-28.
        /// </summary>
        public async Task<List<AnimeEpisode>> GetEpisodesWithScheduleAsync(int animeId, bool force = false,
            CancellationToken cancellationToken = default, Action<List<AnimeEpisode>> onFirstBatch = null,
            IReadOnlyDictionary<int, string> episodeForumTopics = null)
        {
            var tenrai = await GetEpisodes(animeId, force, cancellationToken, onFirstBatch);
            if (!AniListScheduleClient.Enabled)
                return tenrai;

            var schedule = await AniListScheduleClient.GetScheduleAsync(animeId);
            if (schedule == null || !schedule.HasSchedule)
                return tenrai;

            var now = DateTime.UtcNow;
            var aired = schedule.Slots.Where(s => s.AiringUtc <= now).OrderBy(s => s.Number).ToList();
            if (aired.Count == 0)
                return tenrai;

            // Tenrai rows keyed by their number, which is already the real episode number, so the
            // join is exact. The date index is the fallback for the rare entry where the two
            // sources disagree by one episode number but agree on the day.
            var byNumber = new Dictionary<long, AnimeEpisode>();
            var byJstDate = new Dictionary<DateTime, AnimeEpisode>();
            foreach (var episode in tenrai ?? new List<AnimeEpisode>())
            {
                if (episode == null)
                    continue;
                if (episode.EpisodeId > 0)
                    byNumber[episode.EpisodeId] = episode;
                if (episode.AiredDate.HasValue)
                {
                    var key = episode.AiredDate.Value.Date;
                    byJstDate[key] = episode;
                }
            }

            var merged = new List<AnimeEpisode>(aired.Count);
            foreach (var slot in aired)
            {
                if (!byNumber.TryGetValue(slot.Number, out var episode))
                    byJstDate.TryGetValue(slot.AiringJstDate, out episode);

                // The forum thread comes from the same numeric key, so nothing here is guessed. The
                // thread index is scraped from the series' own MAL forum and is keyed by MAL's
                // episode number, exactly like the titles are: MAL opens a thread when an episode
                // airs, so the newest episodes are covered. Verified against One Piece, where
                // episode 1180 (topicid 2280701) and 1179 (2279909) are both present while Tenrai
                // has neither episode at all. No title matching and no name inference involved.
                string forumUrl = null;
                if (episodeForumTopics != null)
                    episodeForumTopics.TryGetValue(slot.Number, out forumUrl);

                merged.Add(new AnimeEpisode
                {
                    EpisodeId = slot.Number,
                    AiredDate = slot.AiringJstDate,
                    Title = FallbackTitle(episode?.Title, slot.Number),
                    TitleJapanese = episode?.TitleJapanese,
                    TitleRomanji = episode?.TitleRomanji,
                    Filler = episode?.Filler ?? false,
                    Recap = episode?.Recap ?? false,
                    VideoUrl = episode?.VideoUrl,
                    ForumUrl = forumUrl ?? episode?.ForumUrl
                });
            }

            // Rows Tenrai has that AniList does not cover: an entry can carry older seasons or a
            // cour AniList has dropped. They stay, ordered by date, so nothing is ever hidden.
            var covered = new HashSet<long>(merged.Select(e => e.EpisodeId));
            foreach (var episode in tenrai ?? new List<AnimeEpisode>())
            {
                if (episode == null || covered.Contains(episode.EpisodeId))
                    continue;
                merged.Add(episode);
            }

            return merged
                .OrderBy(e => e.AiredDate ?? DateTime.MaxValue)
                .ThenBy(e => e.EpisodeId)
                .ToList();
        }

        /// <summary>
        ///     The newest page only, with the same AniList skeleton applied. Used for the
        ///     long-runner fast path where walking every page would be the slow part.
        /// </summary>
        public async Task<List<AnimeEpisode>> GetLastEpisodesWithScheduleAsync(int animeId)
        {
            var tail = await GetLastEpisodesAsync(animeId);
            if (!AniListScheduleClient.Enabled)
                return tail;

            var schedule = await AniListScheduleClient.GetScheduleAsync(animeId);
            if (schedule == null || !schedule.HasSchedule)
                return tail;

            var last = schedule.LastAired(DateTime.UtcNow);
            if (last == null)
                return tail;

            var byNumber = (tail ?? new List<AnimeEpisode>())
                .Where(e => e != null)
                .GroupBy(e => e.EpisodeId)
                .ToDictionary(g => g.Key, g => g.First());

            var now = DateTime.UtcNow;
            var newest = schedule.Slots
                .Where(s => s.AiringUtc <= now && s.Number >= last.Number)
                .OrderBy(s => s.Number)
                .Select(slot =>
                {
                    byNumber.TryGetValue(slot.Number, out var episode);
                    return new AnimeEpisode
                    {
                        EpisodeId = slot.Number,
                        AiredDate = slot.AiringJstDate,
                        Title = FallbackTitle(episode?.Title, slot.Number),
                        TitleJapanese = episode?.TitleJapanese,
                        TitleRomanji = episode?.TitleRomanji,
                        Filler = episode?.Filler ?? false,
                        Recap = episode?.Recap ?? false,
                        VideoUrl = episode?.VideoUrl,
                        ForumUrl = episode?.ForumUrl
                    };
                })
                .ToList();

            return newest.Count > 0 ? newest : tail;
        }

        /// <summary>
        ///     Episodes that are on the schedule but that no title source has a name for yet, which
        ///     is every episode between what Tenrai last stored and now, are still listed. A blank
        ///     title cell reads as a broken row rather than as an episode whose name has not been
        ///     written yet, so the number stands in for the name.
        /// </summary>
        private static string FallbackTitle(string title, int number)
        {
            return string.IsNullOrWhiteSpace(title) ? "Episode " + number : title;
        }
    }
}
