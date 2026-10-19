using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MALPlus.Adapters;
using MALPlus.Adapters.Credentials;
using MALPlus.Models.AdapterModels;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Notifications;

namespace MALPlus.Services;

public class MauiDispatcherAdapter : IDispatcherAdapter
{
    public void Run(Action action)
    {
        MainThread.BeginInvokeOnMainThread(action);
    }
}

public class MauiApplicationDataService : IApplicationDataService
{
    // Settings.* is read from bound property getters (per cell, per bind), and every
    // read used to cost two JNI SharedPreferences round trips plus a few TryParse.
    // Cache the parsed values and drop the entry on write.
    private static readonly Dictionary<string, object> Cache = new();
    private static readonly Dictionary<string, bool> Missing = new();

    public object this[string key]
    {
        get
        {
            if (Cache.TryGetValue(key, out var cached))
                return cached;
            if (Missing.ContainsKey(key))
                return null;

            if (!Preferences.ContainsKey(key))
            {
                Missing[key] = true;
                return null;
            }

            // ContainsKey says nothing about the stored TYPE. Preferences.Get(key, (string)null)
            // marshals straight through to SharedPreferences.getString, so an entry written as an
            // int or a bool makes the JNI layer throw ClassCastException, and because this indexer
            // runs from a static constructor it takes the whole process down at first touch ("UserId"
            // was stored as an int by an older build and every cold start died in Credentials..cctor).
            //
            // The typed overloads are NOT a safe way to probe for that. On Android they answer with
            // the supplied default instead of throwing when the stored value is of another type, so
            // trying them in turn silently turns a string key into 0/False. That is how "Auth" came
            // back True and "UserId" came back 0 for a user who was never signed in: the app decided
            // it had a session and skipped the login screen entirely.
            //
            // So the type has to come from the store itself rather than from a getter. The values are
            // all written as strings by the setter below, and Android's SharedPreferences cannot be
            // asked what a key holds, so the read that can only be correct is the string one; a value
            // of another type is a foreign entry from a build with a different format and is dropped
            // rather than guessed at.
            string raw;
            try
            {
                raw = Preferences.Get(key, (string)null);
            }
            catch (Exception)
            {
                // Not a string. Treated as absent instead of coerced into some other shape: this
                // indexer carries the credentials, and a wrong value here is a wrong identity.
                Missing[key] = true;
                return null;
            }

            if (raw == null)
            {
                Missing[key] = true;
                return null;
            }

            var value = Parse(raw);
            Cache[key] = value;
            return value;
        }
        set
        {
            Cache.Remove(key);
            Missing.Remove(key);
            if (value == null)
            {
                Preferences.Remove(key);
                return;
            }
            Preferences.Set(key, (int)Type.GetTypeCode(value.GetType()) + ":" + value);
        }
    }

    private static object Parse(string raw)
    {
        var sep = raw.IndexOf(':');
        if (sep > 0 && int.TryParse(raw.Substring(0, sep), out var typeCode))
        {
            var payload = raw.Substring(sep + 1);
            switch ((TypeCode)typeCode)
            {
                case TypeCode.Boolean when bool.TryParse(payload, out var b):
                    return b;
                case TypeCode.Int32 when int.TryParse(payload, out var i):
                    return i;
                case TypeCode.Int64 when long.TryParse(payload, out var l):
                    return l;
                case TypeCode.String:
                    return payload;
            }
        }
        if (bool.TryParse(raw, out var legacyBool))
            return legacyBool;
        if (int.TryParse(raw, out var legacyInt))
            return legacyInt;
        if (long.TryParse(raw, out var legacyLong))
            return legacyLong;
        return raw;
    }

    public object this[RoamingDataTypes key]
    {
        get => this[key.ToString()];
        set => this[key.ToString()] = value;
    }
}

public class MauiClipboardProvider : IClipboardProvider
{
    public void SetText(string text)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Clipboard.Default.SetTextAsync(text ?? string.Empty);
        });
    }
}

public class MauiConnectionInfoProvider : IConnectionInfoProvider
{
    private bool _subscribed;

    public void Init()
    {
        UpdateConnectionStatus();
        if (_subscribed)
            return;

        Connectivity.Current.ConnectivityChanged += (_, e) => HasInternetConnection = e.NetworkAccess != NetworkAccess.None;
        _subscribed = true;
    }

