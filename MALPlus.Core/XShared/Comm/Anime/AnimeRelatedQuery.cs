using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.XShared.Utils;
using MALPlus.XShared.Comm.Anime;

namespace MALPlus.XShared.Comm.Anime
{
    public class AnimeRelatedQuery : Query
    {
        private readonly int _animeId;
        private readonly bool _animeMode;

        public AnimeRelatedQuery(int id, bool anime = true)
        {
            Request =
                new Uri(Uri.EscapeUriString($"https://myanimelist.net/{(anime ? "anime" : "manga")}/{id}/"));
            _animeId = id;
            _animeMode = anime;
        }

        public async Task<List<RelatedAnimeData>> GetRelatedAnime(bool force = false)
        {
            var output = force
                ? new List<RelatedAnimeData>()
                : await DataCache.RetrieveRelatedAnimeData(_animeId, _animeMode) ?? new List<RelatedAnimeData>();
            if (output.Count != 0)
            {
                var deduped = Deduplicate(output);
                await EnrichRelatedMetadataAsync(deduped);
                return deduped;
            }

            output = await FetchFromTenraiAsync();
            if (output != null && output.Count > 0)
            {
                await EnrichRelatedMetadataAsync(output);
                await DataCache.SaveRelatedAnimeData(_animeId, output, _animeMode);
                return output;
            }

            output = await FetchFromHtmlScraperAsync();
            if (output != null && output.Count > 0)
            {
                await EnrichRelatedMetadataAsync(output);
                await DataCache.SaveRelatedAnimeData(_animeId, output, _animeMode);
            }
            return output ?? new List<RelatedAnimeData>();
        }

