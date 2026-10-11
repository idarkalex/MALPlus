using System;
using System.Net;
using System.Threading.Tasks;
using System.Xml.Linq;
using MALPlus.Adapters;
using MALPlus.Adapters.Credentials;
using MALPlus.Models.AdapterModels;
using MALPlus.Models.Enums;
using MALPlus.XShared.Comm;
using MALPlus.XShared.ViewModels;

namespace MALPlus.XShared.Utils
{
    public static class Credentials
    {
        public static readonly IApplicationDataService ApplicationDataService;
        public static readonly IPasswordVault PasswordVault;
        private static string _userName;

        static Credentials()
        {
            ApplicationDataService = ResourceLocator.ApplicationDataService;
            PasswordVault = ResourceLocator.PasswordVaultProvider;

            HummingbirdToken = (string)(ApplicationDataService["HummingbirdToken"] ?? "");
            Id = (int)(ApplicationDataService["UserId"] ?? 0);
            // This used to be bool.Parse(ApplicationDataService["Auth"] as string ?? "False"),
            // but the data service hands back a parsed bool, so "as string" was always null
            // and the app came up unauthenticated on every cold start no matter what the
            // token store held.
            Authenticated = ReadBool(ApplicationDataService["Auth"]);
        }

        private static bool ReadBool(object raw)
        {
            switch (raw)
            {
                case null:
                    return false;
                case bool value:
                    return value;
                case string text:
                    return bool.TryParse(text, out var parsed) && parsed;
                default:
                    return false;
            }
        }

        public static string HummingbirdToken { get; private set; }


        public static string UserName
        {
            get { return _userName; }
            set { _userName = value?.Trim(); }
        }

        public static string Password { get; set; }

        /// <summary>
        ///     The MAL website session cookies, kept apart from <see cref="Password" />.
        ///     <para>
        ///     These two used to be the same field: the website login handed its cookies to
        ///     <c>Update</c> as though they were a password, and the vault stored them in the
        ///     password slot. That made "the password" mean two different things depending on how
        ///     the session had been created, and it is the reason the jar could not be matched up
        ///     with its own copy after a sign-out. They are separate values now; a session created
        ///     by the password form keeps an empty cookie string, which is correct, because that
        ///     path never had a website session.
        ///     </para>
        /// </summary>
        public static string WebSessionCookies { get; set; }

        public static int Id { get; private set; }


        public static bool Authenticated
        {
            get;
            set;
        }

        internal static ICredentials GetHttpCreditentials()
        {
            return new NetworkCredential(UserName, Password);
        }

        /// <summary>
        ///     Whether a string is a blob of MAL website cookies rather than a password. Used only to
        ///     read vaults written by older builds, where the two shared one slot.
        /// </summary>
        private static bool LooksLikeWebSession(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf(';') >= 0 &&
                   value.IndexOf('=') >= 0;
        }

        public static void Update(string name, string passwd, ApiType type)
        {
            PasswordVault.Reset();

            UserName = name;
            Password = passwd;
            WebSessionCookies = passwd;

            Query.RefreshClientAuthHeader();

            if (!string.IsNullOrWhiteSpace(passwd))
                PasswordVault.Add(new VaultCredential((type == ApiType.Mal ? "MALPlus" : "MALPlusHum"), UserName, Password));

            // A password is worth restoring across cold starts, and the api will use it. Website
            // cookies are a different thing with a different lifetime, so they get their own entry
            // and are only written when there actually are some: a sign-in made through the password
            // form has no website session to save, and an empty entry would only invite confusion.
            if (!string.IsNullOrWhiteSpace(WebSessionCookies))
                PasswordVault.Add(new VaultCredential(WebSessionDomain, UserName, WebSessionCookies));
        }

        /// <summary>
        ///     Vault domain holding the website cookies, kept apart from the account entry so that a
        ///     change of password never drags the session along with it.
        /// </summary>
        private const string WebSessionDomain = "MALPlusWebSession";

        public static void Reset()
        {
            // First, before the strings that mirror it are wiped. The jar is what actually
            // authenticates the community endpoints, and a jar left populated is what lets a wrong
            // password land on the previous user's session.
            ClearMalWebSession();

            PasswordVault.Reset();
            Settings.ApiToken = null;
            Settings.RefreshToken = null;
            Settings.ApiTokenExpires = 0;
            SetAuthStatus(false);
            SetAuthToken("");
            UserName = Password = string.Empty;
            WebSessionCookies = string.Empty;
        }

