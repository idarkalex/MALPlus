using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MALPlus.Models.Models.Forums;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.Comm.Forums
{
    public class ForumBoardIndexContentQuery : Query
    {
        public ForumBoardIndexContentQuery()
        {
            Request =
                new Uri(Uri.EscapeUriString("https://myanimelist.net/forum/"));
        }

        public async Task<ForumIndexContent> GetPeekPosts()
        {
            var output = new ForumIndexContent();
            var raw = await GetRequestResponse();
            if (string.IsNullOrEmpty(raw))
                return null;
            var doc = new HtmlDocument();
            doc.LoadHtml(raw);

            var list = new List<ForumBoardEntryPeekPost>(2);
            int i = 0;
            foreach (var topics in doc.WhereOfDescendantsWithClass("ul", "topics"))
            {
                try
                {
                    var lis = topics.Descendants("li").ToList();
                    if (lis.Count == 0)
                    {
                        output.ForumBoardEntryPeekPosts.Add(new List<ForumBoardEntryPeekPost>());
                        continue;
                    }
                    bool addMissing = lis.Count == 1;
                    foreach (var post in lis)
                    {
                        i++;
                        try
                        {
                            if (i == 5 || i == 6) //skip db midifiaction board
                                continue;
                            //if(i == 9) //add one more because suggestions have one post
                            //    list.Add(new ForumBoardEntryPeekPost());
                            if(addMissing)
                                list.Add(new ForumBoardEntryPeekPost());

                            var current = new ForumBoardEntryPeekPost();
                            var rawDate = WebUtility.HtmlDecode(post.FirstOfDescendantsWithClass("span", "date di-ib pt4 fs10 fn-grey4")?.InnerText ?? string.Empty);
                            var cleanDate = rawDate.Trim();
                            var byIdx = cleanDate.IndexOf("by ", StringComparison.OrdinalIgnoreCase);
                            if (byIdx > 0)
                                cleanDate = cleanDate.Substring(0, byIdx).Trim().TrimEnd('\r', '\n', '\t', ' ');
                            else
                                cleanDate = cleanDate.Replace("»", "").Trim();
                            current.PostTime = cleanDate;
                            current.ParsedTime = ParseMalForumDate(cleanDate);

                            var titleNode = post.FirstOfDescendantsWithClass("a", "topic-title-link");
                            current.Title = WebUtility.HtmlDecode(titleNode.InnerText);
                            var link = titleNode.Attributes["href"]?.Value ?? string.Empty;
                            var idMatch = System.Text.RegularExpressions.Regex.Match(link, @"topicid=(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            current.Id = idMatch.Success ? idMatch.Groups[1].Value : link.Split('=').Last();
                            var img = post.Descendants("img").FirstOrDefault();
                            if (img != null)
                            {
                                if (img.Attributes.Contains("data-src") && !string.IsNullOrWhiteSpace(img.Attributes["data-src"].Value))
                                    current.User.ImgUrl = img.Attributes["data-src"].Value;
                                else if (img.Attributes.Contains("src"))
                                    current.User.ImgUrl = img.Attributes["src"].Value;
                                current.User.Name = img.Attributes["alt"]?.Value ?? string.Empty;
                            }
                            list.Add(current);
                            if (list.Count == 2) //assume we have 2 for each board
                            {
                                output.ForumBoardEntryPeekPosts.Add(list);
                                list = new List<ForumBoardEntryPeekPost>();
                            }
                        }
                        catch (Exception)
                        {
                            //
                        }
                    }
                }
                catch (Exception)
                {
                   //
                }
            }

            try
            {
                int block = 0;
                var sideBlocks = doc.WhereOfDescendantsWithClass("div", "forum-side-block").Take(4);
                foreach (var sideBlock in sideBlocks)
                {
                    var postList = new List<ForumPostEntry>();
                    foreach (var post in sideBlock.Descendants("li"))
                    {
                        var current = new ForumPostEntry();

                        var titleNode = post.FirstOfDescendantsWithClass("a", "title");
                        var link = titleNode.Attributes["href"]?.Value ?? string.Empty;
                        var idMatch = System.Text.RegularExpressions.Regex.Match(link, @"topicid=(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        current.Id = idMatch.Success ? idMatch.Groups[1].Value : link.Split('=').Last();
                        current.Title = WebUtility.HtmlDecode(titleNode.InnerText.Trim());

                        current.ImgUrl = post.Descendants("img").First().Attributes["src"].Value;

                        var infoSpan = post.FirstOfDescendantsWithClass("span", "information di-ib fs10 fn-grey4");

                        current.Created = infoSpan.ChildNodes[0].InnerText.Trim();
                        current.Created = current.Created.Substring(0, current.Created.Length - 3); //remove "by"

                        current.Op = infoSpan.Descendants("a").First().InnerText.Trim();

                        postList.Add(current);
                    }
                    switch (block)
                    {
                        case 0:
                            output.PopularNewTopics = postList;
                            break;
                        case 1:
                            output.RecentPosts = postList;
                            break;
                        case 2:
                            output.AnimeSeriesDisc = postList;
                            break;
                        default:
                            output.MangaSeriesDisc = postList;
                            break;
                    }
                    block++;
                }
            }
            catch (Exception)
            {
                //
            }

            return output;
        }

        public static DateTime ParseMalForumDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return DateTime.MinValue;

            s = s.Trim();
            var byIdx = s.IndexOf("by ", StringComparison.OrdinalIgnoreCase);
            if (byIdx > 0)
                s = s.Substring(0, byIdx).Trim().TrimEnd('\r', '\n', '\t', ' ');
            s = s.Replace("»", "").Trim();

            var now = DateTime.UtcNow;

            // "X sec/seconds ago"
            var secMatch = System.Text.RegularExpressions.Regex.Match(s, @"^(\d+)\s+sec", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (secMatch.Success && int.TryParse(secMatch.Groups[1].Value, out var secs))
                return now.AddSeconds(-secs);

            // "X min/minutes ago"
            var minMatch = System.Text.RegularExpressions.Regex.Match(s, @"^(\d+)\s+min", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (minMatch.Success && int.TryParse(minMatch.Groups[1].Value, out var mins))
                return now.AddMinutes(-mins);

            // "X hour/hours ago"
            var hrMatch = System.Text.RegularExpressions.Regex.Match(s, @"^(\d+)\s+hour", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (hrMatch.Success && int.TryParse(hrMatch.Groups[1].Value, out var hrs))
                return now.AddHours(-hrs);

            // "Today, 12:18 AM"
            if (s.StartsWith("Today", StringComparison.OrdinalIgnoreCase))
            {
                var timePart = s.Substring("Today".Length).Trim(',', ' ');
                if (DateTime.TryParse(timePart, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t))
                    return new DateTime(now.Year, now.Month, now.Day, t.Hour, t.Minute, 0, DateTimeKind.Utc);
                return now;
            }

            // "Yesterday, 6:41 PM"
            if (s.StartsWith("Yesterday", StringComparison.OrdinalIgnoreCase))
            {
                var timePart = s.Substring("Yesterday".Length).Trim(',', ' ');
                var yesterday = now.AddDays(-1);
                if (DateTime.TryParse(timePart, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var t))
                    return new DateTime(yesterday.Year, yesterday.Month, yesterday.Day, t.Hour, t.Minute, 0, DateTimeKind.Utc);
                return yesterday;
            }

            // Standard formats: "Nov 7, 10:35 AM", "Oct 5, 2015 6:00 AM", "Nov 9", "Dec 20, 2025"
            var formats = new[]
            {
                "MMM d, yyyy h:mm tt",
                "MMM d, yyyy hh:mm tt",
                "MMM d, yyyy",
                "MMM d, h:mm tt",
                "MMM d, hh:mm tt",
                "MMM d",
                "MMMM d, yyyy",
                "MMMM d"
            };

            if (DateTime.TryParseExact(s, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
            {
                if (dt.Year == 1) // year omitted in format
                {
                    dt = new DateTime(now.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, DateTimeKind.Utc);
                    if (dt > now)
                        dt = dt.AddYears(-1);
                }
                return dt;
            }

            if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedGeneral))
                return parsedGeneral;

            return DateTime.MinValue;
        }
    }
}
