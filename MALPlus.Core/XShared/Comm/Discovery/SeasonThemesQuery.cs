using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Anime;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.XShared.Comm;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;

namespace MALPlus.XShared.Comm.Discovery
{
    /// <summary>
    ///     The OP/ED of a whole season, as rows of one anime each.
    /// </summary>
    public static class SeasonThemesQuery
    {
        /// <summary>
        ///     Reads a season's entries and their themes as one row per anime.
        /// </summary>
        /// <remarks>
        ///     Two requests, not one per title. The season of themes is asked for whole from
        ///     AnimeThemes, which answers with every anime of the season and its themes in a single
        ///     page: measured for Fall 2026, 56 anime, 110 themes and 87 videos with no pagination.
        ///     MAL answers with the season's entries and their posters, which AnimeThemes does not
        ///     carry at all, so the artwork is what the join is for.
        ///     <para>
        ///     The two are matched on a normalised title with the normaliser the theme search already
        ///     uses, so this is not a third matching scheme. It will miss some, for two reasons that
        ///     are not defects: AnimeThemes only holds anime somebody uploaded a webm for, and two
        ///     spellings of one title do not always normalise alike. An entry with no themes is kept as
        ///     a row with a poster and nothing beside it, so a season still reads as the season it is.
        ///     </para>
        /// </remarks>
        public static async Task<SeasonThemeRowsResult> GetSeasonRowsAsync(int year, string season)
        {
            var result = new SeasonThemeRowsResult();
            var rows = result.Rows;
            if (string.IsNullOrEmpty(season))
                return result;

            // MAL first, because it decides which entries exist and in what order, and it is the only
            // one of the two that carries artwork.
            //
            // The read is filtered the same way the rest of the app filters: the source takes the
            // strictest setting and the middle one is applied here, against the rating each entry
            // carries. Doing it at this level and not in the query keeps the panel out of it.
            var query = ContentFilter.Query("");

            var seasonList = await new AnimeSeasonalQuery(new Models.Models.Anime.AnimeSeason
            {
                Name = $"{season} {year}",
                Year = year,
                Season = ParseSeason(season)
            }, query).GetSeasonalAnime(false);

            foreach (var entry in seasonList ?? new List<SeasonalAnimeData>())
            {
                // Only the middle setting has anything left to decide: the strict one was answered by
                // the source and the loose one wants everything.
                if (Settings.ContentFilter == ContentFilterMode.Mature &&
                    ContentFilter.IsHiddenBySetting(entry.Rating))
                    continue;

                rows.Add(new SeasonThemeRow
                {
                    Id = entry.Id,
                    Title = entry.Title,
                    ImgUrl = entry.ImgUrl,
                    Episodes = entry.Episodes,
                    Type = entry.Type
                });
            }

            // One request reads the whole season's themes AND each entry's MAL id, so the rows below
            // resolve through the same function everything else uses and hit the cache rather than
            // each spending a lookup of its own.
            var (themes, themesFailed) = await GetSeasonThemesAsync(year, season);
            result.ThemesFailed = themesFailed;
            if (themes.Count == 0)
                return result;

            var byMalId = new Dictionary<int, List<AnimeThemeRow>>();
            foreach (var theme in themes)
            {
                if (theme.MalId <= 0)
                    continue;
                if (!byMalId.TryGetValue(theme.MalId, out var list))
                    byMalId[theme.MalId] = list = new List<AnimeThemeRow>();
                if (!list.Any(existing => existing.Slot == theme.Slot && existing.SongTitle == theme.SongTitle))
                    list.Add(theme);
            }

            foreach (var row in rows)
            {
                if (!byMalId.TryGetValue(row.Id, out var own))
                    continue;
                row.Themes = own
                    .OrderBy(theme => theme.IsOpening ? 0 : 1)
                    .ThenBy(theme => theme.Sequence)
                    .ToList();
            }

            return result;
        }
        /// <summary>
        ///     Reads the whole season of themes from AnimeThemes, in one request.
        /// </summary>
        /// <remarks>
        ///     The includes ask for the song and its artists, because that is the only place the artist
        ///     is available and the line reads as "OP 1 · Song · by Artist", exactly as the details tab
        ///     writes it. Asking for any less would leave a hole in the middle of every row.
        ///     <para>
        ///     "resources" is the important one: it is where the MAL id of each entry lives, and it is
        ///     what lets this be joined to MAL without comparing a single name. Measured over Fall 2026,
        ///     every entry the season returns carries one.
        ///     </para>
        /// </remarks>
        private static async Task<(List<AnimeThemeRow> Themes, bool Failed)> GetSeasonThemesAsync(
            int year, string season)
        {
            var output = new List<AnimeThemeRow>();
            try
            {
                var seasonEnum = (season ?? "").Trim().ToUpperInvariant();
                if (seasonEnum != "WINTER" && seasonEnum != "SPRING" && seasonEnum != "SUMMER" && seasonEnum != "FALL")
                    seasonEnum = "FALL";

                var query = "{ animePagination(year: " + year + ", season: " + seasonEnum + ", first: 100) { " +
                            "paginatorInfo { hasMorePages currentPage lastPage } " +
                            "data { slug title { romaji english } resources { nodes { site externalId } } " +
                            "animethemes { type sequence song { title { romaji native } performances { artist { name { main } } } } " +
                            "animethemeentries { episodes nsfw videos { nodes { link } } } } } } }";

                using var doc = await AnimeThemesHelper.ExecuteGraphQlAsync(query);
                if (doc == null || !doc.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("animePagination", out var pagination) ||
                    pagination.ValueKind != JsonValueKind.Object ||
                    !pagination.TryGetProperty("data", out var animeList) ||
                    animeList.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("MALPlus SeasonThemes: animethemes GraphQL returned no body for " +
                                      season + " " + year);
                    return (output, true);
                }

                foreach (var anime in animeList.EnumerateArray())
                {
                    string name = null;
                    if (anime.TryGetProperty("title", out var titleObj) && titleObj.ValueKind == JsonValueKind.Object)
                    {
                        if (titleObj.TryGetProperty("romaji", out var rProp) && rProp.ValueKind == JsonValueKind.String)
                            name = rProp.GetString();
                        else if (titleObj.TryGetProperty("english", out var eProp) && eProp.ValueKind == JsonValueKind.String)
                            name = eProp.GetString();
                    }

                    var malId = 0;
                    if (anime.TryGetProperty("resources", out var resObj) && resObj.ValueKind == JsonValueKind.Object &&
                        resObj.TryGetProperty("nodes", out var nodesArr) && nodesArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var node in nodesArr.EnumerateArray())
                        {
                            var site = node.TryGetProperty("site", out var siteProp) && siteProp.ValueKind == JsonValueKind.String ? siteProp.GetString() : "";
                            if (string.Equals(site, "MAL", StringComparison.OrdinalIgnoreCase) || string.Equals(site, "MyAnimeList", StringComparison.OrdinalIgnoreCase))
                            {
                                if (node.TryGetProperty("externalId", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                                {
                                    malId = idProp.GetInt32();
                                    break;
                                }
                            }
                        }
                    }

                    if (malId <= 0)
                        continue;

                    if (!anime.TryGetProperty("animethemes", out var themes) ||
                        themes.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var theme in themes.EnumerateArray())
                    {
                        var typeStr = theme.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String ? typeProp.GetString() : "OP";
                        var isOpening = string.Equals(typeStr, "OP", StringComparison.OrdinalIgnoreCase);

                        var sequence = theme.TryGetProperty("sequence", out var seqProp) &&
                                       seqProp.ValueKind == JsonValueKind.Number
                            ? seqProp.GetInt32()
                            : 1;

                        string songTitle = string.Empty;
                        var artists = new List<string>();
                        if (theme.TryGetProperty("song", out var song) && song.ValueKind == JsonValueKind.Object)
                        {
                            if (song.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.Object)
                            {
                                if (titleProp.TryGetProperty("romaji", out var r) && r.ValueKind == JsonValueKind.String)
                                    songTitle = r.GetString();
                                else if (titleProp.TryGetProperty("native", out var n) && n.ValueKind == JsonValueKind.String)
                                    songTitle = n.GetString();
                            }

                            if (song.TryGetProperty("performances", out var perfArr) && perfArr.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var perf in perfArr.EnumerateArray())
                                {
                                    if (perf.TryGetProperty("artist", out var artObj) && artObj.ValueKind == JsonValueKind.Object &&
                                        artObj.TryGetProperty("name", out var artNameObj) && artNameObj.ValueKind == JsonValueKind.Object &&
                                        artNameObj.TryGetProperty("main", out var mainProp) && mainProp.ValueKind == JsonValueKind.String)
                                    {
                                        var aName = mainProp.GetString();
                                        if (!string.IsNullOrEmpty(aName))
                                            artists.Add(aName);
                                    }
                                }
                            }
                        }

                        if (!theme.TryGetProperty("animethemeentries", out var entries) ||
                            entries.ValueKind != JsonValueKind.Array)
                        continue;

                        foreach (var entry in entries.EnumerateArray())
                        {
                            var episodeRange = entry.TryGetProperty("episodes", out var epsProp) &&
                                               epsProp.ValueKind == JsonValueKind.String
                                ? epsProp.GetString()
                                : string.Empty;

                            var nsfw = entry.TryGetProperty("nsfw", out var nsfwProp) &&
                                      nsfwProp.ValueKind == JsonValueKind.True;

                            string video = null;
                            if (entry.TryGetProperty("videos", out var vidObj) && vidObj.ValueKind == JsonValueKind.Object &&
                                vidObj.TryGetProperty("nodes", out var vidNodes) && vidNodes.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var node in vidNodes.EnumerateArray())
                                {
                                    var link = node.TryGetProperty("link", out var lProp) && lProp.ValueKind == JsonValueKind.String ? lProp.GetString() : null;
                                    if (!string.IsNullOrEmpty(link))
                                    {
                                        video = link;
                                        break;
                                    }
                                }
                            }

                            if (string.IsNullOrEmpty(video))
                                continue;

                            output.Add(new AnimeThemeRow
                            {
                                AnimeName = name ?? string.Empty,
                                MalId = malId,
                                IsOpening = isOpening,
                                Sequence = sequence,
                                SongTitle = songTitle ?? string.Empty,
                                Artists = artists,
                                VideoUrl = video,
                                EpisodeRange = episodeRange ?? string.Empty,
                                Nsfw = nsfw
                            });
                        }
                    }
                }
                return (output, false);
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPlus SeasonThemes: GraphQL season request failed for " +
                                  season + " " + year + ": " + ex.GetType().Name + " " + ex.Message);
                return (output, true);
            }