        /// <summary>
        ///     Drops the MAL website session cookies from the device, if this platform has somewhere
        ///     to drop them from. Best effort by design: a platform without a cookie jar, or one whose
        ///     jar refuses the call, must not be able to abort a sign-out that has already succeeded
        ///     everywhere else.
        /// </summary>
        public static void ClearMalWebSession()
        {
            try
            {
                ResourceLocator.MalWebSessionCleaner?.ClearMalWebSession();
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS web session clear failed: " + ex.GetType().Name);
            }
        }

        /// <summary>
        ///     Drops only what stops login.php from rendering its form, leaving the rest of the jar
        ///     alone. A login needs the session gone and nothing more, and clearing more than that
        ///     takes away cookies the form load and the submit behind it rely on.
        /// </summary>
        public static void ClearMalLoginBlocker()
        {
            try
            {
                ResourceLocator.MalWebSessionCleaner?.ClearMalLoginBlocker();
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS login blocker clear failed: " + ex.GetType().Name);
            }
        }

        /// <summary>
        ///     True when anything at all is still stored for this account: either the session is live,
        ///     or the vault still holds a credential whose session went stale.
        ///     <para>
        ///     The sign-out row keys off this rather than off <see cref="Authenticated" /> alone. A
        ///     session that MAL has invalidated leaves Authenticated false while the vault entry, the
        ///     saved username and the website cookies are all still on disk, and a row that hides
        ///     itself in exactly that state removes the only way back out.
        ///     </para>
        /// </summary>
        public static bool HasStoredAccount()
        {
            if (Authenticated || !string.IsNullOrWhiteSpace(UserName))
                return true;

            try
            {
                foreach (var domain in new[] { "MALPlus", "MALPlusHum" })
                {
                    var credential = PasswordVault.Get(domain);
                    if (credential != null && !string.IsNullOrWhiteSpace(credential.UserName))
                        return true;
                }
            }
            catch (Exception)
            {
                // A vault that throws is a vault with nothing to hand back.
            }

            return false;
        }

        public static void SetAuthStatus(bool status)
        {
            Authenticated = status;
            ApplicationDataService["Auth"] = status.ToString();
            ViewModelLocator.GeneralHamburger.UpdateLogInLabel();
        }

        public static void SetId(int id)
        {
            ApplicationDataService["UserId"] = id;
            Id = id;
        }

        public static void SetAuthToken(string token)
        {
            var trimmedToken = token == "" ? "" : token.Substring(1, token.Length - 2);
            ApplicationDataService["HummingbirdToken"] = trimmedToken;
            HummingbirdToken = trimmedToken;
        }

        public static void Init()
        {
            try
            {
                var deductedApiType = ApiType.Mal;
                VaultCredential credential = null;
                try
                {
                    credential = PasswordVault.Get("MALPlus");
                }
                catch (Exception)
                {
                    credential = PasswordVault.Get("MALPlusHum");
                    deductedApiType = ApiType.Hummingbird;
                }
                if (credential != null)
                {
                    Settings.SelectedApiType = deductedApiType;
                    UserName = credential.UserName;
                    Password = credential.Password;

                    // Older builds kept the website cookies in this same password slot. Read them out
                    // of there so an upgrade does not sign the user out of the forums, then move them
                    // to their own entry and leave the slot holding whatever it really held.
                    if (LooksLikeWebSession(Password))
                    {
                        WebSessionCookies = Password;
                        Password = string.Empty;
                        MigrateWebSessionToOwnEntry();
                    }

                    Authenticated = true;
                    if ((Settings.SelectedApiType == ApiType.Mal &&
                        ApplicationDataService["UserId"] == null) ||
                        (Settings.SelectedApiType == ApiType.Hummingbird &&
                        string.IsNullOrEmpty(ApplicationDataService["HummingbirdToken"] as string)))
                        //we have credentials without Id
                        FillInMissingIdData();
                }
                else
                    Authenticated = false;
            }
            catch (Exception)
            {
                Authenticated = false;
            }

            // Authenticated is also persisted, and a stored true is only a claim. If the vault
            // could not hand back a credential with a user name, then whatever the preference says
            // is stale (a dropped keystore, a foreign entry in the preference file) and the user is
            // not signed in. Leaving it true is what let the app open straight into the library
            // with someone else's identity on it.
            if (Authenticated && string.IsNullOrWhiteSpace(UserName))
                Authenticated = false;
        }

