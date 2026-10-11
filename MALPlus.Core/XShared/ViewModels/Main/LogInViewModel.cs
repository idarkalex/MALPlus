using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using MALPlus.Models.Enums;
using MALPlus.XShared.Comm;
using MALPlus.XShared.Interfaces;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.Utils;
using Newtonsoft.Json;

namespace MALPlus.XShared.ViewModels.Main
{

    /// <summary>
    /// This class is literally the last in the process of mvvm conversion... lol.
    /// Believe me or not but log in was all in code behind for whole 9 months.
    /// "Broken code gets fixed, bad code stays bad forever(almost)"
    /// 04.09.2016
    /// </summary>
    public class LogInViewModel : ViewModelBase
    {
        private readonly IMalHttpContextProvider _httpContextProvider;
        private ICommand _logOutCommand;
        private ICommand _logInCommand;
		private ICommand _problemsCommand;
        private ICommand _navigateRegister;
        private bool _authenticating;
        private bool _logOutButtonVisibility;
        private ApiType _currentApiType;

        public LogInViewModel(IMalHttpContextProvider httpContextProvider)
        {
            _httpContextProvider = httpContextProvider;
        }

        public ApiType CurrentApiType
        {
            get { return _currentApiType; }
            private set
            {
                _currentApiType = value;
                RaisePropertyChanged(() => ProblemsButtonVisibility);
				RaisePropertyChanged(() => CurrentApiType);
            }
        }

        public bool LogOutButtonVisibility
        {
            get { return _logOutButtonVisibility; }
            set
            {
                _logOutButtonVisibility = value;
                RaisePropertyChanged(() => LogOutButtonVisibility);
            }
        }


        private string _userNameInput;

        /// <summary>
        ///     Auto-properties here meant the Entry had nothing to bind to a change on: they never
        ///     raise PropertyChanged, so clearing the password after a sign-in left the text in the
        ///     box. Same for the code behind, which writes these directly before running the
        ///     command.
        /// </summary>
        public string UserNameInput
        {
            get => _userNameInput;
            set
            {
                _userNameInput = value;
                RaisePropertyChanged(() => UserNameInput);
            }
        }

        private string _passwordInput;

        public string PasswordInput
        {
            get => _passwordInput;
            set
            {
                _passwordInput = value;
                RaisePropertyChanged(() => PasswordInput);
            }
        }

        public bool Authenticating
        {
            get { return _authenticating; }
            set
            {
                _authenticating = value;
                RaisePropertyChanged(() => Authenticating);
            }
        }

        public bool ProblemsButtonVisibility => CurrentApiType == ApiType.Mal;

        public ICommand LogOutCommand => _logOutCommand ??= new RelayCommand(() =>
        {
            Credentials.Reset();
            ResourceLocator.AnimeLibraryDataStorage.Reset();
            ResourceLocator.MalHttpContextProvider.Invalidate();
            ResourceLocator.DataCacheService.ClearAnimeListData();
            ViewModelLocator.GeneralMain.CurrentOffStatus = "Log In";
            ViewModelLocator.GeneralHamburger.UpdateLogInLabel();
            LogOutButtonVisibility = false;

            // Signing out has to leave the app, not just drop the tokens. The Shell is not
            // reachable from here (Core is platform independent), so the request goes out as an
            // event and the shell decides who moves. Without it the user stays on the page they
            // were reading with an empty library and no obvious way back to the login form.
            try
            {
                LogOutRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception)
            {
            }
        });

        /// <summary>
        ///     Raised after a sign-out has actually dropped the session. AppShell listens and goes
        ///     to the login page. Async void on purpose: the handler navigates, and the command
        ///     cannot wait on it.
        /// </summary>
        public event EventHandler LogOutRequested;

