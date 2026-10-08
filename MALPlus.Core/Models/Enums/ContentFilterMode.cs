namespace MALPlus.Models.Enums
{
    /// <summary>
    ///     How much of MAL's catalogue the app is allowed to show.
    /// </summary>
    /// <remarks>
    ///     Three states rather than a switch, because a switch does not answer the question anyone
    ///     actually has. The source only offers one filter, <c>sfw</c>, and it is stricter and blunter
    ///     than its name suggests: measured over one band of the top anime ranking, it removed every
    ///     entry rated "R+ - Mild Nudity" (twelve of them, among them Tokyo Godfathers, Evangelion
    ///     Movie 2, Berserk III, Maison Ikkoku and Gundam The Origin) while letting through fifty nine
    ///     rated "R - 17+". So the filter is keyed on the content rating, not on whether something is
    ///     adult, and it cuts the mild tier while keeping the violent one. A binary switch would only
    ///     offer "sensible" or "including hentai".
    ///     <para>
    ///     MAL publishes the rating on every entry, so the middle state is the same shape of decision
    ///     made against better data: keep mild nudity, drop the explicit tier.
    ///     </para>
    /// </remarks>
    public enum ContentFilterMode
    {
        /// <summary>
        ///     Everything except "R+ - Mild Nudity" and up. What the source's own filter does, and the
        ///     default, so nobody is shown something new until they ask for it.
        /// </summary>
        Safe = 0,

        /// <summary>
        ///     Everything except the explicit tier. Recovers the mainstream titles the strict filter
        ///     takes out for a few seconds of nudity.
        /// </summary>
        Mature = 1,

        /// <summary>
        ///     Nothing filtered.
        /// </summary>
        Unfiltered = 2
    }
}