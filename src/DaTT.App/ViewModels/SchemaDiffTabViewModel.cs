using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTT.Core.Interfaces;
using DaTT.Core.Models;
using DaTT.Core.Services;

namespace DaTT.App.ViewModels;

public partial class SchemaDiffTabViewModel : TabViewModel
{
    private readonly IDatabaseProvider _provider;

    public override string Title => "Struct Diff";

    [ObservableProperty]
    private string? _selectedSourceTable;

    [ObservableProperty]
    private string? _selectedDestinationTable;

    [ObservableProperty]
    private bool _includeDrops;

    [ObservableProperty]
    private string _generatedSql = string.Empty;

    [ObservableProperty]
    private string _resultMessage = string.Empty;

    public ObservableCollection<string> Tables { get; } = [];
    public ObservableCollection<SchemaDiffItem> DiffItems { get; } = [];

    public SchemaDiffTabViewModel(IDatabaseProvider provider)
    {
        _provider = provider;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            Tables.Clear();
            var tables = await _provider.GetTablesAsync(cancellationToken);
            foreach (var table in tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
                Tables.Add(table.Name);

            if (Tables.Count > 0)
            {
                SelectedSourceTable = Tables[0];
                SelectedDestinationTable = Tables.Count > 1 ? Tables[1] : Tables[0];
            }
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

    [RelayCommand]
    private async Task GenerateDiffAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(SelectedSourceTable) || string.IsNullOrWhiteSpace(SelectedDestinationTable))
        {
            ResultMessage = "Select source and destination tables.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        ResultMessage = string.Empty;

        try
        {
            var sourceColumns = await _provider.GetColumnsAsync(SelectedSourceTable, cancellationToken);
            var sourceIndexes = await _provider.GetIndexesAsync(SelectedSourceTable, cancellationToken);
            var destinationColumns = await _provider.GetColumnsAsync(SelectedDestinationTable, cancellationToken);
            var destinationIndexes = await _provider.GetIndexesAsync(SelectedDestinationTable, cancellationToken);

            var plan = SchemaDiffService.BuildPlan(
                SelectedSourceTable, sourceColumns, sourceIndexes,
                destinationColumns, destinationIndexes,
                _provider.EngineName, IncludeDrops);

            DiffItems.Clear();
            foreach (var item in plan.Items)
                DiffItems.Add(item);

            GeneratedSql = string.Join(Environment.NewLine, plan.Items.Select(i => i.Sql));
            ResultMessage = plan.HasChanges
                ? $"Generated {plan.Items.Count} change(s)."
                : "No differences detected.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ResultMessage = "Failed to generate diff.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyAllAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(GeneratedSql))
        {
            ResultMessage = "No SQL to execute.";
            return;
        }

        var statements = SplitSqlStatements(GeneratedSql);
        if (statements.Count == 0)
        {
            ResultMessage = "No SQL statements found.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            foreach (var statement in statements)
                await _provider.ExecuteAsync(statement, cancellationToken);

            ResultMessage = $"Applied {statements.Count} statement(s).";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            ResultMessage = "Failed to apply diff SQL.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static List<string> SplitSqlStatements(string script)
    {
        var statements = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inSingleQuote = false;
        bool inDoubleQuote = false;

        for (int i = 0; i < script.Length; i++)
        {
            var ch = script[i];

            if (ch == '\'' && !inDoubleQuote)
            {
                if (inSingleQuote && i + 1 < script.Length && script[i + 1] == '\'')
                {
                    current.Append(ch);
                    current.Append(script[++i]);
                    continue;
                }

                inSingleQuote = !inSingleQuote;
            }
            else if (ch == '"' && !inSingleQuote)
            {
                if (inDoubleQuote && i + 1 < script.Length && script[i + 1] == '"')
                {
                    current.Append(ch);
                    current.Append(script[++i]);
                    continue;
                }

                inDoubleQuote = !inDoubleQuote;
            }

            if (ch == ';' && !inSingleQuote && !inDoubleQuote)
            {
                var statement = current.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(statement))
                    statements.Add(statement);
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        var tail = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(tail))
            statements.Add(tail);

        return statements;
    }
}
