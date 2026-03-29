using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using TinyClips.Models;

namespace TinyClips.Services;

/// <summary>
/// Checks GitHub Releases for a newer version and shows a toast notification.
/// </summary>
public static class UpdateChecker
{
    private const string ReleasesUrl = "https://api.github.com/repos/janode/tiny-clips-win/releases/latest";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    public static async Task CheckForUpdateAsync()
    {
        try
        {
            // Throttle: only check once per 24 hours
            var settings = CaptureSettings.Instance;
            if (settings.LastUpdateCheckUtc is DateTime last &&
                DateTime.UtcNow - last < CheckInterval)
            {
                return;
            }

            settings.LastUpdateCheckUtc = DateTime.UtcNow;
            settings.Save();

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TinyClips-UpdateChecker");
            http.Timeout = TimeSpan.FromSeconds(10);

            var release = await http.GetFromJsonAsync<GitHubRelease>(ReleasesUrl);
            if (release?.TagName is null) return;

            var latestVersion = ParseVersion(release.TagName);
            if (latestVersion is null) return;

            var currentVersion = typeof(UpdateChecker).Assembly.GetName().Version;
            if (currentVersion is null) return;

            // Compare major.minor.build (ignore revision)
            if (latestVersion > currentVersion)
            {
                var url = release.HtmlUrl ?? $"https://github.com/janode/tiny-clips-win/releases/tag/{release.TagName}";
                NotificationService.Instance.ShowUpdateNotification(release.TagName, url);
            }
        }
        catch
        {
            // Update check is best-effort — never surface errors to user
        }
    }

    internal static Version? ParseVersion(string tag)
    {
        // Strip leading 'v' if present: "v1.2.3" → "1.2.3"
        var versionStr = tag.StartsWith('v') ? tag[1..] : tag;
        return Version.TryParse(versionStr, out var v) ? v : null;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}
