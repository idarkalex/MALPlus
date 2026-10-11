using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MALPlus.Models.Models.MalSpecific;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;

namespace MALPlus.XShared.Comm.Profile
{
    public class ProfileHistoryQuery : Query
    {
        private string _source;

        /// <summary>
        ///     How many entries into the history to start, for reading the next page.
        /// </summary>
        private int _offset;

        public ProfileHistoryQuery(string source, int offset = 0)
        {
            _source = source;
            _offset = offset;
        }

        public async Task<Dictionary<string, List<MalProfileHistoryEntry>>> GetProfileHistory()
        {
            var output = new Dictionary<string, List<MalProfileHistoryEntry>>();
            try
            {
                var client = await ResourceLocator.MalHttpContextProvider.GetHttpContextAsync();
                // Offset is appended only when there is one, so the first request is byte for byte the
                // request the page used to make and a reader who never scrolls sees no difference.
                var offsetPart = _offset < 1 ? "" : "?offset=" + _offset;
                var raw = await (await client.GetAsync(
                    Uri.EscapeUriString($"https://myanimelist.net/history/{_source}{offsetPart}"))).Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(raw))
                    return null;
                var doc = new HtmlDocument();
                doc.LoadHtml(raw);
                foreach (var historyRow in doc.DocumentNode.Descendants("tr"))
                {
                    try
                    {
                        //so this is one big table if it contains only on chld it means that it's day/week header so
                        if (historyRow.FirstOrDefaultOfDescendantsWithClass("div", "normal_header") != null)
                        {
                            if (historyRow.InnerText.Trim() == "&nbsp;")
                                continue;
                            output.Add(historyRow.InnerText.Trim(), new List<MalProfileHistoryEntry>());
                        }
                        else
                        {
                            var current = new MalProfileHistoryEntry();

                            var link = historyRow.Descendants("a").First();
                            current.Id = int.Parse(link.Attributes["href"].Value.Split('=').Last());
                            current.IsAnime = link.Attributes["href"].Value.Contains("/anime");
                            current.WatchedEpisode = int.Parse(historyRow.Descendants("strong").First().InnerText);
                            current.Date = historyRow.Descendants("td").Last().InnerText.Replace("Edit","").Replace("&nbsp;", "").Trim(); //skip "Edit" button
                            output.Last().Value.Add(current);
                        }
                    }
                    catch (Exception e)
                    {
                        //html
                    }

                }
            }
            catch (Exception e)
            {
                //malsth
            }


            return output;
        }
    }
}