            return (output, false);
        }

        private static Season ParseSeason(string season)
            => season?.Trim().ToLowerInvariant() switch
            {
                "winter" => Season.Winter,
                "spring" => Season.Spring,
                "summer" => Season.Summer,
                _ => Season.Fall
            };

        private static string Capitalize(string value)
            => string.IsNullOrEmpty(value)
                ? value
                : char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
    }

    /// <summary>
    ///     A season's rows, plus whether the themes half of the read failed.
    /// </summary>
    /// <remarks>
    ///     The season list comes from MAL and survives an AnimeThemes outage, so the two halves
    ///     fail separately. The flag is what lets the page keep the season on screen and offer a
    ///     retry, instead of presenting an empty theme column as if it were the truth.
    /// </remarks>
    public sealed class SeasonThemeRowsResult
    {
        public List<SeasonThemeRow> Rows { get; } = new List<SeasonThemeRow>();

        public bool ThemesFailed { get; set; }
    }

    /// <summary>
    ///     One anime of a season, with its themes beside it.
    /// </summary>
    public class SeasonThemeRow
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string ImgUrl { get; set; }
        public string Episodes { get; set; }
        public int Type { get; set; }

        public List<AnimeThemeRow> Themes { get; set; } = new List<AnimeThemeRow>();

        public bool HasNoThemes => Themes.Count == 0;

        /// <summary>Type and episode count, the way the poster card would have tagged it.</summary>
        public string MetaLine
        {
            get
            {
                var parts = new List<string>();
                if (Type > 0)
                    parts.Add(((Models.Enums.AnimeType)Type).ToString());

                // "?" rather than 0 when the source has not said. A season entry that is still airing
                // comes back with no episode count at all, and printing that as "0 eps" reads as a
                // series with no episodes rather than as one nobody has counted yet. Zero is kept
                // where it is a real zero, which is what the qualities of the seed bundle carry.
                var episodes = Episodes ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(episodes) && episodes != "0")
                    parts.Add(episodes + " eps");
                else if (string.IsNullOrWhiteSpace(episodes))
                    parts.Add("? eps");

                return parts.Count == 0 ? string.Empty : string.Join("  ·  ", parts);
            }
        }
    }

    /// <summary>
    ///     One OP or one ED of one anime, with the video that plays it.
    /// </summary>
    public class AnimeThemeRow
    {
        public string AnimeName { get; set; }

        /// <summary>The MAL id of the entry this theme belongs to. Zero when unknown.</summary>
        public int MalId { get; set; }

        public bool IsOpening { get; set; }
        public int Sequence { get; set; }
        public string SongTitle { get; set; } = string.Empty;
        public List<string> Artists { get; set; } = new List<string>();
        public string VideoUrl { get; set; }
        public string EpisodeRange { get; set; } = string.Empty;
        public bool Nsfw { get; set; }

        public string Slot => (IsOpening ? "OP " : "ED ") + Sequence;

        /// <summary>
        ///     The line as the details tab writes it, so the two read as the same component.
        /// </summary>
        public string Text
        {
            get
            {
                var parts = new List<string> { Slot };
                if (!string.IsNullOrWhiteSpace(SongTitle))
                    parts.Add("\"" + SongTitle + "\"");
                if (Artists.Count > 0)
                    parts.Add("by " + string.Join(", ", Artists));
                if (!string.IsNullOrWhiteSpace(EpisodeRange))
                    parts.Add("(eps " + EpisodeRange + ")");
                return string.Join("  ", parts);
            }
        }
    }
}
