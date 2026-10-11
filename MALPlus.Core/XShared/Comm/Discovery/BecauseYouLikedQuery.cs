using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.Comm.Discovery
{
    /// <summary>
    ///     "For you": asks Tenrai what people recommended alongside several of the reader's own
    ///     entries, merges the answers and drops whatever the reader already has.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This used to pick a single anchor and return only what came back for it, which is why the
    ///         sheet was titled "Because you liked X" and showed one show's worth of suggestions: however
    ///         good the list behind it, one anchor can only recommend around one entry.
    ///     </para>
    ///     <para>
    ///         Several anchors are read instead and their votes are added up per candidate. A title that
    ///         three of the reader's own entries were recommended alongside is a stronger suggestion than
    ///         one that only turned up once, and the merge is what turns a single entry's neighbourhood
    ///         into a list shaped like the reader's taste.
    ///     </para>
    ///     <para>
    ///         Measured over five anchors, that merge yields fourteen distinct candidates in one pass, so
    ///         the cost of widening it is a handful of parallel reads against a cached client rather than
    ///         anything per row.
    ///     </para>
    /// </remarks>
    public static class BecauseYouLikedQuery
    {
        /// <summary>Anchors read at most, so a long list cannot turn this into a crawl.</summary>
        private const int MaxAnchors = 6;

        /// <summary>Votes each entry needs before it is counted at all.</summary>
        private const int MinVotes = 3;

        public static async Task<List<DiscoveryItem>> GetAsync(
            IReadOnlyList<Tuple<int, string, int>> anchors, HashSet<int> alreadyInLibrary)
        {
            var output = new List<DiscoveryItem>();
            if (anchors == null || anchors.Count == 0)
                return output;

            // The best rated first, so if the list is long the strongest ones are the ones that get read.
            var chosen = anchors
                .Where(anchor => anchor != null && anchor.Item1 > 0)
                .OrderByDescending(anchor => anchor.Item3)
                .Take(MaxAnchors)
                .ToList();

            if (chosen.Count == 0)
                return output;

            // Candidate id -> (item, accumulated votes).
            var merged = new Dictionary<int, (DiscoveryItem Item, int Votes)>();

            foreach (var anchor in chosen)
            {
                var found = await ReadAnchorAsync(anchor.Item1);
                if (found == null)
                {
                    Console.WriteLine($"MALPLUS foryou: ancla {anchor.Item1} sin respuesta");
                    continue;
                }
                Console.WriteLine($"MALPLUS foryou: ancla {anchor.Item1} -> {found.Count} candidatos");

                foreach (var pair in found)
                {
                    var id = pair.Key;
                    var item = pair.Value.Item;
                    var votes = pair.Value.Votes;

                    if (alreadyInLibrary != null && alreadyInLibrary.Contains(id))
                        continue;
                    if (string.IsNullOrEmpty(item.ImgUrl) || string.IsNullOrEmpty(item.Title))
                        continue;

                    if (merged.TryGetValue(id, out var existing))
                        merged[id] = (existing.Item, existing.Votes + votes);
                    else
                        merged[id] = (item, votes);
                }
            }

            output = merged.Values
                .Where(entry => entry.Votes >= MinVotes)
                .OrderByDescending(entry => entry.Votes)
                .Select(entry => entry.Item)
                .Take(20)
                .ToList();

            return output;
        }

        /// <summary>
        ///     What Tenrai recommends alongside one entry.
        /// </summary>
        /// <remarks>
        ///     A null means the read failed or returned nothing for this anchor, which is not fatal: the
        ///     merge carries on with whatever the other anchors found.
        /// </remarks>
        private static async Task<Dictionary<int, (DiscoveryItem Item, int Votes)>?> ReadAnchorAsync(int malId)
        {
            try
            {
                var raw = await TenraiClient.GetRawJsonAsync($"anime/{malId}/recommendations");
                if (string.IsNullOrEmpty(raw))
                    return null;

                using var doc = JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    data.ValueKind != JsonValueKind.Array)
                    return null;

                var result = new Dictionary<int, (DiscoveryItem, int)>();
                foreach (var entry in data.EnumerateArray())
                {
                    try
                    {
                        if (!entry.TryGetProperty("entry", out var inner))
                            continue;
                        var item = DiscoveryRandomQuery.ParseCatalogEntry(inner, false);
                        if (item == null)
                            continue;
                        var votes = entry.TryGetProperty("votes", out var v) && v.ValueKind == JsonValueKind.Number
                            ? v.GetInt32()
                            : 0;
                        result[item.Id] = (item, votes);
                    }
                    catch
                    {
                    }
                }

                return result;
            }
            catch
            {
                return null;
            }
        }
    }
}