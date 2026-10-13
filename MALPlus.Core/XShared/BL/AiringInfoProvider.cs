using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MALPlus.Adapters;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.Interfaces;
using MALPlus.XShared.Utils;
using Newtonsoft.Json;

namespace MALPlus.XShared.BL
{
    public class AiringInfoProvider : IAiringInfoProvider
    {
        private const string UpdateStorakeKey = "AiringInfoProviderLastUpdateDate";
        //v2: the stored episode timestamps used to be written through a local-time conversion,
        //so every cached entry carried a date shifted by the device UTC offset. New name drops them.
        private const string CacheFileName = "airing_data_v2.json";

        private readonly IDataCache _dataCache;
        private readonly IApplicationDataService _applicationDataService;
        private readonly IMessageDialogProvider _dialogProvider;

        private List<AiringData> _airingData;
        private Task _initTask;
        private List<AiringData> AiringShows
        {
            get { return _airingData; }
            set
            {
                _airingData = value;
                _lookupDictionary =
                    new NullDictionary<int, AiringData>(
                        value.ToDictionary(data => data.MalId, data => data));
            }
        }

        private NullDictionary<int, AiringData> _lookupDictionary;

        public AiringInfoProvider(IDataCache dataCache,
            IApplicationDataService applicationDataService )
        {
            _dataCache = dataCache;
            _applicationDataService = applicationDataService;
        }

        private const string MandatoryRefreshKey = "AiringMandatoryLastRefresh";
        public Task Init(bool cacheOnly, bool force = false)
        {
            if (force)
            {
                _airingData = null;
                _initTask = null;
            }
            if (_airingData != null)
                return Task.CompletedTask;
            //dedupe: let concurrent callers (startup + calendar) share one in-flight fetch
            if (_initTask != null)
                return _initTask;
            _initTask = InitCore(cacheOnly, force);
            return _initTask;
        }

        private async Task InitCore(bool cacheOnly, bool force = false)
        {
            if(!force && _airingData != null)
                return;

            // The declared next air dates are read from disk before anything else so a cold start
            // can show a countdown without waiting on the network.
            if (_nextAirFetchedAtUtc == DateTime.MinValue)
                LoadDeclaredNextAirCache();

            List<AiringData> data = null;
            try
            {
                data = await _dataCache.RetrieveData<List<AiringData>>(CacheFileName, null, 0);
            }
            catch (Exception)
            {
                data = null;
            }

            var lastUpdate = _applicationDataService[UpdateStorakeKey];
            var fresh = !force && lastUpdate != null &&
                        DateTime.Now - DateTime.FromBinary((long) lastUpdate) < TimeSpan.FromHours(8);

            //self-heal: a cache persisted from the legacy feed only carries mal_id+episodes
            //(no titles/images). Treat it as stale so we try to re-fetch the full schedules data.
            var oldSchema = data != null && data.Any() && data.All(x => string.IsNullOrEmpty(x.Title));

            try
            {
                if (data != null && data.Any() && fresh && !oldSchema)
                {
                    ApplyData(data);
                    return;
                }

                if (!cacheOnly)
                {
                    var schedulesTask = new AnimeSchedulesQuery().GetScheduleAsync();
                    var completed = await Task.WhenAny(schedulesTask, Task.Delay(15000));
                    if (completed == schedulesTask)
                    {
                        var schedulesData = await schedulesTask;
                        if (schedulesData != null && schedulesData.Any())
                        {
                            data = schedulesData;
                            _applicationDataService[UpdateStorakeKey] = DateTime.Now.ToBinary();
                            try { _applicationDataService["AiringMandatoryLastRefresh"] = DateTime.UtcNow.ToString("O"); } catch { }
                            try { _dataCache.SaveData(data, CacheFileName, null); } catch (Exception) { }
                        }
                    }
                    // timeout or empty/failed refetch -> keep the stale cache we read above
                }
            }
            catch (Exception)
            {
                // network/refetch failure -> keep whatever cache we already read (stale-not-blank)
            }

            if (oldSchema && data != null && data.Any() && !string.IsNullOrEmpty(data.First().Title))

            if (data == null || !data.Any())
            {
                AiringShows = new List<AiringData>();
                InitializationSuccess = false;
                return;
            }

            ApplyData(data);
            try { AiringsUpdated?.Invoke(); } catch { }
        }

