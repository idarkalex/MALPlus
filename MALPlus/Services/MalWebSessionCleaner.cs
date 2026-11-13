using System;
using System.Collections.Generic;
using MALPlus.Adapters;

namespace MALPlus.Services;

/// <summary>
///     Destroys the MyAnimeList website session held in Android's cookie jar.
/// </summary>
public class MalWebSessionCleaner : IMalWebSessionCleaner
{
    /// <summary>
    ///     Consent management platform cookies, matched case-insensitively by prefix.
    ///     Preserving these prevents the "We value your privacy" modal from re-appearing.
    /// </summary>
    private static readonly string[] ConsentCookiePrefixes =
    {
        "euconsent", "quantcast", "sourcepoint", "trustarc", "cookielaw",
        "addthis", "oneTrust", "onetrust", "sp_consent", "cmpbox", "Optanon"
    };

    public void ClearMalWebSession()
    {
#if ANDROID
        try
        {
            var jar = global::Android.Webkit.CookieManager.Instance;
            jar.SetAcceptCookie(true);
            jar.RemoveSessionCookies(null);

            // Expire all non-consent cookies for myanimelist.net
            foreach (var name in PersistentCookieNames(jar))
            {
                if (IsConsent(name))
                    continue;
                ExpireCookie(jar, name);
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
    ///     Removes all cookies that identify a user session so login.php renders
    ///     the clean login form instead of redirecting away.
    /// </summary>
    public void ClearMalLoginBlocker()
    {
#if ANDROID
        try
        {
            var jar = global::Android.Webkit.CookieManager.Instance;
            jar.SetAcceptCookie(true);

            // Clear all non-consent cookies so login.php renders a clean form instead of redirecting
            foreach (var name in PersistentCookieNames(jar))
            {
                if (IsConsent(name))
                    continue;
                ExpireCookie(jar, name);
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

    /// <summary>
    ///     Total purge of all cookies from the CookieManager.
    /// </summary>
    public void PurgeAllWebCookies()
    {
#if ANDROID
        try
        {
            var jar = global::Android.Webkit.CookieManager.Instance;
            jar.SetAcceptCookie(true);
            jar.RemoveAllCookies(null);
            jar.RemoveSessionCookies(null);
            jar.Flush();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS cookie purge failed: " + ex.GetType().Name);
        }
#endif
    }

#if ANDROID
    private static void ExpireCookie(global::Android.Webkit.CookieManager jar, string name)
    {
        const string expires = "Expires=Thu, 01 Jan 1970 00:00:00 GMT; Max-Age=0; Path=/";
        jar.SetCookie("https://myanimelist.net", $"{name}=; {expires}; Domain=.myanimelist.net");
        jar.SetCookie("https://myanimelist.net", $"{name}=; {expires}; Domain=myanimelist.net");
        jar.SetCookie("https://myanimelist.net", $"{name}=; {expires}");
        jar.SetCookie("http://myanimelist.net", $"{name}=; {expires}; Domain=.myanimelist.net");
        jar.SetCookie("http://myanimelist.net", $"{name}=; {expires}");
    }

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
