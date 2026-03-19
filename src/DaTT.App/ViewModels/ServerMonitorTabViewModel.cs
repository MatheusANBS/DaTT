using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTT.Core.Interfaces;

namespace DaTT.App.ViewModels;

public partial class ServerMonitorTabViewModel : TabViewModel
{
    private readonly IDatabaseProvider _provider;
    private CancellationTokenSource? _autoRefreshCts;

    public override string Title => "Monitor";

    [ObservableProperty]
    private bool _isAutoRefreshEnabled;

    [ObservableProperty]
    private int _refreshIntervalSeconds = 5;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<MonitorMetricItem> Metrics { get; } = [];
    public ObservableCollection<PingSample> PingSamples { get; } = [];

    public ServerMonitorTabViewModel(IDatabaseProvider provider)
    {
        _provider = provider;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            Metrics.Clear();

            var pingMs = await MeasurePingMsAsync(cancellationToken);
            AddPingSample(pingMs);
            Metrics.Add(new MonitorMetricItem("Ping (ms)", pingMs.ToString()));
            Metrics.Add(new MonitorMetricItem("Engine", _provider.EngineName));
            Metrics.Add(new MonitorMetricItem("Timestamp", DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")));

            var providerMetrics = await LoadProviderMetricsAsync(cancellationToken);
            foreach (var item in providerMetrics)
                Metrics.Add(item);

            StatusMessage = "Metrics refreshed.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Refresh canceled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Refresh failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIsAutoRefreshEnabledChanged(bool value)
    {
        if (value)
            _ = StartAutoRefreshAsync();
        else
            StopAutoRefresh();
    }

    private async Task StartAutoRefreshAsync()
    {
        StopAutoRefresh();
        _autoRefreshCts = new CancellationTokenSource();
        var ct = _autoRefreshCts.Token;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await RefreshAsync(ct);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(2, RefreshIntervalSeconds)), ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StopAutoRefresh()
    {
        if (_autoRefreshCts is { IsCancellationRequested: false })
            _autoRefreshCts.Cancel();

        _autoRefreshCts?.Dispose();
        _autoRefreshCts = null;
    }

    private void AddPingSample(long pingMs)
    {
        PingSamples.Insert(0, new PingSample(DateTimeOffset.Now, pingMs));
        if (PingSamples.Count > 30)
            PingSamples.RemoveAt(30);
    }

    private async Task<long> MeasurePingMsAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await _provider.ExecuteAsync("SELECT 1", cancellationToken);
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    private async Task<IReadOnlyList<MonitorMetricItem>> LoadProviderMetricsAsync(CancellationToken cancellationToken)
    {
        var metrics = new List<MonitorMetricItem>();

        if (_provider.EngineName.Contains("Postgre", StringComparison.OrdinalIgnoreCase))
        {
            var version = await SingleValueAsync("SELECT version()", cancellationToken);
            var activeConnections = await SingleValueAsync("SELECT COUNT(*) FROM pg_stat_activity", cancellationToken);
            var database = await SingleValueAsync("SELECT current_database()", cancellationToken);

            metrics.Add(new MonitorMetricItem("Version", version));
            metrics.Add(new MonitorMetricItem("Active Connections", activeConnections));
            metrics.Add(new MonitorMetricItem("Current Database", database));
            return metrics;
        }

        if (_provider.EngineName.Contains("MySQL", StringComparison.OrdinalIgnoreCase)
            || _provider.EngineName.Contains("Maria", StringComparison.OrdinalIgnoreCase))
        {
            var version = await SingleValueAsync("SELECT VERSION()", cancellationToken);
            var threadsConnected = await SingleValueAsync("SHOW STATUS LIKE 'Threads_connected'", cancellationToken, valueColumnIndex: 1);
            var currentDatabase = await SingleValueAsync("SELECT DATABASE()", cancellationToken);

            metrics.Add(new MonitorMetricItem("Version", version));
            metrics.Add(new MonitorMetricItem("Threads Connected", threadsConnected));
            metrics.Add(new MonitorMetricItem("Current Database", currentDatabase));
            return metrics;
        }

        metrics.Add(new MonitorMetricItem("Info", "Provider-specific metrics not configured; ping-only mode."));
        return metrics;
    }

    private async Task<string> SingleValueAsync(string sql, CancellationToken cancellationToken, int valueColumnIndex = 0)
    {
        var result = await _provider.ExecuteAsync(sql, cancellationToken);
        if (!result.IsSuccess || result.Rows.Count == 0)
            return "n/a";

        var row = result.Rows[0];
        if (valueColumnIndex < 0 || valueColumnIndex >= row.Length)
            return "n/a";

        return row[valueColumnIndex]?.ToString() ?? "n/a";
    }
}

public sealed record MonitorMetricItem(string Name, string Value);
public sealed record PingSample(DateTimeOffset At, long PingMs)
{
    public string DisplayAt => At.ToString("HH:mm:ss");
}
