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
        private string _type;

        /// <summary>
        ///     How many entries into the history to start, for reading the next page.
        /// </summary>
        private int _offset;

        public ProfileHistoryQuery(string source, int offset = 0, string type = null)
        {
            _source = source;
            _offset = offset;
            _type = type;
        }

        public async Task<Dictionary<string, List<MalProfileHistoryEntry>>> GetProfileHistory()
        {
            var output = new Dictionary<string, List<MalProfileHistoryEntry>>();
            try
            {
                var client = await ResourceLocator.MalHttpContextProvider.GetHttpContextAsync();
                var typePart = string.IsNullOrEmpty(_type) ? "" : "/" + _type.ToLowerInvariant();
                var offsetPart = _offset < 1 ? "" : "?offset=" + _offset;
                var url = $"https://myanimelist.net/history/{_source}{typePart}{offsetPart}";
                var raw = await (await client.GetAsync(Uri.EscapeUriString(url))).Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(raw))
                    return null;
                var doc = new HtmlDocument();
                doc.LoadHtml(raw);
                foreach (var historyRow in doc.DocumentNode.Descendants("tr"))
                {
                    try
                    {
                        if (historyRow.FirstOrDefaultOfDescendantsWithClass("div", "normal_header") != null)
                        {
                            var headerText = historyRow.InnerText.Trim();
                            if (headerText == "&nbsp;" || string.IsNullOrWhiteSpace(headerText))
                                continue;
                            if (!output.ContainsKey(headerText))
                                output.Add(headerText, new List<MalProfileHistoryEntry>());
                        }
                        else
                        {
                            var link = historyRow.Descendants("a").FirstOrDefault();
                            if (link == null)
                                continue;

                            var href = link.GetAttributeValue("href", "");
                            if (string.IsNullOrEmpty(href))
                                continue;

                            var current = new MalProfileHistoryEntry();
                            current.Title = WebUtility.HtmlDecode(link.InnerText?.Trim());
                            current.IsAnime = href.Contains("/anime");

                            var idMatch = System.Text.RegularExpressions.Regex.Match(href, @"(?:id=|\/(?:anime|manga)\/)(\d+)");
                            if (idMatch.Success && int.TryParse(idMatch.Groups[1].Value, out var parsedId))
                            {
                                current.Id = parsedId;
                            }
                            else if (int.TryParse(href.Split('=').Last(), out var fallbackId))
                            {
                                current.Id = fallbackId;
                            }

                            if (current.Id == 0)
                                continue;

                            var strong = historyRow.Descendants("strong").FirstOrDefault();
                            if (strong != null && int.TryParse(strong.InnerText?.Trim(), out var ep))
                                current.WatchedEpisode = ep;

                            var dateCell = historyRow.Descendants("td").LastOrDefault();
                            if (dateCell != null)
                                current.Date = dateCell.InnerText.Replace("Edit", "").Replace("&nbsp;", "").Trim();

                            if (output.Count == 0)
                                output["Recent"] = new List<MalProfileHistoryEntry>();

                            output.Last().Value.Add(current);
                        }
                    }
                    catch (Exception)
                    {
                        //html
                    }

                }
            }
            catch (Exception)
            {
                //malsth
            }


            return output;
        }
    }
}
