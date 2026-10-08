using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.Comm.Anime
{
    public enum TopAnimeType
    {
        General,
        Airing,
        Upcoming,
        Tv,
        Movies,
        Ovas,
        Popular,
        Favourited
        // Manga used to live here as well and pointed at the same topmanga.php that MangaTopType.All
        // does, which made it a second door to a list the More menu already offers under "Top manga".
        // It is gone; MangaTopType is the one that stays.
    }

    public enum MangaTopType
    {
        All,
        Manga,
        Novels,
        LightNovels,
        OneShots,
        Doujinshi,
        Manhwa,
        Manhua,
        Popular,
        Favourited
    }


    public class AnimeTopQuery : Query
    {
        private static Dictionary<string, (List<TopAnimeData> data, DateTime fetchedAt)> _prevQueriesCache =
            new Dictionary<string, (List<TopAnimeData>, DateTime)>();
        private TopAnimeType _type;
        private MangaTopType _mangaType;
        private bool _isManga;
        private int _page;
        private int _rangeIndex = -1;

        /// <summary>
        ///     How many ranks of MAL's global ranking one range covers, and how many ranges there are.
        /// </summary>
        /// <remarks>
        ///     A range is a band of the ranking, not a slice of the list: the reader sees the entries
        ///     whose own rank falls inside it, and the chip on each card is that same rank. Two hundred
        ///     and fifty was chosen because it is five source pages, which is one request burst short
        ///     enough to come back in about two seconds.
        /// </remarks>
        public const int RangeSize = 250;
        public const int RangeCount = 8;

        /// <summary>
        ///     Rows the source sends per request once it is asked for a big page.
        /// </summary>
        /// <remarks>
        ///     Measured against both top endpoints: fifty is accepted, a hundred is refused with a 400.
        ///     So fifty is the ceiling and every page maths below is built on it.
        /// </remarks>
        private const int SourcePageSize = 50;

        /// <summary>
        ///     Extra pages fetched on each side of the rows a range nominally covers.
        /// </summary>
        /// <remarks>
        ///     The pages are not a clean slice of the ranking: page ten of anime runs from rank 467 to
        ///     511, because ranks are skipped where a list cannot place an entry. Starting the read one
        ///     page early and running one page past is what keeps the first and the last rank of a range
        ///     inside it. Measured on the 251-500 range: seven pages returned every rank from 251 to 499.
        /// </remarks>
        private const int RangePageMargin = 2;

        /// <summary>
        ///     Goes a page that the source refused gets, one at a time, before the band is given up on.
        /// </summary>
        /// <remarks>
        ///     Two, which is three visits to the page in total. Enough to ride out a burst of refusals
        ///     and little enough that a genuinely dead page is found quickly rather than parking the
        ///     reader on a spinner for half a minute.
        /// </remarks>
        private const int RangePageRetries = 2;

        public AnimeTopQuery(TopAnimeType topType, int page = 0, int rangeIndex = -1)
        {
            _page = page;
            _type = topType;
            _isManga = false;
            _rangeIndex = rangeIndex;
        }

        public AnimeTopQuery(MangaTopType topType, int page = 0, int rangeIndex = -1)
        {
            _page = page;
            _mangaType = topType;
            _isManga = true;
            _rangeIndex = rangeIndex;
        }

        /// <summary>
        ///     The first and last MAL rank a range covers, inclusive.
        /// </summary>
        public (int First, int Last) RangeRanks =>
            (_rangeIndex * RangeSize + 1, (_rangeIndex + 1) * RangeSize);

        private string GetEndpoint(TopAnimeType type,int page)
        {
            switch (type)
            {
                case TopAnimeType.General:
                    return $"topanime.php?limit={page*50}";
                case TopAnimeType.Airing:
                    return $"topanime.php?type=airing&limit={page*50}";
                case TopAnimeType.Upcoming:
                    return $"topanime.php?type=upcoming&limit={page*50}";
                case TopAnimeType.Tv:
                    return $"topanime.php?type=tv&limit={page*50}";
                case TopAnimeType.Movies:
                    return $"topanime.php?type=movie&limit={page*50}";
                case TopAnimeType.Ovas:
                    return $"topanime.php?type=ova&limit={page*50}";
                case TopAnimeType.Popular:
                    return $"topanime.php?type=bypopularity&limit={page*50}";
                case TopAnimeType.Favourited:
                    return $"topanime.php?type=favorite&limit={page*50}";
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private string GetMangaEndpoint(MangaTopType type, int page)
        {
            switch (type)
            {
                case MangaTopType.All:
                    return $"topmanga.php?limit={page*50}";
                case MangaTopType.Manga:
                    return $"topmanga.php?type=manga&limit={page*50}";
                case MangaTopType.Novels:
                    return $"topmanga.php?type=novels&limit={page*50}";
                case MangaTopType.LightNovels:
                    return $"topmanga.php?type=lightnovels&limit={page*50}";
                case MangaTopType.OneShots:
                    return $"topmanga.php?type=oneshots&limit={page*50}";
                case MangaTopType.Doujinshi:
                    return $"topmanga.php?type=doujin&limit={page*50}";
                case MangaTopType.Manhwa:
                    return $"topmanga.php?type=manhwa&limit={page*50}";
                case MangaTopType.Manhua:
                    return $"topmanga.php?type=manhua&limit={page*50}";
                case MangaTopType.Popular:
                    return $"topmanga.php?type=bypopularity&limit={page*50}";
                case MangaTopType.Favourited:
                    return $"topmanga.php?type=favorite&limit={page*50}";
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        public async Task<List<TopAnimeData>> GetTopAnimeData(bool force = false)
        {
            // The in-memory copy carries the entry's own age, so it expires with the file it came from.
            // It used to be kept for the whole session with no clock on it at all, which is why a Top
            // list kept serving the same entries for days after the panel was supposed to have picked
            // up whatever changed. The file has a fourteen day life; a mirror of it has to carry the
            // same one or it is quietly a different policy.
            var key = _isManga ? MangaKey() : AnimeKey();
            if (!force && TryGetFresh(key, out var remembered))
                return remembered;

            // A range is the opposite case from the filtered tabs below. The file cache is consulted
            // only AFTER a live read has failed, never before it: this is the one list where going to
            // the network first is right, because what lives in the file is the reader's own last good
            // answer for that exact range and serving it up front would keep it there for the full
            // fourteen days while the ranking moved on underneath.
            // <para>
            // What makes the file safe to serve when the source is down is that it is only ever
            // written from a complete range. A read where any single page failed is not saved at all,
            // so a range in the cache is always rows that were genuinely filtered by rank. Nothing
            // invented and nothing borrowed from another range can reach it.
            // </para>
            if (_rangeIndex >= 0)
            {
                var fetched = await FetchFromTenraiAsync();
                if (fetched != null && fetched.Count > 0)
                {
                    if (_isManga)
                        await DataCache.SaveTopMangaData(fetched, _mangaType, _rangeIndex);
                    else
                        await DataCache.SaveTopAnimeData(fetched, _type, _rangeIndex);
                    Remember(key, fetched);
                    return fetched;
                }

                var cached = _isManga
                    ? (await DataCache.RetrieveTopMangaData(_mangaType, _rangeIndex) ?? new List<TopAnimeData>())
                    : (await DataCache.RetrieveTopAnimeData(_type, _rangeIndex) ?? new List<TopAnimeData>());
                if (cached.Count > 0)
                {
                    Remember(key, cached);
                    return cached;
                }

                return new List<TopAnimeData>();
            }

            var output = force
                ? new List<TopAnimeData>()
                : _isManga
                    ? (await DataCache.RetrieveTopMangaData(_mangaType) ?? new List<TopAnimeData>())
                    : (await DataCache.RetrieveTopAnimeData(_type) ?? new List<TopAnimeData>());
            if (output.Count > 0)
            {
                Remember(key, output);
                return output;
            }

            output = await FetchFromTenraiAsync();
            if (output != null && output.Count > 0)
            {
                MergeWithPreviousPage(output);
                // Awaited: these used to be fire and forget, so switching subcategory quickly could let
                // the previous list's write land last and overwrite this one's file with the wrong rows.
                // That is the "Top anime shows the wrong list" report.
                if (_isManga)
                    await DataCache.SaveTopMangaData(output, _mangaType);
                else
                    await DataCache.SaveTopAnimeData(output, _type);
                Remember(key, output);
                return output;
            }

            return new List<TopAnimeData>();
        }

        private string AnimeKey() => "anime:" + _type + ":" + _rangeIndex + ":" + ContentFilter.CacheTag;

        private string MangaKey() => "manga:" + _mangaType + ":" + _rangeIndex + ":" + ContentFilter.CacheTag;

        private static void Remember(string key, List<TopAnimeData> data)
            => _prevQueriesCache[key] = (data, DateTime.UtcNow);

        /// <summary>
        ///     The remembered list, if it is still inside the same fourteen days the file it mirrors.
        /// </summary>
        /// <remarks>
        ///     A cached entry that holds no list counts as absent. It used to be reported as found with
        ///     a null payload, and every caller that merged pages fed that null straight into
        ///     Enumerable.Concat, which throws. The symptom was the whole panel reporting "could not
        ///     load this list" partway down a ranking that had already loaded fine up to that point.
        /// </remarks>
        private static bool TryGetFresh(string key, out List<TopAnimeData> data)
        {
            data = null;
            if (key == null || !_prevQueriesCache.TryGetValue(key, out var entry))
                return false;
            if (DateTime.UtcNow - entry.fetchedAt >= TopEntryLifetime)
                return false;
            if (entry.data == null)
                return false;
            data = entry.data;
            return true;
        }

        /// <summary>
        ///     How long a Top list stays valid. Matches the fourteen days the cache file asks for.
        /// </summary>
        private static readonly TimeSpan TopEntryLifetime = TimeSpan.FromDays(14);

        /// <summary>
        ///     Rows the source returns per page of a top list when no bigger page is asked for.
        /// </summary>
        /// <remarks>
        ///     Measured, not assumed. It used to be written as fifty, which is the old HTML scraper's
        ///     page size, so a page of twenty five was numbered as if it were fifty and every card past
        ///     the first page sat fifty places too high. It is read only for documentation now: the
        ///     numbering comes from the ranking itself and nothing here divides by a page any more.
        /// </remarks>
        private const int ItemsPerPage = 25;

        private async Task<List<TopAnimeData>> FetchFromTenraiAsync()
        {
            try
            {
                var api = _isManga ? "top/manga" : "top/anime";
                var query = _isManga ? GetMangaApiQuery(_mangaType) : GetAnimeApiQuery(_type);
                if (string.IsNullOrEmpty(query))
                    return null;

                if (_rangeIndex >= 0)
                    return await FetchRangeAsync(api, query);

                // No limit here on purpose. The filtered tabs page twenty five at a time and the
                // grid's own page maths is built on that, so asking for fifty would quietly double
                // how far those tabs reach. Only a range asks for the bigger page.
                var endpoint = $"{api}?{query}&page={Math.Max(_page + 1, 1)}";
                return await ReadTopPageAsync(endpoint);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        ///     Reads one range of the ranking as a single answer.
        /// </summary>
        /// <remarks>
        ///     A range is fetched whole rather than page by page, because a range is the unit the reader
        ///     asks for: two hundred and fifty ranks in one go, arriving as one batch for the grid. That
        ///     is what removes the twenty five row stutter that paging produced, where the grid grew one
        ///     small batch at a time while the reader was still dragging.
        ///     <para>
        ///     The pages are asked for with a margin on both sides because a page is not a clean slice
        ///     of ranks. One page short of any page failing is treated as the whole read failing: a range
        ///     saved with a page missing would look complete to whoever opened it next, and the gap would
        ///     only show up as rows that are mysteriously absent.
        ///     </para>
        /// </remarks>
        private async Task<List<TopAnimeData>> FetchRangeAsync(string api, string query)
        {
            var (firstRank, lastRank) = RangeRanks;
            var pageCount = RangeSize / SourcePageSize + RangePageMargin;
            var firstPage = Math.Max(1, (firstRank - 1) / SourcePageSize);
            var watch = System.Diagnostics.Stopwatch.StartNew();

            // Fired together rather than one after another. The pages were read in a line and that is
            // what made changing band take over ten seconds: measured on a device, each page cost
            // about one and a half seconds wall clock, and the throttle only releases one request per
            // 300ms however long they run, so the wait does not overlap. Sending them all and letting
            // the client's own rate limiter deal them out keeps that spacing without paying it
            // serially: seven pages that took ten seconds are a couple of rounds of it.
            // <para>
            // This is safe from the throttling that spacing is there for. Nothing here asks for more
            // requests per second, it asks for them to be queued instead of waited on in turn, and
            // the source still sees one leaving every 300ms. A read that fails is still fatal to the
            // band below, so the behaviour on a dead page has not changed.
            // </para>
            var reads = new List<Task<List<TopAnimeData>>>(pageCount);
            for (var offset = 0; offset < pageCount; offset++)
            {
                var page = firstPage + offset;
                reads.Add(ReadTopPageAsync($"{api}?{query}&limit={SourcePageSize}&page={page}"));
            }

            var pages = await Task.WhenAll(reads);

            // A refused page gets another go, one at a time and with a pause in between. Sending the
            // seven together is what makes them fast, and it is also what gets some of them refused:
            // measured, seven requests leaving at once produce one or two 429s even with the client's
            // own spacing between them, because tenrai's limit is tighter than 300ms. The client only
            // retries a refusal twice with a cap of two seconds, and a page that exhausts that comes
            // back as nothing, which used to kill the whole band: top manga 1-250 read as "could not
            // load" while 251-500, asked for a moment later, read perfectly.
            // <para>
            // A second chance is taken here rather than in the client because the client's patience is
            // deliberately short everywhere else, so a throttled detail page never parks the panel it
            // belongs to. A band is one big read the reader is already waiting on, and a dead band is
            // a dead screen, so it is the one place that can afford to wait.
            // </para>
            for (var attempt = 1; attempt <= RangePageRetries; attempt++)
            {
                var refused = Enumerable.Range(0, pages.Length)
                    .Where(index => pages[index] == null || pages[index].Count == 0)
                    .ToList();
                if (refused.Count == 0)
                    break;

                await Task.Delay(TimeSpan.FromSeconds(attempt));
                foreach (var index in refused)
                {
                    var page = firstPage + index;
                    pages[index] = await ReadTopPageAsync($"{api}?{query}&limit={SourcePageSize}&page={page}");
                    Console.WriteLine(
                        $"MALPLUS Top {api} range={_rangeIndex} page={page} retry={attempt} rows={pages[index]?.Count ?? -1} total={watch.ElapsedMilliseconds}");
                }
            }

            var byId = new Dictionary<int, TopAnimeData>();
            for (var index = 0; index < pages.Length; index++)
            {
                var rows = pages[index];
                Console.WriteLine(
                    $"MALPLUS Top {api} range={_rangeIndex} page={firstPage + index} rows={rows?.Count ?? -1} total={watch.ElapsedMilliseconds}");
                if (rows == null || rows.Count == 0)
                    return null;
                foreach (var row in rows)
                    if (row.Index >= firstRank && row.Index <= lastRank)
                        byId[row.Id] = row;
            }

            Console.WriteLine(
                $"MALPLUS Top {api} range={_rangeIndex} done cards={byId.Count} ranks={firstRank}-{lastRank} ms={watch.ElapsedMilliseconds}");
            return byId.Count > 0 ? byId.Values.OrderBy(row => row.Index).ToList() : null;
        }

        private async Task<List<TopAnimeData>> ReadTopPageAsync(string endpoint)
        {
            try
            {
                // The array reader, NOT GetDataAsync. A top list is one of the endpoints whose "data" is
                // an array, and the object reader rejects those on purpose because it applies a
                // truncation guard that only makes sense on an object. Calling it here threw every
                // time, was swallowed by the catch below, returned null, and every Top panel fell
                // through to the HTML scraper for nothing: measured 0 rows with the source answering
                // 25 rows perfectly well.
                var data = await TenraiClient.GetDataArrayAsync(endpoint);
                JsonElement items;
                if (data.ValueKind == JsonValueKind.Array)
                    items = data;
                else if (!data.TryGetProperty("data", out items) || items.ValueKind != JsonValueKind.Array)
                    return null;

                var output = new List<TopAnimeData>();
                foreach (var item in items.EnumerateArray())
                {
                    try
                    {
                        if (item.ValueKind != JsonValueKind.Object)
                            continue;
                        var malId = item.TryGetProperty("mal_id", out var idProp) && idProp.ValueKind == JsonValueKind.Number
                            ? idProp.GetInt32()
                            : 0;
                        if (malId <= 0 && !item.TryGetProperty("title", out _))
                            continue;

                        var current = new TopAnimeData();
                        current.Id = malId;
                        current.Title = WebUtility.HtmlDecode(
                            item.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String
                                ? titleProp.GetString()
                                : "");

                        if (item.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Object &&
                            images.TryGetProperty("jpg", out var jpg) && jpg.ValueKind == JsonValueKind.Object &&
                            jpg.TryGetProperty("image_url", out var imgProp) && imgProp.ValueKind == JsonValueKind.String)
                        {
                            current.ImgUrl = NormalizeImageUrl(imgProp.GetString());
                        }

                        if (item.TryGetProperty("score", out var scoreProp) && scoreProp.ValueKind == JsonValueKind.Number)
                            current.Score = (float)scoreProp.GetDouble();
                        else
                            current.Score = 0;

                        // Null is the normal value here for anything still airing or not yet started, which is
                        // most of the Airing and Upcoming lists. Left unset the card renders an empty
                        // episode count; "?" reads as "not known yet" and does not look broken.
                        current.Episodes = "0";
                        if (item.TryGetProperty("episodes", out var epsProp))
                        {
                            if (epsProp.ValueKind == JsonValueKind.Number)
                                current.Episodes = epsProp.GetInt32().ToString();
                            else if (epsProp.ValueKind != JsonValueKind.Null && epsProp.ValueKind != JsonValueKind.Undefined)
                                current.Episodes = WebUtility.HtmlDecode(epsProp.ToString());
                        }

                        if (item.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
                            current.Type = _isManga
                                ? (int) MalTypeParser.ParseMangaType(typeProp.GetString())
                                : (int) MalTypeParser.ParseAnimeType(typeProp.GetString());

                        // Dropped here only when the source did not do it for us. In the strictest mode
                        // the filter travels in the query and nothing reaches this point that should
                        // not. In the two looser modes the same page is asked for without it, and the
                        // entry's own rating is the only thing left to judge it by. Doing it before
                        // anything is built on top of the row keeps a refused entry out of the cache
                        // and out of the band, not merely off the screen.
                        if (!ContentFilter.UsesSourceFilter &&
                            item.TryGetProperty("rating", out var ratingProp) &&
                            ContentFilter.IsHiddenBySetting(ratingProp.GetString()))
                            continue;

                        // The chip is the entry's own place in MAL's global ranking, on every tab
                        // that has one, and a row that cannot be placed is only dropped where the
                        // place IS the question.
                        // <para>
                        // Two different rules, and mixing them up is what broke Upcoming. A band is
                        // defined by rank, so a row without one is not in it and has to go. A FILTERED
                        // list is not defined by rank at all: Upcoming is what has not aired yet, and
                        // MAL does not rank anything until it has aired, so every single row of that
                        // list comes back with no rank at all. Measured: twenty five rows, zero of them
                        // ranked. Treating those rows as unrankable emptied the tab completely, and it
                        // had been doing so before this change as well, since the old numbering branch
                        // dropped them for the filtered tabs too.
                        // </para>
                        // <para>
                        // So here the rank is read if it is there and the row is kept either way. The
                        // card decides whether to show a chip from the rank itself, which is why an
                        // entry keeps its place in a filtered list with an honest blank chip instead of
                        // being given a made-up number. The unfiltered list used to invent numbers for
                        // unranked rows to keep the sequence unbroken, which is how "Idol" and "Alien
                        // Stage" ended up in the top: both arrive with no rank and were handed the next
                        // number. That run was never a run.
                        // </para>
                        if (item.TryGetProperty("rank", out var rankProp) &&
                            rankProp.ValueKind == JsonValueKind.Number &&
                            rankProp.GetInt32() > 0)
                            current.Index = rankProp.GetInt32();
                        else if (_rangeIndex >= 0)
                            continue;

                        output.Add(current);
                    }
                    catch (Exception)
                    {
                        // skip malformed entry
                    }
                }

                return output.Count > 0 ? output : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void MergeWithPreviousPage(List<TopAnimeData> output)
        {
            if (_page == 0)
                return;
            // Same reason as the other merge: an absent key was never an error, it is just a first
            // page asked for on its own, and treating it as one threw out the whole list.
            if (!TryGetFresh(_isManga ? MangaKey() : AnimeKey(), out var previous) ||
                previous == null || previous.Count == 0 || output == null)
                return;
            output.InsertRange(0, previous);
            var deduped = output
                .GroupBy(item => item.Id)
                .Select(group => group.First())
                .ToList();
            output.Clear();
            output.AddRange(deduped);
        }

        private static string GetAnimeApiQuery(TopAnimeType type)
        {
            switch (type)
            {
                case TopAnimeType.General:
                    return ContentFilter.Query("");
                case TopAnimeType.Airing:
                    return ContentFilter.Query("filter=airing");
                case TopAnimeType.Upcoming:
                    return ContentFilter.Query("filter=upcoming");
                case TopAnimeType.Tv:
                    return ContentFilter.Query("type=tv");
                case TopAnimeType.Movies:
                    return ContentFilter.Query("type=movie");
                case TopAnimeType.Ovas:
                    return ContentFilter.Query("type=ova");
                case TopAnimeType.Popular:
                    return ContentFilter.Query("filter=bypopularity");
                case TopAnimeType.Favourited:
                    return ContentFilter.Query("filter=favorite");
                default:
                    return null;
            }
        }

        private static string GetMangaApiQuery(MangaTopType type)
        {
            switch (type)
            {
                case MangaTopType.All:
                    return ContentFilter.Query("");
                case MangaTopType.Manga:
                    return ContentFilter.Query("type=manga");
                case MangaTopType.Novels:
                    return ContentFilter.Query("type=novel");
                case MangaTopType.LightNovels:
                    return ContentFilter.Query("type=lightnovel");
                case MangaTopType.OneShots:
                    return ContentFilter.Query("type=oneshot");
                case MangaTopType.Doujinshi:
                    return ContentFilter.Query("type=doujin");
                case MangaTopType.Manhwa:
                    return ContentFilter.Query("type=manhwa");
                case MangaTopType.Manhua:
                    return ContentFilter.Query("type=manhua");
                case MangaTopType.Popular:
                    return "filter=bypopularity&ContentFilter.Query(";
                case MangaTopType.Favourited:
                    return "filter=favorite&ContentFilter.Query(";
                default:
                    return null;
            }
        }

        private static string NormalizeImageUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            url = Regex.Replace(url, @"\/r\/\d+x\d+\/", "/");
            var qPos = url.IndexOf('?');
            if (qPos > 0) url = url.Substring(0, qPos);
            var dotPos = url.LastIndexOf('.');
            if (dotPos > 0)
            {
                var beforeDot = url.Substring(0, dotPos);
                var lastChar = beforeDot[beforeDot.Length - 1];
                if (lastChar != 'l' && lastChar != 'm' && lastChar != 's')
                    url = beforeDot + "l" + url.Substring(dotPos);
            }
            return url;
        }
    }
}