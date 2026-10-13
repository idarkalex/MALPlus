using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MALPlus.Adapters;
using MALPlus.Models.Enums;
using MALPlus.Models.Models;
using MALPlus.Models.Models.Anime;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.Models.Models.Library;
using MALPlus.Models.Models.MalSpecific;
using MALPlus.Models.Models.Misc;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.ViewModels;
using Newtonsoft.Json;

//Okay it's big copy paste... feel free to laugh

namespace MALPlus.XShared.Utils
{
    /// <summary>
    ///     Contains stuff like GlobalScore and air date
    /// </summary>
    public class VolatileDataCache
    {
        public float GlobalScore { get; set; }
        public int DayOfAiring { get; set; }
        public ExactAiringTimeData ExactAiringTime { get; set; }
        public DateTime? LastFailedAiringTimeFetchAttempt { get; set; }
        public List<string> Genres { get; set; }
        public string AirStartDate { get; set; }
        public string TimeTillNextAir { get; set; }
        public DateTime? NextAirUtc { get; set; }
        public DateTime? NextAirFetchedAtUtc { get; set; }
        public string LastKnownStatus { get; set; }
    }

    public static class DataCache
    {
        private static readonly IDataCache DataCacheService;
        private static readonly IApplicationDataService ApplicationDataService;
        private static readonly IConnectionInfoProvider ConnectionInfoProvider;

        static DataCache()
        {
            DataCacheService = ResourceLocator.DataCacheService;
            ApplicationDataService = ResourceLocator.ApplicationDataService;
            ConnectionInfoProvider = ResourceLocator.ConnectionInfoProvider;
            LoadVolatileData();
            RetrieveHumMalIdDictionary();
        }

        public static async Task ClearApiRelatedCache()
        {
			await DataCacheService.ClearApiRelatedCache();
            _volatileDataCache.Clear();
        }

        public static async Task ClearAnimeListData()
        {
            await DataCacheService.ClearAnimeListData();
        }

        public static async Task SaveDataRoaming<T>(T data, string filename)
        {
            try
            {
                await DataCacheService.SaveDataRoaming(data, filename);
            }
            catch (Exception e)
            {
                //magic
            }
        }

        public static async Task SaveData<T>(T data, string filename, string targetFolder)
        {
            await DataCacheService.SaveData(data, filename,targetFolder);
        }

        public static async Task<T> RetrieveData<T>(string filename, string originFolder, int expiration)
        {
            return await DataCacheService.RetrieveData<T>(filename, originFolder, expiration);
        }

        public static async Task<T> RetrieveDataRoaming<T>(string filename,int expiration)
        {
            try
            {
                return await DataCacheService.RetrieveDataRoaming<T>(filename, expiration);
            }
            catch (Exception)
            {
                //No file
            }
            return default(T);
        }

        #region UserData

        public static async Task SaveDataForUser(string user, IEnumerable<ILibraryData> data, AnimeListWorkModes mode)
        {
            if (!Settings.IsCachingEnabled)
                return;
            try
            {
                if (mode == AnimeListWorkModes.Anime)
                {
                    await DataCacheService.SaveData(
                        new Tuple<DateTime, IEnumerable<AnimeLibraryItemData>>(DateTime.Now,
                            data.Select(item => item as AnimeLibraryItemData)),
                        $"{(mode == AnimeListWorkModes.Anime ? "anime" : "manga")}_data_{user.ToLower()}.json", "");
                }
                else
                {
                    await DataCacheService.SaveData(
                        new Tuple<DateTime, IEnumerable<MangaLibraryItemData>>(DateTime.Now,
                            data.Select(item => item as MangaLibraryItemData)),
                        $"{(mode == AnimeListWorkModes.Anime ? "anime" : "manga")}_data_{user.ToLower()}.json", "");
                }
            }
            catch (Exception)
            {
                //
            }
        }

