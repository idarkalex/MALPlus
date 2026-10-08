using MALPlus.Models.Enums;
using MALPlus.Models.Models.Anime;
using MALPlus.XShared.Comm.Anime;
using MALPlus.XShared.Utils;

namespace MALPlus.XShared.Comm
{
    /// <summary>
    ///     One place that decides how much of MAL's catalogue a request is allowed to see.
    /// </summary>
    /// <remarks>
    ///     The source filter was written into the query string of every Tenrai call by hand, which
    ///     meant there was nowhere to turn it off and no way to see from one place what the app was
    ///     asking for. All of that now goes through here.
    ///     <para>
    ///     Only the strictest mode can be expressed in the query, because that is the only filter the
    ///     source offers. The other two have to ask for the same page without it and drop rows on the
    ///     way in, against the content rating the source already sends on every entry. That is why the
    ///     decision is made on <c>rating</c> and not on genres: the genre of an entry never says whether
    ///     it is explicit, and the rating does.
    ///     </para>
    /// </remarks>
    public static class ContentFilter
    {
        /// <summary>
        ///     The strictest mode, handed to the source so the work is done there.
        /// </summary>
        public static bool UsesSourceFilter => Settings.ContentFilter == ContentFilterMode.Safe;

        /// <summary>
        ///     Adds the content decision to a query as an explicit parameter.
        /// </summary>
        /// <remarks>
        ///     Every caller used to write the filter into its own query string by hand, which left the
        ///     decision spread over nine files and nowhere to turn it off.
        ///     <para>
        ///     The filter is always written, in one form or the other, and this is the reason that
        ///     matters: the subcategory tables return nothing at all when they have no query to give
        ///     back, because an empty answer is what they use to mean "I do not know this one". So the
        ///     two looser modes used to fall into that and the whole list silently stopped loading. The
        ///     loose modes therefore ask for the filter to be off rather than asking not to send it,
        ///     which the endpoint accepts and answers with the same fifty rows either way.
        ///     </para>
        /// </remarks>
        public static string Query(string query)
        {
            var filter = UsesSourceFilter ? "sfw" : "sfw=false";
            var combined = string.IsNullOrWhiteSpace(query) ? filter : query + "&" + filter;
            return combined.Replace("&&", "&").Trim('&');
        }

        /// <summary>
        ///     What to hand the user library endpoint's own adult-content flag.
        /// </summary>
        /// <remarks>
        ///     The library call was hardcoded to ask for adult entries as well, so in the strictest mode
        ///     the user's own list could still surface something the catalogue lists hide. That is the
        ///     filter being applied to one door and not the next.
        /// </remarks>
        public static string NsfwFlag =>
            Settings.ContentFilter == ContentFilterMode.Safe ? "false" : "true";

        /// <summary>
        ///     A short tag for the content mode, to go into a cache file name.
        /// </summary>
        /// <remarks>
        ///     A cached list is only the answer to the question that was asked of it. Switching the
        ///     content setting asks a different one, and without this the app went on serving the list
        ///     built under the previous setting for as long as the file lived, which is how the strict
        ///     mode's missing entries stayed missing after the setting was loosened. Keying on the mode
        ///     costs one extra file per mode and needs nothing to be invalidated by hand.
        /// </remarks>
        public static string CacheTag =>
            Settings.ContentFilter switch
            {
                ContentFilterMode.Mature => "m",
                ContentFilterMode.Unfiltered => "u",
                _ => "s"
            };

        /// <summary>
        ///     True when a manga subcategory may be offered at all under the current setting.
        /// </summary>
        /// <remarks>
        ///     Doujinshi is the one subcategory that is nearly the explicit tier by definition, and it is
        ///     kept out of the menus unless the filter is off entirely rather than merely loosened. The
        ///     middle setting is not enough on its own: with it on, the client still strips anything
        ///     rated Rx, so the category would be offered and then come back with almost nothing in it.
        ///     A list that exists in the menu and is empty on screen reads as a broken screen.
        ///     <para>
        ///     Both places that list the subcategories read this, so the panel in More and the dropdown
        ///     on the top page cannot drift into offering different sets.
        ///     </para>
        /// </remarks>
        public static bool AllowsMangaSubcategory(MangaTopType type) =>
            type != MangaTopType.Doujinshi || Settings.ContentFilter == ContentFilterMode.Unfiltered;

        /// <summary>
        ///     True when an entry's own rating says it is in the tier that must not be shown.
        /// </summary>
        /// <remarks>
        ///     Read from the <c>rating</c> the source sends, which mirrors MAL's own content rating.
        ///     Only the explicit tier is refused in the middle mode; the mild tier is what that mode
        ///     exists to let through. An entry with no rating at all is kept: an unknown rating is not
        ///     evidence of anything, and dropping every unrated entry would empty the lists of anything
        ///     that has not aired yet.
        /// </remarks>
        public static bool IsHiddenBySetting(string rating)
        {
            if (Settings.ContentFilter == ContentFilterMode.Unfiltered)
                return false;
            if (string.IsNullOrWhiteSpace(rating))
                return false;
            if (Settings.ContentFilter == ContentFilterMode.Safe)
                return true;
            return rating.StartsWith("Rx", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}