using System;
using System.Threading.Tasks;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Anime;

namespace MALPlus.Models.Models.AnimeScrapped
{
    public class RecommendationData
    {
        private bool _loaded;
        //Keys
        public string DependentTitle { get; set; }
        public int DependentId { get; set; }
        public string RecommendationTitle { get; set; }
        public int RecommendationId { get; set; }

        public bool IsAnime { get; set; }

        public string Description { get; set; }

        /// <summary>
        ///     Cover of the entry the recommendation is based on, the "If you liked..." half.
        /// </summary>
        /// <remarks>
        ///     Both covers were already in the page the query scrapes and were being thrown away: the
        ///     parse only looked at anchors, so every card it built had no picture and rendered as the
        ///     card's own dark backing. Reading them costs no extra request.
        /// </remarks>
        public string DependentImageUrl { get; set; }

        /// <summary>
        ///     Cover of the suggested entry, the "...then you might like" half.
        /// </summary>
        public string RecommendationImageUrl { get; set; }

        /// <summary>
        ///     Who wrote the recommendation. The page prints it as "Anime rec by someone - 2 hours ago".
        /// </summary>
        public string RecommendedBy { get; set; }

        /// <summary>
        ///     How long ago it was written, as the page words it. Kept as text rather than parsed into
        ///     a date because the page's own wording is the useful part and it is not a stable format.
        /// </summary>
        public string RecommendedWhen { get; set; }

        public int Votes { get; set; }
        public bool HasVotes => Votes > 0;
        public string VotesText => Votes == 1 ? "1 vote" : $"{Votes:N0} votes";

        public string DependentType { get; set; }
        public double DependentScore { get; set; }
        public int DependentAllEpisodes { get; set; }

        public string RecommendationType { get; set; }
        public double RecommendationScore { get; set; }
        public int RecommendationAllEpisodes { get; set; }

        public AnimeGeneralDetailsData AnimeDependentData { get; set; }

        public AnimeGeneralDetailsData AnimeRecommendationData { get; set; }

    }
}