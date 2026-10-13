using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;

namespace MALPlus.XShared.BL
{
    public static class AiringDatumRepository
    {
        private static readonly Dictionary<int, (DateTime? nextAirUtc, DateTime fetchedAt)> _memCache = new Dictionary<int, (DateTime?, DateTime)>();
        private static readonly Dictionary<int, SemaphoreSlim> _locks = new Dictionary<int, SemaphoreSlim>();
        private static readonly object _lockObj = new object();

        private static SemaphoreSlim GetLock(int malId)
        {
            lock (_lockObj)
            {
                if (!_locks.TryGetValue(malId, out var sem))
                {
                    sem = new SemaphoreSlim(1, 1);
                    _locks[malId] = sem;
                }
                return sem;
            }
        }

        public static async Task<DateTime?> GetNextAirUtcAsync(int malId, bool force = false)
        {
            var now = DateTime.UtcNow;
            if (!force && DataCache.TryRetrieveDataForId(malId, out var vd) && vd.NextAirUtc.HasValue)
            {
                if (!AirTimeUtils.NeedsRefresh(vd.NextAirUtc, vd.NextAirFetchedAtUtc, now))
                {
                    // The TTL expired check used to return the persisted date with no status test at
                    // all, which is how a series that finished kept counting down: the prediction
                    // had been written while the entry was still running, the status that would
                    // have contradicted it was missing, and inside the TTL nothing ever looked.
                    // The provider is the cheap positive signal here, because being in the airing
                    // feed IS proof that it is running.
                    if (ResourceLocator.AiringInfoProvider.TryGetNextAirDate(malId, now, out _))
                        return vd.NextAirUtc;
                    if (!string.IsNullOrEmpty(vd.LastKnownStatus) && !AirTimeUtils.IsCurrentlyAiringStatus(vd.LastKnownStatus))
                    {
                        DataCache.UpdateVolatileDataWithNextAir(malId, null);
                        return null;
                    }
                    // Unknown status and not in the feed: fall through to a real resolution instead
                    // of serving a value nobody has re-verified.
                }
                else if (!string.IsNullOrEmpty(vd.LastKnownStatus) && !AirTimeUtils.IsCurrentlyAiringStatus(vd.LastKnownStatus))
                    return null;
            }
            lock (_lockObj)
            {
                if (!force && _memCache.TryGetValue(malId, out var mem))
                {
                    var ttl = AirTimeUtils.CacheTtl(mem.nextAirUtc, now);
                    if (mem.nextAirUtc.HasValue && (mem.nextAirUtc.Value > now || AirTimeUtils.IsInAiringWindow(mem.nextAirUtc.Value, now)))
                    {
                        if (now - mem.fetchedAt < ttl)
                            return mem.nextAirUtc;
                    }
                    else if (!mem.nextAirUtc.HasValue && now - mem.fetchedAt < TimeSpan.FromMinutes(15))
                        return null;
                }
            }

            var sem = GetLock(malId);
            await sem.WaitAsync();
            try
            {
                now = DateTime.UtcNow;
                if (!force && DataCache.TryRetrieveDataForId(malId, out var vd2) && vd2.NextAirUtc.HasValue)
                {
                    if (!AirTimeUtils.NeedsRefresh(vd2.NextAirUtc, vd2.NextAirFetchedAtUtc, now) &&
                        ResourceLocator.AiringInfoProvider.TryGetNextAirDate(malId, now, out _))
                    {
                        lock (_lockObj) _memCache[malId] = (vd2.NextAirUtc, vd2.NextAirFetchedAtUtc ?? now);
                        return vd2.NextAirUtc;
                    }
                }
                // The real schedule, when there is one. AniList declares the next episode of a series outright;
                // MyAnimeList does not, and the weekly broadcast slot is a statement about when a
                // slot usually is rather than about a chapter coming out in it.
                var declared = await ResolveDeclaredNextAirAsync(malId, force);
                if (declared.HasValue)
                {
                    lock (_lockObj) _memCache[malId] = (declared, now);
                    DataCache.UpdateVolatileDataWithNextAir(malId, declared);
                    return declared;
                }

                // Ask AniList whether this thing is still running BEFORE extrapolating a cadence
                // from its own episode list. It is the only source consulted here that is not our
                // own possibly-stale cache: Mushoku Tensei III reads "Currently Airing" in the
                // details cache for up to fourteen days after it finished, and that stale string
                // was enough to keep predicting a weekly episode forever.
                var stillRunning = await ResolveIsStillRunningAsync(malId);

                var fromEpisodes = await GetEpisodesFallbackAsync(malId);
                if (fromEpisodes.HasValue)
                {
                    var isAiring = stillRunning ?? await IsCurrentlyAiringAsync(malId);
                    if (isAiring == true)
                    {
                        lock (_lockObj) _memCache[malId] = (fromEpisodes, now);
                        DataCache.UpdateVolatileDataWithNextAir(malId, fromEpisodes);
                        return fromEpisodes;
                    }
                }

                // The weekly slot is the last resort, and only as a calendar convenience: it is a
                // slot, not a chapter, so it never reaches a countdown on its own. Anything the
                // real sources refused is left with no date rather than with an invented one.
                lock (_lockObj) _memCache[malId] = (null, now);
                DataCache.UpdateVolatileDataWithNextAir(malId, null);
                DataCache.RegisterVolatileDataAiringTimeFetchFailure(malId);
                return null;
            }
            finally
            {
                sem.Release();
            }
        }