        /// <summary>
        /// The vault is the nice path, but it is not the only one: a refresh token survives
        /// in the preferences even when the vault write was dropped or the keystore entry
        /// went away. Rehydrate the session from it so a cold start does not dump the user
        /// back on the login form every single time.
        /// </summary>
        public static async Task<bool> TryRestoreSessionAsync()
        {
            if (Authenticated && !string.IsNullOrWhiteSpace(UserName))
                return true;
            if (string.IsNullOrWhiteSpace(Settings.RefreshToken) || string.IsNullOrWhiteSpace(Settings.ApiToken))
                return false;
            try
            {
                // Refreshing also proves the token is still good.
                var client = await ResourceLocator.MalHttpContextProvider.GetApiHttpContextAsync();
                if (client == null)
                    return false;
                var profile = await client.GetStringAsync("https://api.myanimelist.net/v2/users/@me");
                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(profile);
                if (data == null)
                    return false;
                UserName = (string)data["name"];
                var id = (int?)data["id"] ?? 0;
                if (!string.IsNullOrWhiteSpace(UserName) && id > 0)
                {
                    SetId(id);
                    Settings.SelectedApiType = ApiType.Mal;
                    SetAuthStatus(true);
                    ViewModelLocator.AnimeList.ListSource = UserName;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS session restore failed: " + ex.GetType().Name + " " + ex.Message);
            }
            return false;
        }

        /// <summary>
        /// The MAL website session cookies. They are what the community endpoints (forums, clubs,
        /// wall) authenticate with, and they only ever lived in memory after a sign in, so every
        /// cold start used to come back with a working official api and a completely dead forum
        /// section. Restored from their own vault entry, written at login time.
        /// </summary>
        public static bool TryRestoreWebSession()
        {
            if (!string.IsNullOrWhiteSpace(WebSessionCookies))
                return true;

            try
            {
                var credential = PasswordVault.Get(WebSessionDomain);
                if (credential != null && !string.IsNullOrWhiteSpace(credential.Password))
                {
                    WebSessionCookies = credential.Password;
                    if (string.IsNullOrWhiteSpace(UserName))
                        UserName = credential.UserName;
                    Console.WriteLine("MALPLUS web session restored");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS web session restore failed: " + ex.GetType().Name);
            }

            // Nothing in the new entry: an older build may still have the cookies sitting in the
            // account slot, so look there too and migrate rather than sign the user out.
            foreach (var domain in new[] { "MALPlus", "MALPlusHum" })
            {
                try
                {
                    var credential = PasswordVault.Get(domain);
                    if (credential == null || !LooksLikeWebSession(credential.Password))
                        continue;
                    if (string.IsNullOrWhiteSpace(UserName))
                        UserName = credential.UserName;
                    WebSessionCookies = credential.Password;
                    MigrateWebSessionToOwnEntry();
                    Console.WriteLine("MALPLUS web session migrated from " + domain);
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("MALPLUS web session restore failed for " + domain + ": " + ex.GetType().Name);
                }
            }
            return false;
        }

        /// <summary>
        ///     Copies the cookies into their own vault entry. Additive on purpose: the account entry
        ///     is left exactly as it was, so a failure here costs the migration and nothing else, and
        ///     <see cref="Update" /> rewrites both entries from scratch on the next sign-in anyway.
        /// </summary>
        private static void MigrateWebSessionToOwnEntry()
        {
            if (string.IsNullOrWhiteSpace(WebSessionCookies))
                return;
            try
            {
                PasswordVault.Add(new VaultCredential(WebSessionDomain, UserName, WebSessionCookies));
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS web session migration failed: " + ex.GetType().Name);
            }
        }

        private static async void FillInMissingIdData()
        {
            try
            {
                string response = null;
                switch (Settings.SelectedApiType)
                {
                    case ApiType.Mal:
                        response = await new AuthQuery().GetRequestResponse();
                        if (string.IsNullOrEmpty(response))
                            throw new Exception();
                        var doc = XDocument.Parse(response);
                        SetId(int.Parse(doc.Element("user").Element("id").Value));
                        break;
                    case ApiType.Hummingbird:
                        response = await new AuthQuery().GetRequestResponse();
                        if (string.IsNullOrEmpty(response))
                            throw new Exception();
                        if (response.Contains("\"error\": \"Invalid credentials\""))
                            throw new Exception();
                        SetAuthToken(response);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            catch (Exception)
            {
                Authenticated = false;
            }
        }
    }
}