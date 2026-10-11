using System;
using MALPlus.Models.Enums;

namespace MALPlus.Models.Models.Library
{
    public interface ILibraryData
    {
        int Id { get; set; }
        int MalId { get; set; }
        string Title { get; set; }
        AnimeStatus MyStatus { get; set; }
        float MyScore { get; set; }

        /// <summary>
        ///     Community mean score of the entry, 0 when unknown. It is not the user's score, which is
        ///     <see cref="MyScore" />. Lives on the library payload itself so a card can show it without the
        ///     entry having been opened, and so the abstraction has one place to read it from whatever
        ///     query produced the entry.
        /// </summary>
        float GlobalScore { get; set; }
        int MyEpisodes { get; set; }
        int AllEpisodes { get; set; }
        string ImgUrl { get; set; }
        int Type { get; set; }
        DateTime LastWatched { get; set; }
        string MyStartDate { get; set; }
        string MyEndDate { get; set; }
        string Notes { get; set; }
        bool IsRewatching { get; set; }
        string AlternateTitle { get; set; }
        AnimePriority Priority { get; set; }
        int RewatchingCount { get; set; }
    }
}