        /// <summary>
        ///     The next episode date AniList declares, checked against the series' own history.
        ///     <para>
        ///     A date alone is not enough to trust. AniList keeps a scheduled slot in
        ///     <c>nextAiringEpisode</c> for a series that is months away from its next batch, and
        ///     that is a planning slot rather than an announced chapter: One Piece carried one for
        ///     January while its most recent real episode was five weeks old, and a countdown to a
        ///     date nobody had announced is worse than no countdown at all. So a far off date only
        ///     counts when the series has actually been emitting; otherwise it is reported as no
        ///     date and the status still reads as airing.
        ///     </para>
        /// </summary>
private static async Task<DateTime?> ResolveDeclaredNextAirAsync(int malId, bool force)
        {
            try
            {
                await ResourceLocator.AiringInfoProvider.RequestDeclaredNextAir(malId);

            if (!ResourceLocator.AiringInfoProvider.TryGetDeclaredNextAirDate(malId, out var declared))
                return null;

            var now = DateTime.UtcNow;
            if (declared <= now)
                return null;

            // Within the horizon the date is taken as given: it is the published schedule.
            if (declared - now <= DeclaredHorizon)
                return declared;

            // Far off: only believable if the series has actually been emitting recently.
            var latestEpisode = await GetLatestRealEpisodeUtcAsync(malId);
            if (latestEpisode.HasValue && now - latestEpisode.Value <= StaleEpisodeWindow)
                return declared;

            return null;
            }
            catch (Exception)
            {
                // The service was unreachable, or answered something unreadable. No declared date.
                return null;
            }
        }

        /// <summary>
        ///     How far ahead a declared date is believed without checking the series' history.
        ///     A countdown two weeks out is useful; one three months out is a plan, not a date.
        /// </summary>
        private static readonly TimeSpan DeclaredHorizon = TimeSpan.FromDays(14);

        /// <summary>
        ///     How long a series may have been quiet before a distant declared date stops being
        ///     believed. Roughly two missed weekly slots.
        /// </summary>
        private static readonly TimeSpan StaleEpisodeWindow = TimeSpan.FromDays(14);