    private void UpdateConnectionStatus()
    {
        HasInternetConnection = Connectivity.NetworkAccess != NetworkAccess.None;
    }

    public bool HasInternetConnection { get; set; }
}

public class MauiShareProvider : IShareProvider
{
    public void Share(string message)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Microsoft.Maui.ApplicationModel.DataTransfer.Share.Default.RequestAsync(new ShareTextRequest { Text = message ?? string.Empty });
        });
    }
}

public class MauiMessageDialogProvider : IMessageDialogProvider
    {
        public void ShowMessageDialog(string content, string title)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (Shell.Current != null)
                    await OptionsSheetHost.ConfirmAsync(title ?? string.Empty, content ?? string.Empty, "OK");
            });
        }

        public Task ShowMessageDialogAsync(string content, string title)
        {
            if (Shell.Current == null)
                return Task.CompletedTask;
            return OptionsSheetHost.ConfirmAsync(title ?? string.Empty, content ?? string.Empty, "OK");
        }

        public void ShowMessageDialogWithInput(string content, string title, string trueCommand, string falseCommand,
            Action callbackOnTrue, Action callBackOnFalse = null)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (Shell.Current == null)
                    return;
                // The two labels are the whole point of the signature: callers have always passed a
                // real word for each answer and it used to be dropped on the floor, which is why every
                // one of them got a sheet reading "Yes" and then "Cancel". Handing the false label
                // through also puts the two answers on one row, because a question with a real word
                // for each side is not a menu.
                var yes = await OptionsSheetHost.ConfirmAsync(title ?? string.Empty, content ?? string.Empty,
                    trueCommand, destructive: false, cancelLabel: falseCommand);
                if (yes)
                    callbackOnTrue?.Invoke();
                else
                    callBackOnFalse?.Invoke();
            });
        }

    public void ShowLoadingPopup(string title, string content)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Shell.Current != null)
                await Shell.Current.Navigation.PushModalAsync(new LoadingPopupPage(title, content), false);
        });
    }

    public void HideLoadingDialog()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Shell.Current?.Navigation?.ModalStack?.LastOrDefault() is LoadingPopupPage)
                await Shell.Current.Navigation.PopModalAsync(false);
        });
    }

    public void UpdateLoadingPopup(string title, string content)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Shell.Current?.Navigation?.ModalStack?.LastOrDefault() is LoadingPopupPage page)
                page.Update(title, content);
        });
    }
}

public class MauiSnackbarProvider : ISnackbarProvider
{
    public void ShowText(string text)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Shell.Current != null)
                await OptionsSheetHost.ConfirmAsync("MAL+", text ?? string.Empty, "OK");
        });
    }
}

public class MauiImageDownloaderService : IImageDownloaderService
{
    public void DownloadImage(string url, string suggestedFilename, bool animeConver)
    {
        DownloadImageDefault(url, suggestedFilename, animeConver);
    }

    public void DownloadImageDefault(string url, string suggestedFilename, bool animeCover)
    {
        Task.Run(async () =>
        {
            using var client = new HttpClient();
            var bytes = await client.GetByteArrayAsync(url);
            var path = Path.Combine(FileSystem.CacheDirectory, suggestedFilename ?? "image.jpg");
            await File.WriteAllBytesAsync(path, bytes);
        });
    }
}

public class MauiPasswordVault : IPasswordVault
{
    private const int ReadTimeoutMs = 4000;

    private static readonly object Sync = new();
    private static readonly Dictionary<string, VaultCredential> Cache = new();
    private static bool _primed;

