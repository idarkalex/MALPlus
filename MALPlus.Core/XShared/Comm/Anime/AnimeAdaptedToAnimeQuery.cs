using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.Comm.Anime
{
    public enum MangaAdaptedType
    {
        All,
        AiringNow,
        UpcomingAnime
    }

    /// <summary>
    ///     Manga titles that have received an anime adaptation.
    ///     Migrated from legacy HTML web scraping to Tenrai REST API (GET /manga/adapted).
    /// </summary>
    public class AnimeAdaptedToAnimeQuery : Query
    {
        private static Dictionary<MangaAdaptedType, List<TopAnimeData>> _prevQueriesCache =
            new Dictionary<MangaAdaptedType, List<TopAnimeData>>();
        private readonly MangaAdaptedType _type;

        public AnimeAdaptedToAnimeQuery(MangaAdaptedType type)
        {
            _type = type;
        }

        public static string ToDisplayName(MangaAdaptedType type)
        {
            switch (type)
            {
                case MangaAdaptedType.All:
                    return "All";
                case MangaAdaptedType.AiringNow:
                    return "Airing Now";
                case MangaAdaptedType.UpcomingAnime:
                    return "Upcoming Anime";
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private static string GetEndpoint(MangaAdaptedType type)
        {
            var filter = type switch
            {
                MangaAdaptedType.AiringNow => "filter=airing&",
                MangaAdaptedType.UpcomingAnime => "filter=upcoming&",
                _ => ""
            };
            return ContentFilter.Query($"manga/adapted?{filter}limit=50");
        }

        public async Task<List<TopAnimeData>> GetAdaptedToAnimeData(bool force = false)
        {
            if (!force)
                if (_prevQueriesCache.TryGetValue(_type, out var cached) && cached.Count > 0)
                    return cached;

            var output = force ? new List<TopAnimeData>() : (await DataCache.RetrieveAdaptedToAnimeData(_type) ?? new List<TopAnimeData>());
            if (output.Count > 0)
            {
                _prevQueriesCache[_type] = output;
                return output;
            }

            try
            {
                var endpoint = GetEndpoint(_type);
                var (items, _) = await TenraiClient.GetPaginatedAsync(endpoint);
                if (items != null && items.Count > 0)
                {
                    var index = 0;
                    foreach (var item in items)
                    {
                        var malId = GetInt(item, "mal_id");
                        if (malId <= 0) continue;

                        output.Add(new TopAnimeData
                        {
                            Id = malId,
                            Title = GetString(item, "title"),
                            ImgUrl = GetNestedImageUrl(item),
                            Score = (float)GetDouble(item, "score"),
                            Type = (int)MalTypeParser.ParseMangaType(GetString(item, "type")),
                            Index = ++index
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS Tenrai manga/adapted fetch failed: " + ex.Message);
            }

            if (output.Count > 0)
            {
                await DataCache.SaveAdaptedToAnimeData(output, _type);
                _prevQueriesCache[_type] = output;
            }
            return output;
        }

        private static string GetString(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : "";

        private static int GetInt(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;

        private static double GetDouble(JsonElement el, string prop) =>
            el.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0;

        private static string GetNestedImageUrl(JsonElement el)
        {
            if (el.TryGetProperty("images", out var images) &&
                images.TryGetProperty("jpg", out var jpg) &&
                jpg.TryGetProperty("image_url", out var img) &&
                img.ValueKind == JsonValueKind.String)
            {
                return img.GetString() ?? "";
            }
            return "";
        }
    }
}
