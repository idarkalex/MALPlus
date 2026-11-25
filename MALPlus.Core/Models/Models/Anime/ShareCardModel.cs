using System;

namespace MALPlus.Models.Models.Anime
{
    public class ShareCardModel
    {
        public string Title { get; set; }
        public int Id { get; set; }
        public bool IsAnime { get; set; } = true;
        public string ImageUrl { get; set; }
        public string StatusText { get; set; }
        public string ProgressText { get; set; }
        public int Score { get; set; }
        public float GlobalScore { get; set; }
        public string Type { get; set; }
        public string SeasonTag { get; set; }
        public int TotalEpisodes { get; set; }
        public int WatchedEpisodes { get; set; }
        public int RewatchCount { get; set; }
        public string ShareText { get; set; }
        public string Url { get; set; }
    }
}
