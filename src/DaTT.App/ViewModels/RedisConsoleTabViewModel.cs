using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTT.Core.Interfaces;
using DaTT.Core.Models;

namespace DaTT.App.ViewModels;

public partial class RedisConsoleTabViewModel : TabViewModel
{
    private readonly IDatabaseProvider _provider;

    public override string Title => "Redis Console";

    [ObservableProperty]
    private string _commandText = "PING";

    [ObservableProperty]
    private string? _selectedKey;

    [ObservableProperty]
    private string _renameTarget = string.Empty;

    [ObservableProperty]
    private string _ttlSecondsInput = "60";

    [ObservableProperty]
    private string _setValueInput = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _selectedKeyType = "n/a";

    [ObservableProperty]
    private string _selectedKeyTtl = "n/a";

    public ObservableCollection<string> Keys { get; } = [];
    public ObservableCollection<string> ConsoleLines { get; } = [];
    public ObservableCollection<RedisStatusMetric> StatusMetrics { get; } = [];

    public RedisConsoleTabViewModel(IDatabaseProvider provider)
    {
        _provider = provider;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshKeysAsync(cancellationToken);
        await RefreshStatusAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task ExecuteCommandAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(CommandText))
            return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var command = CommandText.Trim();
            var verb = command.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();

            if (IsWriteCommand(verb))
            {
                var writeResult = await _provider.ExecuteAsync(command, cancellationToken);
                ConsoleLines.Insert(0, $"> {command}");
                ConsoleLines.Insert(0, $"affected: {writeResult.AffectedRows}");
            }
            else
            {
                var result = await _provider.ExecuteAsync(command, cancellationToken);
                ConsoleLines.Insert(0, $"> {command}");

                if (!result.IsSuccess)
                {
                    ConsoleLines.Insert(0, $"error: {result.Error}");
                }
                else
                {
                    var rowCount = result.Rows.Count;
                    ConsoleLines.Insert(0, $"rows: {rowCount}, time: {result.ExecutionTime.TotalMilliseconds:F0} ms");
                    foreach (var row in result.Rows.Take(20))
                        ConsoleLines.Insert(0, string.Join(" | ", row.Select(v => v?.ToString() ?? "NULL")));
                }
            }

            while (ConsoleLines.Count > 400)
                ConsoleLines.RemoveAt(ConsoleLines.Count - 1);

            await RefreshStatusAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(SelectedKey))
                await LoadSelectedKeyDetailsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ConsoleLines.Insert(0, $"error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshKeysAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var keys = await _provider.GetTablesAsync(cancellationToken);
            Keys.Clear();
            foreach (var key in keys)
                Keys.Add(key.Name);

            if (SelectedKey is null && Keys.Count > 0)
                SelectedKey = Keys[0];

            StatusMessage = $"Loaded {Keys.Count} key(s).";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Failed to refresh keys.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedKeyChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _ = LoadSelectedKeyDetailsAsync();
    }

    [RelayCommand]
    private async Task LoadSelectedKeyDetailsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(SelectedKey))
            return;

        try
        {
            var typeResult = await _provider.ExecuteAsync($"TYPE {SelectedKey}", cancellationToken);
            var ttlResult = await _provider.ExecuteAsync($"TTL {SelectedKey}", cancellationToken);

            SelectedKeyType = typeResult.Rows.FirstOrDefault()?[0]?.ToString() ?? "n/a";
            SelectedKeyTtl = ttlResult.Rows.FirstOrDefault()?[0]?.ToString() ?? "n/a";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            SelectedKeyType = "error";
            SelectedKeyTtl = "error";
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedKeyAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(SelectedKey))
            return;

        await _provider.ExecuteAsync($"DEL {SelectedKey}", cancellationToken);
        ConsoleLines.Insert(0, $"deleted key: {SelectedKey}");
        SelectedKey = null;
        await RefreshKeysAsync(cancellationToken);
        await RefreshStatusAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RenameSelectedKeyAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(SelectedKey) || string.IsNullOrWhiteSpace(RenameTarget))
            return;

        await _provider.ExecuteAsync($"RENAME {SelectedKey} {RenameTarget.Trim()}", cancellationToken);
        ConsoleLines.Insert(0, $"renamed key: {SelectedKey} -> {RenameTarget.Trim()}");
        SelectedKey = RenameTarget.Trim();
        RenameTarget = string.Empty;
        await RefreshKeysAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task SetTtlSelectedKeyAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(SelectedKey) || !int.TryParse(TtlSecondsInput, out var seconds) || seconds < 0)
            return;

        await _provider.ExecuteAsync($"EXPIRE {SelectedKey} {seconds}", cancellationToken);
        ConsoleLines.Insert(0, $"ttl set: {SelectedKey} = {seconds}s");
        await LoadSelectedKeyDetailsAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task SetStringValueAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(SelectedKey))
            return;

        var value = (SetValueInput ?? string.Empty).Replace("\"", "\\\"");
        await _provider.ExecuteAsync($"SET {SelectedKey} \"{value}\"", cancellationToken);
        ConsoleLines.Insert(0, $"set value: {SelectedKey}");
        await LoadSelectedKeyDetailsAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task RefreshStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var ping = await _provider.ExecuteAsync("PING", cancellationToken);
            var dbsize = await _provider.ExecuteAsync("DBSIZE", cancellationToken);
            var info = await _provider.ExecuteAsync("INFO", cancellationToken);

            StatusMetrics.Clear();

            var pingMs = ping.Rows.FirstOrDefault()?[0]?.ToString() ?? "n/a";
            var dbSizeValue = dbsize.Rows.FirstOrDefault()?[0]?.ToString() ?? "n/a";

            StatusMetrics.Add(new RedisStatusMetric("Ping (ms)", pingMs));
            StatusMetrics.Add(new RedisStatusMetric("DB Size", dbSizeValue));

            foreach (var row in info.Rows)
            {
                var section = row.ElementAtOrDefault(0)?.ToString() ?? string.Empty;
                var key = row.ElementAtOrDefault(1)?.ToString() ?? string.Empty;
                var value = row.ElementAtOrDefault(2)?.ToString() ?? string.Empty;

                if (section == "Server" && (key == "redis_version" || key == "uptime_in_seconds"))
                    StatusMetrics.Add(new RedisStatusMetric(key, value));

                if (section == "Memory" && (key == "used_memory_human" || key == "maxmemory_human"))
                    StatusMetrics.Add(new RedisStatusMetric(key, value));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private static bool IsWriteCommand(string verb)
        => verb is "DEL" or "EXPIRE" or "RENAME" or "SET" or "HSET" or "SADD" or "LPUSH";
}

public sealed record RedisStatusMetric(string Metric, string Value);
