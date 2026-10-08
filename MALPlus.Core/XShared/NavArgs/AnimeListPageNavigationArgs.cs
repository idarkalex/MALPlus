using MALPlus.Models.Enums;
using MALPlus.Models.Models.Anime;
using MALPlus.XShared.Comm.Anime;

namespace MALPlus.XShared.NavArgs
{
    public class AnimeListPageNavigationArgs
    {
        public int SelectedItemIndex = -1;
        public bool ResetBackNav = true;
        public bool FromMore;
        public readonly bool Descending;
        public readonly bool HideNotAired;
        public readonly bool NavArgs;
        public readonly int Status;
        public readonly int? StatusIndex;
        public AnimeSeason CurrSeason;
        public AnimeListDisplayModes DisplayMode;
        public string ListSource;
        public SortOptions SortOption;
        public TopAnimeType TopWorkMode = TopAnimeType.General;
        public MangaTopType MangaTopWorkMode = MangaTopType.All;
        public MangaAdaptedType MangaAdaptedWorkMode = MangaAdaptedType.AiringNow;
        public AnimeListWorkModes WorkMode = AnimeListWorkModes.Anime;
        public AnimeGenreSearch Genre;
        public AnimeStudios Studio;

        /// <summary>
        ///     Which band of MAL's ranking an unfiltered top list is showing, or -1 for the whole list.
        /// </summary>
        /// <remarks>
        ///     Only the tabs that ARE the ranking read it. The filtered ones (Airing, TV, Upcoming and
        ///     the rest) page through their own slice and have no band to speak of.
        /// </remarks>
        public int TopRangeIndex = -1;

        public AnimeListPageNavigationArgs(SortOptions sort, int status, bool desc,
            AnimeListWorkModes seasonal, string source, AnimeSeason season, AnimeListDisplayModes dispMode)
        {
            SortOption = sort;
            Status = status;
            Descending = desc;
            WorkMode = seasonal;
            ListSource = source;
            NavArgs = true;
            CurrSeason = season;
            DisplayMode = dispMode;
        }

        private AnimeListPageNavigationArgs()
        {
        }

        public AnimeListPageNavigationArgs(int filterIndex, AnimeListWorkModes workMode)
        {
            WorkMode = workMode;
            StatusIndex = filterIndex;
        }

        public AnimeListPageNavigationArgs(AnimeStudios studio)
        {
            WorkMode = AnimeListWorkModes.AnimeByStudio;
            Studio = studio;
        }

        public AnimeListPageNavigationArgs(AnimeGenreSearch genre)
        {
            WorkMode = AnimeListWorkModes.AnimeByGenre;
            Genre = genre;
        }

        public static AnimeListPageNavigationArgs Seasonal
            => new AnimeListPageNavigationArgs { WorkMode = AnimeListWorkModes.SeasonalAnime };

        /// <summary>
        ///     Seasonal with the season named outright.
        /// </summary>
        /// <remarks>
        ///     Coming back from an anime re-enters Seasonal through the args the details page
        ///     registered as its back navigation, and a bare <see cref="Seasonal" /> carries no
        ///     season, so the view model would put the current one on screen instead of the one the
        ///     user was looking at.
        /// </remarks>
        public static AnimeListPageNavigationArgs SeasonalFor(AnimeSeason season)
            => new AnimeListPageNavigationArgs
            {
                WorkMode = AnimeListWorkModes.SeasonalAnime,
                CurrSeason = season
            };

        public static AnimeListPageNavigationArgs Manga
            => new AnimeListPageNavigationArgs { WorkMode = AnimeListWorkModes.Manga };


        public static AnimeListPageNavigationArgs TopManga
            => new AnimeListPageNavigationArgs { WorkMode = AnimeListWorkModes.TopManga };

        public static AnimeListPageNavigationArgs TopMangaCategory(MangaTopType type) =>
            new AnimeListPageNavigationArgs { WorkMode = AnimeListWorkModes.TopManga, MangaTopWorkMode = type };

        public static AnimeListPageNavigationArgs TopAnime(TopAnimeType type) =>
            new AnimeListPageNavigationArgs { WorkMode = AnimeListWorkModes.TopAnime, TopWorkMode = type };

        public static AnimeListPageNavigationArgs MangaAdapted(MangaAdaptedType type) =>
            new AnimeListPageNavigationArgs { WorkMode = AnimeListWorkModes.MangaAdapted, MangaAdaptedWorkMode = type };
    }




}
