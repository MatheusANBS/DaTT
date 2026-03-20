using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace DaTT.App.Infrastructure;

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public GitHubAsset[] Assets { get; set; } = [];

    public Version? ParsedVersion
    {
        get
        {
            var tag = TagName.TrimStart('v', 'V');
            return Version.TryParse(tag, out var v) ? v : null;
        }
    }
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public static class UpdateService
{
    private const string ApiUrl = "https://api.github.com/repos/MatheusANBS/DaTT/releases/latest";

    private static readonly HttpClient _http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DaTT-Updater");
        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        return client;
    }

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version;
            return v is not null ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : new Version(1, 0, 0);
        }
    }

    public static string CurrentVersionString => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    public static async Task<GitHubRelease?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var release = await _http.GetFromJsonAsync<GitHubRelease>(ApiUrl, ct);
            if (release?.ParsedVersion is null) return null;

            var remote = new Version(release.ParsedVersion.Major, release.ParsedVersion.Minor, Math.Max(release.ParsedVersion.Build, 0));
            return remote > CurrentVersion ? release : null;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Update check failed: {ex.Message}");
            return null;
        }
    }

    public static async Task<string?> DownloadInstallerAsync(GitHubRelease release, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var asset = release.Assets.FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                ?? release.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

            if (asset is null)
            {
                AppLog.Warn("No installer asset found in release");
                return null;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "DaTT", "Updates");
            Directory.CreateDirectory(tempDir);
            var filePath = Path.Combine(tempDir, asset.Name);

            if (File.Exists(filePath))
                File.Delete(filePath);

            using var response = await _http.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
            var downloaded = 0L;

            await using var remote = await response.Content.ReadAsStreamAsync(ct);
            await using var local = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buf = new byte[8192];
            int read;
            while ((read = await remote.ReadAsync(buf.AsMemory(), ct)) > 0)
            {
                await local.WriteAsync(buf.AsMemory(0, read), ct);
                downloaded += read;
                if (totalBytes > 0)
                    progress?.Report(downloaded * 100.0 / totalBytes);
            }

            return filePath;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Update download failed: {ex.Message}");
            return null;
        }
    }

    public static void InstallAndRestart(string installerPath)
    {
        var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(currentExe))
            throw new InvalidOperationException("Unable to resolve current executable path for restart.");

        const string installerArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";
        var cmdArgs = $"/C \"\"{installerPath}\" {installerArgs} & start \"\" \"{currentExe}\"\"";

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = cmdArgs,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        Dispatcher.UIThread.InvokeAsync(() =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown());
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1_048_576) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1_048_576.0:F1} MB";
    }
}
