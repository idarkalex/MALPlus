using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MALPlus.Models.Models.AnimeScrapped;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.Comm.MalSpecific
{
    public class PopularVideosQuery : Query
    {
        private readonly int _page;

        public PopularVideosQuery(int page = 1)
        {
            _page = Math.Max(page, 1);
            Request =
                new Uri(
                    Uri.EscapeUriString(
                        "https://myanimelist.net/watch/promotion" +
                        (page < 2 ? "" : "?p=" + page)));
        }

        public async Task<List<AnimeVideoData>> GetVideos()
        {
            // 1. Native Tenrai /top/videos (fast, structured, statistics-ranked)
            var tenraiVideos = await FetchFromTenraiAsync();
            if (tenraiVideos != null && tenraiVideos.Count > 0)
                return tenraiVideos;

            // 2. Fallback to HTML scraper
            return await FetchFromHtmlAsync();
        }

        private async Task<List<AnimeVideoData>> FetchFromTenraiAsync()
        {
            try
            {
                var (items, _) = await TenraiClient.GetPaginatedAsync($"top/videos?page={_page}");
                if (items == null || items.Count == 0)
                    return null;

                var output = new List<AnimeVideoData>();
                foreach (var item in items)
                {
                    try
                    {
                        var ytUrl = item.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
                            ? urlProp.GetString() : null;
                        if (string.IsNullOrEmpty(ytUrl))
                            continue;

                        var thumb = "";
                        if (item.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Object)
                        {
                            if (images.TryGetProperty("large_image_url", out var lProp) && lProp.ValueKind == JsonValueKind.String)
                                thumb = lProp.GetString();
                            else if (images.TryGetProperty("maximum_image_url", out var mProp) && mProp.ValueKind == JsonValueKind.String)
                                thumb = mProp.GetString();
                            else if (images.TryGetProperty("image_url", out var dProp) && dProp.ValueKind == JsonValueKind.String)
                                thumb = dProp.GetString();
                        }

                        int animeId = 0;
                        string animeTitle = "";
                        string videoName = "";

                        if (item.TryGetProperty("entry", out var entryArr) && entryArr.ValueKind == JsonValueKind.Array)
                        {
                            var firstEntry = entryArr.EnumerateArray().FirstOrDefault();
                            if (firstEntry.ValueKind == JsonValueKind.Object)
                            {
                                if (firstEntry.TryGetProperty("mal_id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                                    animeId = idProp.GetInt32();
                                if (firstEntry.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                                    animeTitle = titleProp.GetString();
                                if (firstEntry.TryGetProperty("video_title", out var vtProp) && vtProp.ValueKind == JsonValueKind.String)
                                    videoName = vtProp.GetString();
                            }
                        }

                        if (string.IsNullOrEmpty(videoName))
                        {
                            if (item.TryGetProperty("title", out var tProp) && tProp.ValueKind == JsonValueKind.String)
                                videoName = tProp.GetString();
                        }

                        output.Add(new AnimeVideoData
                        {
                            AnimeId = animeId,
                            AnimeTitle = animeTitle,
                            Name = videoName,
                            Thumb = thumb,
                            YtLink = ytUrl
                        });
                    }
                    catch
                    {
                    }
                }
                return output.Count > 0 ? output : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MALPLUS top/videos Tenrai failed: {ex.Message}");
                return null;
            }
        }

        private async Task<List<AnimeVideoData>> FetchFromHtmlAsync()
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
                    System.Diagnostics.Debug.WriteLine(
                        "MALPLUS promotion parse skip: " + e.GetType().Name + " :: " + e.Message);
                }
            }

            return output;
        }
    }
}
