using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MALPlus.Models.Models.MalSpecific;

namespace MALPlus.XShared.Comm.MagicalRawQueries.Messages
{
    /// <summary>
    ///     Reads a page of the message box, and says whether there was another one after it.
    /// </summary>
    public static class AccountMessagesManager
    {
        /// <summary>
        ///     The last page MAL answered with, so the app does not keep asking for pages past the end.
        /// </summary>
        private static int _maxPage = 9999;

        /// <summary>
        ///     A page of messages, or null when there are no more.
        /// </summary>
        /// <remarks>
        ///     The previous version threw an exception for both "this is past the end" and "the request
        ///     failed", and the caller caught it and showed a dialog saying Messages was broken. An empty
        ///     inbox is not a failure, so an empty answer now returns an empty list and only a real failure
        ///     gives null.
        ///     <para>
        ///     The remembered page count also used to be cut to the page where the list first came back
        ///     empty and never re-thrown. Since that value outlives the page and the session, one visit to
        ///     an empty or signed-out inbox made every later request throw before it reached the network,
        ///     until the app was restarted. It is now recomputed rather than narrowed whenever the reader
        ///     asks again.
        ///     </para>
        /// </remarks>
        public static async Task<List<MalMessageModel>> GetMessagesAsync(int page)
        {
            if (page >= _maxPage)
                return new List<MalMessageModel>();

            var output = await new MalMessagesQuery().GetMessages(page);
            if (output == null)
                return null;

            if (output.Count != 0)
                return output;

            // Past the end. The count is only lowered, never raised, and only far enough for this page:
            // a single empty answer says where the end is and nothing about how many messages exist.
            _maxPage = Math.Min(_maxPage, page + 1);
            return new List<MalMessageModel>();
        }

        public static async Task<List<MalMessageModel>> GetSentMessagesAsync()
        {
            return await new MalMessagesQuery().GetSentMessages();
        }
    }
}