    public void Add(VaultCredential credential)
    {
        if (credential == null)
            return;
        lock (Sync)
            Cache[credential.Domain] = credential;
        WriteFallbackFile(credential);
        // Deliberately not awaited with a short cap. The first SecureStorage write has to
        // create the Tink keyset in the AndroidKeyStore, which on this device takes well
        // over a second and a half, and abandoning it here is exactly why the app came back
        // logged out on the next cold start even though the tokens were on disk.
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    await SecureStorage.Default.SetAsync(credential.Domain + "_user", credential.UserName ?? string.Empty);
                    await SecureStorage.Default.SetAsync(credential.Domain + "_pass", credential.Password ?? string.Empty);
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("MALPLUS vault write attempt " + attempt + " failed: " + ex.GetType().Name);
                    await Task.Delay(400);
                }
            }
        });
    }

    public VaultCredential Get(string domain)
    {
        lock (Sync)
        {
            if (Cache.TryGetValue(domain, out var cached))
                return cached;
        }

        var credential = TryReadSecureStorage(domain) ?? TryReadFallbackFile(domain);
        if (credential == null)
            throw new Exception("Credential not found.");
        lock (Sync)
            Cache[domain] = credential;
        return credential;
    }

    /// <summary>
    /// SecureStorage is EncryptedSharedPreferences + Tink + AndroidKeyStore. It does not always
    /// come back: on this device every read and even RemoveAll throws, which is how the MAL session
    /// cookies (the ones the forum endpoints need) got lost on a cold start. The credential is
    /// therefore mirrored into the app private directory, which is sandboxed like the keystore
    /// store, and read back whenever the keystore path yields nothing.
    ///
    /// That mirror used to be three plaintext lines, and line 3 is the real MAL password: the
    /// login is classic basic-auth (NetworkCredential(UserName, Password)), not OAuth, so there is
    /// no refresh token and the password is the only thing that can restore the session. It is
    /// AES-GCM encrypted here instead. The key is a per-install random secret kept in the app's
    /// own preferences, which is not an improvement against code running as this app, but it does
    /// mean the secret no longer sits in the clear in a file that survives in backups, in log
    /// dumps and in any filesystem capture.
    /// </summary>
    private static string FallbackPath(string domain)
    {
        return Path.Combine(FileSystem.AppDataDirectory, "vault_" + domain + ".dat");
    }

    private const string FallbackKeyPrefName = "MALPlus_vault_key";
    private static byte[] _fallbackKey;

    private static byte[] FallbackKey()
    {
        if (_fallbackKey != null)
            return _fallbackKey;

        // Read defensively rather than with Preferences.Get: if any other part of the app ever wrote
        // this name with a different overload, GetString throws through JNI and a key lookup can no
        // longer fail into a crash.
        string stored = null;
        try
        {
            stored = Preferences.Get(FallbackKeyPrefName, (string)null);
        }
        catch (Exception)
        {
            stored = null;
        }

        if (!string.IsNullOrEmpty(stored))
        {
            try
            {
                _fallbackKey = Convert.FromBase64String(stored);
                if (_fallbackKey.Length == 32)
                    return _fallbackKey;
            }
            catch (Exception)
            {
                // Corrupt entry: fall through and mint a fresh key. The old file can no longer be
                // decrypted, so it is replaced on the next write.
            }
        }

        var key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        Preferences.Set(FallbackKeyPrefName, Convert.ToBase64String(key));
        _fallbackKey = key;
        return key;
    }

    private static byte[] EncryptFallback(byte[] plaintext)
    {
        // 12-byte nonce is the GCM standard; the 16-byte tag is appended by AesGcm.
        var nonce = new byte[12];
        System.Security.Cryptography.RandomNumberGenerator.Fill(nonce);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new System.Security.Cryptography.AesGcm(FallbackKey(), 16);
        aes.Encrypt(nonce, plaintext, cipher, tag);

        // MAGIC + nonce + tag + cipher, so a plaintext leftover from an older build is detected
        // instead of being fed to the cipher as if it were valid.
        var magic = System.Text.Encoding.ASCII.GetBytes("MPV1");
        var output = new byte[4 + 12 + 16 + cipher.Length];
        Buffer.BlockCopy(magic, 0, output, 0, 4);
        Buffer.BlockCopy(nonce, 0, output, 4, 12);
        Buffer.BlockCopy(tag, 0, output, 16, 16);
        Buffer.BlockCopy(cipher, 0, output, 32, cipher.Length);
        return output;
    }

    private static byte[] DecryptFallback(byte[] input)
    {
        if (input == null || input.Length < 32)
            return null;
        var magic = System.Text.Encoding.ASCII.GetString(input, 0, 4);
        if (magic != "MPV1")
            return null;

        var nonce = new byte[12];
        var tag = new byte[16];
        Buffer.BlockCopy(input, 4, nonce, 0, 12);
        Buffer.BlockCopy(input, 16, tag, 0, 16);
        var cipher = new byte[input.Length - 32];
        Buffer.BlockCopy(input, 32, cipher, 0, cipher.Length);

        var plain = new byte[cipher.Length];
        using var aes = new System.Security.Cryptography.AesGcm(FallbackKey(), 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private static void WriteFallbackFile(VaultCredential credential)
    {
        try
        {
            var plaintext = System.Text.Encoding.UTF8.GetBytes(
                (credential.Domain ?? string.Empty) + "\n" +
                (credential.UserName ?? string.Empty) + "\n" +
                (credential.Password ?? string.Empty));
            File.WriteAllBytes(FallbackPath(credential.Domain), EncryptFallback(plaintext));
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS vault fallback write failed: " + ex.GetType().Name);
        }
    }

    private static VaultCredential TryReadFallbackFile(string domain)
    {
        try
        {
            var path = FallbackPath(domain);
            if (!File.Exists(path))
                return null;
            var plain = DecryptFallback(File.ReadAllBytes(path));
            if (plain == null)
            {
                // Either a pre-encryption build left it in the clear, or the key was regenerated.
                // Both mean the stored bytes are unusable, so drop them rather than keep a file
                // that cannot be read and looks like a valid cache hit.
                File.Delete(path);
                return null;
            }

            var lines = System.Text.Encoding.UTF8.GetString(plain).Split('\n');
            if (lines.Length < 3 || string.IsNullOrEmpty(lines[1]))
                return null;
            Console.WriteLine("MALPLUS vault recovered " + domain + " from the private file store");
            return new VaultCredential(domain, lines[1], lines[2]);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS vault fallback read failed: " + ex.GetType().Name);
            return null;
        }
    }

    private static void DeleteFallbackFiles()
    {
        try
        {
            foreach (var domain in new[] {"MALPlus", "MALPlusHum"})
            {
                var path = FallbackPath(domain);
                if (File.Exists(path))
                    File.Delete(path);
                // The per-install key goes with the data, so a re-login mints a fresh one.
                if (domain == "MALPlus")
                    Preferences.Remove(FallbackKeyPrefName);
            }
            _fallbackKey = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS vault fallback delete failed: " + ex.GetType().Name);
        }
    }

    /// <summary>
    /// SecureStorage is EncryptedSharedPreferences + Tink + AndroidKeyStore. Two ways it
    /// takes the whole app down, both seen on device after a reinstall:
    /// (1) the keystore entry is gone, so decrypt throws AEADBadTagException, and because
    ///     this runs under a WebView navigation callback the exception is unhandled and
    ///     kills the process;
    /// (2) the old code did Task.Run(...).Result, so the UI thread waited on a task that
    ///     needs the very looper it was blocking. That is the ANR: main thread parked,
    ///     zero CPU, "Waited 5000ms for MotionEvent".
    /// So: never block longer than ReadTimeoutMs, cache every read, and treat a broken
    /// keystore as "no credential" instead of a crash.
    /// </summary>
    private static VaultCredential TryReadSecureStorage(string domain)
    {
        if (!_primed)
        {
            _primed = true;
            Task.Run(() => TryReadSecureStorage("MALPlus"));
        }
        try
        {
            var read = Task.Run(() =>
            {
                var user = SecureStorage.Default.GetAsync(domain + "_user").GetAwaiter().GetResult();
                var pass = SecureStorage.Default.GetAsync(domain + "_pass").GetAwaiter().GetResult();
                return new VaultCredential(domain, user, pass);
            });
            if (!read.Wait(ReadTimeoutMs))
            {
                Console.WriteLine("MALPLUS vault read timed out for " + domain);
                return null;
            }
            var result = read.Result;
            return string.IsNullOrEmpty(result?.UserName) ? null : result;
        }
        catch (Exception ex)
        {
            // Most likely AEADBadTagException/KeyStoreException after a reinstall: the
            // stored ciphertext can never be decrypted again. Drop it so the next login
            // starts from a clean store instead of failing forever.
            Console.WriteLine("MALPLUS vault read failed for " + domain + ": " + ex.GetType().Name);
            TryResetBrokenStore();
            return null;
        }
    }

    private static void TryResetBrokenStore()
    {
        try
        {
            Task.Run(() => SecureStorage.Default.RemoveAll()).Wait(ReadTimeoutMs);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS vault reset failed: " + ex.GetType().Name);
        }
    }

    public void Reset()
    {
        lock (Sync)
            Cache.Clear();
        DeleteFallbackFiles();
        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    SecureStorage.Default.RemoveAll();
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("MALPLUS vault reset attempt " + attempt + " failed: " + ex.GetType().Name);
                    await Task.Delay(400);
                }
            }
        });
    }
}