        /// <summary>
        ///     Cached in the field, which is the only reason this works at all. RelayCommand holds
        ///     the delegate as a MvvmLight WeakAction: a command built inline from a lambda has no
        ///     strong reference once the constructor returns, the collector takes it, and Execute
        ///     becomes a silent no-op that still answers CanExecute == true.
        /// </summary>
        public ICommand LogInCommand => _logInCommand ??= new RelayCommand(AttemptAuthentication);

		public ICommand ProblemsCommand => _problemsCommand ?? (_problemsCommand = new RelayCommand(() =>
											{
												ResourceLocator.MessageDialogProvider.ShowMessageDialog("If you are experiencing constant error messages while trying to log in, resetting your password on MAL may solve this issue. Why you may ask... MAL api is just very very bad and it tends to do such things which are beyond my control.\n\nIf you've registered using Twitter or Facebook... MAL does not allow 3rd party apps to authenticate such accounts therefore I cannot sign you in. All you need to do though is to go to website and set your password manually over there and everything will work smoothly. Nothing I can do about it.","Something went wrong?™");
											}));



        public ICommand NavigateRegister => new RelayCommand(async () =>
        {
            //ResourceLocator.MessageDialogProvider.ShowMessageDialog(
            //    "Sorry, MyAnimeList.net has disabled registration for the time being. It's not something I can change, please go complain to MAL directly via support ticket.",
            //    "Registration disabled");
            try
            {
                ResourceLocator.SystemControlsLauncherService.LaunchUri(new Uri("https://myanimelist.net/register.php"));
            }
            catch (Exception e)
            {
                ResourceLocator.MessageDialogProvider.ShowMessageDialog("Looks like there's nothing that can launch this link... You can register at: https://myanimelist.net/register.php", "Error");
            }
        });

        public string PkceChallenge { get; } = RandomString(50);