        private async Task EnrichRelatedMetadataAsync(List<RelatedAnimeData> items)
        {
            if (items == null || items.Count == 0)
                return;

            var missing = new List<RelatedAnimeData>();
            foreach (var item in items)
            {
                if (item.Id <= 0) continue;

                // Check volatile memory cache first (instant)
                if (DataCache.TryRetrieveDataForId(item.Id, out var vd))
                {
                    if (string.IsNullOrEmpty(item.ScoreTag) && vd.GlobalScore > 0)
                        item.ScoreTag = vd.GlobalScore.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    if (string.IsNullOrEmpty(item.SeasonTag) && !string.IsNullOrEmpty(vd.AirStartDate))
                    {
                        var s = Utils.Utilities.SeasonToCapitalLetterWithYear(vd.AirStartDate);
                        if (!string.IsNullOrEmpty(s) && s != "N/A")
                            item.SeasonTag = s;
                    }
                }

                var isAnime = item.Type == RelatedItemType.Anime || (item.Type != RelatedItemType.Manga && _animeMode);
                var cached = await DataCache.RetrieveAnimeSearchResultsData(item.Id.ToString(), isAnime);
                if (cached != null)
                {
                    if (string.IsNullOrEmpty(item.MediaType) && !string.IsNullOrEmpty(cached.Type))
                        item.MediaType = NormalizeMediaType(cached.Type);
                    if (item.AllEpisodes == 0 && cached.AllEpisodes > 0)
                        item.AllEpisodes = cached.AllEpisodes;
                    if (string.IsNullOrEmpty(item.ScoreTag) && cached.GlobalScore > 0)
                        item.ScoreTag = cached.GlobalScore.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    if (string.IsNullOrEmpty(item.SeasonTag) && !string.IsNullOrEmpty(cached.StartDate))
                    {
                        var s = Utils.Utilities.SeasonToCapitalLetterWithYear(cached.StartDate);
                        if (!string.IsNullOrEmpty(s) && s != "N/A")
                            item.SeasonTag = s;
                    }
                }

                if (string.IsNullOrEmpty(item.ScoreTag) || string.IsNullOrEmpty(item.MediaType))
                    missing.Add(item);
            }

            if (missing.Count == 0)
                return;

            // Batch fetch missing items via Tenrai /full?ids=...
            foreach (var group in missing.GroupBy(item => item.Type == RelatedItemType.Anime || (item.Type != RelatedItemType.Manga && _animeMode)))
            {
                var isAnime = group.Key;
                var mediaPrefix = isAnime ? "anime" : "manga";
                var groupItems = group.ToList();

                for (var i = 0; i < groupItems.Count; i += 25)
                {
                    var chunk = groupItems.Skip(i).Take(25).ToList();
                    var first = chunk[0];
                    var others = chunk.Skip(1).Select(x => x.Id).ToList();

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

                        if (doc.RootElement.TryGetProperty("included", out var includedArr) && includedArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var inc in includedArr.EnumerateArray())
                                if (inc.ValueKind == JsonValueKind.Object)
                                    entries.Add(inc);
                        }

                        foreach (var entry in entries)
                        {
                            var malId = entry.TryGetProperty("mal_id", out var idProp) && idProp.ValueKind == JsonValueKind.Number
                                ? idProp.GetInt32()
                                : 0;
                            if (malId <= 0 && entry.TryGetProperty("id", out var fallbackIdProp) && fallbackIdProp.ValueKind == JsonValueKind.Number)
                                malId = fallbackIdProp.GetInt32();
                            if (malId <= 0)
                                continue;

                            var targetItem = chunk.FirstOrDefault(x => x.Id == malId);
                            if (targetItem == null)
                                continue;

                            var rawType = GetString(entry, "type");
                            if (string.IsNullOrEmpty(rawType))
                                rawType = GetString(entry, "media_type");

                            if (!string.IsNullOrEmpty(rawType))
                                targetItem.MediaType = NormalizeMediaType(rawType);

                            if (targetItem.AllEpisodes == 0)
                            {
                                var eps = isAnime
                                    ? (entry.TryGetProperty("episodes", out var epP) && epP.ValueKind == JsonValueKind.Number ? epP.GetInt32() : 0)
                                    : (entry.TryGetProperty("chapters", out var chP) && chP.ValueKind == JsonValueKind.Number ? chP.GetInt32() : 0);
                                if (eps > 0)
                                    targetItem.AllEpisodes = eps;
                            }

                            double? entryScore = null;
                            if (entry.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number)
                            {
                                var score = sc.GetDouble();
                                if (score > 0)
                                {
                                    entryScore = score;
                                    if (string.IsNullOrEmpty(targetItem.ScoreTag))
                                        targetItem.ScoreTag = score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                                }
                            }

                            var dateProp = isAnime ? "aired" : "published";
                            string airStartDate = null;
                            if (entry.TryGetProperty(dateProp, out var dObj) && dObj.ValueKind == JsonValueKind.Object &&
                                dObj.TryGetProperty("from", out var fStr) && fStr.ValueKind == JsonValueKind.String)
                            {
                                airStartDate = fStr.GetString();
                                if (string.IsNullOrEmpty(targetItem.SeasonTag) && !string.IsNullOrEmpty(airStartDate))
                                {
                                    var s = Utils.Utilities.SeasonToCapitalLetterWithYear(airStartDate);
                                    if (!string.IsNullOrEmpty(s) && s != "N/A")
                                        targetItem.SeasonTag = s;
                                }
                            }

                            string status = entry.TryGetProperty("status", out var stEl) && stEl.ValueKind == JsonValueKind.String
                                ? stEl.GetString()
                                : null;

                            if (!string.IsNullOrEmpty(status) || !string.IsNullOrEmpty(airStartDate) || entryScore > 0)
                            {
                                DataCache.UpdateVolatileData(targetItem.Id, status: status, airStartDate: airStartDate, globalScore: (float?)entryScore);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"MALPLUS RelatedEnrich batch failed: {ex.GetType().Name} - {ex.Message}");
                    }
                }
            }
        }

