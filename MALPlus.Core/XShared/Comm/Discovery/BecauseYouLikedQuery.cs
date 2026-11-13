using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.Comm.Discovery
{
    public class RecommendationAnchor
    {
        public int MalId { get; set; }
        public string Title { get; set; }
        public int Score { get; set; }
        public int Weight { get; set; }
    }

    /// <summary>
    ///     "For you": asks Tenrai what people recommended alongside several of the reader's own
    ///     entries, merges the answers and drops whatever the reader already has.
    /// </summary>
    public static class BecauseYouLikedQuery
    {
        /// <summary>Anchors read at most in parallel to provide diversity without excess requests.</summary>
        private const int MaxAnchors = 6;

        public static async Task<List<DiscoveryItem>> GetAsync(
            IReadOnlyList<RecommendationAnchor> anchors, HashSet<int> alreadyInLibrary)
        {
            var output = new List<DiscoveryItem>();
            if (anchors == null || anchors.Count == 0)
                return output;

            var validAnchors = anchors
                .Where(a => a != null && a.MalId > 0 && !string.IsNullOrWhiteSpace(a.Title))
                .ToList();

            if (validAnchors.Count == 0)
                return output;

            var rng = new Random();

            // 1. Deduplicate by franchise so multiple seasons (e.g. S1 and S2) don't hog anchor slots
            var byFranchise = new Dictionary<string, RecommendationAnchor>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in validAnchors)
            {
                var key = GetFranchiseKey(a.Title);
                if (string.IsNullOrEmpty(key))
                    key = a.MalId.ToString();

                if (!byFranchise.TryGetValue(key, out var existing) || a.Weight > existing.Weight)
                {
                    byFranchise[key] = a;
                }
            }

            var uniqueFranchises = byFranchise.Values.ToList();

            // 2. Diversity & True Randomization:
            // Group by weight tier, shuffle COMPLETELY within each tier, and select up to MaxAnchors.
            // This prevents stable-sort from defaulting to alphabetical library order.
            var chosen = uniqueFranchises
                .GroupBy(a => a.Weight)
                .OrderByDescending(g => g.Key)
                .SelectMany(g => g.OrderBy(_ => rng.Next()))
                .Take(MaxAnchors)
                .ToList();

            Console.WriteLine($"MALPLUS foryou: consultando {chosen.Count} anclas distintas en paralelo: " +
                              string.Join(", ", chosen.Select(c => c.Title)));

            // Fetch recommendations in parallel
            var tasks = chosen.Select(async anchor =>
            {
                var recs = await ReadAnchorAsync(anchor.MalId);
                return (Anchor: anchor, Recs: recs);
            });

            var results = await Task.WhenAll(tasks);

            // Candidate id -> (Item, AccumulatedScore, Set of contributing anchor titles)
            var merged = new Dictionary<int, (DiscoveryItem Item, double Score, List<string> AnchorTitles)>();
            // Per-anchor candidate pools: Anchor -> List<(DiscoveryItem Item, int Votes)>
            var perAnchorPools = new Dictionary<RecommendationAnchor, List<(DiscoveryItem Item, int Votes)>>();

            foreach (var result in results)
            {
                var anchor = result.Anchor;
                var found = result.Recs;
                if (found == null || found.Count == 0)
                    continue;

                var anchorCandidates = new List<(DiscoveryItem Item, int Votes)>();

                foreach (var pair in found)
                {
                    var id = pair.Key;
                    var item = pair.Value.Item;
                    var votes = Math.Max(1, pair.Value.Votes);

                    if (alreadyInLibrary != null && alreadyInLibrary.Contains(id))
                        continue;
                    if (string.IsNullOrEmpty(item.ImgUrl) || string.IsNullOrEmpty(item.Title))
                        continue;

                    // Contribution: votes scaled by anchor weight
                    var contribution = votes * (anchor.Weight / 10.0);

                    if (merged.TryGetValue(id, out var existing))
                    {
                        var updatedScore = existing.Score + contribution;
                        if (!existing.AnchorTitles.Contains(anchor.Title))
                            existing.AnchorTitles.Add(anchor.Title);
                        merged[id] = (existing.Item, updatedScore, existing.AnchorTitles);
                    }
                    else
                    {
                        merged[id] = (item, contribution, new List<string> { anchor.Title });
                    }

                    anchorCandidates.Add((item, votes));
                }

                // Sort anchor candidates descending by votes so the highest-quality recs come first
                perAnchorPools[anchor] = anchorCandidates
                    .OrderByDescending(c => c.Votes)
                    .ToList();
            }

            var addedIds = new HashSet<int>();
            var seenFranchises = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Seed seenFranchises with chosen anchors themselves to prevent recommending sequels/prequels
            // of the anchor anime that the user already completed or is watching.
            foreach (var a in chosen)
            {
                var key = GetFranchiseKey(a.Title);
                if (!string.IsNullOrEmpty(key))
                    seenFranchises.Add(key);
            }

            // Step 1: Overlapping candidates (recommended by 2 or more distinct anchors)
            var overlaps = merged.Values
                .Where(entry => entry.AnchorTitles.Count > 1)
                .OrderByDescending(entry => entry.AnchorTitles.Count)
                .ThenByDescending(entry => entry.Score)
                .ToList();

            foreach (var overlap in overlaps)
            {
                var item = overlap.Item;
                if (addedIds.Contains(item.Id))
                    continue;

                var fKey = GetFranchiseKey(item.Title);
                if (!string.IsNullOrEmpty(fKey) && seenFranchises.Contains(fKey))
                    continue;

                item.Subtitle = $"Because you liked {overlap.AnchorTitles[0]} & {overlap.AnchorTitles[1]}";
                output.Add(item);
                addedIds.Add(item.Id);
                if (!string.IsNullOrEmpty(fKey))
                    seenFranchises.Add(fKey);

                if (output.Count >= 6) // Leave room for diverse single-anchor items
                    break;
            }

            // Step 2: Round-Robin interleaving across all chosen anchors.
            // Caps each anchor to MaxPerAnchor slots in the first pass to guarantee
            // fair, balanced diversity so that no single anchor monopolizes the feed.
            const int MaxPerAnchor = 3;
            var anchorContrib = chosen.ToDictionary(a => a.MalId, _ => 0);

            bool anyAdded = true;
            while (output.Count < 25 && anyAdded)
            {
                anyAdded = false;
                foreach (var anchor in chosen)
                {
                    if (output.Count >= 25)
                        break;

                    if (anchorContrib[anchor.MalId] >= MaxPerAnchor)
                        continue;

                    if (!perAnchorPools.TryGetValue(anchor, out var pool) || pool.Count == 0)
                        continue;

                    while (pool.Count > 0)
                    {
                        var candidate = pool[0].Item;
                        pool.RemoveAt(0);

                        if (addedIds.Contains(candidate.Id))
                            continue;

                        var fKey = GetFranchiseKey(candidate.Title);
                        if (!string.IsNullOrEmpty(fKey) && seenFranchises.Contains(fKey))
                            continue;

                        candidate.Subtitle = $"Because you liked {anchor.Title}";
                        output.Add(candidate);
                        addedIds.Add(candidate.Id);
                        if (!string.IsNullOrEmpty(fKey))
                            seenFranchises.Add(fKey);

                        anchorContrib[anchor.MalId]++;
                        anyAdded = true;
                        break; // Advance to next anchor in round-robin cycle
                    }
                }
            }

            // Step 3: Second pass with relaxed cap (up to 6 per anchor) if total results are below 20
            if (output.Count < 20)
            {
                const int MaxPerAnchorRelaxed = 6;
                anyAdded = true;
                while (output.Count < 25 && anyAdded)
                {
                    anyAdded = false;
                    foreach (var anchor in chosen)
                    {
                        if (output.Count >= 25)
                            break;

                        if (anchorContrib[anchor.MalId] >= MaxPerAnchorRelaxed)
                            continue;

                        if (!perAnchorPools.TryGetValue(anchor, out var pool) || pool.Count == 0)
                            continue;

                        while (pool.Count > 0)
                        {
                            var candidate = pool[0].Item;
                            pool.RemoveAt(0);

                            if (addedIds.Contains(candidate.Id))
                                continue;

                            var fKey = GetFranchiseKey(candidate.Title);
                            if (!string.IsNullOrEmpty(fKey) && seenFranchises.Contains(fKey))
                                continue;

                            candidate.Subtitle = $"Because you liked {anchor.Title}";
                            output.Add(candidate);
                            addedIds.Add(candidate.Id);
                            if (!string.IsNullOrEmpty(fKey))
                                seenFranchises.Add(fKey);

                            anchorContrib[anchor.MalId]++;
                            anyAdded = true;
                            break;
                        }
                    }
                }
            }

            Console.WriteLine($"MALPLUS foryou: total items={output.Count} across {chosen.Count} anchors: " +
                              string.Join(", ", chosen.Select(c => $"{c.Title} ({anchorContrib[c.MalId]})")));

            // Fallback: If fewer than 5 recommendations found, enrich with Tenrai top anime excluding library
            if (output.Count < 5)
            {
                try
                {
                    var excludeParam = alreadyInLibrary != null && alreadyInLibrary.Count > 0
                        ? $"&exclude_ids={string.Join(",", alreadyInLibrary.Take(100))}"
                        : "";
                    var fallbackRaw = await TenraiClient.GetRawJsonAsync($"top/anime?page[size]=15{excludeParam}");
                    if (!string.IsNullOrEmpty(fallbackRaw))
                    {
                        using var doc = JsonDocument.Parse(fallbackRaw);
                        if (doc.RootElement.TryGetProperty("data", out var fData) && fData.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var fEntry in fData.EnumerateArray())
                            {
                                var fItem = DiscoveryRandomQuery.ParseCatalogEntry(fEntry, false);
                                if (fItem == null || (alreadyInLibrary != null && alreadyInLibrary.Contains(fItem.Id)))
                                    continue;
                                if (output.Any(o => o.Id == fItem.Id))
                                    continue;

                                fItem.Subtitle = "Popular on MyAnimeList";
                                output.Add(fItem);
                                if (output.Count >= 20)
                                    break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("MALPLUS foryou fallback exception: " + ex.Message);
                }
            }

            // Enrich all chosen recommendations with format type, mean score and episodes via Tenrai batch
            await EnrichDiscoveryItemsAsync(output);

            return output;
        }

        /// <summary>
        ///     Batch-enriches discovery items with metadata (Type, Score, Episodes) via Tenrai /full?ids=...
        /// </summary>
        private static async Task EnrichDiscoveryItemsAsync(List<DiscoveryItem> items)
        {
            if (items == null || items.Count == 0)
                return;

            var missing = items.Where(x => x.Id > 0 && (string.IsNullOrEmpty(x.Type) || x.Score <= 0)).ToList();
            if (missing.Count == 0)
                return;

            var stillMissing = new List<DiscoveryItem>();
            foreach (var item in missing)
            {
                var cached = await DataCache.RetrieveAnimeSearchResultsData(item.Id.ToString(), !item.IsManga);
                if (cached != null)
                {
                    if (string.IsNullOrEmpty(item.Type) && !string.IsNullOrEmpty(cached.Type))
                        item.Type = cached.Type;
                    if (item.Score <= 0 && cached.GlobalScore > 0)
                        item.Score = cached.GlobalScore;
                    if (item.AllEpisodes <= 0 && cached.AllEpisodes > 0)
                        item.AllEpisodes = cached.AllEpisodes;
                }

                if (string.IsNullOrEmpty(item.Type) || item.Score <= 0)
                    stillMissing.Add(item);
            }

            if (stillMissing.Count == 0)
                return;

            for (int i = 0; i < stillMissing.Count; i += 25)
            {
                var chunk = stillMissing.Skip(i).Take(25).ToList();
                var first = chunk[0];
                var others = chunk.Skip(1).Select(x => x.Id).ToList();

                var mediaPrefix = first.IsManga ? "manga" : "anime";
                var endpoint = others.Count > 0
                    ? $"{mediaPrefix}/{first.Id}/full?ids={string.Join(",", others)}"
                    : $"{mediaPrefix}/{first.Id}/full";

                try
                {
                    var raw = await TenraiClient.GetRawJsonAsync(endpoint);
                    if (string.IsNullOrEmpty(raw))
                        continue;

                    using var doc = JsonDocument.Parse(raw);
                    var entries = new List<JsonElement>();

                    if (doc.RootElement.TryGetProperty("data", out var dataEl))
                    {
                        if (dataEl.ValueKind == JsonValueKind.Object)
                            entries.Add(dataEl);
                        else if (dataEl.ValueKind == JsonValueKind.Array)
                            entries.AddRange(dataEl.EnumerateArray());
                    }

                    if (doc.RootElement.TryGetProperty("included", out var incEl) && incEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var inc in incEl.EnumerateArray())
                            if (inc.ValueKind == JsonValueKind.Object)
                                entries.Add(inc);
                    }

                    foreach (var entry in entries)
                    {
                        int malId = 0;
                        if (entry.TryGetProperty("mal_id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                            malId = idProp.GetInt32();
                        if (malId <= 0)
                            continue;

                        var target = chunk.FirstOrDefault(c => c.Id == malId);
                        if (target == null)
                            continue;

                        if (string.IsNullOrEmpty(target.Type) && entry.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
                            target.Type = typeProp.GetString();

                        if (target.Score <= 0 && entry.TryGetProperty("score", out var scoreProp) && scoreProp.ValueKind == JsonValueKind.Number)
                            target.Score = scoreProp.GetDouble();

                        if (target.AllEpisodes <= 0 && entry.TryGetProperty("episodes", out var epProp) && epProp.ValueKind == JsonValueKind.Number)
                            target.AllEpisodes = epProp.GetInt32();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("MALPLUS enrich error: " + ex.Message);
                }
            }
        }

        /// <summary>
        ///     Normalizes titles to extract a clean franchise base key, collapsing sequels
        ///     (e.g. "3-gatsu no Lion 2nd Season" -> "3-gatsu no lion", "86 Part 2" -> "86").
        /// </summary>
        private static string GetFranchiseKey(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            var t = title;
            var colonIdx = t.IndexOf(':');
            if (colonIdx > 0)
                t = t.Substring(0, colonIdx);

            var dashIdx = t.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIdx > 0)
                t = t.Substring(0, dashIdx);

            t = Regex.Replace(t, @"(?i)\s+(?:part|season|s)\s*\d+.*$", "");
            t = Regex.Replace(t, @"(?i)\s+\d+(?:st|nd|rd|th)?\s+season.*$", "");
            t = Regex.Replace(t, @"(?i)\s+(?:2nd|3rd|4th|5th|final|kanketsu).*$", "");
            t = Regex.Replace(t, @"\s+\d+$", "");

            return t.Trim().ToLowerInvariant();
        }

        /// <summary>
        ///     What Tenrai recommends alongside one entry.
        /// </summary>
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
