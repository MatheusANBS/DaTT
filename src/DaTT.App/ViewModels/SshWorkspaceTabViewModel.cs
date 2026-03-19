using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace DaTT.App.ViewModels;

public partial class SshWorkspaceTabViewModel : TabViewModel, IAsyncDisposable
{
    private readonly SshConnectionSettings _settings;
    private SshClient? _sshClient;
    private SftpClient? _sftpClient;
    private readonly ConcurrentDictionary<Guid, ForwardedPortLocal> _activeForwards = new();

    public override string Title => $"SSH: {_settings.DisplayName}";

    [ObservableProperty]
    private string _currentPath = "/";

    [ObservableProperty]
    private string? _selectedPath;

    [ObservableProperty]
    private SshFileEntry? _selectedEntry;

    [ObservableProperty]
    private string _newFolderName = string.Empty;

    [ObservableProperty]
    private string _newFileName = string.Empty;

    [ObservableProperty]
    private string _terminalCommand = "pwd";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _localPortInput = "8080";

    [ObservableProperty]
    private string _remoteHostInput = "127.0.0.1";

    [ObservableProperty]
    private string _remotePortInput = "80";

    public ObservableCollection<SshFileEntry> Entries { get; } = [];
    public ObservableCollection<string> TerminalLines { get; } = [];
    public ObservableCollection<SshPortForwardRule> PortForwardRules { get; } = [];

    public bool IsConnected => _sshClient?.IsConnected == true && _sftpClient?.IsConnected == true;

    public SshWorkspaceTabViewModel(SshConnectionSettings settings)
    {
        _settings = settings;
        _currentPath = settings.StartPath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await ConnectAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            StatusMessage = "SSH is already connected.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var connectionInfo = _settings.BuildConnectionInfo();
            _sshClient = new SshClient(connectionInfo);
            _sftpClient = new SftpClient(connectionInfo);

            await Task.Run(() => _sshClient.Connect(), cancellationToken);
            await Task.Run(() => _sftpClient.Connect(), cancellationToken);

            OnPropertyChanged(nameof(IsConnected));
            StatusMessage = $"Connected to {_settings.Host}:{_settings.Port}";
            await RefreshDirectoryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Failed to connect SSH.";
            await DisconnectInternalAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await DisconnectInternalAsync();
        Entries.Clear();
        StatusMessage = "Disconnected.";
    }

