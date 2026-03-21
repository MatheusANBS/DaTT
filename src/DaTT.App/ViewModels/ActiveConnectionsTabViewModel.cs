using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTT.Core.Interfaces;

namespace DaTT.App.ViewModels;

public record ActiveConnectionRow(
    string Id,
    string User,
    string Host,
    string Database,
    string State,
    string Duration,
    string Query
);

public partial class ActiveConnectionsTabViewModel : TabViewModel
{
    private readonly IDatabaseProvider _provider;
    private CancellationTokenSource? _autoRefreshCts;

    public override string Title => "Active Connections";

    [ObservableProperty]
    private bool _isAutoRefreshEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshIntervalSeconds), nameof(UnitIsSec), nameof(UnitIsMin), nameof(UnitIsHour))]
    private string _intervalUnit = "s";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshIntervalSeconds))]
    private int _intervalAmount = 10;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ActiveConnectionRow? _selectedRow;

    public int RefreshIntervalSeconds => IntervalUnit switch
    {
        "min" => IntervalAmount * 60,
        "h"   => IntervalAmount * 3600,
        _     => Math.Max(2, IntervalAmount)
    };

    public bool UnitIsSec  => IntervalUnit == "s";
    public bool UnitIsMin  => IntervalUnit == "min";
    public bool UnitIsHour => IntervalUnit == "h";

    public ObservableCollection<ActiveConnectionRow> Connections { get; } = [];

    public ActiveConnectionsTabViewModel(IDatabaseProvider provider)
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
            var rows = await LoadConnectionsAsync(cancellationToken);
            Connections.Clear();
            foreach (var row in rows)
                Connections.Add(row);

            StatusMessage = $"{Connections.Count} connection(s) — {DateTimeOffset.Now:HH:mm:ss}";
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

    [RelayCommand(CanExecute = nameof(CanTerminate))]
    private async Task TerminateConnectionAsync()
    {
        if (SelectedRow is null) return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            string sql = _provider.EngineName switch
            {
                var e when e.Contains("Postgre", StringComparison.OrdinalIgnoreCase)
                    => $"SELECT pg_terminate_backend({SelectedRow.Id})",
                var e when e.Contains("MySQL", StringComparison.OrdinalIgnoreCase)
                           || e.Contains("Maria", StringComparison.OrdinalIgnoreCase)
                    => $"KILL {SelectedRow.Id}",
                _ => throw new NotSupportedException($"Terminate not supported for {_provider.EngineName}.")
            };

            await _provider.ExecuteAsync(sql);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanTerminate() => SelectedRow is not null;

    partial void OnSelectedRowChanged(ActiveConnectionRow? value)
        => TerminateConnectionCommand.NotifyCanExecuteChanged();

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

    partial void OnIsAutoRefreshEnabledChanged(bool value)
    {
        if (value)
        {
            StatusMessage = $"Auto a cada {IntervalAmount}{IntervalUnit}";
            _ = StartAutoRefreshAsync();
        }
        else
        {
            StopAutoRefresh();
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
        catch (OperationCanceledException) { }
    }

    private void StopAutoRefresh()
    {
        if (_autoRefreshCts is { IsCancellationRequested: false })
            _autoRefreshCts.Cancel();

        _autoRefreshCts?.Dispose();
        _autoRefreshCts = null;
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
                WHERE state IS NOT NULL
                ORDER BY query_start DESC
                """;

            var result = await _provider.ExecuteAsync(sql, ct);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Error);

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
            if (!result.IsSuccess) throw new InvalidOperationException(result.Error);

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

        throw new NotSupportedException($"Active connections not supported for {_provider.EngineName}.");
    }
}
