using MALClient.Adapters;
using MALClient.XShared.ViewModels;

namespace MALClient.XShared.Utils;

/// <summary>
/// Single source of truth for the MyAnimeList OAuth client. The client id used to be copied
/// into six places by hand, which meant every call site could drift and there was no way to
/// point the app at a different application.
/// </summary>
public static class MalOAuthSettings
{
    private static IApplicationDataService Store => ResourceLocator.ApplicationDataService;

    /// <summary>
    /// The client id of the application registered on MyAnimeList, compiled into the build.
    ///
    /// This is not a secret and it is not meant to be hidden. OAuth calls an application without
    /// a server behind it a "public client", and the reference says so explicitly: PKCE exists
    /// mainly for native apps, and a client that has no secret simply sends an empty one. The
    /// client id ships in the binary and in every authorization URL, and every installation of
    /// the app shares it. What protects the user is the PKCE code verifier, which is generated
    /// fresh per sign-in, so knowing this value does not let anyone obtain anyone else's tokens
    /// without that person's password and their consent.
    /// </summary>
    private const string BuiltInClientId = "030f8e30cb57bce625dda6ca8637b75e";

    /// <summary>
    /// Overrides the compiled client id. This exists so a build can be pointed at another
    /// application without recompiling; an installed release has it empty and uses the value
    /// above.
    /// </summary>
    private static string StoredClientId
    {
        get => (string)(Store[nameof(ClientId)] ?? string.Empty);
        set => Store[nameof(ClientId)] = value;
    }

    public static string ClientId
    {
        get
        {
            var stored = StoredClientId;
            return string.IsNullOrWhiteSpace(stored) ? BuiltInClientId : stored;
        }
        set => StoredClientId = value;
    }

    /// <summary>
    /// The client secret, only needed when MyAnimeList issued one. It is never bundled into the
    /// app: a secret inside a distributed binary protects nothing, since anyone can read it out.
    /// An installed release leaves this empty, which the reference documents as valid for a
    /// client that has no secret, and the safety comes from PKCE instead.
    /// </summary>
    public static string ClientSecret
    {
        get => (string)(Store[nameof(ClientSecret)] ?? string.Empty);
        set => Store[nameof(ClientSecret)] = value;
    }

    /// <summary>
    /// Where MyAnimeList sends the authorization code back to. It has to match one of the
    /// redirect URIs registered with the application, otherwise MyAnimeList refuses the request.
    ///
    /// The registered value is a localhost address on purpose. Nothing ever listens there: the
    /// authorize request is made from inside the app's own WebView, and the WebView intercepts
    /// the redirect before it leaves the device, so the code is read straight out of the URL.
    /// That is why it works with a localhost redirect and why the address itself is irrelevant.
    /// Pointing this at the system browser instead breaks it, because then the redirect leaves
    /// the app and really does try to reach localhost.
    /// </summary>
    public static string RedirectUri
    {
        get => (string)(Store[nameof(RedirectUri)] ?? "http://localhost/maloauth");
        set => Store[nameof(RedirectUri)] = value;
    }

    /// <summary>
    /// True when there is enough configuration to attempt a sign-in. Without a client id the
    /// token exchange cannot work, so the login page can say so instead of failing later with
    /// an opaque 400 from the site.
    /// </summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);
}