    [RelayCommand]
    private async Task RefreshDirectoryAsync(CancellationToken cancellationToken = default)
    {
        if (!EnsureConnected())
            return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var targetPath = string.IsNullOrWhiteSpace(SelectedPath) ? CurrentPath : SelectedPath;
            var list = await Task.Run(() => _sftpClient!.ListDirectory(targetPath).ToList(), cancellationToken);

            Entries.Clear();
            foreach (var item in list
                         .Where(x => x.Name is not "." and not "..")
                         .OrderByDescending(x => x.IsDirectory)
                         .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                Entries.Add(new SshFileEntry(
                    item.Name,
                    item.FullName,
                    item.IsDirectory,
                    item.Length,
                    item.LastWriteTime));
            }

            CurrentPath = targetPath;
            SelectedPath = null;
            SelectedEntry = null;
            StatusMessage = $"Loaded {Entries.Count} item(s) from {CurrentPath}";
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
    private async Task NavigateUpAsync(CancellationToken cancellationToken = default)
    {
        if (!EnsureConnected())
            return;

        var parent = GetParentPath(CurrentPath);
        SelectedPath = parent;
        await RefreshDirectoryAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task OpenSelectedEntryAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedEntry is null || !SelectedEntry.IsDirectory)
            return;

        SelectedPath = SelectedEntry.FullPath;
        await RefreshDirectoryAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task CreateFolderAsync(CancellationToken cancellationToken = default)
    {
        if (!EnsureConnected())
            return;

        if (string.IsNullOrWhiteSpace(NewFolderName))
            return;

        var remotePath = CombinePath(CurrentPath, NewFolderName.Trim());

        try
        {
            await Task.Run(() => _sftpClient!.CreateDirectory(remotePath), cancellationToken);
            NewFolderName = string.Empty;
            await RefreshDirectoryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task CreateFileAsync(CancellationToken cancellationToken = default)
    {
        if (!EnsureConnected())
            return;

        if (string.IsNullOrWhiteSpace(NewFileName))
            return;

        var remotePath = CombinePath(CurrentPath, NewFileName.Trim());

        try
        {
            await Task.Run(() =>
            {
                using var stream = _sftpClient!.Create(remotePath);
            }, cancellationToken);

            NewFileName = string.Empty;
            await RefreshDirectoryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedEntryAsync(CancellationToken cancellationToken = default)
    {
        if (!EnsureConnected())
            return;

        if (SelectedEntry is null)
            return;

        try
        {
            await Task.Run(() =>
            {
                if (SelectedEntry.IsDirectory)
                    _sftpClient!.DeleteDirectory(SelectedEntry.FullPath);
                else
                    _sftpClient!.DeleteFile(SelectedEntry.FullPath);
            }, cancellationToken);

            await RefreshDirectoryAsync(cancellationToken);
        }
        catch (SftpPermissionDeniedException)
        {
            ErrorMessage = "Permission denied while deleting selected entry.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task UploadFileAsync(string localPath, CancellationToken cancellationToken = default)
    {
        if (!EnsureConnected())
            return;

        var fileName = Path.GetFileName(localPath);
        if (string.IsNullOrWhiteSpace(fileName))
            return;

        var remotePath = CombinePath(CurrentPath, fileName);

        try
        {
            await using var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await Task.Run(() => _sftpClient!.UploadFile(fs, remotePath, true), cancellationToken);
            StatusMessage = $"Uploaded: {fileName}";
            await RefreshDirectoryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task DownloadSelectedFileAsync(string localPath, CancellationToken cancellationToken = default)
    {
        if (!EnsureConnected())
            return;

        if (SelectedEntry is null || SelectedEntry.IsDirectory)
            return;

        try
        {
            await using var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await Task.Run(() => _sftpClient!.DownloadFile(SelectedEntry.FullPath, fs), cancellationToken);
            StatusMessage = $"Downloaded: {SelectedEntry.Name}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ExecuteTerminalCommandAsync(CancellationToken cancellationToken = default)
    {
        if (!EnsureConnected())
            return;

        if (string.IsNullOrWhiteSpace(TerminalCommand))
            return;

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            var commandText = TerminalCommand.Trim();
            TerminalLines.Insert(0, $"> {commandText}");

            var result = await Task.Run(() => _sshClient!.RunCommand(commandText), cancellationToken);

            if (!string.IsNullOrWhiteSpace(result.Result))
                foreach (var line in SplitLines(result.Result).Reverse())
                    TerminalLines.Insert(0, line);

            if (!string.IsNullOrWhiteSpace(result.Error))
                foreach (var line in SplitLines(result.Error).Reverse())
                    TerminalLines.Insert(0, "ERR: " + line);

            StatusMessage = $"Command exit status: {result.ExitStatus}";
            TrimTerminalLines();
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
    private void AddPortForwardRule()
    {
        if (!int.TryParse(LocalPortInput, out var localPort) || localPort is <= 0 or > 65535)
        {
            ErrorMessage = "Invalid local port.";
            return;
        }

        if (!int.TryParse(RemotePortInput, out var remotePort) || remotePort is <= 0 or > 65535)
        {
            ErrorMessage = "Invalid remote port.";
            return;
        }

        var host = string.IsNullOrWhiteSpace(RemoteHostInput) ? "127.0.0.1" : RemoteHostInput.Trim();
        var rule = new SshPortForwardRule(Guid.NewGuid(), localPort, host, remotePort, "Stopped");
        PortForwardRules.Add(rule);
        ErrorMessage = null;
    }

    [RelayCommand]
    private void RemovePortForwardRule(SshPortForwardRule? rule)
    {
        if (rule is null)
            return;

        StopPortForwardRule(rule);
        PortForwardRules.Remove(rule);
    }

    [RelayCommand]
    private void StartPortForwardRule(SshPortForwardRule? rule)
    {
        if (rule is null || !EnsureConnected())
            return;

        if (_activeForwards.ContainsKey(rule.Id))
            return;

        try
        {
            var forward = new ForwardedPortLocal("127.0.0.1", (uint)rule.LocalPort, rule.RemoteHost, (uint)rule.RemotePort);
            _sshClient!.AddForwardedPort(forward);
            forward.Start();

            _activeForwards[rule.Id] = forward;
            rule.Status = "Running";
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            rule.Status = "Error";
        }
    }

    [RelayCommand]
    private void StopPortForwardRule(SshPortForwardRule? rule)
    {
        if (rule is null)
            return;

        if (_activeForwards.TryRemove(rule.Id, out var forward))
        {
            try
            {
                if (forward.IsStarted)
                    forward.Stop();

                _sshClient?.RemoveForwardedPort(forward);
            }
            catch
            {
            }
            finally
            {
                forward.Dispose();
            }
        }

        rule.Status = "Stopped";
    }

    private bool EnsureConnected()
    {
        if (IsConnected)
            return true;

        ErrorMessage = "SSH is not connected.";
        return false;
    }

    private async Task DisconnectInternalAsync()
    {
        foreach (var rule in PortForwardRules.ToList())
            StopPortForwardRule(rule);

        await Task.Run(() =>
        {
            if (_sftpClient?.IsConnected == true)
                _sftpClient.Disconnect();

            if (_sshClient?.IsConnected == true)
                _sshClient.Disconnect();
        });

        _sftpClient?.Dispose();
        _sshClient?.Dispose();
        _sftpClient = null;
        _sshClient = null;
        OnPropertyChanged(nameof(IsConnected));
    }

    private static string CombinePath(string basePath, string segment)
    {
        if (string.IsNullOrWhiteSpace(basePath) || basePath == "/")
            return "/" + segment.TrimStart('/');

        return basePath.TrimEnd('/') + "/" + segment.Trim('/');
    }

    private static string GetParentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            return "/";

        var normalized = path.TrimEnd('/');
        var idx = normalized.LastIndexOf('/');
        if (idx <= 0)
            return "/";

        return normalized[..idx];
    }

    private static IEnumerable<string> SplitLines(string content)
        => content.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private void TrimTerminalLines()
    {
        while (TerminalLines.Count > 600)
            TerminalLines.RemoveAt(TerminalLines.Count - 1);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectInternalAsync();
    }
}

public sealed class SshConnectionSettings
{
    public required string DisplayName { get; init; }
    public required string Host { get; init; }
    public int Port { get; init; } = 22;
    public required string Username { get; init; }
    public string? Password { get; init; }
    public string? PrivateKeyPath { get; init; }
    public string? PrivateKeyPassphrase { get; init; }
    public string StartPath { get; init; } = "/";

    public ConnectionInfo BuildConnectionInfo()
    {
        var methods = new List<AuthenticationMethod>();

        if (!string.IsNullOrWhiteSpace(PrivateKeyPath) && File.Exists(PrivateKeyPath))
        {
            PrivateKeyFile keyFile = string.IsNullOrWhiteSpace(PrivateKeyPassphrase)
                ? new PrivateKeyFile(PrivateKeyPath)
                : new PrivateKeyFile(PrivateKeyPath, PrivateKeyPassphrase);

            methods.Add(new PrivateKeyAuthenticationMethod(Username, keyFile));
        }

        if (!string.IsNullOrWhiteSpace(Password))
            methods.Add(new PasswordAuthenticationMethod(Username, Password));

        if (methods.Count == 0)
            throw new InvalidOperationException("SSH connection requires password or private key authentication.");

        return new ConnectionInfo(Host, Port, Username, methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public static SshConnectionSettings Parse(string connectionName, string connectionString)
    {
        if (!connectionString.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SSH connection string must start with ssh://");

        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2, StringSplitOptions.TrimEntries);
        if (userInfo.Length == 0 || string.IsNullOrWhiteSpace(userInfo[0]))
            throw new InvalidOperationException("SSH connection string must include username.");

        string? password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null;

        var queryParams = ParseQuery(uri.Query);
        var startPath = queryParams.TryGetValue("path", out var path) && !string.IsNullOrWhiteSpace(path)
            ? path
            : "/";

        queryParams.TryGetValue("keyFile", out var keyFile);
        queryParams.TryGetValue("passphrase", out var passphrase);

        return new SshConnectionSettings
        {
            DisplayName = connectionName,
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 22,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = password,
            PrivateKeyPath = keyFile,
            PrivateKeyPassphrase = passphrase,
            StartPath = startPath.StartsWith('/') ? startPath : "/" + startPath
        };
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return result;

        var trimmed = query.TrimStart('?');
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }
}

public sealed class SshFileEntry
{
    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public long Size { get; }
    public DateTime LastWriteTime { get; }
    public string Type => IsDirectory ? "dir" : "file";

    public SshFileEntry(string name, string fullPath, bool isDirectory, long size, DateTime lastWriteTime)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        Size = size;
        LastWriteTime = lastWriteTime;
    }
}

public partial class SshPortForwardRule : ObservableObject
{
    public Guid Id { get; }
    public int LocalPort { get; }
    public string RemoteHost { get; }
    public int RemotePort { get; }

    [ObservableProperty]
    private string _status;

    public SshPortForwardRule(Guid id, int localPort, string remoteHost, int remotePort, string status)
    {
        Id = id;
        LocalPort = localPort;
        RemoteHost = remoteHost;
        RemotePort = remotePort;
        _status = status;
    }
}
