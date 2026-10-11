using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MALPlus.XShared.Comm
{
    public static class TenraiClient
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        /// <summary>
        ///     How many requests may be in flight at once.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     This used to be a semaphore of ONE, on the assumption that every endpoint is a single
        ///     request. Paginated ones are not: a season is eight or nine requests, and one permit plus
        ///     the spacing below turned that into a queue. Measured on a device against a source that
        ///     answers a page in about 300ms, Spring 2026 took 18 seconds because each page waited on
        ///     the clock behind the one before it.
        ///     </para>
        ///     <para>
        ///     Three is enough to overlap a season's pages without looking like a flood: the spacing
        ///     below is still enforced between them, so the source sees at most one request every
        ///     RequestSpacingMs even while several are queued behind it.
        ///     </para>
        /// </remarks>
        /// <remarks>
        ///     <para>
        ///     Measured against the live source (2026-10-08), this is the value that matters, not the
        ///     concurrency: at 150 ms apart the source answered 429 twice in twelve requests, at
        ///     300 ms it answered 429 zero times, and 500 ms was clean but wasted half the time.
        ///     </para>
        ///     <para>
        ///     Tenrai is NOT unlimited, whatever the announcement says. Twenty requests fired back to
        ///     back came back twelve times with 429. Concurrency is therefore what it is because
        ///     parallel requests are what earn the 429, and the spacing below is what keeps the
        ///     client under the line.
        ///     </para>
        /// </remarks>
        private static readonly SemaphoreSlim RateLimiter = new SemaphoreSlim(3, 3);
        private static DateTime _lastRequest = DateTime.MinValue;

        /// <summary>
        ///     Minimum gap between two requests leaving the client.
        /// </summary>
        /// <remarks>
        ///     Three hundred milliseconds, measured rather than guessed: it is the gap at which the
        ///     source stopped answering 429. It was 120 ms, which is a request every hundred and twenty
        ///     milliseconds, about eight a second, and that is precisely what the source refuses.
        /// </remarks>
        private const int RequestSpacingMs = 300;

        /// <summary>
        ///     How many times one request is retried before the panel gives up on it.
        /// </summary>
        /// <remarks>
        ///     Two, and the delays are one and two seconds. It used to be four attempts with a two
        ///     seconds base, so a throttled page waited 2 + 4 + 8 = fourteen seconds before the list
        ///     reported anything, and that wait is the twenty second freeze the panel showed. A page
        ///     that cannot be had is better served by the rows already on screen than by holding the
        ///     whole panel hostage while it is retried.
        /// </remarks>
        private const int MaxAttempts = 2;

        private static readonly string[] BaseUrls =
        {
            "https://api.tenrai.org/v1"
        };

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly Dictionary<string, (JsonElement data, DateTime fetchedAt)> _dataCache = new Dictionary<string, (JsonElement, DateTime)>();
        private static readonly ConcurrentDictionary<string, Task<JsonElement>> _inFlight = new();
        private static readonly object _cacheLock = new object();
        private const int DataCacheTtlMinutes = 30;

        static TenraiClient()
        {
            Client.DefaultRequestHeaders.Add("User-Agent", "MalPlus/3.0");
        }

        private static Task<string> GetStringAsync(string endpoint)
        {
            return GetStringAsync(endpoint, CancellationToken.None);
        }

        private static readonly SemaphoreSlim _spacingGate = new SemaphoreSlim(1, 1);

        /// <summary>
        ///     Leaves at most one request per <see cref="RequestSpacingMs" />.
        /// </summary>
        /// <remarks>
        ///     Separate from the concurrency permit on purpose. The permit bounds how much is in
        ///     flight; this bounds how fast they are issued, and it has to be a second lock, otherwise
        ///     three permits would each read the same "time since last request" and all three would
        ///     decide they were first.
        /// </remarks>
        private static async Task ThrottleAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                await _spacingGate.WaitAsync(cancellationToken);
                try
                {
                    var wait = RequestSpacingMs - (DateTime.UtcNow - _lastRequest).TotalMilliseconds;
                    if (wait > 0)
                        await Task.Delay((int)wait, cancellationToken);
                    _lastRequest = DateTime.UtcNow;
                    return;
                }
                finally
                {
                    _spacingGate.Release();
                }
            }
        }

        private static async Task<string> GetStringCoreAsync(string url, CancellationToken cancellationToken = default)
        {
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                HttpResponseMessage response;
                await RateLimiter.WaitAsync(cancellationToken);
                try
                {
                    // The gap is taken under the permit, so with several permits in flight two
                    // requests could still leave together. A second gate outside it is what keeps the
                    // promise to the source: at most one request leaves per RequestSpacingMs, however
                    // many are waiting to go.
                    await ThrottleAsync(cancellationToken);

                    response = await Client.GetAsync(url, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (HttpRequestException)
                {
                    if (attempt >= MaxAttempts)
                        throw;
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                    continue;
                }
                finally
                {
                    RateLimiter.Release();
                }

                using (response)
                {
                    var status = (int)response.StatusCode;

                    if (status == 429)
                    {
                        // Capped hard, and it was not fifteen seconds for no reason. A throttled
                        // response used to park the panel for up to fourteen seconds of backoff,
                        // which read as the app hanging rather than as a refused page. The header
                        // is still honoured, but only up to two seconds; past that the caller is
                        // better served by whatever it already has than by a silent wait.
                        var retryAfterSeconds = response.Headers.RetryAfter?.Delta?.TotalSeconds;
                        var delay = Math.Min(retryAfterSeconds ?? 1.0 * attempt, 2.0);
                        if (attempt < MaxAttempts)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                            continue;
                        }
                        throw new HttpRequestException("Tenrai rate limit exceeded (429).");
                    }

                    if (status >= 500)
                    {
                        if (attempt < MaxAttempts)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                            continue;
                        }
                        throw new HttpRequestException($"Tenrai server error {(int)response.StatusCode}.");
                    }

                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException($"Tenrai request failed: {(int)response.StatusCode}");

                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }

            throw new HttpRequestException("Unexpected retry exhaustion.");
        }

        public static Task<string> GetRawJsonAsync(string endpoint)
        {
            return GetStringAsync(endpoint);
        }

        public static async Task<JsonElement> GetDataAsync(string endpoint)
        {
            return await GetDataSingleFlightAsync(endpoint, null);
        }

        public static async Task<JsonElement> GetDataAsync(string endpoint, TimeSpan timeout)
        {
            return await GetDataSingleFlightAsync(endpoint, timeout);
        }

        /// <summary>
        /// Same contract as <see cref="GetDataAsync"/> but for endpoints whose "data" is an
        /// array (/recommendations, /characters, /staff). GetDataAsync rejects arrays on
        /// purpose so it can apply its truncated-payload guard, which meant every array
        /// endpoint silently threw and fell through to the HTML scraper.
        /// </summary>
        public static Task<JsonElement> GetDataArrayAsync(string endpoint)
        {
            return GetDataArraySingleFlightAsync(endpoint);
        }

        private static async Task<JsonElement> GetDataArraySingleFlightAsync(string endpoint)
        {
            lock (_cacheLock)
            {
                if (_dataCache.TryGetValue(endpoint, out var cached) && DateTime.UtcNow - cached.fetchedAt < TimeSpan.FromMinutes(DataCacheTtlMinutes))
                    return cached.data.Clone();
            }

            if (_inFlight.TryGetValue(endpoint, out var existing))
                return (await existing).Clone();

            var task = GetDataArrayCoreAsync(endpoint);
            if (!_inFlight.TryAdd(endpoint, task))
                return (await _inFlight[endpoint]).Clone();

            try
            {
                var result = await task;
                return result.Clone();
            }
            finally
            {
                _inFlight.TryRemove(endpoint, out _);
            }
        }

        private static async Task<JsonElement> GetDataArrayCoreAsync(string endpoint)
        {
            var json = await GetStringAsync(endpoint);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                root.TryGetProperty("status", out _) ||
                root.TryGetProperty("error", out _))
                throw new InvalidOperationException(
                    $"Tenrai returned no data for '{endpoint}': {json.Substring(0, System.Math.Min(160, json.Length))}");

            var result = root.GetProperty("data").Clone();
            if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
                throw new InvalidOperationException($"Tenrai payload looks empty for '{endpoint}'");

            lock (_cacheLock) _dataCache[endpoint] = (result.Clone(), DateTime.UtcNow);
            return result;
        }

        private static async Task<JsonElement> GetDataSingleFlightAsync(string endpoint, TimeSpan? timeout)
        {
            lock (_cacheLock)
            {
                if (_dataCache.TryGetValue(endpoint, out var cached) && DateTime.UtcNow - cached.fetchedAt < TimeSpan.FromMinutes(DataCacheTtlMinutes))
                    return cached.data.Clone();
            }

            if (_inFlight.TryGetValue(endpoint, out var existing))
                return (await existing).Clone();

            var task = GetDataCoreAsync(endpoint, timeout);
            if (!_inFlight.TryAdd(endpoint, task))
                return (await _inFlight[endpoint]).Clone();

            try
            {
                var result = await task;
                return result.Clone();
            }
            finally
            {
                _inFlight.TryRemove(endpoint, out _);
            }
        }

        private static async Task<JsonElement> GetDataCoreAsync(string endpoint, TimeSpan? timeout)
        {
            var json = timeout.HasValue ? await GetStringAsync(endpoint, timeout.Value) : await GetStringAsync(endpoint);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Tenrai answers failures with an error envelope instead of a data object. Caching one
            // of those (or a truncated body) poisons every reader for the whole TTL, which showed up
            // as character/staff pages randomly missing their about text and credit lists.
            if (root.ValueKind != JsonValueKind.Object ||
                root.TryGetProperty("status", out _) ||
                root.TryGetProperty("error", out _))
                throw new InvalidOperationException(
                    $"Tenrai returned no data for '{endpoint}': {json.Substring(0, System.Math.Min(160, json.Length))}");

            var result = root.GetProperty("data").Clone();
            if (result.ValueKind != JsonValueKind.Object || CountProperties(result) < 3)
                throw new InvalidOperationException($"Tenrai payload looks truncated for '{endpoint}'");

            lock (_cacheLock) _dataCache[endpoint] = (result.Clone(), DateTime.UtcNow);
            return result;
        }

        private static int CountProperties(JsonElement element)
        {
            var count = 0;
            foreach (var _ in element.EnumerateObject())
                if (++count >= 3)
                    break;
            return count;
        }

        private static async Task<string> GetStringAsync(string endpoint, CancellationToken cancellationToken)
        {
            Exception lastError = null;
            foreach (var baseUrl in BaseUrls)
            {
                try
                {
                    return await GetStringCoreAsync($"{baseUrl}/{endpoint}", cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (HttpRequestException e)
                {
                    lastError = e;
                }
                catch (TaskCanceledException e)
                {
                    lastError = e;
                }
            }

            throw lastError ?? new HttpRequestException("All Tenrai mirrors failed.");
        }

        private static async Task<string> GetStringAsync(string endpoint, TimeSpan timeout)
        {
            var client = new HttpClient { Timeout = timeout };
            client.DefaultRequestHeaders.Add("User-Agent", "MalPlus/3.0");
            try
            {
                await RateLimiter.WaitAsync();
                try
                {
                    var sinceLast = DateTime.UtcNow - _lastRequest;
                    if (sinceLast.TotalMilliseconds < RequestSpacingMs)
                        await Task.Delay(RequestSpacingMs - (int)sinceLast.TotalMilliseconds);

                    _lastRequest = DateTime.UtcNow;
                    using (var response = await client.GetAsync($"{BaseUrls[0]}/{endpoint}"))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException($"Tenrai request failed: {(int)response.StatusCode}");
                        return await response.Content.ReadAsStringAsync();
                    }
                }
                finally
                {
                    RateLimiter.Release();
                }
            }
            finally
            {
                client.Dispose();
            }
        }

        private static readonly Dictionary<string, (List<JsonElement> items, bool hasNext, DateTime fetchedAt)> _paginatedCache = new Dictionary<string, (List<JsonElement>, bool, DateTime)>();
        public static async Task<(List<JsonElement> Items, bool HasNextPage)> GetPaginatedAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            lock (_cacheLock)
            {
                if (_paginatedCache.TryGetValue(endpoint, out var cached) && DateTime.UtcNow - cached.fetchedAt < TimeSpan.FromMinutes(5))
                    return (cached.items.Select(e => e.Clone()).ToList(), cached.hasNext);
            }
            var json = await GetStringAsync(endpoint, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var data = root.GetProperty("data");
            var items = new List<JsonElement>();
            foreach (var item in data.EnumerateArray())
                items.Add(item.Clone());

            var hasNext = false;
            if (root.TryGetProperty("pagination", out var pagination))
                hasNext = pagination.GetProperty("has_next_page").GetBoolean();

            lock (_cacheLock) _paginatedCache[endpoint] = (items.Select(e => e.Clone()).ToList(), hasNext, DateTime.UtcNow);
            return (items, hasNext);
        }

        public static async Task<List<JsonElement>> GetAllPagesAsync(Func<int, string> endpointForPage, int maxPages = 10)
        {
            var allItems = new List<JsonElement>();
            for (int page = 1; page <= maxPages; page++)
            {
                var (items, hasNext) = await GetPaginatedAsync(endpointForPage(page));
                allItems.AddRange(items);
                if (!hasNext) break;
            }
            return allItems;
        }

        /// <summary>
        ///     Every page of a paginated endpoint, fetched in parallel once the first one has said how
        ///     many there are.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     The sequential loop this replaces cost one round trip PER PAGE, which for a busy season
        ///     meant eight or nine of them in a row while the grid waited: measured 27 seconds for
        ///     Spring 2026. It also multiplied badly, because a page that failed walked the mirror list
        ///     one timeout at a time, so one bad mirror cost every page the full budget instead of one.
        ///     </para>
        ///     <para>
        ///     The first page is still fetched on its own, because it is what says whether there is a
        ///     second page at all. The rest go out together and in page order, so the result is
        ///     identical to reading them in sequence.
        ///     </para>
        /// </remarks>
        public static async Task<List<JsonElement>> GetAllPagesParallelAsync(
            Func<int, string> endpointForPage, int firstPage, int lastPage, CancellationToken cancellationToken = default)
        {
            if (lastPage <= firstPage)
                return new List<JsonElement>();

            var remaining = new List<Task<List<JsonElement>>>();
            for (var page = firstPage; page <= lastPage; page++)
            {
                var captured = page;
                remaining.Add(GetPageOrEmptyAsync(endpointForPage(captured), cancellationToken));
            }

            var fetched = await Task.WhenAll(remaining);

            var all = new List<JsonElement>();
            foreach (var page in fetched)
                all.AddRange(page);
            return all;
        }

        /// <summary>
        ///     A page, or nothing.
        /// </summary>
        /// <remarks>
        ///     Never throws. A page that fails has to cost that page and nothing else: the caller
        ///     fetched the first page separately and would otherwise throw away a season it had
        ///     already paid for, because one page out of eight timed out. The page holes up instead,
        ///     which is a visibly short list rather than an empty screen.
        /// </remarks>
        private static async Task<List<JsonElement>> GetPageOrEmptyAsync(string endpoint, CancellationToken cancellationToken)
        {
            try
            {
                var (items, _) = await GetPaginatedAsync(endpoint, cancellationToken);
                return items;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS Tenrai page failed for " + endpoint + ": " + ex.GetType().Name);
                return new List<JsonElement>();
            }
        }
    }
}