        private void ApplyData(List<AiringData> data)
        {
            foreach (var airingData in data)
            {
                if (airingData.Episodes != null)
                    airingData.Episodes = airingData.Episodes.OrderBy(episode => episode.Timestamp).ToList();
            }
            AiringShows = data.GroupBy(x => x.MalId).Select(g => g.First()).ToList();
            InitializationSuccess = true;
        }

        #region Real next air dates

        private const string NextAirCacheFile = "anilist_next_air_v1.json";
        private const string NextAirUpdatedKey = "AiringInfoProviderNextAirLastUpdate";

        /// <summary>
        ///     malId -> the next episode date AniList declares, in UTC.
        ///     <para>
        ///     A series that AniList has no next episode for is simply absent. That absence is the
        ///     answer, not a gap to be filled from the weekly slot: MyAnimeList reports a slot for
        ///     every airing series and no next episode at all, and treating the slot as a schedule
        ///     is what produced countdowns for chapters nobody had announced.
        ///     </para>
        /// </summary>
        private Dictionary<int, DateTime> _nextAirByMalId = new();
        private readonly object _nextAirMapLock = new();
        private DateTime _nextAirFetchedAtUtc = DateTime.MinValue;
        private readonly SemaphoreSlim _nextAirGate = new(1, 1);

        /// <summary>
        ///     Records that a series is wanted and makes sure a fetch happens for it, batching every
        ///     request that arrives in the same moment into one call.
        ///     <para>
        ///     Every countdown in the app asks for its own series, and a screen asks for a dozen at
        ///     once. Fetching per ask is both wasteful and, past a handful of series, refused
        ///     outright: AniList starts answering 429 well before a season's worth is done, and a
        ///     refusal is indistinguishable from a series with no announced episode, so the entries
        ///     that lost the race quietly fell back to a cadence nobody had announced. Waiting a
        ///     moment before going out is what turns a screenful of simultaneous asks into one
        ///     request, and it is why this lives here rather than in each screen.
        ///     </para>
        /// </summary>
        public Task RequestDeclaredNextAir(int malId)
        {
            if (malId <= 0)
                return Task.CompletedTask;

            lock (_nextAirPendingLock)
            {
                if (_nextAirKnown(malId))
                    return Task.CompletedTask;

                if (_nextAirWaiters.TryGetValue(malId, out var existing))
                    return existing.Task;

                var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _nextAirWaiters[malId] = waiter;
                _nextAirPending.Add(malId);

                if (!_nextAirFlushScheduled)
                {
                    _nextAirFlushScheduled = true;
                    _ = FlushPendingDeclaredAirsAsync();
                }

                // Bounded so a refused or hanging request cannot hold a card's resolution open. The
                // per series fallbacks take over from here.
                return WaitBoundedAsync(waiter.Task);
            }
        }

        private static async Task WaitBoundedAsync(Task inner)
        {
            var finished = await Task.WhenAny(inner, Task.Delay(TimeSpan.FromSeconds(12)));
            if (finished != inner)
                inner.ContinueWith(_ => { }, TaskScheduler.Default);
        }

        private readonly HashSet<int> _nextAirPending = new HashSet<int>();
        private readonly object _nextAirPendingLock = new();
        private bool _nextAirFlushScheduled;

        /// <summary>
        ///     How long to let a screen's requests pile up before spending them. Long enough that a
        ///     grid binding and a page load coalesce, short enough that a single card still gets its
        ///     date while the user is still looking at it.
        /// </summary>
        private static readonly TimeSpan PendingFlushDelay = TimeSpan.FromMilliseconds(250);

        private bool _nextAirKnown(int malId)
        {
            if (_nextAirByMalId.ContainsKey(malId))
                return true;

            // A series asked about inside the window already counts as answered, even when the
            // answer was "nothing scheduled". Otherwise it would be re-asked on every bind.
            return _nextAirFetchedAtUtc > DateTime.MinValue &&
                   DateTime.UtcNow - _nextAirFetchedAtUtc < TimeSpan.FromHours(8) &&
                   _nextAirAsked.Contains(malId);
        }

        private readonly HashSet<int> _nextAirAsked = new HashSet<int>();