        private static readonly Random Random = new Random();
        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[Random.Next(s.Length)]).ToArray());
        }

        public void Init()
        {
            ViewModelLocator.GeneralMain
                .CurrentOffStatus = Credentials.Authenticated ? $"Logged in as {Credentials.UserName}" : "Log In";

            CurrentApiType = ApiType.Mal;//Settings.SelectedApiType;
            LogOutButtonVisibility = Credentials.Authenticated;
        }

        /// <summary>
        ///     The direct path: check the typed username and password against MyAnimeList before
        ///     touching any credential state.
        ///     <para>
        ///     It used to write the password straight into <c>Credentials</c> and then ask
        ///     <c>AuthQuery</c> for "the context", which authenticated with that password read as
        ///     MAL session cookies. The request therefore went out with no session, always failed,
        ///     and reported a bad password for good ones. It now asks MAL directly, and only stores
        ///     anything once MAL has actually accepted the pair.
        ///     </para>
        /// </summary>
        private async void AttemptAuthentication()
        {
            if (Authenticating)
                return;

            var user = (UserNameInput ?? string.Empty).Trim();
            var pass = PasswordInput ?? string.Empty;

            Authenticating = true;
            try
            {
                if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
                {
                    ResourceLocator.MessageDialogProvider.ShowMessageDialog(
                        "Enter your MyAnimeList username and password.", "Sign in");
                    return;
                }

                var check = new AuthQuery();
                var outcome = await check.CheckCredentialsAsync(user, pass);

                if (outcome != AuthQuery.AuthOutcome.Success)
                {
                    Credentials.SetAuthStatus(false);
                    Credentials.Update(string.Empty, string.Empty, ApiType.Mal);

                    // A verification wall is not a wrong password and not a dead network, and telling
                    // the user either of those leaves them retyping a password that was always right.
                    // The form already carries a "Use the MyAnimeList site instead" button, so the
                    // message names it rather than adding a second way in.
                    if (outcome == AuthQuery.AuthOutcome.Challenge)
                    {
                        ResourceLocator.MessageDialogProvider.ShowMessageDialog(
                            "MyAnimeList is asking to verify that you are not a robot, so it will not " +
                            "take the password here. Tap \"Use the MyAnimeList site instead\" below and " +
                            "sign in on their site; the app picks the session up from there.",
                            "Verification required");
                        return;
                    }

                    var message = string.IsNullOrWhiteSpace(check.Detail)
                        ? "Unable to authorize with the provided credentials."
                        : check.Detail;
                    ResourceLocator.MessageDialogProvider.ShowMessageDialog(message, "Authorization failed.");
                    return;
                }

                // Only now is it safe to hold the password anywhere.
                Credentials.Update(user, pass, ApiType.Mal);
                Query.RefreshClientAuthHeader();
                Settings.SelectedApiType = ApiType.Mal;
                Credentials.SetAuthStatus(true);
                ResourceLocator.TelemetryProvider.TelemetryTrackEvent(TelemetryTrackedEvents.LoggedInMyAnimeList);
            }
            catch (Exception)
            {
                Credentials.SetAuthStatus(false);
                Credentials.Update(string.Empty, string.Empty, ApiType.Mal);
                ResourceLocator.MessageDialogProvider.ShowMessageDialog(
                    "Unable to authorize with the provided credentials.", "Authorization failed.");
                return;
            }
            finally
            {
                PasswordInput = string.Empty;
                RaisePropertyChanged(() => PasswordInput);
                Authenticating = false;
            }

            try
            {
                await ViewModelLocator.GeneralHamburger.UpdateProfileImg();
            }
            catch (Exception)
            {
                //
            }

            try
            {
                ViewModelLocator.GeneralMain.HideOffContentCommand.Execute(null);
                await DataCache.ClearApiRelatedCache();
                NavigateToStartPage();
            }
            catch (Exception)
            {
                // A throw in this tail left Authenticating true, which made the
                // sign-in button permanently unresponsive.
            }
        }

        public async void SignIn(string cookies, string apiCode)
        {
            Authenticating = true;
            // Do not gate on the is_logged_in cookie. Reaching the OAuth callback at all
            // means MyAnimeList accepted the credentials, and the token exchange below is
            // the real proof: it fails loudly if the code is not usable. The cookie check
            // used to be the only gate, and when the cookie was absent from the jar the
            // whole sign-in was skipped with no error at all.
            if (string.IsNullOrEmpty(apiCode))
            {
                Credentials.SetAuthStatus(false);
                Credentials.Update(string.Empty, string.Empty, ApiType.Mal);
                ResourceLocator.MessageDialogProvider.ShowMessageDialog("Unable to authorize with provided credentials. If problem persists please try to sign-in on website.", "Authorization failed.");
                Authenticating = false;
                return;
            }
            {
                Credentials.SetAuthStatus(true);
                Credentials.Update("", cookies ?? string.Empty, ApiType.Mal);
                _httpContextProvider.SetCookies(cookies ?? string.Empty);

                try
                {
                    var context = await _httpContextProvider.GetHttpContextAsync();

                    using var tokenClient = new HttpClient();
                    using var content = new FormUrlEncodedContent(new List<KeyValuePair<string, string>>
                    {
                        new("client_id", MalOAuthSettings.ClientId),
                        new("grant_type", "authorization_code"),
                        new("code", apiCode),
                        new("code_verifier", PkceChallenge),
                    });
                    var response = await tokenClient.PostAsync("https://myanimelist.net/v1/oauth2/token", content);

                    var json = await response.Content.ReadAsStringAsync();
                    var tokens = JsonConvert.DeserializeObject<TokenResponse>(json);
                    // A refused exchange answers with an error object and no tokens at all.
                    // Reading through it used to throw a NullReferenceException instead of
                    // telling the user what happened, which is how a rejected sign-in looked
                    // like a crash.
                    if (tokens == null || string.IsNullOrEmpty(tokens.access_token))
                    {
                        Credentials.SetAuthStatus(false);
                        Credentials.Update(string.Empty, string.Empty, ApiType.Mal);
                        ResourceLocator.MessageDialogProvider.ShowMessageDialog(
                            "MyAnimeList did not accept the sign-in. Please try again.",
                            "Authorization failed.");
                        Authenticating = false;
                        return;
                    }
                    Settings.ApiToken = tokens.access_token;
                    Settings.RefreshToken = tokens.refresh_token;
                    // MyAnimeList documents a one hour access token, so the safety margin cannot
                    // be a fixed hour: on a short lived token that subtracted the whole lifetime
                    // and the token was born already expired, which turned every call into a 401.
                    var lifetimeSeconds = tokens.expires_in > 0 ? tokens.expires_in : 3600;
                    var marginSeconds = Math.Max(60, lifetimeSeconds / 10);
                    Settings.ApiTokenExpires = DateTimeOffset.UtcNow
                        .AddSeconds(lifetimeSeconds - marginSeconds)
                        .ToUnixTimeSeconds();

                    var client = await ResourceLocator.MalHttpContextProvider.GetApiHttpContextAsync();
                    var profileData = JsonConvert.DeserializeObject<AccountResponse>(await client.GetStringAsync("https://api.myanimelist.net/v2/users/@me"));

                    Credentials.SetId(profileData.id);
                    Credentials.UserName = profileData.name;

                    Credentials.Update(Credentials.UserName, cookies, ApiType.Mal);
                    ViewModelLocator.AnimeList.ListSource = Credentials.UserName;
                    Settings.SelectedApiType = ApiType.Mal;
                    Credentials.SetAuthStatus(true);
                    ResourceLocator.TelemetryProvider.TelemetryTrackEvent(TelemetryTrackedEvents.LoggedInMyAnimeList);

                    try
                    {
                        await ViewModelLocator.GeneralHamburger.UpdateProfileImg();
                    }
                    catch (Exception)
                    {
                        //
                    }

                    ViewModelLocator.AnimeList.ListSource = Credentials.UserName;
                    ViewModelLocator.GeneralMain.HideOffContentCommand.Execute(null);
                    await DataCache.ClearApiRelatedCache();
                    NavigateToStartPage();

                    Authenticating = false;
                }
                catch (Exception e)
                {
                    Credentials.SetAuthStatus(false);
                    Credentials.Update(string.Empty, string.Empty, ApiType.Mal);
                    ResourceLocator.MessageDialogProvider.ShowMessageDialog($"Unable to authorize, try again later. {e}", "Authorization failed.");
                    Authenticating = false;
                    return;
                }
            }
        }

        public void FailedSignIn()
        {
            Authenticating = false;
            ResourceLocator.MessageDialogProvider.ShowMessageDialog("Unable to authorize, try again.", "Authorization failed.");
        }

        private void NavigateToStartPage()
        {
            switch (Settings.DefaultMenuTab)
            {
                case "manga":
                    ViewModelLocator.GeneralMain.Navigate(PageIndex.PageMangaList);
                    ViewModelLocator.GeneralHamburger.SetActiveButton(HamburgerButtons.MangaList);
                    break;
                case "discover":
                    ViewModelLocator.GeneralMain.Navigate(PageIndex.PageDiscover);
                    ViewModelLocator.GeneralHamburger.SetActiveButton(HamburgerButtons.Discover);
                    break;
                default:
                    ViewModelLocator.GeneralMain.Navigate(PageIndex.PageAnimeList);
                    ViewModelLocator.GeneralHamburger.SetActiveButton(HamburgerButtons.AnimeList);
                    break;
            }
        }
    }


    public class AccountResponse
    {
        public int id { get; set; }
        public string name { get; set; }
        public string gender { get; set; }
        public string birthday { get; set; }
        public string location { get; set; }
        public DateTime joined_at { get; set; }
        public string picture { get; set; }
    }


    public class TokenResponse
    {
        public string token_type { get; set; }
        public int expires_in { get; set; }
        public string access_token { get; set; }
        public string refresh_token { get; set; }
    }
}
