using System;
using System.Text.RegularExpressions;

namespace MALPlus.XShared.Utils
{
    /// <summary>
    ///     The one place a MAL cover URL is turned into the variant worth actually downloading.
    /// </summary>
    /// <remarks>
    ///     MAL serves the same picture at four sizes behind one URL and the size is the LAST LETTER of the
        ///     file name: s is small, m medium, l large, and the bare number is the original. Separately,
        ///     resized copies live under /r/WxH/ and some carries a ?v= cache buster.
        ///     <para>
        ///         Scraped pages hand over whatever the markup happened to be pointing at, which is nearly
        ///         always the thumbnail: a recommendation row's img carries data-src ending in t.jpg with a
        ///         width="50" next to it. Drawing that on a card is roughly three times its own size, so it
        ///         arrives as a blur. Swapping the suffix for l costs one string operation and no request.
        ///     </para>
        ///     <para>
        ///         The size letter is REPLACED, not appended. The thumbnail of 101060 is 101060t, and
        ///         appending produced 101060tl, which is not a picture MAL serves: the request 404s and
        ///         the card draws nothing at all, which is worse than the blur it was meant to fix. The
        ///         three original copies had the same append and so shared the same latent fault; only a
        ///         thumbnail ever reached them, so it never showed.
        ///     </para>
        ///     <para>
        ///         This existed as four private copies, three of them complete and one that stopped before
        ///         the suffix swap, so anything that happened to call the incomplete one got small covers
        ///         and nothing said why. They now all come here.
        ///     </para>
        /// </remarks>
    public static class MalImageUrl
    {
        private static readonly Regex ResizeSegment = new Regex(@"/r/\d+x\d+/?", RegexOptions.Compiled);

        /// <summary>
        ///     The size letters MAL puts at the end of a cover's file name, longest first so m is not
        ///     read as the tail of a name that ends in "m".
        /// </summary>
        private static readonly char[] SizeSuffixes = { 'l', 'm', 's', 't' };

        /// <summary>
        ///     Strips the resize segment and the cache buster, and asks for the large variant unless the
        ///     URL already names a size the app uses.
        /// </summary>
        public static string Normalize(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return url;

            url = ResizeSegment.Replace(url, "/");

            var query = url.IndexOf('?');
            if (query > 0)
                url = url.Substring(0, query);

            var dot = url.LastIndexOf('.');
            if (dot <= 0 || dot >= url.Length - 1)
                return url;

            var stem = url.Substring(0, dot);
            if (stem.Length == 0)
                return url;

            // Already the size we want.
            if (stem[stem.Length - 1] == 'l')
                return url;

            // Replace the trailing size letter rather than adding one after it.
            var last = stem[stem.Length - 1];
            if (Array.IndexOf(SizeSuffixes, last) >= 0)
                stem = stem.Substring(0, stem.Length - 1);

            return stem + "l" + url.Substring(dot);
        }
    }
}