        private async Task FlushPendingDeclaredAirsAsync()
        {
            try
            {
                await Task.Delay(PendingFlushDelay);

                List<int> batch;
                List<TaskCompletionSource<bool>> waiters;
                lock (_nextAirPendingLock)
                {
                    if (_nextAirPending.Count == 0)
                        return;
                    batch = _nextAirPending.ToList();
                    waiters = batch.Select(id => _nextAirWaiters.TryGetValue(id, out var w) ? w : null)
                                   .Where(w => w != null).ToList();
                    _nextAirPending.Clear();
                    foreach (var id in batch)
                    {
                        _nextAirAsked.Add(id);
                        _nextAirWaiters.Remove(id);
                    }
                    _nextAirFlushScheduled = false;
                }

                await RefreshDeclaredNextAirAsync(batch);

                // Released only once the request is over, so a caller that reads the table right
                // after this sees its own series rather than whichever ones happened to land first.
                foreach (var waiter in waiters)
                    waiter.TrySetResult(true);
            }
            catch (Exception)
            {
                List<TaskCompletionSource<bool>> waiters;
                lock (_nextAirPendingLock)
                {
                    waiters = _nextAirWaiters.Values.ToList();
                    _nextAirPending.Clear();
                    _nextAirWaiters.Clear();
                    _nextAirFlushScheduled = false;
                }

                foreach (var waiter in waiters)
                    waiter.TrySetResult(false);
            }
        }

        private readonly Dictionary<int, TaskCompletionSource<bool>> _nextAirWaiters = new();

        /// <summary>
        ///     The declared next air date for a series, or null when there is none to believe.
        /// </summary>
        public bool TryGetDeclaredNextAirDate(int malId, out DateTime dateUtc)
        {
            lock (_nextAirMapLock)
                return _nextAirByMalId.TryGetValue(malId, out dateUtc);
        }

        /// <summary>
        ///     Fetches the declared next air dates for the given series.
        ///     <para>
        ///     The window is a cache for what has been answered, not a wall in front of the service.
        ///     A series already held, and a series already asked about inside the window, are both
        ///     skipped; anything new goes out. Sharing one timestamp across every caller used to
        ///     mean the first one paid and the rest were told "not yet" against an empty table, and
        ///     every series on screen then looked like it had no announced episode.
        ///     </para>
        /// </summary>
        public async Task RefreshDeclaredNextAirAsync(IEnumerable<int> malIds, bool force = false)
        {
            var ids = malIds?.Where(id => id > 0).Distinct().ToList();
            if (ids == null || ids.Count == 0)
                return;

            var now = DateTime.UtcNow;

            await _nextAirGate.WaitAsync();
            try
            {
                var window = TimeSpan.FromHours(8);
                bool withinWindow;
                List<int> wanted;
                lock (_nextAirMapLock)
                {
                    withinWindow = _nextAirFetchedAtUtc > DateTime.MinValue &&
                                   now - _nextAirFetchedAtUtc < window;
                    wanted = ids
                        .Where(id => !_nextAirByMalId.ContainsKey(id))
                        .Where(id => force || !withinWindow || !_nextAirAsked.Contains(id))
                        .ToList();
                    foreach (var id in ids)
                        _nextAirAsked.Add(id);
                }

                if (wanted.Count == 0)
                    return;

                var fetched = await AniListAiringQuery.GetNextAiringAsync(wanted);
                if (fetched == null || fetched.Count == 0)
                    return;

                lock (_nextAirMapLock)
                {
                    foreach (var pair in fetched)
                    {
                        // A batched request can answer the same series twice, once with a schedule
                        // and once without, and the bare one can come back first: AniList returned
                        // JoJo's two entries that way, and keeping the first left the series looking
                        // like it had no announced episode. The query drops those on the way in;
                        // this is the belt to that pair of braces.
                        _nextAirByMalId[pair.Key] = pair.Value;
                    }
                }

                _nextAirFetchedAtUtc = now;
                try { _applicationDataService[NextAirUpdatedKey] = now.ToBinary(); } catch { }
                try
                {
                    Dictionary<int, DateTime> snapshot;
                    lock (_nextAirMapLock) { snapshot = new Dictionary<int, DateTime>(_nextAirByMalId); }
                    _dataCache.SaveData(snapshot, NextAirCacheFile, null);
                }
                catch { }
            }
            catch (Exception)
            {
                // AniList unreachable: keep the dates already known.
            }
            finally
            {
                _nextAirGate.Release();
            }
        }

