using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MALPlus.XShared.Comm;

namespace MALPlus.XShared.Utils
{
    /// <summary>
    ///     Studio logos for the search picker.
    /// </summary>
    /// <remarks>
    ///     MAL returns these nowhere: not on the anime payload, not on the producer page. Tenrai
    ///     does, at /v1/producers/{mal_id}, and those are MAL's own company logos. The
    ///     AnimeStudios enum already stores all 618 ids, so nothing needed mapping.
    ///
    ///     Two ways in, because asking per studio turns out to be the wrong shape. Fetching one at a
    ///     time costs a request for every row the user scrolls past, which is the thing to avoid. The
    ///     list endpoint returns 25 producers WITH their logo per page and Tenrai holds more than a
    ///     thousand, so paging blind is worse still, but the enum knows exactly which ids we care
    ///     about, which is what makes it bounded: WarmMap pages until all of them are accounted for
    ///     and then stops. That is around 25 small JSON responses, once, saved as one file.
    ///
    ///     So: WarmMap fills the map in the background, the per studio fetch stays as the fallback
    ///     for anything the warm missed, and the rows themselves get their images through the normal
    ///     image converter, which caches them to disk as they are displayed.
    /// </remarks>
    public static class StudioIconCache
    {
        private static readonly ConcurrentDictionary<int, string> _resolved = new();
        private static readonly ConcurrentDictionary<int, Task<string>> _inFlight = new();
        private static readonly HashSet<int> _noLogo = new();
        private static readonly object _gate = new();

        /// <summary>Producer id to logo URL, from disk or from a warm.</summary>
        private static Dictionary<int, string> _map = new();

        private static bool _mapLoaded;
        private static bool _warming;

        /// <summary>Tenrai caps a page at 25 and offers no total, so this is a sanity bound.</summary>
        private const int MaxWarmPages = 30;

        /// <summary>Raised once per studio as its URL is discovered.</summary>
        public static event Action<int, string> IconResolved;

        /// <summary>Raised when the bulk map arrives, so every visible row can catch up at once.</summary>
        public static event Action MapReady;

        private static string CacheDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StudioIcons");

        private static string MapFile => Path.Combine(CacheDirectory, "producers.json");

        /// <summary>
        ///     The logo for a studio, or an empty string while it is still being fetched or when
        ///     there is none. Reading this also starts a fetch, which is what makes it on demand for
        ///     anything the warm has not covered.
        /// </summary>
        public static string GetIconUrl(int malId)
        {
            if (malId <= 0)
                return "";
            if (_resolved.TryGetValue(malId, out var known))
                return known;
            lock (_gate)
            {
                if (_mapLoaded && _map.TryGetValue(malId, out var mapped))
                {
                    _resolved[malId] = mapped;
                    return mapped;
                }
                if (_noLogo.Contains(malId))
                    return "";
            }
            Request(malId);
            return "";
        }

        public static void Request(int malId)
        {
            if (malId <= 0 || _resolved.ContainsKey(malId))
                return;
            lock (_gate)
                if (_noLogo.Contains(malId))
                    return;
            _inFlight.GetOrAdd(malId, id => FetchAsync(id));
        }

        /// <summary>
        ///     Fills the map in the background, bounded by the ids we actually want. Fire and
        ///     forget: nothing here may block the Studios tab, and a failure only costs the per
        ///     studio fallback.
        /// </summary>
        public static void WarmMap(IEnumerable<int> wantedIds)
        {
            var wanted = new HashSet<int>(wantedIds.Where(id => id > 0));
            if (wanted.Count == 0)
                return;
            lock (_gate)
            {
                if (_warming || (_mapLoaded && wanted.All(_map.ContainsKey)))
                    return;
                _warming = true;
            }

            Task.Run(async () =>
            {
                try
                {
                    var map = LoadMap();
                    var pending = new HashSet<int>(wanted.Where(id => !map.ContainsKey(id)));
                    for (var page = 1; page <= MaxWarmPages && pending.Count > 0; page++)
                    {
                        JsonElement items;
                        try
                        {
                            items = await TenraiClient.GetDataArrayAsync($"producers?page={page}");
                        }
                        catch (Exception)
                        {
                            // A page failing must not abandon the ones that already worked, and must
                            // not spin retrying the same page either.
                            break;
                        }
                        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
                            break;

                        foreach (var item in items.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.Object)
                                continue;
                            if (!item.TryGetProperty("mal_id", out var idProp) ||
                                idProp.ValueKind != JsonValueKind.Number)
                                continue;
                            var id = idProp.GetInt32();
                            var url = ReadLogo(item);
                            if (id > 0 && !string.IsNullOrEmpty(url))
                                map[id] = url;
                            pending.Remove(id);
                        }
                        SaveMap(map);
                        // Paced so the warm does not run into the same rate limiting everything
                        // else on the screen is competing for.
                        await Task.Delay(200);
                    }

                    lock (_gate)
                    {
                        _map = map;
                        _mapLoaded = true;
                    }
                    MapReady?.Invoke();
                }
                catch (Exception)
                {
                    // Nothing to do: the per studio path still works.
                }
                finally
                {
                    lock (_gate)
                        _warming = false;
                }
            });
        }

        private static async Task<string> FetchAsync(int malId)
        {
            var url = "";
            try
            {
                var cached = CachedFile(malId);
                if (cached != null)
                {
                    url = cached;
                }
                else
                {
                    url = ReadLogo(await TenraiClient.GetDataAsync($"producers/{malId}"));
                    if (!string.IsNullOrEmpty(url))
                        WriteCachedFile(malId, url);
                }
            }
            catch (Exception)
            {
                url = "";
            }
            finally
            {
                _inFlight.TryRemove(malId, out _);
            }

            if (string.IsNullOrEmpty(url))
            {
                // Remembered so a studio with no logo is not asked again on every bind. Plenty of
                // small studios have none, and hammering the endpoint for them is the worst shape
                // this could take.
                lock (_gate)
                    _noLogo.Add(malId);
                return "";
            }

            _resolved[malId] = url;
            IconResolved?.Invoke(malId, url);
            return url;
        }

        private static string ReadLogo(JsonElement producer)
        {
            if (producer.ValueKind != JsonValueKind.Object)
                return "";
            if (!producer.TryGetProperty("images", out var images) ||
                images.ValueKind != JsonValueKind.Object)
                return "";
            if (!images.TryGetProperty("jpg", out var jpg) || jpg.ValueKind != JsonValueKind.Object)
                return "";
            return jpg.TryGetProperty("image_url", out var url) && url.ValueKind == JsonValueKind.String
                ? url.GetString() ?? ""
                : "";
        }

        private static Dictionary<int, string> LoadMap()
        {
            try
            {
                if (!File.Exists(MapFile))
                    return new Dictionary<int, string>();
                return JsonSerializer.Deserialize<Dictionary<int, string>>(File.ReadAllText(MapFile))
                       ?? new Dictionary<int, string>();
            }
            catch (Exception)
            {
                return new Dictionary<int, string>();
            }
        }

        private static void SaveMap(Dictionary<int, string> map)
        {
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                File.WriteAllText(MapFile, JsonSerializer.Serialize(map));
            }
            catch (Exception)
            {
            }
        }

        private static string CachedFile(int malId)
        {
            try
            {
                var file = Path.Combine(CacheDirectory, malId + ".txt");
                return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void WriteCachedFile(int malId, string url)
        {
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                File.WriteAllText(Path.Combine(CacheDirectory, malId + ".txt"), url);
            }
            catch (Exception)
            {
            }
        }
    }
}