using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using MALPlus.XShared.ViewModels;

namespace MALPlus.XShared.Comm
{
    public class AppUpdateInfo
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseName { get; set; }
        public string ReleaseNotes { get; set; }
        public string HtmlUrl { get; set; }
        public string Architecture { get; set; }
    }

    public class AppUpdateQuery
    {
        private const string ReleasesUrl = "https://api.github.com/repos/idarkalex/MALPlus/releases";
        public static string CustomReleasesUrl { get; set; } =
            Environment.GetEnvironmentVariable("MALPLUS_UPDATE_URL");
        private readonly HttpClient _httpClient = new HttpClient(ResourceLocator.MalHttpContextProvider.GetHandler());

        public static Func<string> TargetArchitectureProvider { get; set; }

        public AppUpdateQuery()
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MALPlus/0.7.0");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        }

        public async Task<AppUpdateInfo> GetUpdateInfo()
        {
            try
            {
                var fetchUrl = CustomReleasesUrl ?? ReleasesUrl;
                var json = await _httpClient.GetStringAsync(fetchUrl);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return null;

                var targetArch = TargetArchitectureProvider?.Invoke()?.ToLowerInvariant();
                if (string.IsNullOrEmpty(targetArch))
                {
                    var pArch = RuntimeInformation.ProcessArchitecture;
                    targetArch = (pArch == Architecture.X64 || pArch == Architecture.X86)
                        ? "x86_64"
                        : "arm64";
                }

                bool wantsX64 = targetArch.Contains("x86") || targetArch.Contains("x64");

                foreach (var release in doc.RootElement.EnumerateArray())
                {
                    if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
                        continue;

                    var tagName = release.TryGetProperty("tag_name", out var tag) && tag.ValueKind == JsonValueKind.String
                        ? tag.GetString()
                        : null;
                    if (string.IsNullOrEmpty(tagName))
                        continue;

                    var releaseName = release.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                        ? name.GetString()
                        : tagName;

                    // Exclude legacy Xamarin / MALClient builds (e.g. MALClient v1.2.1)
                    if (releaseName.Contains("MALClient", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var versionStr = tagName.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                        ? tagName.Substring(1)
                        : tagName;

                    string bestCandidateUrl = null;
                    string fallbackUrl = null;

                    if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            var assetName = asset.TryGetProperty("name", out var an) && an.ValueKind == JsonValueKind.String
                                ? an.GetString()
                                : "";

                            // Exclude legacy com.idarkalex.malclient.apk
                            if (assetName.Contains("malclient", StringComparison.OrdinalIgnoreCase))
                                continue;

                            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                            if (isWindows)
                            {
                                if (!assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                                    !assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                var assetUrl = asset.TryGetProperty("browser_download_url", out var url) && url.ValueKind == JsonValueKind.String
                                    ? url.GetString()
                                    : null;
                                if (assetUrl == null)
                                    continue;

                                if (assetName.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                                    assetName.Contains("installer", StringComparison.OrdinalIgnoreCase))
                                {
                                    bestCandidateUrl = assetUrl;
                                    break;
                                }
                                fallbackUrl ??= assetUrl;
                            }
                            else
                            {
                                if (!assetName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                var assetUrl = asset.TryGetProperty("browser_download_url", out var url) && url.ValueKind == JsonValueKind.String
                                    ? url.GetString()
                                    : null;
                                if (assetUrl == null)
                                    continue;

                                bool assetIsX64 = assetName.Contains("x86_64", StringComparison.OrdinalIgnoreCase) ||
                                                  assetName.Contains("x64", StringComparison.OrdinalIgnoreCase);
                                bool assetIsArm64 = assetName.Contains("arm64", StringComparison.OrdinalIgnoreCase);

                                if (wantsX64 && assetIsX64)
                                {
                                    bestCandidateUrl = assetUrl;
                                    break;
                                }
                                if (!wantsX64 && assetIsArm64)
                                {
                                    bestCandidateUrl = assetUrl;
                                    break;
                                }

                                if (fallbackUrl == null)
                                {
                                    if (wantsX64 && !assetIsArm64) fallbackUrl = assetUrl;
                                    else if (!wantsX64 && !assetIsX64) fallbackUrl = assetUrl;
                                }
                            }
                        }
                    }

                    var downloadUrl = bestCandidateUrl ?? fallbackUrl;
                    var htmlUrl = release.TryGetProperty("html_url", out var hu) && hu.ValueKind == JsonValueKind.String
                        ? hu.GetString()
                        : $"https://github.com/idarkalex/MALPlus/releases/tag/{tagName}";

                    return new AppUpdateInfo
                    {
                        Version = versionStr,
                        ReleaseName = releaseName,
                        ReleaseNotes = release.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String
                            ? body.GetString()
                            : "",
                        DownloadUrl = downloadUrl ?? htmlUrl,
                        HtmlUrl = htmlUrl,
                        Architecture = wantsX64 ? "x86_64" : "arm64"
                    };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AppUpdateQuery error: {ex.Message}");
            }

            return null;
        }
    }
}