public class MauiTelemetryProvider : ITelemetryProvider
{
    public void Init()
    {
    }

    public void TelemetryTrackEvent(TelemetryTrackedEvents @event)
    {
    }

    public void TelemetryTrackEvent(TelemetryTrackedEvents @event, params (string Key, string Param)[] args)
    {
    }

    public void TelemetryTrackNavigation(PageIndex page)
    {
    }

    public void TelemetryTrackNavigation(ForumsPageIndex page)
    {
    }

    public void LogEvent(string @event)
    {
    }

    public void TrackExceptionWithMessage(Exception e, string message)
    {
    }

    public void TrackException(Exception e, [System.Runtime.CompilerServices.CallerMemberName] string caller = null)
    {
    }

    public void TrackExceptionWithAttachment(Exception e, string attachment = null, [System.Runtime.CompilerServices.CallerMemberName] string caller = null)
    {
    }
}

public class MauiNotificationsTaskManager : INotificationsTaskManager
{
    public void StartTask(BgTasks task)
    {
    }

    public void StopTask(BgTasks task)
    {
    }

    public void CallTask(BgTasks task)
    {
    }
}

public class MauiScheduledJobsManager : ISchdeuledJobsManger
{
    public void StartJob(ScheduledJob job, int reccurence, Action jobDefinition)
    {
    }

