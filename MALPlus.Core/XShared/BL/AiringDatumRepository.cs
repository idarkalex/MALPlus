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
            if (AiringOverrides.IsOverriddenAsNotAiring(malId))
            {
                //The source keeps advertising a weekly slot for this series even though it
                //switched to batch releases, so every fallback below would invent a date.
                //Deliberately NOT re-checked against the local episode cache: that cache is fed by
                //the seed bundle, whose synthetic weekly dates run past the real last episode and
                //would read as "it resumed airing". Only a live fetch (AnimeEpisodesQuery) can
                //lift this, so the override holds until the next cour is really published.
                lock (_lockObj) _memCache[malId] = (null, now);
                DataCache.UpdateVolatileDataWithNextAir(malId, null);
                return null;
            }
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
                if (ResourceLocator.AiringInfoProvider.TryGetNextAirDate(malId, now, out var airDate) && (airDate > now || AirTimeUtils.IsInAiringWindow(airDate, now)))
                {
                    var res = airDate;
                    lock (_lockObj) _memCache[malId] = (res, now);
                    DataCache.UpdateVolatileDataWithNextAir(malId, res);
                    return res;
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

                var broadcast = await GetBroadcastFallbackAsync(malId, stillRunning);
                if (broadcast.HasValue)
                {
                    lock (_lockObj) _memCache[malId] = (broadcast, now);
                    DataCache.UpdateVolatileDataWithNextAir(malId, broadcast);
                    return broadcast;
                }

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
                var schedule = await AniListScheduleClient.GetScheduleAsync(malId);
                if (schedule != null && !string.IsNullOrWhiteSpace(schedule.Status))
                {
                    var mapped = schedule.IsReleasing ? "Currently Airing" : "Finished Airing";
                    DataCache.UpdateVolatileStatus(malId, mapped);
                    return schedule.IsReleasing;
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
    }
}