        /// <summary>
        ///     Reads the cached dates so a cold start can show a countdown before the network is
        ///     touched.
        /// </summary>
        private void LoadDeclaredNextAirCache()
        {
            try
            {
                var raw = _applicationDataService[NextAirUpdatedKey];
                if (raw != null)
                    _nextAirFetchedAtUtc = DateTime.FromBinary(Convert.ToInt64(raw));

                var cached = _dataCache.RetrieveData<Dictionary<int, DateTime>>(NextAirCacheFile, null, 0).Result;
                if (cached != null && cached.Count > 0)
                {
                    lock (_nextAirMapLock)
                    {
                        foreach (var pair in cached)
                            _nextAirByMalId[pair.Key] = pair.Value;
                    }
                }
            }
            catch (Exception)
            {
                // No cache, or one written by a build that stored it differently.
            }
        }

        #endregion

        public bool TryGetCurrentEpisode(int id, out int episode, DateTime? forDay = null)
        {
            episode = 0;
            if (_lookupDictionary == null) return false;
            var currentTimestamp = Utilities.ConvertToUnixTimestamp(DateTime.UtcNow);
            var data = _lookupDictionary[id];
            if (data == null)
                return false;

            try
            {
                if (forDay == null)
                {
                    episode = GetCurrentEpisode(data,currentTimestamp);
                }
                else
                {
                    var todaysMatch =
                        data.Episodes.FirstOrDefault(ep => CalendarTimeZone.ToZoneDayOfYear(
                                                               Utilities.ConvertFromUnixTimestamp(ep.Timestamp),
                                                               CalendarTimeZone.Resolved) == forDay.Value.DayOfYear);
                    if (todaysMatch != null)
                        episode = todaysMatch.EpisodeNumber;
                    else
                        episode = GetCurrentEpisode(data, currentTimestamp);

                }

                if (episode <= 0)
                    return false;
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public bool TryGetLastEpisode(int id, out int ep)
        {
            if (_lookupDictionary == null) { ep = 0; return false; }
            var data = _lookupDictionary[id];
            if (data?.Episodes == null || !data.Episodes.Any())
            {
                ep = 0;
                return false;
            }

            ep = data.Episodes.Max(episode => episode.EpisodeNumber);

            return true;
        }

        private int GetCurrentEpisode(AiringData data, int currentTimestamp)
        {
            var next = data.Episodes.FirstOrDefault(ep => ep.Timestamp >= currentTimestamp);
            if (next != null)
                return next.EpisodeNumber - 1;

            if (data.Episodes.Last().Timestamp < currentTimestamp)
            {
                return data.Episodes.Last().EpisodeNumber;
            }

            return 0;
        }

        public bool TryGetNextAirDate(int id, DateTime forDay, out DateTime date)
        {
            date = DateTime.MinValue;
            if (_lookupDictionary == null) return false;

            var data = _lookupDictionary[id];
            if (data == null)
                return false;

            try
            {
                // Where the real next episode lands, not where the weekly slot would. This used to
                // read the fabricated slots, which meant every airing entry always had a future date
                // and a series that had stopped still counted down to it.
                if (TryGetDeclaredNextAirDate(id, out var declared) &&
                    (declared > DateTime.UtcNow || AirTimeUtils.IsInAiringWindow(declared, DateTime.UtcNow)))
                {
                    date = declared;
                    return true;
                }

                if (data.Episodes == null || data.Episodes.Count == 0)
                    return false;

                var todaysMatch =
                    data.Episodes.FirstOrDefault(ep => CalendarTimeZone.ToZoneDayOfYear(
                                                           Utilities.ConvertFromUnixTimestamp(ep.Timestamp),
                                                           CalendarTimeZone.Resolved) == forDay.DayOfYear);
                if (todaysMatch != null)
                    date = Utilities.ConvertFromUnixTimestamp(todaysMatch.Timestamp);
                else
                {
                    var currentTimestamp = Utilities.ConvertToUnixTimestamp(DateTime.UtcNow);
                    var next = data.Episodes.FirstOrDefault(ep => ep.Timestamp >= currentTimestamp);
                    if (next != null)
                        date = Utilities.ConvertFromUnixTimestamp(next.Timestamp);
                    else if (data.Episodes.Last().Timestamp < currentTimestamp)
                    {
                        date = Utilities.ConvertFromUnixTimestamp(data.Episodes.Last().Timestamp);
                    }

                }
                if (date == DateTime.MinValue)
                    return false;
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        public bool TryGetAiringDay(int id, out DayOfWeek day)
        {
            day = DayOfWeek.Monday;
            if (_lookupDictionary == null) return false;
            var data = _lookupDictionary[id];
            if (data == null)
                return false;

            // The weekday comes from the real next episode when there is one, because that is the
            // day it will actually air; the weekly slot is only a fallback for a series whose date
            // nobody has published. A slot read as gospel is how a series that moved its broadcast
            // day kept being filed under the old one.
            if (TryGetDeclaredNextAirDate(id, out var declared))
            {
                day = CalendarTimeZone.ToZoneDay(declared, CalendarTimeZone.Resolved);
                return true;
            }

            if (data.BroadcastDay.HasValue)
            {
                day = data.BroadcastDay.Value;
                return true;
            }

            if (data.Episodes == null || !data.Episodes.Any())
                return false;

            //MAL publishes the broadcast slot in JST, but the weekday an entry is filed under has
            //to be the one the viewer is actually on, so this follows the chosen calendar zone.
            day = CalendarTimeZone.ToZoneDay(
                Utilities.ConvertFromUnixTimestamp(data.Episodes[0].Timestamp), CalendarTimeZone.Resolved);

            return true;
        }

        public IEnumerable<int> GetAllAiringIds()
        {
            if (_airingData == null)
                yield break;
            foreach (var data in _airingData)
            {
                if (data != null)
                    yield return data.MalId;
            }
        }

        public bool HasAiringEntry(int id)
        {
            if (_lookupDictionary == null) return false;
            return _lookupDictionary.ContainsKey(id);
        }

        public bool TryGetEntry(int id, out AiringData entry)
        {
            if (_lookupDictionary == null) { entry = null; return false; }
            entry = _lookupDictionary[id];
            return entry != null;
        }

        public bool InitializationSuccess { get; set; }
        public event Action AiringsUpdated;
        public void NotifyUpdated() => AiringsUpdated?.Invoke();


        public class Episode
        {
            [JsonProperty("t")]
            public int Timestamp { get; set; }
            [JsonProperty("n")]
            public int EpisodeNumber { get; set; }
        }


        public class AiringData
        {
            [JsonProperty("mal_id")]
            public int MalId { get; set; }
            [JsonProperty("airing")]
            public List<Episode> Episodes { get; set; }
            [JsonProperty("title")]
            public string Title { get; set; }
            [JsonProperty("img_url")]
            public string ImgUrl { get; set; }
            [JsonProperty("type")]
            public int Type { get; set; }
            [JsonProperty("all_episodes")]
            public int AllEpisodes { get; set; }

            /// <summary>
            ///     The weekly broadcast slot, when the schedule endpoint publishes one.
            ///     <para>
            ///     Not marked with JsonProperty: it is filled in by the query that reads the schedule
            ///     endpoint, and it is never serialised into the cached table, so an entry restored
            ///     from disk keeps whatever the query set when it was fetched.
            ///     </para>
            ///     <para>
            ///     This is a position in the week, not a date. It used to be rolled forward into a
            ///     fabricated list of episodes, which is what gave a series a countdown to a chapter
            ///     nobody had announced; it now only answers what weekday the series is associated
            ///     with.
            /// </para>
            public DayOfWeek? BroadcastDay { get; set; }
            public TimeSpan? BroadcastTime { get; set; }
        }

        class NullDictionary<TKey, TVal> : Dictionary<TKey, TVal>
        {
            public NullDictionary()
            {

            }

            public NullDictionary(Dictionary<TKey,TVal> source)
            {
                foreach (var val in source)
                {
                    Add(val.Key,val.Value);
                }
            }

            public new TVal this[TKey key]
            {
                get
                {
                    if(ContainsKey(key))
                        return base[key];
                    return default(TVal);
                }
                set => base[key] = value;
            }
        }
    }
}