    public void StopJob(ScheduledJob job)
    {
    }
}

public class MauiChangeLogProvider : IChangeLogProvider
{
    public bool NewChangelog { get; set; }
    public string CurrentVersion => AppInfo.VersionString;
    public string DateWithVersion => AppInfo.VersionString;
    public List<string> Changelog => new();
}

public class MauiSystemControlsLauncher : ISystemControlsLauncherService
{
    public void LaunchUri(Uri uri)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Launcher.Default.OpenAsync(uri);
        });
    }
}

public class MauiAiringNotificationsAdapter : MALPlus.XShared.Interfaces.IAiringNotificationsAdapter
{
    public void ScheduleToast(AiringShowNotificationEntry entry)
    {
    }

    public void RemoveToasts(string id)
    {
    }

    public bool AreNotificationRegistered(string id)
    {
        return false;
    }
}

public class MauiDialogsProvider : MALPlus.XShared.Interfaces.IDialogsProvider
{
    /// <summary>
    /// Reached whenever a list item is marked Completed with the "ask for a score" setting on,
    /// and from the Details score entry. It was an empty method, so the row highlighted, the
    /// sheet closed and nothing happened.
    ///
    /// It cannot reuse ShowMessageDialogWithInput: that one shows an action sheet with no text
    /// field, so it can only ever confirm. ChangeScore also takes the value as a string and
    /// parses it, so the value is formatted back out to text before being handed over.
    /// </summary>
public void ShowScoreDialog(MALPlus.XShared.ViewModels.AnimeItemViewModel vm)
        {
            if (vm == null)
                return;

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                var page = Shell.Current?.CurrentPage;
                if (page == null)
                    return;

                var current = vm.MyScore > 0 ? vm.MyScore.ToString("0") : string.Empty;
                var input = await OptionsSheetHost.PromptAsync("Score",
                    $"Your score for {vm.Title}\nLeave empty to remove it.", current, "0 - 10", maxLength: 3);

                if (string.IsNullOrWhiteSpace(input))
                    return;

                if (!double.TryParse(input.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    await OptionsSheetHost.ConfirmAsync("Score", "That is not a number from 0 to 10.", "OK");
                    return;
                }

                if (parsed is < 0 or > 10)
                {
                    await OptionsSheetHost.ConfirmAsync("Score", "The score has to be between 0 and 10.", "OK");
                    return;
                }

            // ChangeScore reads the value as a string, so it is passed as text on purpose.
            vm.ChangeScore(parsed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
    }
}

public class MauiLiveTilesManager : ILiveTilesManager
{
    public void UpdateTile(MALPlus.Models.Models.Library.IAnimeData item)
    {
    }
}

public class MauiPinTileService : IPinTileService
{
    public void Load(object data)
    {
    }
}

public class MauiCalendarExportProvider : ICalendarExportProvider
{
    public void ExportToCalendar(object animeItemViewModel)
    {
    }
}

public class MauiDataCache : IDataCache
{
    private static readonly JsonSerializerOptions CacheSerializerOptions = new()
        {
            // Read-only properties are skipped when restoring unless the object is told to fill the
            // existing instance instead of replacing it. ProfileData exposes Friends that way, and with
            // the default handling the friends list came back empty every time the cache was read: the
            // friend feeds page therefore had nothing to iterate and showed "No activity yet", despite
            // the friend being right there.
            //
            // On this runtime the knob lives on JsonSerializerOptions as Preferred..., and it is the
            // preferred hint rather than a hard one, which is enough here because everything serialised
            // into the cache opts in unless it says otherwise.
            PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate
        };

    private static string Root => FileSystem.AppDataDirectory;
    private static readonly HashSet<string> EnsuredDirs = new();

    private static string Resolve(string filename, string folder)
    {
        var dir = string.IsNullOrEmpty(folder) ? Root : Path.Combine(Root, folder);
        // CreateDirectory is a stat+mkdir syscall, and this ran on EVERY cache read and
        // write. The folder set is tiny and fixed, so remember what we already made.
        lock (EnsuredDirs)
        {
            if (EnsuredDirs.Add(dir))
                Directory.CreateDirectory(dir);
        }
        return Path.Combine(dir, filename);
    }

    public async Task SaveData<T>(T data, string filename, string targetFolder)
    {
        var json = JsonSerializer.Serialize(data, CacheSerializerOptions);
        await File.WriteAllTextAsync(Resolve(filename, targetFolder), json);
    }

    public Task SaveDataRoaming<T>(T data, string filename)
    {
        return SaveData(data, filename, "roaming");
    }

    public async Task<T> RetrieveData<T>(string filename, string originFolder, int expiration)
    {
        var path = Resolve(filename, originFolder);
        if (!File.Exists(path))
            return default;
        if (expiration > 0 && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromDays(expiration))
            return default;
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<T>(json, CacheSerializerOptions);
    }

    public Task<T> RetrieveDataRoaming<T>(string filename, int expiration)
    {
        return RetrieveData<T>(filename, "roaming", expiration);
    }

public Task ClearApiRelatedCache()
    {
        // Do NOT call MALPlus.XShared.Utils.DataCache.ClearApiRelatedCache() from here.
        // That static method dispatches through the IoC, and IDataCache is registered as
        // MauiDataCache, so it re-entered this method forever and killed the process with
        // a StackOverflowException on hwuiTask1 right after a successful sign-in.
        lock (EnsuredDirs)
        {
            EnsuredDirs.Clear();
        }

        foreach (var dir in SafeEnumerateDirectories())
        {
            var name = Path.GetFileName(dir);
            // "roaming" is not API cache: it holds the seed import gate and the settings.
            if (name.Equals("roaming", StringComparison.OrdinalIgnoreCase))
                continue;
            TryDeleteDirectory(dir);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    ///     How many files the cache holds. The setting page shows this, and it used to be a
    ///     placeholder that nothing ever filled in.
    /// </summary>
    public int CountCachedFiles()
    {
        var total = 0;
        lock (EnsuredDirs)
        {
            EnsuredDirs.Clear();
        }

        foreach (var dir in SafeEnumerateDirectories())
        {
            var name = Path.GetFileName(dir);
            // Same rule the wipe follows, so the number describes what "Clear caches" would remove.
            if (name.Equals("roaming", StringComparison.OrdinalIgnoreCase))
                continue;
            total += CountFilesIn(dir);
        }
        return total;
    }

    private static int CountFilesIn(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public Task ClearAnimeListData()
    {
        return ClearApiRelatedCache();
    }

    private static IEnumerable<string> SafeEnumerateDirectories()
    {
        try
        {
            return Directory.Exists(Root) ? Directory.EnumerateDirectories(Root).ToList() : new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS cache delete failed for " + dir + ": " + ex.Message);
        }
    }
}
