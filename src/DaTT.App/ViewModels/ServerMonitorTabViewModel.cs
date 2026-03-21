using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTT.Core.Interfaces;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace DaTT.App.ViewModels;

public partial class ServerMonitorTabViewModel : TabViewModel
{
    private readonly IDatabaseProvider _provider;
    private CancellationTokenSource? _autoRefreshCts;

    public override string Title => "Monitor";

    [ObservableProperty]
    private bool _isAutoRefreshEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshIntervalSeconds), nameof(UnitIsSec), nameof(UnitIsMin), nameof(UnitIsHour))]
    private string _intervalUnit = "s";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshIntervalSeconds))]
    private int _intervalAmount = 5;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public int RefreshIntervalSeconds => IntervalUnit switch
    {
        "min" => IntervalAmount * 60,
        "h"   => IntervalAmount * 3600,
        _     => Math.Max(2, IntervalAmount)
    };

    public bool UnitIsSec  => IntervalUnit == "s";
    public bool UnitIsMin  => IntervalUnit == "min";
    public bool UnitIsHour => IntervalUnit == "h";

    [RelayCommand]
    private void SetUnit(string unit)
    {
        IntervalUnit = unit;
        IntervalAmount = unit switch
        {
            "h"   => Math.Clamp(IntervalAmount, 1, 24),
            "min" => Math.Clamp(IntervalAmount, 1, 59),
            _     => Math.Clamp(IntervalAmount, 2, 59)
        };
    }

    [RelayCommand]
    private void IncrementAmount()
    {
        int max = IntervalUnit == "h" ? 24 : 59;
        IntervalAmount = Math.Min(max, IntervalAmount + 1);
    }

    [RelayCommand]
    private void DecrementAmount()
    {
        int min = IntervalUnit == "s" ? 2 : 1;
        IntervalAmount = Math.Max(min, IntervalAmount - 1);
    }

    public ObservableCollection<MonitorMetricItem> Metrics { get; } = [];
    public ObservableCollection<PingSample> PingSamples { get; } = [];
    public ObservableCollection<ActiveConnectionRow> Connections { get; } = [];

    // ─── Chart ─────────────────────────────────────────────────────────
    private readonly ObservableCollection<ObservableValue> _pingValues = [];
    public ISeries[] PingSeries { get; }
    public Axis[] XAxes { get; }
    public Axis[] YAxes { get; }

    [ObservableProperty] private long _pingMs;
    [ObservableProperty] private int _activeConnectionCount;
    [ObservableProperty] private string _dbVersion = "—";
    [ObservableProperty] private string _currentDatabase = "—";
    public string EngineName => _provider.EngineName;

    [ObservableProperty]
    private ActiveConnectionRow? _selectedConnection;

    partial void OnSelectedConnectionChanged(ActiveConnectionRow? value)
        => TerminateConnectionCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanTerminate))]
    private async Task TerminateConnectionAsync()
    {
        if (SelectedConnection is null) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            string sql = _provider.EngineName switch
            {
                var e when e.Contains("Postgre", StringComparison.OrdinalIgnoreCase)
                    => $"SELECT pg_terminate_backend({SelectedConnection.Id})",
                var e when e.Contains("MySQL", StringComparison.OrdinalIgnoreCase)
                           || e.Contains("Maria", StringComparison.OrdinalIgnoreCase)
                    => $"KILL {SelectedConnection.Id}",
                _ => throw new NotSupportedException($"Terminate not supported for {_provider.EngineName}.")
            };
            await _provider.ExecuteAsync(sql);
            await RefreshAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private bool CanTerminate() => SelectedConnection is not null;

    public ServerMonitorTabViewModel(IDatabaseProvider provider)
    {
        _provider = provider;

        PingSeries =
        [
            new LineSeries<ObservableValue>
            {
                Values = _pingValues,
                LineSmoothness = 1,
                GeometrySize = 5,
                GeometryStroke = new SolidColorPaint(SKColor.Parse("#4EC9B0"), 2),
                GeometryFill   = new SolidColorPaint(SKColor.Parse("#1E2030")),
                Stroke         = new SolidColorPaint(SKColor.Parse("#4EC9B0"), 2),
                Fill           = new LinearGradientPaint(
                    new SKColor[] { SKColor.Parse("#504EC9B0"), SKColor.Parse("#004EC9B0") },
                    new SKPoint(0.5f, 0f), new SKPoint(0.5f, 1f))
            }
        ];

        XAxes =
        [
            new Axis { IsVisible = false, ShowSeparatorLines = false }
        ];

        YAxes =
        [
            new Axis
            {
                Labeler          = v => $"{v:0}",
                TextSize         = 9,
                LabelsPaint      = new SolidColorPaint(SKColor.Parse("#88AAAAAA")),
                SeparatorsPaint  = new SolidColorPaint(SKColor.Parse("#22FFFFFF")) { StrokeThickness = 1 },
                MinLimit         = 0,
                ShowSeparatorLines = true
            }
        ];
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
            PingMs = pingMs;

            await LoadProviderMetricsAsync(cancellationToken);

            var conns = await LoadConnectionsAsync(cancellationToken);
            Connections.Clear();
            foreach (var c in conns)
                Connections.Add(c);
            // Count only non-idle connections (active, idle in transaction, etc.)
            // Pure "idle" connections are connection-pool slots, not real active users.
            ActiveConnectionCount = Connections.Count(c =>
                !string.Equals(c.State, "idle", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(c.State, "Sleep", StringComparison.OrdinalIgnoreCase));

            StatusMessage = $"Atualizado {DateTimeOffset.Now:HH:mm:ss}";
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
        {
            StatusMessage = $"a cada {IntervalAmount}{IntervalUnit}";
            _ = StartAutoRefreshAsync();
        }
        else
        {
            StopAutoRefresh();
            StatusMessage = string.Empty;
        }
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
                await Task.Delay(TimeSpan.FromSeconds(RefreshIntervalSeconds), ct);
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

        _pingValues.Add(new ObservableValue(pingMs));
        if (_pingValues.Count > 30)
            _pingValues.RemoveAt(0);
    }

    private async Task<long> MeasurePingMsAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await _provider.ExecuteAsync("SELECT 1", cancellationToken);
        sw.Stop();
        return sw.ElapsedMilliseconds;
    }

    private async Task LoadProviderMetricsAsync(CancellationToken cancellationToken)
    {
        if (_provider.EngineName.Contains("Postgre", StringComparison.OrdinalIgnoreCase))
        {
            DbVersion = await SingleValueAsync("SELECT version()", cancellationToken);
            CurrentDatabase = await SingleValueAsync("SELECT current_database()", cancellationToken);
            return;
        }

        if (_provider.EngineName.Contains("MySQL", StringComparison.OrdinalIgnoreCase)
            || _provider.EngineName.Contains("Maria", StringComparison.OrdinalIgnoreCase))
        {
            DbVersion = await SingleValueAsync("SELECT VERSION()", cancellationToken);
            CurrentDatabase = await SingleValueAsync("SELECT DATABASE()", cancellationToken);
        }
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

    private async Task<IReadOnlyList<ActiveConnectionRow>> LoadConnectionsAsync(CancellationToken ct)
    {
        if (_provider.EngineName.Contains("Postgre", StringComparison.OrdinalIgnoreCase))
        {
            const string sql = """
                SELECT
                    pid::text,
                    COALESCE(usename, '') AS usename,
                    COALESCE(client_addr::text, 'local') AS client_addr,
                    COALESCE(datname, '') AS datname,
                    COALESCE(state, '') AS state,
                    COALESCE(EXTRACT(EPOCH FROM (now() - query_start))::int::text || 's', '') AS duration,
                    COALESCE(LEFT(query, 120), '') AS query
                FROM pg_stat_activity
                WHERE backend_type = 'client backend'
                ORDER BY query_start DESC
                """;

            var result = await _provider.ExecuteAsync(sql, ct);
            if (!result.IsSuccess) return [];

            return result.Rows
                .Select(r => new ActiveConnectionRow(
                    Id:       r[0]?.ToString() ?? "",
                    User:     r[1]?.ToString() ?? "",
                    Host:     r[2]?.ToString() ?? "",
                    Database: r[3]?.ToString() ?? "",
                    State:    r[4]?.ToString() ?? "",
                    Duration: r[5]?.ToString() ?? "",
                    Query:    r[6]?.ToString() ?? ""))
                .ToList();
        }

        if (_provider.EngineName.Contains("MySQL", StringComparison.OrdinalIgnoreCase)
            || _provider.EngineName.Contains("Maria", StringComparison.OrdinalIgnoreCase))
        {
            const string sql = """
                SELECT
                    ID, USER, HOST,
                    COALESCE(DB, ''),
                    COMMAND,
                    CONCAT(TIME, 's'),
                    COALESCE(LEFT(INFO, 120), '')
                FROM information_schema.PROCESSLIST
                ORDER BY TIME DESC
                """;

            var result = await _provider.ExecuteAsync(sql, ct);
            if (!result.IsSuccess) return [];

            return result.Rows
                .Select(r => new ActiveConnectionRow(
                    Id:       r[0]?.ToString() ?? "",
                    User:     r[1]?.ToString() ?? "",
                    Host:     r[2]?.ToString() ?? "",
                    Database: r[3]?.ToString() ?? "",
                    State:    r[4]?.ToString() ?? "",
                    Duration: r[5]?.ToString() ?? "",
                    Query:    r[6]?.ToString() ?? ""))
                .ToList();
        }

        return [];
    }
}

public sealed record MonitorMetricItem(string Name, string Value);
public sealed record PingSample(DateTimeOffset At, long PingMs)
{
    public string DisplayAt => At.ToString("HH:mm:ss");
}
