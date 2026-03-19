using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using DaTT.App.ViewModels;

namespace DaTT.App.Views;

public partial class SshWorkspaceTabView : UserControl
{
    public SshWorkspaceTabView()
    {
        InitializeComponent();
    }

    private async void OnEntriesDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not SshWorkspaceTabViewModel vm)
            return;

        await vm.OpenSelectedEntryCommand.ExecuteAsync(null);
    }

    private async void OnUploadClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not SshWorkspaceTabViewModel vm)
            return;

        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
            return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Upload file",
            AllowMultiple = false
        });

        var localPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath))
            return;

        await vm.UploadFileAsync(localPath);
    }

    private async void OnDownloadClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not SshWorkspaceTabViewModel vm || vm.SelectedEntry is null || vm.SelectedEntry.IsDirectory)
            return;

        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is null)
            return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Download file",
            SuggestedFileName = vm.SelectedEntry.Name
        });

        var localPath = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath))
            return;

        await vm.DownloadSelectedFileAsync(localPath);
    }
}