        public static async Task<List<ILibraryData>> RetrieveDataForUser(string user, AnimeListWorkModes mode)
        {
            if (!Settings.IsCachingEnabled)
                return null;
            try
            {
                var decoded = new List<ILibraryData>();
                if (mode == AnimeListWorkModes.Anime)
                {
                    var jsonObj =
                        await
                            DataCacheService.RetrieveData<Tuple<DateTime, List<AnimeLibraryItemData>>>(
                                $"{(mode == AnimeListWorkModes.Anime ? "anime" : "manga")}_data_{user.ToLower()}.json",
                                "", 0);
                    if (!CheckForOldData(jsonObj.Item1))
                    {
                        return null;
                    }
                    decoded.AddRange(jsonObj.Item2.Select(item => item as ILibraryData));
                }
                else
                {
                    var jsonObj =
                        await
                            DataCacheService.RetrieveData<Tuple<DateTime, List<MangaLibraryItemData>>>(
                                $"{(mode == AnimeListWorkModes.Anime ? "anime" : "manga")}_data_{user.ToLower()}.json",
                                "", 0);
                    if (!CheckForOldData(jsonObj.Item1))
                    {
                        return null;
                    }
                    decoded.AddRange(jsonObj.Item2.Select(item => item as ILibraryData));
                }

                return decoded;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool CheckForOldData(DateTime timestamp)
        {
            if (!ConnectionInfoProvider.HasInternetConnection)
                return true;

            var diff = DateTime.Now.ToUniversalTime().Subtract(timestamp);
            var roamingUpdate = ApplicationDataService[RoamingDataTypes.LastLibraryUpdate] as long?;
            if (diff.TotalSeconds > Settings.CachePersitence || (roamingUpdate != null && timestamp < DateTime.FromBinary(roamingUpdate.Value)))
                return false;
            return true;
        }

        #endregion

        #region SeasonData

        /// <summary>
        ///     Persists one season's rows.
        /// </summary>
        /// <remarks>
        ///     Awaited, where this was <c>async void</c> and nobody waited. The season cache is the
        ///     one that decides whether a season costs eight requests or none, so a write that had
        ///     not landed by the next read simply meant paying for the season again.
        /// </remarks>
        public static async Task SaveSeasonalData(List<SeasonalAnimeData> data, string tag)
        {
            try
            {
                await DataCacheService.SaveData(data, $"seasonal_data{tag}_v2.json", "");
            }
            catch (Exception)
            {
                // file replace exception?
            }
        }

        public static async Task<List<SeasonalAnimeData>> RetrieveSeasonalData(string tag)
        {
            try
            {
                return
                    await
                        DataCacheService.RetrieveData<List<SeasonalAnimeData>>(
                            $"seasonal_data{tag}_v2.json", "", 7);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        /// <summary>
        ///     Reads a season's rows even when the entry has expired.
        /// </summary>
        /// <remarks>
        ///     For seasons that have already finished, where the contents cannot change: an old file
        ///     is not stale data there, it IS the data. Needed because the normal read returns nothing
        ///     once the seven days are up, which is what made a finished season disappear every week
        ///     and re-download in full on the next visit. An expiry of zero rather than a second read
        ///     of the file, so the deserialiser stays the one the normal path uses.
        /// </remarks>
        public static async Task<List<SeasonalAnimeData>> RetrieveSeasonalDataIgnoringExpiry(string tag)
        {
            try
            {
                return await DataCacheService.RetrieveData<List<SeasonalAnimeData>>(
                    $"seasonal_data{tag}_v2.json", "", 0);
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region VolatileData

        // This was a plain Dictionary while the airing-countdown fan-out wrote to it
        // from background threads and bound getters read it from the UI thread on every
        // traversal. Concurrent read+write on a Dictionary can corrupt its buckets and
        // spin, which is what the ~1s UI-thread stalls with no matching app work were.
        private static readonly ConcurrentDictionary<int, VolatileDataCache> _volatileDataCache =
            new ConcurrentDictionary<int, VolatileDataCache>();

        private static async void LoadVolatileData()
        {
            try
            {
                // Schema is part of the file name, so a build that changes what it stores stops
                // reading the previous build's table at once. Without it a corrected write is
                // invisible for the whole TTL, and every old entry is a lie the app keeps
                // repeating with total confidence.
                var loaded = await DataCacheService.RetrieveData<Dictionary<int, VolatileDataCache>>(
                    "volatile_data_v2.json", "", 0);
                if (loaded == null)
                    return;
                foreach (var kvp in loaded)
                    _volatileDataCache[kvp.Key] = kvp.Value;
            }
            catch (Exception)
            {
            }
        }

        public static async Task SaveVolatileData()
        {
            try
            {
                await DataCacheService.SaveData(
                    _volatileDataCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    "volatile_data_v2.json", "");
            }
            catch (Exception)
            {
                //ignored
            }
        }

        public static void RegisterVolatileData(int id, VolatileDataCache data)
        {
            if (_volatileDataCache.ContainsKey(id))
            {
                //We don't want to lose data here , only anime from seasonal contains genres data.
                if (data.Genres != null && data.Genres.Count > 0)
                    _volatileDataCache[id].Genres = data.Genres;
                _volatileDataCache[id].DayOfAiring = data.DayOfAiring;
                _volatileDataCache[id].GlobalScore = data.GlobalScore;
                _volatileDataCache[id].AirStartDate = data.AirStartDate;
            }
            else
                _volatileDataCache[id] = data;
        }

        public static void UpdateVolatileDataWithExactDate(int id, ExactAiringTimeData data)
        {
            if (_volatileDataCache.ContainsKey(id))
            {
                _volatileDataCache[id].ExactAiringTime = data;
                _volatileDataCache[id].LastFailedAiringTimeFetchAttempt = null;
            }
        }

        public static void UpdateVolatileDataAirDay(int id, int day)
        {
            if (_volatileDataCache.ContainsKey(id))
            {
                _volatileDataCache[id].DayOfAiring = day;
            }
        }

        public static void UpdateVolatileDataWithTimeTillNextAir(int id, string timeTillNextAir)
        {
            if (_volatileDataCache.ContainsKey(id))
            {
                _volatileDataCache[id].TimeTillNextAir = timeTillNextAir;
            }
            else
            {
                _volatileDataCache[id] = new VolatileDataCache { TimeTillNextAir = timeTillNextAir };
            }
        }

        public static void UpdateVolatileDataWithNextAir(int id, DateTime? nextAirUtc)
        {
            if (_volatileDataCache.ContainsKey(id))
            {
                _volatileDataCache[id].NextAirUtc = nextAirUtc;
                _volatileDataCache[id].NextAirFetchedAtUtc = DateTime.UtcNow;
                if (!nextAirUtc.HasValue)
                    _volatileDataCache[id].TimeTillNextAir = "";
            }
            else
            {
                _volatileDataCache[id] = new VolatileDataCache
                {
                    NextAirUtc = nextAirUtc,
                    NextAirFetchedAtUtc = DateTime.UtcNow
                };
            }
        }

        public static void UpdateVolatileStatus(int id, string status)
        {
            if (_volatileDataCache.ContainsKey(id))
                _volatileDataCache[id].LastKnownStatus = status;
            else
                _volatileDataCache[id] = new VolatileDataCache { LastKnownStatus = status };
            if (!string.IsNullOrEmpty(status) && !AirTimeUtils.IsCurrentlyAiringStatus(status))
            {
                _volatileDataCache[id].NextAirUtc = null;
                _volatileDataCache[id].NextAirFetchedAtUtc = null;
                _volatileDataCache[id].TimeTillNextAir = "";
            }
        }

        public static void RegisterVolatileDataAiringTimeFetchFailure(int id)
        {
            if (_volatileDataCache.ContainsKey(id))
            {
                _volatileDataCache[id].LastFailedAiringTimeFetchAttempt = DateTime.UtcNow;
            }
        }

        public static bool TryRetrieveDataForId(int id, out VolatileDataCache data)
        {
            try
            {
                return _volatileDataCache.TryGetValue(id, out data);
            }
            catch (Exception)
            {
                data = null;
                return false;
            }
        }

        #endregion

        #region AnimeDetailsData

        public static async void SaveAnimeDetails(int id, AnimeDetailsData data, bool anime = true)
        {
            try
            {

                await Task.Run(async () =>
                {
                    await
                        DataCacheService.SaveData(data, $"{data.Source}_{id}.json",
                            anime ? "AnimeDetails" : "MangaDetails");
                });
            }
            catch (Exception)
            {
                //probably failed to create folder #windowsmagic
            }
        }

        public static async Task<AnimeDetailsData> RetrieveAnimeGeneralDetailsData(int id, DataSource source,
            bool anime = true)
        {
            try
            {
                var final = await DataCacheService.RetrieveData<AnimeDetailsData>($"{source}_final_{id}.json",
                    anime ? "AnimeDetails" : "MangaDetails", 0);
                if (final != null)
                    return final;
                return await DataCacheService.RetrieveData<AnimeDetailsData>($"{source}_{id}.json",
                    anime ? "AnimeDetails" : "MangaDetails", 1);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        #endregion

        #region Episodes

        public static async Task SaveAnimeEpisodes(int id, List<AnimeEpisode> data)
        {
            try
            {
                await Task.Run(async () =>
                {
                    await DataCacheService.SaveData(data, $"episodes_{id}.json", "AnimeDetails");
                });
            }
            catch (Exception)
            {
                //magic
            }
        }

        public static async Task<List<AnimeEpisode>> RetrieveAnimeEpisodes(int id, bool airing)
        {
            try
            {
                return await DataCacheService.RetrieveData<List<AnimeEpisode>>($"episodes_{id}.json", "AnimeDetails",
                    airing ? 1 : 0);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        public static async Task<List<AnimeEpisode>> RetrieveAnimeEpisodesStale(int id)
        {
            try
            {
                return await DataCacheService.RetrieveData<List<AnimeEpisode>>($"episodes_{id}.json", "AnimeDetails", 0);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        #endregion

        #region DetailsScrapped

        public static async void SaveAnimeDetailsScrappedByStatus(int id, AnimeScrappedDetails data, bool airing)
        {
            try
            {
                await Task.Run(async () =>
                {
                    await DataCacheService.SaveData(data, airing ? $"{id}.json" : $"{id}_final.json",
                        "anime_details_scrapped");
                });
            }
            catch (Exception)
            {
                //magic
            }
        }

        public static async Task<AnimeScrappedDetails> RetrieveAnimeDetailsScrapped(int id, bool airing)
        {
            try
            {
                var final = await DataCacheService.RetrieveData<AnimeScrappedDetails>($"{id}_final.json",
                    "anime_details_scrapped", 0);
                if (final != null)
                    return final;
                return await DataCacheService.RetrieveData<AnimeScrappedDetails>($"{id}.json",
                    "anime_details_scrapped", airing ? 1 : 0);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        public static async Task<AnimeScrappedDetails> RetrieveAnimeDetailsScrappedStale(int id)
        {
            try
            {
                return await DataCacheService.RetrieveData<AnimeScrappedDetails>($"{id}.json",
                    "anime_details_scrapped", 0);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        #endregion

        #region Reviews

        public static async void SaveAnimeReviews(int id, List<AnimeReviewData> data, bool anime)
        {
            try
            {
                await Task.Run(async () =>
                {
                    await DataCacheService.SaveData(data, $"reviews_{id}.json", anime ? "AnimeDetails" : "MangaDetails");
                });
            }
            catch (Exception)
            {
                //magic
            }
        }

        public static async Task<List<AnimeReviewData>> RetrieveReviewsData(int animeId, bool anime)
        {
            try
            {
                return
                    await
                        DataCacheService.RetrieveData<List<AnimeReviewData>>($"reviews_{animeId}.json",
                            anime ? "AnimeDetails" : "MangaDetails", 14);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        #endregion

        #region DirectRecommendations

        public static async Task SaveDirectRecommendationsData(int id, List<DirectRecommendationData> data,
            bool anime)
        {
            try
            {
                await Task.Run(async () =>
                {
                    await DataCacheService.SaveData(data, $"direct_recommendations_v2_{id}.json",
                        anime ? "AnimeDetails" : "MangaDetails");
                });
            }
            catch (Exception)
            {
                //magic
            }
        }

        public static async Task<List<DirectRecommendationData>> RetrieveDirectRecommendationData(int id,
            bool anime)
        {
            try
            {
                return await DataCacheService.RetrieveData<List<DirectRecommendationData>>(
                    $"direct_recommendations_v2_{id}.json", anime ? "AnimeDetails" : "MangaDetails", 14);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        #endregion

        #region RelatedAnime

        public static async Task SaveRelatedAnimeData(int id, List<RelatedAnimeData> data, bool anime)
        {
            try
            {
                await Task.Run(async () =>
                {

                    await
                        DataCacheService.SaveData(data, $"related_anime_v4_{id}.json",
                            anime ? "AnimeDetails" : "MangaDetails");
                });
            }
            catch (Exception)
            {
                //magic
            }
        }

        public static async Task<List<RelatedAnimeData>> RetrieveRelatedAnimeData(int animeId, bool anime)
        {
            try
            {
                return
                    await
                        DataCacheService.RetrieveData<List<RelatedAnimeData>>($"related_anime_v4_{animeId}.json",
                            anime ? "AnimeDetails" : "MangaDetails", 14);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        #endregion

#region AnimeSerachResults

    /// <summary>
    ///     How long a Finished Airing entry stays cached. It used to be passed as 0, which
    ///     <see cref="RetrieveData{T}" /> reads as "never expires", so a finished title was frozen
    ///     for the life of the install: its rank, popularity, members and favourites never moved
    ///     again, and no schema change could ever reach it. Sixty days costs one request per
    ///     finished entry every couple of months and lets both drift and new fields settle in.
    /// </summary>
    private const int FinishedDetailsTtlDays = 60;

    /// <summary>
    ///     Airing schedules are cached on disk too, not just in memory, because AniList is rate
    ///     limited to 30 requests a minute and every launch would otherwise re-ask for the same
    ///     posters. Six hours is well inside how often a broadcast day changes, and the countdown
    ///     itself re-reads the persisted value continuously, so a stale schedule costs nothing in
    ///     accuracy: it just means the next refresh is up to six hours away.
    /// </summary>
    private const int AiringScheduleTtlHours = 6;

    /// <summary>
    ///     Wraps the schedule with the moment it was fetched. The cache store's own expiry works in
    ///     whole days and this wants hours, so the age travels with the payload instead of relying
    ///     on a file timestamp that the platform layer owns.
    /// </summary>
    private class CachedAiringSchedule
    {
        public AniListSchedule Schedule { get; set; }
        public DateTime FetchedAtUtc { get; set; }
    }

    /// <summary>
    ///     Forgets the stored schedule so the next read asks rather than answering from disk.
    /// </summary>
    public static async Task ClearAiringSchedule(int id)
    {
        try
        {
            // Overwriting with an unreadable entry is the same as forgetting it here:
            // RetrieveAiringSchedule reads an empty or unparsable body as "nothing stored" and
            // goes to the network, so there is no need for a delete the cache interface does not
            // have.
            await DataCacheService.SaveData("{}", "anilist_schedule_v2_" + id + ".json", "AiringSchedule");
        }
        catch (Exception)
        {
        }
    }

    public static async Task SaveAiringSchedule(int id, AniListSchedule schedule)    {
        if (schedule == null)
            return;
        try
        {
            // Schema is part of the file name: a build that changes how the record is picked up stops
            // reading the previous build's answer at once, instead of repeating a wrong schedule for
            // as long as the TTL lasts.
            var json = System.Text.Json.JsonSerializer.Serialize(new CachedAiringSchedule
            {
                Schedule = schedule,
                FetchedAtUtc = DateTime.UtcNow
            });
            await DataCacheService.SaveData(json, $"anilist_schedule_v2_{id}.json", "AiringSchedule");
        }
        catch (Exception)
        {
        }
    }

public static async Task<AniListSchedule> RetrieveAiringSchedule(int id)
        {
        try
        {
            var json = await DataCacheService.RetrieveData<string>($"anilist_schedule_v2_{id}.json",
                "AiringSchedule", 0);
            if (string.IsNullOrEmpty(json))
                return null;
            var cached = System.Text.Json.JsonSerializer.Deserialize<CachedAiringSchedule>(json);
            if (cached?.Schedule == null)
                return null;
            if ((DateTime.UtcNow - cached.FetchedAtUtc).TotalHours > AiringScheduleTtlHours)
                return null;
            return cached.Schedule;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task SaveAnimeSearchResultsData(string id, AnimeGeneralDetailsData data, bool anime)
    {
        try
        {
            await Task.Run(async () =>
            {
                await
                    DataCacheService.SaveData(data, $"mal_details_v5_{id}.json",
                        anime ? "AnimeDetails" : "MangaDetails");
            });
        }
        catch (Exception)
        {
            //magic
        }
    }

        public static async Task SaveAnimeSearchResultsDataFinal(string id, AnimeGeneralDetailsData data, bool anime)
        {
            try
            {
                await Task.Run(async () =>
                {
                    await
                        DataCacheService.SaveData(data, $"mal_details_final_{id}.json",
                            anime ? "AnimeDetails" : "MangaDetails");
                });
            }
            catch (Exception)
            {
                //magic
            }
        }

        public static async Task SaveGeneralDetailsByStatus(string id, AnimeGeneralDetailsData data, bool anime)
        {
// Manga is never "Airing": the API says "Finished Publishing"/"Publishing"/"On Hiatus"/"Discontinued",
// so comparing against the anime-only string sent every manga to the 14-day key. That looked fine
// until an entry was cached while publishing and then finished: the permanent final file was never
// written and every later open re-fetched. It also made the finished TTL unreachable for manga.
var finished = !string.IsNullOrEmpty(data.Status) &&
                        data.Status.StartsWith("Finished", StringComparison.CurrentCultureIgnoreCase);
            if (finished)
            await SaveAnimeSearchResultsDataFinal(id, data, anime);
            else
                await SaveAnimeSearchResultsData(id, data, anime);
        }

        public static async Task<AnimeGeneralDetailsData> RetrieveAnimeSearchResultsData(string animeId, bool anime)
        {
            try
            {
                var folder = anime ? "AnimeDetails" : "MangaDetails";
                var final =
                    await DataCacheService.RetrieveData<AnimeGeneralDetailsData>(
                        $"mal_details_final_{animeId}.json", folder, FinishedDetailsTtlDays);
                if (final != null)
                    return final;
                return await DataCacheService.RetrieveData<AnimeGeneralDetailsData>(
                    $"mal_details_v5_{animeId}.json", folder, 14);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        /// <summary>
        ///     Last resort when the network is gone. It has to try the finished file too, not just
        ///     the airing one: a finished entry is only ever written to mal_details_final_, so with
        ///     a finite TTL on that file an expired finished entry would otherwise find no stale
        ///     copy at all and the details page would come up empty while offline.
        /// </summary>
        public static async Task<AnimeGeneralDetailsData> RetrieveAnimeSearchResultsDataStale(string animeId, bool anime)
        {
            try
            {
                var folder = anime ? "AnimeDetails" : "MangaDetails";
                var final = await DataCacheService.RetrieveData<AnimeGeneralDetailsData>(
                    $"mal_details_final_{animeId}.json", folder, 0);
                if (final != null)
                    return final;
                return await DataCacheService.RetrieveData<AnimeGeneralDetailsData>(
                    $"mal_details_v5_{animeId}.json", folder, 0);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        #endregion

        #region TopAnime

        /// <summary>
        ///     Persists one of the Top anime lists.
        /// </summary>
        /// <remarks>
        ///     Returns a Task and is awaited by the caller, which it previously was not: this was
        ///     <c>async void</c>, so the write raced every read. Switching subcategories quickly could
        ///     land the write for the list you left AFTER the write for the one you are on, and the
        ///     file would hold the wrong list under the right name. The in-memory cache then handed
        ///     that out for the rest of the session.
        /// </remarks>
        public static async Task SaveTopAnimeData(List<TopAnimeData> data, TopAnimeType type, int rangeIndex = -1)
        {
            try
            {
                // v4 suffix bump: the cache used to store each row's local tab position in its
                // Index, which is wrong now that the chip reads the global rank. Old files get a
                // fresh name, so they are ignored automatically instead of poisoning every chip.
                // The range lands in the name because a range IS a different answer to the same
                // question: without it every range would overwrite the last one read.
                await DataCacheService.SaveData(data, TopAnimeFileName(type, rangeIndex), "");
            }
            catch (Exception)
            {
                //magic
            }
        }

        public static async Task<List<TopAnimeData>> RetrieveTopAnimeData(TopAnimeType type, int rangeIndex = -1)
        {
            try
            {
                // See SaveTopAnimeData for why this moved off v2: position-in-tab was not the
                // global ranking, and old files with that baked in are invisible now.
                return await DataCacheService.RetrieveData<List<TopAnimeData>>(TopAnimeFileName(type, rangeIndex), "", 14);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        private static string TopAnimeFileName(TopAnimeType type, int rangeIndex) =>
            rangeIndex >= 0
                ? $"top_{type}_r{rangeIndex}_{Comm.ContentFilter.CacheTag}_v4.json"
                : $"top_{type}_{Comm.ContentFilter.CacheTag}_v4.json";

        private static string TopMangaFileName(MangaTopType type, int rangeIndex) =>
            rangeIndex >= 0
                ? $"topmanga_{type}_r{rangeIndex}_{Comm.ContentFilter.CacheTag}_v5.json"
                : $"topmanga_{type}_{Comm.ContentFilter.CacheTag}_v5.json";

        /// <summary>
        ///     Persists one of the Top manga lists. Awaited for the same reason as the anime one: a
        ///     fire and forget write lands whenever it lands.
        /// </summary>
        public static async Task SaveTopMangaData(List<TopAnimeData> data, MangaTopType type, int rangeIndex = -1)
        {
            try
            {
                // Same global-rank semantics change as Top anime: bumped off the file name that was
                // saved under the old, local-tab-position meaning.
                await DataCacheService.SaveData(data, TopMangaFileName(type, rangeIndex), "");
            }
            catch (Exception)
            {
                //magic
            }
        }

        public static async Task<List<TopAnimeData>> RetrieveTopMangaData(MangaTopType type, int rangeIndex = -1)
        {
            try
            {
                return await DataCacheService.RetrieveData<List<TopAnimeData>>(TopMangaFileName(type, rangeIndex), "", 14);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        /// <summary>
        ///     Persists the Adapted to anime list. Awaited for the same reason as the Top ones.
        /// </summary>
        public static async Task SaveAdaptedToAnimeData(List<TopAnimeData> data, MangaAdaptedType type)
        {
            try
            {
                await DataCacheService.SaveData(data, $"adapted_{type}_data.json", "");
            }
            catch (Exception)
            {
                //magic
            }
        }

        public static async Task<List<TopAnimeData>> RetrieveAdaptedToAnimeData(MangaAdaptedType type)
        {
            try
            {
                return await DataCacheService.RetrieveData<List<TopAnimeData>>($"adapted_{type}_data.json", "", 14);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        #endregion

        #region MalToHum

        public static async Task SaveHumMalIdDictionary()
        {
            try
            {
                //await DataCacheService.SaveData(AnimeDetailsHummingbirdQuery.MalToHumId, "mal_to_hum.json", "");
            }
            catch (Exception)
            {
                //ignored
            }
        }

        public static async void RetrieveHumMalIdDictionary()
        {
            var result = new Dictionary<int, int>();
            try
            {
                result = await DataCacheService.RetrieveData<Dictionary<int, int>>("mal_to_hum.json", "", 0);
            }
            catch (Exception)
            {
                result = new Dictionary<int, int>();
            }
            //AnimeDetailsHummingbirdQuery.MalToHumId = result ?? new Dictionary<int, int>();
        }

        #endregion

        #region ProfileData

        public static async void SaveProfileData(string user, ProfileData data)
        {
            try
            {
                await Task.Run(async () =>
                {
                    await DataCacheService.SaveData(data, $"mal_profile_details_{user}.json", "ProfileData");
                });
            }
            catch (Exception)
            {
                //magic
            }
        }

        public static async Task<ProfileData> RetrieveProfileData(string user)
        {
            try
            {
                return
                    await
                        DataCacheService.RetrieveData<ProfileData>($"mal_profile_details_{user}.json", "ProfileData", 1);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        #endregion

        #region ArticlesIndex

        public static async void SaveArticleIndexData(ArticlePageWorkMode mode, List<MalNewsUnitModel> data)
        {
            try
            {
                await Task.Run(async () =>
                {
                    await
                        DataCacheService.SaveData(data,
                            mode == ArticlePageWorkMode.Articles ? "mal_article_index_v2.json" : "mal_news_index_v2.json",
                            "Articles");
                });
            }
            catch (Exception)
            {
                //magic
            }
        }

        public static async Task<List<MalNewsUnitModel>> RetrieveArticleIndexData(ArticlePageWorkMode mode)
        {
            try
            {
                return await DataCacheService.RetrieveData<List<MalNewsUnitModel>>(mode == ArticlePageWorkMode.Articles
                    ? "mal_article_index_v2.json"
                    : "mal_news_index_v2.json", "Articles", 1);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        #endregion

        #region ArticlesContent

        public static async void SaveArticleContentData(string title, string htmlData, MalNewsType type)
        {
            try
            {
                await Task.Run(async () =>
                {
                    await
                        DataCacheService.SaveData(htmlData,
                            $"mal_{(type == MalNewsType.Article ? "article" : "news")}_html_{title}.json", "Articles");
                });
            }
            catch (Exception e)
            {
                //magic
            }
        }

        public static async Task<string> RetrieveArticleContentData(string title, MalNewsType type)
        {
            try
            {
                return
                    await
                        DataCacheService.RetrieveData<string>(
                            $"mal_{(type == MalNewsType.Article ? "article" : "news")}_html_{title}.json", "Articles", 7);
            }
            catch (Exception)
            {
                //No file
            }
            return null;
        }

        #endregion
    }
}

