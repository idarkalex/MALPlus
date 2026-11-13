using System;
using System.Net.Http;
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
    }

    public class AppUpdateQuery
    {
        private const string ReleasesUrl = "https://api.github.com/repos/idarkalex/MALPlus/releases";
        private readonly HttpClient _httpClient = new HttpClient(ResourceLocator.MalHttpContextProvider.GetHandler());

        public AppUpdateQuery()
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MALPlus-Android/0.7.0");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        }

        public async Task<AppUpdateInfo> GetUpdateInfo()
        {
            try
            {
                var json = await _httpClient.GetStringAsync(ReleasesUrl);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return null;

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

                    string downloadUrl = null;
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

                            if (assetName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                            {
                                downloadUrl = asset.TryGetProperty("browser_download_url", out var url) && url.ValueKind == JsonValueKind.String
                                    ? url.GetString()
                                    : null;
                                break;
                            }
                        }
                    }

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
                        HtmlUrl = htmlUrl
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
