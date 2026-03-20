using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DaTT.App.Infrastructure;

namespace DaTT.App.Views;

public partial class UpdateWindow : Window
{
    private readonly GitHubRelease _release;
    private bool _isDownloading;

    public UpdateWindow(GitHubRelease release)
    {
        InitializeComponent();
        _release = release;

        var newVersion = release.TagName.TrimStart('v', 'V');
        VersionText.Text = $"v{UpdateService.CurrentVersionString}  →  v{newVersion}";

        foreach (var asset in release.Assets)
        {
            if (asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                VersionText.Text += $"  •  {UpdateService.FormatSize(asset.Size)}";
                break;
            }
        }

        var changelog = string.IsNullOrWhiteSpace(release.Body)
            ? "No release notes available."
            : release.Body
                .Replace("## ", "").Replace("### ", "")
                .Replace("**", "")
                .Replace("- ", "• ").Replace("* ", "• ");

        ChangelogText.Text = changelog;
    }

    private async void Update_Click(object? sender, RoutedEventArgs e)
    {
        if (_isDownloading) return;
        _isDownloading = true;

        ProgressPanel.IsVisible = true;
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "Downloading...";
        CancelButton.IsEnabled = false;

        try
        {
            var progress = new Progress<double>(percent => Dispatcher.UIThread.Post(() =>
            {
                DownloadProgress.Value = percent;
                ProgressPercentText.Text = $"{percent:F0}%";
            }));

            var installerPath = await UpdateService.DownloadInstallerAsync(_release, progress);

            if (installerPath is not null)
            {
                ProgressStatusText.Text = "Installing...";
                ProgressPercentText.Text = "100%";
                DownloadProgress.Value = 100;

                await Task.Delay(500);
                UpdateService.InstallAndRestart(installerPath);
            }
            else
            {
                ProgressStatusText.Text = "Download failed. Please try again.";
                UpdateButton.Content = "Retry";
                UpdateButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
                _isDownloading = false;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Update install failed: {ex.Message}");
            ProgressStatusText.Text = $"Error: {ex.Message}";
            UpdateButton.Content = "Retry";
            UpdateButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            _isDownloading = false;
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
