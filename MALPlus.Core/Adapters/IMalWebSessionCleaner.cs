namespace MALPlus.Adapters
{
    /// <summary>
    ///     Destroys the MyAnimeList *website* session held on the device.
    ///     <para>
    ///     Dropping the API token is not the same thing as signing out. The MAL session cookies
    ///     live in the platform's own cookie jar, which no amount of clearing tokens or the password
    ///     vault touches, and those cookies are what authenticate the community endpoints (forums,
    ///     clubs, wall). A leftover jar also has a nastier failure: arriving at login.php with a live
    ///     session makes MAL redirect to the consent page, the app accepts it, and it captures the
    ///     OLD user's cookie as if it belonged to whatever account was just typed.
    ///     </para>
    /// </summary>
    public interface IMalWebSessionCleaner
    {
        /// <summary>
        ///     Removes every session cookie of the domain, and every persistent cookie except the
        ///     consent ones: re-reading a privacy notice on each sign-in is a worse trade than a
        ///     cookie that grants nothing.
        ///     <para>
        ///     This is the sign-out boundary and it is deliberately total. It is NOT what runs before
        ///     showing the login form: doing that destroyed cookies the sign-in POST and the pages
        ///     behind it rely on, for no gain, since a login only needs the session gone.
        ///     </para>
        /// </summary>
        void ClearMalWebSession();

        /// <summary>
        ///     Removes just the cookies that make login.php redirect to the consent page instead of
        ///     rendering the form. Everything else is left alone on purpose, because the narrow
        ///     problem and the total one have different right answers.
        /// </summary>
        void ClearMalLoginBlocker();
    }
}