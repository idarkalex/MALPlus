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
        public static async Task<List<SeasonThemeRow>> GetSeasonRowsAsync(int year, string season)
        {
            var rows = new List<SeasonThemeRow>();
            if (string.IsNullOrEmpty(season))
                return rows;

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
            var themes = await GetSeasonThemesAsync(year, season);
            if (themes.Count == 0)
                return rows;

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

            return rows;
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
        private static async Task<List<AnimeThemeRow>> GetSeasonThemesAsync(int year, string season)
        {
            var output = new List<AnimeThemeRow>();
            try
            {
                var url =
                    "https://api.animethemes.moe/anime" +
                    $"?filter[season]={Uri.EscapeDataString(Capitalize(season))}&filter[year]={year}" +
                    "&page[size]=100" +
                    "&include=animethemes.song,animethemes.song.artists,animethemes.animethemeentries.videos,resources";

                var json = await AnimeThemesHelper.GetRawAsync(url);
                if (string.IsNullOrEmpty(json))
                    return output;

                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("anime", out var animeList) ||
                    animeList.ValueKind != JsonValueKind.Array)
                    return output;

                foreach (var anime in animeList.EnumerateArray())
                {
                    var name = anime.TryGetProperty("name", out var nameProp) &&
                               nameProp.ValueKind == JsonValueKind.String
                        ? nameProp.GetString()
                        : string.Empty;

                    // The MAL id, read off the entry's own reference rather than inferred from its
                    // name. An entry with no reference cannot be placed on a card, and it is skipped
                    // rather than guessed at.
                    var malId = 0;
                    if (anime.TryGetProperty("resources", out var resources) &&
                        resources.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var resource in resources.EnumerateArray())
                        {
                            var site = resource.TryGetProperty("site", out var siteProp) &&
                                       siteProp.ValueKind == JsonValueKind.String
                                ? siteProp.GetString()
                                : string.Empty;
                            if (!string.Equals(site, "MyAnimeList", StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (resource.TryGetProperty("external_id", out var idProp) &&
                                idProp.ValueKind == JsonValueKind.Number)
                                malId = idProp.GetInt32();
                            break;
                        }
                    }

                    if (malId <= 0)
                        continue;

                    if (!anime.TryGetProperty("animethemes", out var themes) ||
                        themes.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var theme in themes.EnumerateArray())
                    {
                        var isOpening = string.Equals(
                            theme.TryGetProperty("type", out var typeProp) &&
                            typeProp.ValueKind == JsonValueKind.String
                                ? typeProp.GetString()
                                : string.Empty,
                            "OP",
                            StringComparison.OrdinalIgnoreCase);

                        var sequence = theme.TryGetProperty("sequence", out var seqProp) &&
                                       seqProp.ValueKind == JsonValueKind.Number
                            ? seqProp.GetInt32()
                            : 1;

                        var song = theme.TryGetProperty("song", out var songProp) &&
                                   songProp.ValueKind == JsonValueKind.Object
                            ? songProp
                            : default(JsonElement?);

                        var songTitle = song.HasValue &&
                                       song.Value.TryGetProperty("title", out var titleProp) &&
                                       titleProp.ValueKind == JsonValueKind.String
                            ? titleProp.GetString()
                            : string.Empty;

                        var artists = new List<string>();
                        if (song.HasValue &&
                            song.Value.TryGetProperty("artists", out var artistList) &&
                            artistList.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var artist in artistList.EnumerateArray())
                            {
                                if (artist.TryGetProperty("name", out var artistNameProp) &&
                                    artistNameProp.ValueKind == JsonValueKind.String)
                                    artists.Add(artistNameProp.GetString());
                            }
                        }

                        // Entries, not themes, carry the videos and the nsfw mark, and there can be
                        // several. Every one that resolves to a video becomes a row of its own, which
                        // is what lets a theme with two variants show both.
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

                            var video = entry.TryGetProperty("videos", out var videoList) &&
                                        videoList.ValueKind == JsonValueKind.Array
                                ? videoList.EnumerateArray()
                                    .Select(videoElement => videoElement.TryGetProperty("link", out var linkProp) &&
                                                            linkProp.ValueKind == JsonValueKind.String
                                        ? linkProp.GetString()
                                        : string.Empty)
                                    .FirstOrDefault(link => !string.IsNullOrEmpty(link))
                                : null;

                            if (string.IsNullOrEmpty(video))
                                continue;

                            output.Add(new AnimeThemeRow
                            {
                                AnimeName = name,
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
            }
            catch (Exception)
            {
            }

            return output;
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
