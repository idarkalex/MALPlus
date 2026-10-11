using System;
using System.Collections.Generic;
using MALPlus.Adapters;

namespace MALPlus.Services;

/// <summary>
///     Destroys the MyAnimeList website session held in Android's cookie jar.
///     <para>
///     Dropping the API token leaves this jar untouched, and that jar is what authenticates the
///     community endpoints. Worse, a jar that still holds a session makes login.php answer with a
///     redirect to the consent page instead of the login form, so the app accepts the consent and
///     captures the OLD user's cookie as if it belonged to the account just typed. That is the whole
///     reason a wrong password could end up signed in as somebody else.
///     </para>
/// </summary>
public class MalWebSessionCleaner : IMalWebSessionCleaner
{
    /// <summary>
    ///     Consent management platform cookies, matched case-insensitively by prefix.
    ///     <para>
    ///     Wiping these brought MyAnimeList's privacy notice back on every single sign-in, which is
    ///     a worse trade than keeping a cookie that grants nothing. Only ever a NON-session consent
    ///     cookie is spared: <see cref="RemoveSessionCookies" /> takes out every session cookie by
    ///     definition, so a consent cookie without an expiry still goes, which is the deal we want.
    ///     </para>
    /// </summary>
    private static readonly string[] ConsentCookiePrefixes =
    {
        "euconsent", "quantcast", "sourcepoint", "trustarc", "cookielaw",
        "addthis", "oneTrust", "onetrust", "sp_consent", "cmpbox"
    };

    public void ClearMalWebSession()
    {
#if ANDROID
        try
        {
            var jar = global::Android.Webkit.CookieManager.Instance;
            jar.SetAcceptCookie(true);

            // Every cookie without an expiry. This is the broad sweep and it needs no name list:
            // a session cookie is defined by having none, so this removes the session ones whatever
            // MAL decides to call them next.
            jar.RemoveSessionCookies(null);

            // Whatever survived has an expiry, so it is persistent. Those still have to go one by
            // one, except the consent ones listed above.
            foreach (var name in PersistentCookieNames(jar))
            {
                if (IsConsent(name) || IsMalSession(name))
                    continue;
                jar.SetCookie("https://myanimelist.net",
                    name + "=; Max-Age=0; Path=/; Domain=.myanimelist.net");
                jar.SetCookie("https://myanimelist.net", name + "=; Max-Age=0; Path=/");
            }

            jar.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS cookie jar clear failed: " + ex.GetType().Name);
        }
#endif
    }

    /// <summary>
    ///     The narrow sweep, and the one that runs before the login form is shown.
    ///     <para>
    ///     Only the cookies that turn login.php into a redirect to the consent page are expired.
    ///     This used to be the whole method and it worked; the total sweep was folded into it for
    ///     the sign-out fix, and that is what made the automatic sign-in unreliable, because the
    ///     form load and the POST behind it need the rest of the jar. There is no reason for a login
    ///     to clear anything beyond the session it is trying to replace.
    ///     </para>
    /// </summary>
    public void ClearMalLoginBlocker()
    {
#if ANDROID
        try
        {
            var jar = global::Android.Webkit.CookieManager.Instance;
            jar.SetAcceptCookie(true);

            foreach (var name in PersistentCookieNames(jar))
            {
                if (!IsMalSession(name))
                    continue;
                jar.SetCookie("https://myanimelist.net",
                    name + "=; Max-Age=0; Path=/; Domain=.myanimelist.net");
                jar.SetCookie("https://myanimelist.net", name + "=; Max-Age=0; Path=/");
            }

            jar.RemoveSessionCookies(null);
            jar.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS login blocker clear failed: " + ex.GetType().Name);
        }
#endif
    }

#if ANDROID
    /// <summary>
    ///     The session cookies, matched by prefix so a variant MAL invents later is still caught.
    ///     <c>MAL_SESSION</c> and <c>MAL_SESSIONSS</c> are the pair MAL has actually used, and
    ///     <c>MALHLOGSESS</c> is the Hummingbird one kept for vaults that still carry it.
    /// </summary>
    private static readonly string[] MalSessionCookiePrefixes =
    {
        "MAL_SESSION", "MALHLOGSESS", "MAL_LANG"
    };

    private static bool IsMalSession(string name)
    {
        foreach (var prefix in MalSessionCookiePrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
#endif

#if ANDROID
    /// <summary>
    ///     Reads the jar and returns the cookie names currently set for the domain.
    ///     <para>
    ///     GetCookie returns "name=value; name2=value2" and nothing else, which is enough: the
    ///     attributes are deliberately not used here because the only question is which names exist.
    /// </para>
    /// </summary>
    private static IEnumerable<string> PersistentCookieNames(global::Android.Webkit.CookieManager jar)
    {
        var names = new List<string>();
        string header;
        try
        {
            header = jar.GetCookie("https://myanimelist.net");
        }
        catch (Exception)
        {
            return names;
        }

        if (string.IsNullOrEmpty(header))
            return names;

        foreach (var pair in header.Split(';'))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
                continue;
            var name = pair.Substring(0, eq).Trim();
            if (name.Length == 0 || names.Contains(name))
                continue;
            names.Add(name);
        }

        return names;
    }

    private static bool IsConsent(string name)
    {
        foreach (var prefix in ConsentCookiePrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
#endif
}