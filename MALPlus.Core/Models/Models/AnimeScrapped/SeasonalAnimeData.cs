using System.Collections.Generic;

namespace MALPlus.Models.Models.AnimeScrapped
{
    public class SeasonalAnimeData : ISeasonalAnimeBaseData
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public List<string> Genres { get; set; }
        public float Score { get; set; }
        public string ImgUrl { get; set; }
        public string Episodes { get; set; }
        public int Index { get; set; }
        public int AirDay { get; set; }
        public string AirStartDate { get; set; }

            /// <summary>
            ///     MALPlus.Models.Enums.AnimeType / MangaType value. 0 means "unknown" and
            ///     hides the format badge; entries that already live in the user's library
            ///     carry their own type and ignore this.
            /// </summary>
            public int Type { get; set; }

            /// <summary>
            ///     MAL's content rating, as the source spells it: "PG-13", "R - 17+",
            ///     "R+ - Mild Nudity", "Rx - Hentai".
            /// </summary>
            /// <remarks>
            ///     Needed so a caller can apply the content setting to the list itself, which is the
            ///     only place a rating is available: the panel that draws the list already holds it.
            /// </remarks>
            public string Rating { get; set; }
    }
}