        /// <summary>
        ///     The most recent real episode date on record for a series, or null when there is none.
        /// </summary>
        private static async Task<DateTime?> GetLatestRealEpisodeUtcAsync(int malId)
        {
            try
            {
                var episodes = await DataCache.RetrieveAnimeEpisodesStale(malId);
                if (episodes == null || episodes.Count == 0)
                    episodes = await new AnimeEpisodesQuery().GetLastEpisodesAsync(malId);
                if (episodes == null || episodes.Count == 0)
                    return null;

                return episodes
                    .Where(ep => ep.AiredDate.HasValue)
                    .Select(ep => ep.AiredDate.Value)
                    .OrderByDescending(d => d)
                    .FirstOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static async Task<DateTime?> GetEpisodesFallbackAsync(int malId)
        {
            try
            {
                var episodes = await DataCache.RetrieveAnimeEpisodesStale(malId);
                if (episodes == null || episodes.Count == 0)
                    episodes = await new AnimeEpisodesQuery().GetLastEpisodesAsync(malId);
                if (episodes == null || episodes.Count == 0) return null;
                return AirTimeUtils.ComputeNextAirFromEpisodes(episodes, DateTime.UtcNow);
            }
            catch { return null; }
        }

        private static async Task<bool> IsCurrentlyAiringAsync(int malId)
        {
            try
            {
                var data = await DataCache.RetrieveAnimeSearchResultsDataStale(malId.ToString(), true);
                if (data == null || string.IsNullOrEmpty(data.Status))
                    data = await new AnimeGeneralDetailsQuery().GetAnimeDetails(false, malId.ToString(), "", true);
                if (data == null) return false;
                if (!string.IsNullOrEmpty(data.Status))
                    DataCache.UpdateVolatileStatus(malId, data.Status);
                return AirTimeUtils.IsCurrentlyAiringStatus(data.Status);
            }
            catch { return false; }
        }

        private static async Task<DateTime?> GetBroadcastFallbackAsync(int malId, bool? stillRunning = null)
        {
            try
            {
                var data = await DataCache.RetrieveAnimeSearchResultsDataStale(malId.ToString(), true);
                if (data == null || string.IsNullOrEmpty(data.Broadcast))
                    data = await new AnimeGeneralDetailsQuery().GetAnimeDetails(false, malId.ToString(), "", true);
                if (data?.Broadcast == null) return null;
                if (!string.IsNullOrEmpty(data.Status))
                    DataCache.UpdateVolatileStatus(malId, data.Status);
                if (!string.Equals(data.Status, "Currently Airing", StringComparison.CurrentCultureIgnoreCase))
                    return null;
                return AirTimeUtils.ComputeNextAirDate(data.Broadcast, DateTime.UtcNow, true);
            }
            catch { return null; }
        }

        /// <summary>
        ///     Is this entry actually still running? AniList first, because it answers fresh and its
        ///     status field is what the schedule itself is derived from, then our own details cache
        ///     as the fallback. Null means nobody could say, which the caller treats as "do not
        ///     extrapolate" rather than as a yes.
        ///
        ///     The value is also written into the volatile cache, because LastKnownStatus being
        ///     empty is what let a finished series keep a countdown: every guard in the app is
        ///     conditional on that string being populated, and it was only ever written when the
        ///     user happened to open the details page.
        /// </summary>
        private static async Task<bool?> ResolveIsStillRunningAsync(int malId)
        {
            if (AniListScheduleClient.Enabled)
            {
                // The status, and only the status. This used to pull the whole schedule, two or three
                // requests per series, to read one string: and it ran for every series WITHOUT an
                // announced episode, which is to say for precisely the ones a library full of airing
                // shows is made of. That is where the request storm came from, and it is what left the
                // Episodes tab queued behind the limiter for the better part of a minute.
                // The batched status query answers the same question in one request.
                var statuses = await AniListAiringQuery.GetStatusAsync(new[] { malId });
                if (statuses != null && statuses.TryGetValue(malId, out var anilistStatus) &&
                    !string.IsNullOrWhiteSpace(anilistStatus))
                {
                    var mapped = IsReleasingStatus(anilistStatus) ? "Currently Airing" : "Finished Airing";
                    DataCache.UpdateVolatileStatus(malId, mapped);
                    return IsReleasingStatus(anilistStatus);
                }
            }

            try
            {
                var data = await DataCache.RetrieveAnimeSearchResultsDataStale(malId.ToString(), true);
                // NOT forced. Every one of the three reads below asks the search cache first and then,
                // when that misses, used to ask the source with force: true, which skips the fourteen
                // day details cache sitting right there. An entry whose status had been resolved days
                // ago was therefore re-read from the network once per card, on a grid handed two
                // hundred and fifty cards at once: up to four requests each, all queued behind a
                // throttle that only releases one every 300ms. That queue is what the source's own
                // pages were stuck behind, which is why reading a fresh band of the ranking took
                // twenty seconds when its seven requests measure three. Reading the cache turns almost
                // all of them into nothing.
                if (data == null || string.IsNullOrEmpty(data.Status))
                    data = await new AnimeGeneralDetailsQuery().GetAnimeDetails(false, malId.ToString(), "", true);
                if (data == null || string.IsNullOrEmpty(data.Status))
                    return null;
                DataCache.UpdateVolatileStatus(malId, data.Status);
                return AirTimeUtils.IsCurrentlyAiringStatus(data.Status);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        ///     AniList's own spelling of "still running". Its status field is a handful of words and
        ///     only RELEASING means episodes are still coming; everything else, including a series it
        ///     has simply forgotten, is answered as not running so no countdown is invented for it.
        /// </summary>
        private static bool IsReleasingStatus(string status) =>
            string.Equals(status, "RELEASING", StringComparison.OrdinalIgnoreCase) ||
            MALPlus.XShared.Utils.AirTimeUtils.IsCurrentlyAiringStatus(status);
    }
}
