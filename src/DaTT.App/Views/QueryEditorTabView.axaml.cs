using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using DaTT.App.ViewModels;

namespace DaTT.App.Views;

public partial class QueryEditorTabView : UserControl
{
    private QueryEditorTabViewModel? _vm;

    public QueryEditorTabView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.QueryResultColumns.CollectionChanged -= OnColumnsChanged;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as QueryEditorTabViewModel;
        if (_vm is null) return;

        _vm.QueryResultColumns.CollectionChanged += OnColumnsChanged;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _ = _vm.InitializeLanguageToolsAsync();
        _vm.RefreshSqlOutline(_vm.QueryText);
    }

    private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildColumns();

    private void RebuildColumns()
    {
        var grid = this.FindControl<DataGrid>("ResultGrid");
        if (grid is null || _vm is null) return;

        grid.Columns.Clear();
        for (int i = 0; i < _vm.QueryResultColumns.Count; i++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = _vm.QueryResultColumns[i],
                Binding = new Binding($"[{i}]") { TargetNullValue = string.Empty },
                IsReadOnly = true
            });
        }
    }

    private async void OnRunClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null) return;
        var editor = this.FindControl<TextBox>("QueryEditor");
        if (editor is null) return;

        var selected = GetSelectedText(editor);
        if (!string.IsNullOrWhiteSpace(selected))
        {
            await _vm.RunSingleSqlAsync(selected);
            return;
        }

        var current = QueryEditorTabViewModel.ExtractCurrentStatement(editor.Text ?? string.Empty, editor.CaretIndex);
        if (!string.IsNullOrWhiteSpace(current))
            await _vm.RunSingleSqlAsync(current);
    }

    private async void OnRunAllClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null) return;
        await _vm.RunBatchCommand.ExecuteAsync(null);
    }

    private async void OnQueryEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _vm is null)
            return;

        var hasCtrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (!hasCtrl)
            return;

        var hasShift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        if (hasShift)
            await _vm.RunBatchCommand.ExecuteAsync(null);
        else
            OnRunClick(sender, e);

        e.Handled = true;
    }

    private void OnQueryEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_vm is null || sender is not TextBox editor)
            return;

        _vm.RefreshSqlOutline(editor.Text ?? string.Empty);
        _vm.UpdateCompletionSuggestions(editor.Text ?? string.Empty, editor.CaretIndex);
    }

    private void OnQueryEditorKeyUp(object? sender, KeyEventArgs e)
    {
        if (_vm is null || sender is not TextBox editor)
            return;

        _vm.UpdateCompletionSuggestions(editor.Text ?? string.Empty, editor.CaretIndex);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null)
            return;

        if (e.PropertyName == nameof(QueryEditorTabViewModel.QueryText))
        {
            var editor = this.FindControl<TextBox>("QueryEditor");
            if (editor is not null)
                _vm.UpdateCompletionSuggestions(editor.Text ?? string.Empty, editor.CaretIndex);
        }
    }

    private void OnOutlineSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm is null || sender is not ListBox listBox)
            return;

        if (listBox.SelectedItem is not SqlOutlineItem selected)
            return;

        var editor = this.FindControl<TextBox>("QueryEditor");
        if (editor is null)
            return;

        editor.Focus();
        editor.CaretIndex = Math.Clamp(selected.StartIndex, 0, (editor.Text ?? string.Empty).Length);
        _vm.UpdateCompletionSuggestions(editor.Text ?? string.Empty, editor.CaretIndex);
    }

    private void OnSuggestionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm is null || sender is not ListBox suggestionList)
            return;

        if (suggestionList.SelectedItem is not string suggestion)
            return;

        var editor = this.FindControl<TextBox>("QueryEditor");
        if (editor is null)
            return;

        var text = editor.Text ?? string.Empty;
        var (start, end, _) = QueryEditorTabViewModel.GetTokenReplacementRange(text, editor.CaretIndex);

        var updated = text[..start] + suggestion + text[end..];
        editor.Text = updated;
        editor.CaretIndex = start + suggestion.Length;
        suggestionList.SelectedItem = null;

        _vm.UpdateCompletionSuggestions(updated, editor.CaretIndex);
    }

    private async void OnQueryTableAtCaretClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null)
            return;

        var editor = this.FindControl<TextBox>("QueryEditor");
        if (editor is null)
            return;

        await _vm.QueryTableAtCaretAsync(editor.Text ?? string.Empty, editor.CaretIndex);
    }

    private void OnHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_vm is null || sender is not ListBox list)
            return;

        if (list.SelectedItem is not QueryHistoryEntry entry)
            return;

        _vm.LoadHistoryEntryCommand.Execute(entry);
    }

    private async void OnRunSelectedHistoryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null)
            return;

        var historyList = this.FindControl<ListBox>("HistoryList");
        if (historyList?.SelectedItem is not QueryHistoryEntry entry)
            return;

        await _vm.RunHistoryEntryCommand.ExecuteAsync(entry);
    }

    private static string? GetSelectedText(TextBox editor)
    {
        var start = Math.Min(editor.SelectionStart, editor.SelectionEnd);
        var end = Math.Max(editor.SelectionStart, editor.SelectionEnd);
        if (end <= start)
            return null;

        var text = editor.Text ?? string.Empty;
        if (start < 0 || end > text.Length)
            return null;

        return text.Substring(start, end - start);
    }
}