        private async Task<List<RelatedAnimeData>> FetchFromTenraiAsync()
        {
            try
            {
                var endpoint = _animeMode ? $"anime/{_animeId}/full" : $"manga/{_animeId}/full";
                var data = await TenraiClient.GetDataAsync(endpoint);
                if (!data.TryGetProperty("relations", out var relations) || relations.ValueKind != JsonValueKind.Array)
                    return null;

                var output = new List<RelatedAnimeData>();
                foreach (var rel in relations.EnumerateArray())
                {
                    try
                    {
                        if (rel.ValueKind != JsonValueKind.Object)
                            continue;
                        if (!rel.TryGetProperty("relation", out var relationProp) || relationProp.ValueKind != JsonValueKind.String)
                            continue;
                        if (!rel.TryGetProperty("entry", out var entry))
                            continue;

                        var relation = WebUtility.HtmlDecode(relationProp.GetString());
                        if (string.IsNullOrWhiteSpace(relation))
                            continue;
                        IEnumerable<JsonElement> entries;
                        if (entry.ValueKind == JsonValueKind.Array)
                            entries = entry.EnumerateArray().ToList();
                        else if (entry.ValueKind == JsonValueKind.Object)
                            entries = new[] { entry };
                        else
                            continue;

                        foreach (var ent in entries)
                        {
                            try
                            {
                                if (ent.ValueKind != JsonValueKind.Object)
                                    continue;
                                var malId = ent.TryGetProperty("mal_id", out var idProp) && idProp.ValueKind == JsonValueKind.Number
                                    ? idProp.GetInt32()
                                    : 0;
                                if (malId <= 0)
                                    continue;

                                var typeStr = ent.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String
                                    ? typeProp.GetString()
                                    : null;

                                var current = new RelatedAnimeData();
                                current.WholeRelation = relation;
                                current.Relations.Add(relation);
                                current.Type = typeStr?.ToLowerInvariant() == "anime"
                                    ? RelatedItemType.Anime
                                    : typeStr?.ToLowerInvariant() == "manga" ? RelatedItemType.Manga : RelatedItemType.Unknown;
                                current.MediaType = NormalizeMediaType(GetString(ent, "media_type"));
                                current.Id = malId;
                                current.Title = WebUtility.HtmlDecode(
                                    ent.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                                        ? nameProp.GetString()
                                        : "");

                                var imgUrl = GetNestedImageUrl(ent);
                                if (!string.IsNullOrEmpty(imgUrl))
                                    current.ImgUrl = MalImageUrl.Normalize(imgUrl);

                                output.Add(current);
                            }
                            catch (Exception)
                            {
                                // skip malformed relation entry
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // skip malformed relation
                    }
                }

                return Deduplicate(output);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private async Task<List<RelatedAnimeData>> FetchFromHtmlScraperAsync()
        {
            var output = new List<RelatedAnimeData>();

            var raw = await GetRequestResponse();
            if (string.IsNullOrEmpty(raw))
                return null;

            var doc = new HtmlDocument();
            doc.LoadHtml(raw);
            try
            {
                var relationsNode = doc.DocumentNode.Descendants("div")
                    .First(
                        node =>
                            node.Attributes.Contains("class") &&
                            node.Attributes["class"].Value ==
                            "related-entries");


                try
                {
                    var tile = relationsNode.Descendants("div")
                        .First(
                            node =>
                                node.Attributes.Contains("class") &&
                                node.Attributes["class"].Value ==
                                "entries-tile");
                    var tileContents = tile.Descendants("div")
                        .Where(
                            node =>
                                node.Attributes.Contains("class") &&
                                node.Attributes["class"].Value ==
                                "content").ToList();

foreach (var content in tileContents)
                    {
                        // One entry per try: the whole loop used to sit inside a single catch, so
                        // a single unexpected tile threw away every entry after it. That is why
                        // One Piece showed twenty four of the thirty one relations MAL lists.
                        try
                        {
                            var relationDiv = content.Descendants("div")
                                .First(
                                    node =>
                                        node.Attributes.Contains("class") &&
                                        node.Attributes["class"].Value ==
                                        "relation");

                            var relation = WebUtility.HtmlDecode(relationDiv.InnerText.Trim());
                            relation = Regex.Replace(relation.Trim(), @"\t|\n|\r|  ", "");
                            if (string.IsNullOrWhiteSpace(relation))
                                continue;

                            var titleDiv = content.Descendants("div")
                                .First(
                                    node =>
                                        node.Attributes.Contains("class") &&
                                        node.Attributes["class"].Value ==
                                        "title");

                            var linkNode = titleDiv.Descendants("a").First();

                            var current = new RelatedAnimeData();
                            current.WholeRelation = relation;
                        current.Relations.Add(relation);
                        var link = linkNode.Attributes["href"].Value.Split('/');
                        current.Type = link.Length > 3 && link[3] == "anime"
                            ? RelatedItemType.Anime
                            : link.Length > 3 && link[3] == "manga" ? RelatedItemType.Manga : RelatedItemType.Unknown;
                        if (current.Type == RelatedItemType.Unknown || !int.TryParse(link[4], out var tileId))
                            continue;
                        current.Id = tileId;
                        current.Title = WebUtility.HtmlDecode(linkNode.InnerText.Trim().Trim('\n'));

                        // The entry's real poster lives in the sibling div.image of
                        // this entry (not the whole tile) - scope by the current entry
                        // so each related title gets its own poster.
                        var entry = content.ParentNode;
                        var imgNode = entry.Descendants("img").FirstOrDefault();
                        var imgSrc = imgNode?.Attributes["data-src"]?.Value ?? imgNode?.Attributes["src"]?.Value;
                        if (!string.IsNullOrEmpty(imgSrc))
                        {
                            if (imgSrc.StartsWith("//"))
                                imgSrc = "https:" + imgSrc;
                            current.ImgUrl = MalImageUrl.Normalize(imgSrc);
                        }

                        output.Add(current);
                        }
                        catch (Exception)
                        {
                            // One malformed entry must not cost the rest of the tile.
                        }
                    }
                }
                catch (Exception)
                {
                    //mystery
                }

                try
                {
                    var table = relationsNode.Descendants("table").First();
                    var trs = table.Descendants("tr").ToList();

                    foreach (var t in trs)
                    {
                        // Same per row guard as the tiles: indexing tds[0] and tds[1] blindly
                        // threw on any row that did not have exactly two cells, and the catch
                        // around the whole table then cost every row after it.
                        try
                        {
                            var tds = t.Descendants("td").ToList();
                            if (tds.Count == 0)
                                continue;

                            // The relation sits in its own cell, but which one is not guaranteed,
                            // so it is taken from the first cell with text and no link in it.
                            var relation = "";
                            foreach (var td in tds)
                            {
                                if (td.Descendants("a").Any())
                                    continue;
                                var text = WebUtility.HtmlDecode(td.InnerText.Trim());
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    relation = text;
                                    break;
                                }
                            }
                            if (string.IsNullOrWhiteSpace(relation))
                                continue;

                            foreach (var td in tds)
                            foreach (var linkNode in td.Descendants("a"))
                            {
                                var href = linkNode.Attributes["href"]?.Value;
                                if (string.IsNullOrEmpty(href))
                                    continue;
                                var link = href.Split('/');
                                if (link.Length < 5)
                                    continue;
                                var current = new RelatedAnimeData();
                                current.WholeRelation = relation;
                                current.Relations.Add(relation);
                                current.Type = link[3] == "anime"
                                    ? RelatedItemType.Anime
                                    : link[3] == "manga" ? RelatedItemType.Manga : RelatedItemType.Unknown;
                                if (current.Type == RelatedItemType.Unknown || !int.TryParse(link[4], out var rowId))
                                    continue;
                                current.Id = rowId;
                                current.Title = WebUtility.HtmlDecode(linkNode.InnerText.Trim());
                                output.Add(current);
                            }
                        }
                        catch (Exception)
                        {
                            // One malformed row must not cost the rest of the table.
                        }
                    }
                }
                catch (Exception)
                {
                    //mystery
                }

            }
            catch (Exception ex)
            {
            }

            output = Deduplicate(output);
            return output;
        }

        private static List<RelatedAnimeData> Deduplicate(List<RelatedAnimeData> entries)
        {
            var output = new List<RelatedAnimeData>();
            var byTypeAndId = new Dictionary<(RelatedItemType Type, int Id), RelatedAnimeData>();
            foreach (var entry in entries)
            {
                if (entry == null || entry.Id <= 0)
                    continue;
                entry.Relations = (entry.Relations ?? new List<string>())
                    .Where(relation => !string.IsNullOrWhiteSpace(relation))
                    .Select(relation => relation.Trim())
                    .Distinct()
                    .ToList();
                if (!string.IsNullOrWhiteSpace(entry.WholeRelation))
                {
                    entry.WholeRelation = entry.WholeRelation.Trim();
                    entry.Relations.Remove(entry.WholeRelation);
                    entry.Relations.Insert(0, entry.WholeRelation);
                }
                else if (entry.Relations.Count > 0)
                {
                    entry.WholeRelation = entry.Relations[0];
                }
                entry.MediaType = NormalizeMediaType(entry.MediaType);

                var key = (entry.Type, entry.Id);
                if (!byTypeAndId.TryGetValue(key, out var existing))
                {
                    byTypeAndId[key] = entry;
                    output.Add(entry);
                    continue;
                }

                if (string.IsNullOrEmpty(existing.Title))
                    existing.Title = entry.Title;
                if (string.IsNullOrEmpty(existing.ImgUrl))
                    existing.ImgUrl = entry.ImgUrl;
                if (string.IsNullOrEmpty(existing.MediaType))
                    existing.MediaType = entry.MediaType;
                if (string.IsNullOrEmpty(existing.WholeRelation))
                {
                    existing.WholeRelation = entry.WholeRelation;
                    if (!string.IsNullOrEmpty(entry.WholeRelation))
                    {
                        existing.Relations.Remove(entry.WholeRelation);
                        existing.Relations.Insert(0, entry.WholeRelation);
                    }
                }
                foreach (var relation in entry.Relations)
                    if (!string.IsNullOrEmpty(relation) && !existing.Relations.Contains(relation))
                        existing.Relations.Add(relation);
            }
            return output;
        }

        private static string GetString(JsonElement element, string property)
        {
            return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : "";
        }

        private static string NormalizeMediaType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return type;
            return type switch
            {
                "tv" => "TV",
                "tv_special" => "TV Special",
                "movie" => "Movie",
                "ova" => "OVA",
                "ona" => "ONA",
                "special" => "Special",
                "music" => "Music",
                "cm" => "Commercial",
                "pv" => "PV",
                "manga" => "Manga",
                "novel" => "Novel",
                "light_novel" => "Light Novel",
                "one_shot" => "One-shot",
                "oneshot" => "One-shot",
                "doujinshi" => "Doujinshi",
                "doujin" => "Doujinshi",
                "manhwa" => "Manhwa",
                "manhua" => "Manhua",
                _ => type
            };
        }

        private static string GetNestedImageUrl(JsonElement entry)
        {
            if (!entry.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Object)
                return null;
            if (!images.TryGetProperty("jpg", out var jpg) || jpg.ValueKind != JsonValueKind.Object)
                return null;
            if (!jpg.TryGetProperty("image_url", out var url) || url.ValueKind != JsonValueKind.String)
                return null;
            return url.GetString();
        }
    }
}
