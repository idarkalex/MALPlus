using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.Comm.MalSpecific
{
    public class PopularVideosQuery : Query
    {
        /// <summary>
        ///     Page number to ask for. MAL's promotion feed is paged with ?p= and every page carries
        ///     about thirty entries.
        /// </summary>
        /// <remarks>
        ///     The parameter is the number of the page, not an offset. Asking with offset=30 does nothing
        ///     at all: the URL goes through but the same first entries come back, which is the sort of
        ///     thing that reads as a server error until the correct parameter is found.
        /// </remarks>
        public PopularVideosQuery(int page = 1)
        {
            Request =
                new Uri(
                    Uri.EscapeUriString(
                        "https://myanimelist.net/watch/promotion" +
                        (page < 2 ? "" : "?p=" + page)));
        }

        public async Task<List<AnimeVideoData>> GetVideos()
        {
            var output = new List<AnimeVideoData>();
            var raw = await GetRequestResponse();
            if (string.IsNullOrEmpty(raw))
                return output;

            var doc = new HtmlDocument();
            doc.LoadHtml(raw);

            foreach (var videoNode in doc.WhereOfDescendantsWithClass("div", "video-list-outer-vertical"))
            {
                try
                {
                    var current = new AnimeVideoData();

                    var link = videoNode.ChildNodes.First(node => node.Name == "a");
                    current.Thumb = link.Attributes["data-bg"].Value;
                    if (current.Thumb.Contains("banned"))
                        continue;
                    current.AnimeId = int.Parse(link.Attributes["data-anime-id"].Value);

                    var href = link.Attributes["href"].Value;
                    var pos = href.IndexOf('?');
                    // -1, not an exception, when the href carries no query: previously that threw and the
                    // whole entry was dropped, which is exactly how a list ends up shorter than it looks.
                    href = pos < 0 ? href : href.Substring(0, pos);
                    current.YtLink = $"https://www.youtube.com/watch?v={href.Split('/').Last()}";

                    current.Name =
                        WebUtility.HtmlDecode(
                            videoNode.FirstOfDescendantsWithClass("div", "info-container").InnerText.Trim());

                    current.AnimeTitle = WebUtility.HtmlDecode(videoNode.Descendants("a").Last().InnerText.Trim());

                    output.Add(current);
                }
                catch (Exception e)
                {
                    // Logged rather than swallowed. A silent catch here turned every change in MAL's
                    // markup into "the list just stops here" with nothing to look at.
                    System.Diagnostics.Debug.WriteLine(
                        "MALPLUS promotion parse skip: " + e.GetType().Name + " :: " + e.Message);
                }
            }

            return output;
        }
    }
}
