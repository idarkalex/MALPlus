using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MALPlus.Models.Models.MalSpecific;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;

namespace MALPlus.XShared.Comm.MagicalRawQueries.Messages
{
    public class MalMessageDetailsQuery
    {
        public async Task<MalMessageModel> GetMessageDetails(MalMessageModel msg, bool sentMessage)
        {
            try
            {
                if (string.IsNullOrEmpty(msg.Id) || msg.Id == "0")
                    return msg;

                var client = await ResourceLocator.MalHttpContextProvider.GetHttpContextAsync();
                var response = await client.GetAsync($"https://myanimelist.net/mymessages.php?go=read&id={msg.Id}{(sentMessage ? "&f=1" : "")}");
                var raw = await response.Content.ReadAsStringAsync();

                var doc = new HtmlDocument();
                doc.LoadHtml(raw);

                var msgNode = doc.FirstOfDescendantsWithClass("td", "dialog-text")
                           ?? doc.FirstOfDescendantsWithClass("div", "dialog-text")
                           ?? doc.FirstOfDescendantsWithClass("div", "pmessage-text");

                if (msgNode != null)
                {
                    var text = msgNode.InnerText.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        msg.Content = WebUtility.HtmlDecode(text);
                    }

                    try
                    {
                        foreach (var img in msgNode.Descendants("img"))
                        {
                            var src = img.Attributes["src"]?.Value;
                            if (!string.IsNullOrEmpty(src))
                                msg.Images.Add(src);
                        }
                    }
                    catch { }
                }

                var replyMatch = Regex.Match(raw, @"[?&](?:amp;)?replyid=(\d+)");
                if (replyMatch.Success) msg.ReplyId = replyMatch.Groups[1].Value;

                var threadMatch = Regex.Match(raw, @"[?&](?:amp;)?threadid=(\d+)");
                if (threadMatch.Success) msg.ThreadId = threadMatch.Groups[1].Value;

                if (string.IsNullOrEmpty(msg.ReplyId)) msg.ReplyId = msg.Id;
                if (string.IsNullOrEmpty(msg.ThreadId)) msg.ThreadId = msg.Id;

                return msg;
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS GetMessageDetails failed: " + ex.Message);
            }

            return msg ?? new MalMessageModel();
        }

        public async Task<List<MalMessageModel>> GetMessagesInThread(MalMessageModel msg)
        {
            var output = new List<MalMessageModel>();
            if (msg == null) return output;

            try
            {
                var queryId = !string.IsNullOrEmpty(msg.Id) && msg.Id != "0" ? msg.Id : msg.ThreadId;
                if (string.IsNullOrEmpty(queryId) || queryId == "0")
                {
                    output.Add(msg);
                    return output;
                }

                var client = await ResourceLocator.MalHttpContextProvider.GetHttpContextAsync();
                var threadParam = !string.IsNullOrEmpty(msg.ThreadId) && msg.ThreadId != "0" ? $"&threadid={msg.ThreadId}" : "";
                var sentParam = msg.IsMine ? "&f=1" : "";
                var url = $"https://myanimelist.net/mymessages.php?go=read&id={queryId}{threadParam}{sentParam}";

                var response = await client.GetAsync(url);
                var raw = await response.Content.ReadAsStringAsync();

                var doc = new HtmlDocument();
                doc.LoadHtml(raw);

                // 1. Update the main message from this page
                var msgNode = doc.FirstOfDescendantsWithClass("td", "dialog-text")
                           ?? doc.FirstOfDescendantsWithClass("div", "dialog-text")
                           ?? doc.FirstOfDescendantsWithClass("div", "pmessage-text");

                if (msgNode != null)
                {
                    var text = msgNode.InnerText.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        msg.Content = WebUtility.HtmlDecode(text);
                    }

                    try
                    {
                        foreach (var img in msgNode.Descendants("img"))
                        {
                            var src = img.Attributes["src"]?.Value;
                            if (!string.IsNullOrEmpty(src))
                                msg.Images.Add(src);
                        }
                    }
                    catch { }
                }

                var replyMatch = Regex.Match(raw, @"[?&](?:amp;)?replyid=(\d+)");
                if (replyMatch.Success) msg.ReplyId = replyMatch.Groups[1].Value;

                var threadMatch = Regex.Match(raw, @"[?&](?:amp;)?threadid=(\d+)");
                if (threadMatch.Success) msg.ThreadId = threadMatch.Groups[1].Value;

                if (string.IsNullOrEmpty(msg.ReplyId)) msg.ReplyId = msg.Id;
                if (string.IsNullOrEmpty(msg.ThreadId)) msg.ThreadId = msg.Id;

                // 2. Parse any previous message history in table.pmessage-message-history
                var historyTable = doc.FirstOfDescendantsWithClass("table", "pmessage-message-history");
                var historyItems = new List<MalMessageModel>();
                if (historyTable != null)
                {
                    foreach (var tr in historyTable.Descendants("tr"))
                    {
                        var tds = tr.Descendants("td").ToList();
                        if (tds.Count >= 3)
                        {
                            var historyItem = new MalMessageModel
                            {
                                Subject = msg.Subject,
                                Date = tds[0].InnerText.Trim(),
                                Sender = tds[1].InnerText.Trim(),
                                Content = WebUtility.HtmlDecode(tds[2].InnerText.Trim()),
                                ThreadId = msg.ThreadId,
                                ReplyId = msg.ReplyId,
                                Target = msg.Target
                            };

                            if (string.IsNullOrEmpty(historyItem.Content))
                                continue;

                            try
                            {
                                foreach (var img in tds[2].Descendants("img"))
                                {
                                    var src = img.Attributes["src"]?.Value;
                                    if (!string.IsNullOrEmpty(src))
                                        historyItem.Images.Add(src);
                                }
                            }
                            catch { }

                            historyItems.Add(historyItem);
                        }
                    }
                }

                if (historyItems.Count > 0)
                {
                    // table.pmessage-message-history on MAL already contains all messages in the conversation.
                    var unique = new List<MalMessageModel>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in historyItems)
                    {
                        if (string.IsNullOrWhiteSpace(item.Content)) continue;
                        var norm = Regex.Replace(item.Content.Trim(), @"\s+", " ");
                        var key = $"{item.Sender?.Trim()}|{norm}";
                        if (seen.Add(key))
                        {
                            unique.Add(item);
                        }
                    }
                    return unique.Count > 0 ? unique : historyItems;
                }
                else
                {
                    output.Add(msg);
                    return output;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS GetMessagesInThread failed: " + ex.Message);
            }

            // Fallback: return at least the message the user clicked on
            if (output.Count == 0 && msg != null)
            {
                output.Add(msg);
            }
            return output;
        }
    }
}
