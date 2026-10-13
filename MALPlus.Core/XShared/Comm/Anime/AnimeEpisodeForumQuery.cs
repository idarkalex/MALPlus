using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MALPlus.Models.Models.Anime;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.Comm.Anime
{
    /// <summary>
    /// MAL only creates an episode page once the episode is well past, so an airing cour can
    /// be missing its last one or two rows even though they already aired and already have a
    /// discussion thread. This reads the per-anime forum index and maps "Episode N
    /// Discussion" back to its topic id, so those rows still open the real thread.
    /// </summary>
    public class AnimeEpisodeForumQuery : Query
    {
        private readonly int _animeId;
        private readonly bool _animeMode;

        public AnimeEpisodeForumQuery(int animeId, bool animeMode)
        {
            _animeId = animeId;
            _animeMode = animeMode;
        }

        /// <summary>
        ///     Windows of the entry's community board that get fetched, and unioned.
        ///     <para>
        ///     This started out reading /anime/{id}/forum, which is the wrong page entirely: it
        ///     carries a "Recent Forum Discussion" block of exactly five rows, and on One Piece
        ///     those five were episodes 1179, 1178, 546, 545 and 544. Anything else simply had no
        ///     link, which is why 1180 looked broken while 1179 and 1178 worked.
        ///     </para>
        ///     <para>
        ///     The real source is /forum/?animeid={id}. It hard caps a page at 50 threads and picks
        ///     the slice with a "show" parameter that behaves as a window selector rather than an
        ///     offset: on One Piece show=50 topped out at episode 1176, show=100 at 1177, show=150
        ///     at 1173, show=1000 at 1134, and the negative values all returned the newest slice
        ///     with 1180. No single value covered 1175 through 1180, so three are unioned. That is
        ///     measured, not designed, and it takes 144 episodes for One Piece against 49 on any
        ///     one page. Anything still missing simply keeps its row without a thread link.
        ///     </para>
        /// </summary>
        private static readonly int[] BoardWindows = { 50, 100, -50 };

        private Uri BoardUri(int window) => new(
            $"https://myanimelist.net/forum/?{(_animeMode ? "animeid" : "mangaid")}={_animeId}&show={window}");

        /// <summary>
        ///     One anchor, then read the episode number out of its text. Anchoring on topicid
        ///     followed by the closing quote is what keeps the "...&amp;show=50" page links in the
        ///     row from being read as the thread itself, and insisting on &lt;/a&gt; rather than any
        ///     tag means the title is the whole link and not a fragment that happens to contain the
        ///     word Episode.
        /// </summary>
        private static readonly Regex TopicPattern = new Regex(
            "topicid=(\\d+)\"[^>]{0,140}>([^<]{0,140}?)</a>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EpisodePattern = new Regex(
            "Episode\\s*(\\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public async Task<Dictionary<int, string>> GetEpisodeTopicsAsync()
        {
            var result = new Dictionary<int, string>();
            if (_animeId <= 0)
                return result;
            try
            {
                var pages = await Task.WhenAll(
                    BoardWindows.Select(window => GetAnonymousRequestResponse(BoardUri(window))));
                foreach (var html in pages)
                {
                    if (string.IsNullOrEmpty(html))
                        continue;
                    foreach (Match match in TopicPattern.Matches(html))
                    {
                        if (!int.TryParse(match.Groups[1].Value, out var topicId) || topicId <= 0)
                            continue;
                        var number = EpisodePattern.Match(match.Groups[2].Value);
                        if (!number.Success || !int.TryParse(number.Groups[1].Value, out var episodeNumber)
                            || episodeNumber <= 0)
                            continue;
                        if (!result.ContainsKey(episodeNumber))
                            result[episodeNumber] = $"https://myanimelist.net/forum/?topicid={topicId}";
                    }
                }
            }
            catch
            {
                // A missing forum index only costs us the thread link, never the row.
            }
            return result;
        }
    }
}

