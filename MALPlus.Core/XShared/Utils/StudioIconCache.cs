using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using MALClient.XShared.Comm;

namespace MALClient.XShared.Utils
{
    /// <summary>
    ///     Studio logos for the search picker.
    /// </summary>
    /// <remarks>
    ///     MAL does not return these anywhere, not on the anime payload and not on the producer
    ///     page. Tenrai does: /v1/producers/{mal_id} carries images.jpg.image_url, which is MAL's
    ///     own company logo, and the AnimeStudios enum already stores every studio's MAL id as its
    ///     value, so nothing has to be mapped by hand.
    ///
    ///     Loaded on demand, one studio at a time, the moment a row is realised, and then cached to
    ///     disk. Preloading would mean 25 pages of /v1/producers to cover all 618 studios before
    ///     the tab showed anything, which is a long wait to look at a grid the user only scrolled
    ///     to once.
    ///
    ///     Concurrent requests for the same studio are collapsed, and a studio with no logo keeps
    ///     an empty URL rather than being retried forever: plenty of small studios have none, and
    ///     hammering the endpoint for them would be the worst version of this.
    /// </remarks>
    public static class StudioIconCache
    {
        private static readonly ConcurrentDictionary<int, string> _resolved = new();
        private static readonly ConcurrentDictionary<int, Task<string>> _inFlight = new();
        private static readonly HashSet<int> _noLogo = new();
        private static readonly object _noLogoLock = new();

        /// <summary>
        ///     Same base directory the AnimeThemes cache already uses, so there is one place to look
        ///     for on disk caches rather than two conventions side by side.
        /// </summary>
        private static string CacheDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StudioIcons");

        /// <summary>
        ///     Raised when a studio's logo has been resolved, so the rows already on screen can fill
        ///     themselves in. There is no way to push it to them otherwise: the fetch starts inside
        ///     a bind and finishes long after it.
        /// </summary>
        public static event Action<int, string> IconResolved;

        /// <summary>
        ///     The logo for a studio, or an empty string while it is still being fetched or when
        ///     there is none.
        ///     <para>
        ///     Reading this is also what STARTS the fetch, which is what makes it on demand without
        ///     anyone having to know which rows exist: only the rows a CollectionView actually
        ///     realises get bound, so a studio the user never scrolled to is never asked for.
        ///     </para>
        /// </summary>
        public static string GetIconUrl(int malId)
        {
            if (malId <= 0)
                return "";
            if (_resolved.TryGetValue(malId, out var known))
                return known;
            lock (_noLogoLock)
                if (_noLogo.Contains(malId))
                    return "";
            Request(malId);
            return "";
        }

        /// <summary>
        ///     Starts a fetch unless one is already running or the studio is known to have no logo.
        ///     Fire and forget by design: the caller is a bind, and the row updates itself when the
        ///     URL lands.
        /// </summary>
        public static void Request(int malId)
        {
            if (malId <= 0 || _resolved.ContainsKey(malId))
                return;
            lock (_noLogoLock)
                if (_noLogo.Contains(malId))
                    return;
            _inFlight.GetOrAdd(malId, id => FetchAsync(id));
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
                    var json = await TenraiClient.GetDataAsync($"producers/{malId}");
                    url = ReadLogo(json);
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
                // Remembered so a studio with no logo, or one that failed once, is not asked again
                // on every bind. A later visit can still get it from the network.
                lock (_noLogoLock)
                    _noLogo.Add(malId);
                return "";
            }

            _resolved[malId] = url;
            IconResolved?.Invoke(malId, url);
            return url;
        }

        private static string ReadLogo(JsonElement data)
        {
            if (data.ValueKind != JsonValueKind.Object)
                return "";
            if (data.TryGetProperty("data", out var wrapper) && wrapper.ValueKind == JsonValueKind.Object)
                data = wrapper;
            if (!data.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Object)
                return "";
            if (!images.TryGetProperty("jpg", out var jpg) || jpg.ValueKind != JsonValueKind.Object)
                return "";
            return jpg.TryGetProperty("image_url", out var url) && url.ValueKind == JsonValueKind.String
                ? url.GetString() ?? ""
                : "";
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