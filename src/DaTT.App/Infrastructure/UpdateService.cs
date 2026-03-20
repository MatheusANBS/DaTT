using System.Reflection;
using System.Text.Json;

namespace DaTT.App.Infrastructure;

public static class UpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/MatheusANBS/DaTT/releases/latest";

    public record UpdateInfo(string CurrentVersion, string LatestVersion, string DownloadUrl, bool HasUpdate);

    public static string CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

    public static async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        var current = CurrentVersion;

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DaTT-Updater");
        http.Timeout = TimeSpan.FromSeconds(10);

        var json = await http.GetStringAsync(ApiUrl, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.GetProperty("tag_name").GetString() ?? "";
        var latestVersion = tagName.TrimStart('V', 'v');

        var downloadUrl = root.TryGetProperty("html_url", out var htmlUrl)
            ? htmlUrl.GetString() ?? ""
            : "";

        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? downloadUrl;
                    break;
                }
            }
        }

        var hasUpdate = Version.TryParse(latestVersion, out var latest)
                     && Version.TryParse(current, out var curr)
                     && latest > curr;

        return new UpdateInfo(current, latestVersion, downloadUrl, hasUpdate);
    }
}
