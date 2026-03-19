using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTT.Core.Interfaces;
using DaTT.Core.Models;
using DaTT.Core.Services;
using Renci.SshNet;

namespace DaTT.App.ViewModels;

public partial class ConnectionManagerViewModel : TabViewModel
{
    public override string Title => "Connections";
    private const int DefaultConnectivityTimeoutSeconds = 10;
    private readonly IConnectionConfigService _configService;
    private readonly IProviderFactory _providerFactory;

    public ObservableCollection<ConnectionConfig> Connections { get; } = [];

    [ObservableProperty]
    private ConnectionConfig? _selectedConnection;

    [ObservableProperty]
    private string _newConnectionName = string.Empty;

    [ObservableProperty]
    private string _newConnectionString = string.Empty;

    [ObservableProperty]
    private string _selectedEngine = "MySQL";

    [ObservableProperty]
    private bool _useBuilderMode = true;

    [ObservableProperty]
    private string _builderHost = "127.0.0.1";

    [ObservableProperty]
    private string _builderPort = "3306";

    [ObservableProperty]
    private string _builderDatabase = string.Empty;

    [ObservableProperty]
    private string _builderUsername = string.Empty;

    [ObservableProperty]
    private string _builderPassword = string.Empty;

    [ObservableProperty]
    private bool _builderUseSsl;

    [ObservableProperty]
    private string _builderRedisDb = "0";

    [ObservableProperty]
    private string _builderElasticUrl = "http://127.0.0.1:9200";

    [ObservableProperty]
    private string _builderElasticAuthMode = "none";

    [ObservableProperty]
    private string _builderElasticApiKey = string.Empty;

    [ObservableProperty]
    private string _builderSshHost = "127.0.0.1";

    [ObservableProperty]
    private string _builderSshPort = "22";

    [ObservableProperty]
    private string _builderSshUsername = "root";

    [ObservableProperty]
    private string _builderSshAuthType = "password";

    [ObservableProperty]
    private string _builderSshPassword = string.Empty;

    [ObservableProperty]
    private string _builderSshPrivateKeyPath = string.Empty;

    [ObservableProperty]
    private string _builderSshPassphrase = string.Empty;

    [ObservableProperty]
    private string _builderSshStartPath = "/";

    [ObservableProperty]
    private string _builderPreviewConnectionString = string.Empty;

    [ObservableProperty]
    private string? _testResultMessage;

    [ObservableProperty]
    private bool _isTestSuccess;

    public IReadOnlyList<string> SupportedEngines { get; }
    public IReadOnlyList<string> ElasticAuthModes { get; } = ["none", "basic", "token"];
    public IReadOnlyList<string> SshAuthModes { get; } = ["password", "privateKey"];

    public bool IsSshBuilderVisible => SelectedEngine.Equals("SSH", StringComparison.OrdinalIgnoreCase);
    public bool IsElasticBuilderVisible => SelectedEngine.Equals("ElasticSearch", StringComparison.OrdinalIgnoreCase);
    public bool IsRedisBuilderVisible => SelectedEngine.Equals("Redis", StringComparison.OrdinalIgnoreCase);
    public bool IsGenericBuilderVisible => !IsSshBuilderVisible && !IsElasticBuilderVisible;

    public ConnectionManagerViewModel(IConnectionConfigService configService, IProviderFactory providerFactory)
    {
        _configService = configService;
        _providerFactory = providerFactory;
        SupportedEngines = providerFactory.SupportedEngineNames
            .Append("SSH")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

            ApplyEngineDefaults(SelectedEngine);
            UpdateBuilderPreview();
    }

    public async Task LoadConnectionsAsync()
    {
        var configs = await _configService.GetAllAsync();
        Connections.Clear();
        foreach (var c in configs)
            Connections.Add(c);
    }

    partial void OnNewConnectionStringChanged(string value)
    {
        var detected = DetectEngine(value);
        if (detected is not null)
            SelectedEngine = detected;
    }

    partial void OnSelectedEngineChanged(string value)
    {
        ApplyEngineDefaults(value);
        RaiseBuilderVisibilityChanged();
        UpdateBuilderPreview();
    }

    partial void OnSelectedConnectionChanged(ConnectionConfig? value)
    {
        if (value is null)
            return;

        SelectedEngine = value.Engine;
        NewConnectionName = value.Name;
        NewConnectionString = value.ConnectionString;

        if (value.Parameters is not null)
            LoadBuilderFromParameters(value.Engine, value.Parameters);

        UpdateBuilderPreview();
    }

    private static string? DetectEngine(string cs) => cs switch
    {
        _ when cs.StartsWith("mariadb://", StringComparison.OrdinalIgnoreCase) => "MariaDB",
        _ when cs.StartsWith("mysql://", StringComparison.OrdinalIgnoreCase) => "MySQL",
        _ when cs.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) => "PostgreSQL",
        _ when cs.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) => "PostgreSQL",
        _ when cs.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase) => "MongoDB",
        _ when cs.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) => "MongoDB",
        _ when cs.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) => "Redis",
        _ when cs.StartsWith("elasticsearch://", StringComparison.OrdinalIgnoreCase) => "ElasticSearch",
        _ when cs.StartsWith("es://", StringComparison.OrdinalIgnoreCase) => "ElasticSearch",
        _ when cs.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) => "SSH",
        _ when cs.StartsWith("jdbc:oracle", StringComparison.OrdinalIgnoreCase) => "Oracle",
        _ when cs.StartsWith("jdbc:hive2", StringComparison.OrdinalIgnoreCase) => "Hive",
        _ => null
    };

    [RelayCommand]
    private async Task AddConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewConnectionName))
        {
            IsTestSuccess = false;
            TestResultMessage = "Connection name is required.";
            return;
        }

        var effectiveConnectionString = ResolveDraftConnectionString();
        if (string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            IsTestSuccess = false;
            TestResultMessage = "Connection string is required or configure builder fields.";
            return;
        }

        if (Connections.Any(c => string.Equals(c.Name, NewConnectionName, StringComparison.OrdinalIgnoreCase)))
        {
            IsTestSuccess = false;
            TestResultMessage = $"A connection named '{NewConnectionName}' already exists.";
            return;
        }

        var config = new ConnectionConfig
        {
            Name = NewConnectionName,
            Engine = SelectedEngine,
            ConnectionString = effectiveConnectionString,
            Parameters = BuildParameterMap()
        };

        await _configService.SaveAsync(config);
        Connections.Add(config);
        SelectedConnection = config;
        IsTestSuccess = true;
        TestResultMessage = $"Connection '{config.Name}' saved.";
        NewConnectionName = string.Empty;
        NewConnectionString = string.Empty;
        UpdateBuilderPreview();
    }

    [RelayCommand]
    private async Task RemoveConnectionAsync()
    {
        if (SelectedConnection is null)
            return;

        await _configService.DeleteAsync(SelectedConnection.Id);
        Connections.Remove(SelectedConnection);
        SelectedConnection = null;
    }

    [RelayCommand]
    private async Task UpdateSelectedConnectionAsync()
    {
        if (SelectedConnection is null)
        {
            IsTestSuccess = false;
            TestResultMessage = "Select a connection to update.";
            return;
        }

        var effectiveConnectionString = ResolveDraftConnectionString();
        if (string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            IsTestSuccess = false;
            TestResultMessage = "Connection string is required or configure builder fields.";
            return;
        }

        SelectedConnection.Name = string.IsNullOrWhiteSpace(NewConnectionName) ? SelectedConnection.Name : NewConnectionName.Trim();
        SelectedConnection.Engine = SelectedEngine;
        SelectedConnection.ConnectionString = effectiveConnectionString;
        SelectedConnection.Parameters = BuildParameterMap();

        await _configService.SaveAsync(SelectedConnection);
        TestResultMessage = $"Connection '{SelectedConnection.Name}' updated.";
        IsTestSuccess = true;
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        var effectiveConnectionString = ResolveDraftConnectionString();
        if (string.IsNullOrWhiteSpace(effectiveConnectionString))
        {
            IsTestSuccess = false;
            TestResultMessage = "Provide a connection string or configure builder fields.";
            return;
        }

        try
        {
            var startTime = DateTime.UtcNow;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultConnectivityTimeoutSeconds));
            var success = effectiveConnectionString.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
                ? await TestSshConnectionAsync("new-ssh", effectiveConnectionString, cts.Token)
                : await _providerFactory.CreateForConnectionString(effectiveConnectionString)
                    .TestConnectionAsync(effectiveConnectionString, cts.Token);

            var latency = (DateTime.UtcNow - startTime).TotalMilliseconds;

            IsTestSuccess = success;
            TestResultMessage = success
                ? $"Connected successfully ({latency:F0} ms)"
                : "Connection failed. Check the connection string and server availability.";
        }
        catch (OperationCanceledException)
        {
            IsTestSuccess = false;
            TestResultMessage = $"Connection test timed out after {DefaultConnectivityTimeoutSeconds} seconds.";
        }
        catch (Exception ex)
        {
            IsTestSuccess = false;
            TestResultMessage = $"Error: {SanitizeErrorMessage(ex.Message)}";
        }
    }

    [RelayCommand]
    private async Task CheckAliveSelectedAsync()
    {
        var selected = SelectedConnection;
        if (selected is null)
        {
            TestResultMessage = "Select a saved connection first.";
            IsTestSuccess = false;
            return;
        }

        try
        {
            var effectiveConnectionString = GetEffectiveConnectionString(selected);
            if (string.IsNullOrWhiteSpace(effectiveConnectionString))
            {
                IsTestSuccess = false;
                TestResultMessage = "Selected connection has no valid connection settings.";
                return;
            }

            var startTime = DateTime.UtcNow;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultConnectivityTimeoutSeconds));
            var alive = effectiveConnectionString.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase)
                ? await TestSshConnectionAsync(selected.Name, effectiveConnectionString, cts.Token)
                : await _providerFactory.CreateForConnectionString(effectiveConnectionString)
                    .TestConnectionAsync(effectiveConnectionString, cts.Token);

            var latency = (DateTime.UtcNow - startTime).TotalMilliseconds;

            IsTestSuccess = alive;
            TestResultMessage = alive
                ? $"{selected.Name} is alive ({latency:F0} ms)"
                : $"{selected.Name} is not reachable.";
        }
        catch (OperationCanceledException)
        {
            IsTestSuccess = false;
            TestResultMessage = $"Alive check timed out after {DefaultConnectivityTimeoutSeconds} seconds.";
        }
        catch (Exception ex)
        {
            IsTestSuccess = false;
            TestResultMessage = $"Alive check error: {SanitizeErrorMessage(ex.Message)}";
        }
    }

    private static string SanitizeErrorMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Unknown error.";

        var sanitized = Regex.Replace(message, @"(?i)(password\s*=\s*)([^;\s]+)", "$1******");
        sanitized = Regex.Replace(sanitized, @"(?i)(pwd\s*=\s*)([^;\s]+)", "$1******");
        sanitized = Regex.Replace(sanitized, @"(?i)(://[^:/@\s]+:)([^@/\s]+)(@)", "$1******$3");
        return sanitized;
    }

    public string GetEffectiveConnectionString(ConnectionConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            var trimmed = config.ConnectionString.Trim();
            if (trimmed.Contains("://", StringComparison.Ordinal))
                return trimmed;

            if (config.Engine.Equals("SSH", StringComparison.OrdinalIgnoreCase))
                return NormalizeLegacySshConnectionString(trimmed, config.Parameters);

            return trimmed;
        }

        if (config.Parameters is null)
            return string.Empty;

        return BuildConnectionString(config.Engine, config.Parameters);
    }

    private static string NormalizeLegacySshConnectionString(string legacyValue, IReadOnlyDictionary<string, string>? parameters)
    {
        var user = parameters is not null && parameters.TryGetValue("sshUsername", out var pUser) && !string.IsNullOrWhiteSpace(pUser)
            ? pUser
            : "root";
        var port = parameters is not null && parameters.TryGetValue("sshPort", out var pPort) && !string.IsNullOrWhiteSpace(pPort)
            ? pPort
            : "22";
        var path = parameters is not null && parameters.TryGetValue("sshPath", out var pPath) && !string.IsNullOrWhiteSpace(pPath)
            ? pPath
            : "/";

        var trimmed = legacyValue.Trim();
        if (trimmed.Contains('@', StringComparison.Ordinal))
        {
            var parts = trimmed.Split('@', 2, StringSplitOptions.TrimEntries);
            if (!string.IsNullOrWhiteSpace(parts[0]))
                user = parts[0];

            var hostPart = parts[1];
            if (hostPart.Contains(':', StringComparison.Ordinal))
            {
                var hostPieces = hostPart.Split(':', 2, StringSplitOptions.TrimEntries);
                var host = hostPieces[0];
                if (hostPieces.Length > 1 && !string.IsNullOrWhiteSpace(hostPieces[1]))
                    port = hostPieces[1];

                return $"ssh://{Uri.EscapeDataString(user)}@{host}:{port}?path={Uri.EscapeDataString(path)}";
            }

            return $"ssh://{Uri.EscapeDataString(user)}@{hostPart}:{port}?path={Uri.EscapeDataString(path)}";
        }

        return $"ssh://{Uri.EscapeDataString(user)}@{trimmed}:{port}?path={Uri.EscapeDataString(path)}";
    }

    [RelayCommand]
    private void BuildConnectionString()
    {
        var built = BuildConnectionString(SelectedEngine, BuildParameterMap());
        NewConnectionString = built;
        BuilderPreviewConnectionString = built;
    }

    private string ResolveDraftConnectionString()
    {
        if (!UseBuilderMode && !string.IsNullOrWhiteSpace(NewConnectionString))
            return NewConnectionString.Trim();

        var built = BuildConnectionString(SelectedEngine, BuildParameterMap());
        if (!string.IsNullOrWhiteSpace(built))
        {
            BuilderPreviewConnectionString = built;
            return built;
        }

        return NewConnectionString.Trim();
    }

    private Dictionary<string, string> BuildParameterMap()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["host"] = BuilderHost.Trim(),
            ["port"] = BuilderPort.Trim(),
            ["database"] = BuilderDatabase.Trim(),
            ["username"] = BuilderUsername.Trim(),
            ["password"] = BuilderPassword,
            ["ssl"] = BuilderUseSsl ? "true" : "false",
            ["redisDb"] = BuilderRedisDb.Trim(),
            ["elasticUrl"] = BuilderElasticUrl.Trim(),
            ["elasticAuthMode"] = BuilderElasticAuthMode,
            ["elasticApiKey"] = BuilderElasticApiKey,
            ["sshHost"] = BuilderSshHost.Trim(),
            ["sshPort"] = BuilderSshPort.Trim(),
            ["sshUsername"] = BuilderSshUsername.Trim(),
            ["sshAuthType"] = BuilderSshAuthType,
            ["sshPassword"] = BuilderSshPassword,
            ["sshPrivateKeyPath"] = BuilderSshPrivateKeyPath.Trim(),
            ["sshPassphrase"] = BuilderSshPassphrase,
            ["sshPath"] = BuilderSshStartPath.Trim()
        };
    }

    private static string BuildConnectionString(string engine, IReadOnlyDictionary<string, string> parameters)
    {
        string Get(string key, string fallback = "")
            => parameters.TryGetValue(key, out var value) ? value : fallback;

        var host = Get("host", "127.0.0.1");
        var port = Get("port", "");
        var database = Get("database", "");
        var username = Get("username", "");
        var password = Get("password", "");

        if (engine.Equals("SSH", StringComparison.OrdinalIgnoreCase))
        {
            var sshHost = Get("sshHost", "127.0.0.1");
            var sshPort = Get("sshPort", "22");
            var sshUser = Get("sshUsername", "root");
            var sshType = Get("sshAuthType", "password");
            var sshPass = Get("sshPassword", "");
            var sshPath = Get("sshPath", "/");
            var sshKeyPath = Get("sshPrivateKeyPath", "");
            var sshPassphrase = Get("sshPassphrase", "");

            var userInfo = sshType.Equals("privateKey", StringComparison.OrdinalIgnoreCase)
                ? Uri.EscapeDataString(sshUser)
                : $"{Uri.EscapeDataString(sshUser)}:{Uri.EscapeDataString(sshPass)}";

            var query = new List<string>
            {
                $"path={Uri.EscapeDataString(string.IsNullOrWhiteSpace(sshPath) ? "/" : sshPath)}"
            };

            if (sshType.Equals("privateKey", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(sshKeyPath))
                query.Add($"keyFile={Uri.EscapeDataString(sshKeyPath)}");

            if (sshType.Equals("privateKey", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(sshPassphrase))
                query.Add($"passphrase={Uri.EscapeDataString(sshPassphrase)}");

            return $"ssh://{userInfo}@{sshHost}:{sshPort}?{string.Join("&", query)}";
        }

        if (engine.Equals("ElasticSearch", StringComparison.OrdinalIgnoreCase))
        {
            var elasticUrl = Get("elasticUrl", "http://127.0.0.1:9200").Trim();
            var authMode = Get("elasticAuthMode", "none");
            var token = Get("elasticApiKey", string.IsNullOrWhiteSpace(password) ? string.Empty : password);

            static string AppendToken(string cs, string tokenValue)
            {
                if (string.IsNullOrWhiteSpace(tokenValue))
                    return cs;

                var separator = cs.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                return cs + separator + "token=" + Uri.EscapeDataString(tokenValue);
            }

            if (authMode.Equals("basic", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(username))
            {
                var uri = new Uri(elasticUrl);
                var userInfo = string.IsNullOrWhiteSpace(password)
                    ? Uri.EscapeDataString(username)
                    : $"{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}";

                return $"elasticsearch://{userInfo}@{uri.Host}:{(uri.Port > 0 ? uri.Port : 9200)}";
            }

            if (authMode.Equals("token", StringComparison.OrdinalIgnoreCase))
            {
                if (elasticUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    elasticUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var uri = new Uri(elasticUrl);
                    return AppendToken($"elasticsearch://{uri.Host}:{(uri.Port > 0 ? uri.Port : 9200)}", token);
                }

                var normalized = elasticUrl.StartsWith("elasticsearch://", StringComparison.OrdinalIgnoreCase)
                    ? elasticUrl
                    : "elasticsearch://" + elasticUrl;

                return AppendToken(normalized, token);
            }

            if (elasticUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                elasticUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(elasticUrl);
                return $"elasticsearch://{uri.Host}:{(uri.Port > 0 ? uri.Port : 9200)}";
            }

            return elasticUrl.StartsWith("elasticsearch://", StringComparison.OrdinalIgnoreCase)
                ? elasticUrl
                : "elasticsearch://" + elasticUrl;
        }

        if (engine.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            var db = Get("redisDb", "0");
            var auth = string.IsNullOrWhiteSpace(password)
                ? string.Empty
                : $":{Uri.EscapeDataString(password)}@";

            return $"redis://{auth}{host}:{(string.IsNullOrWhiteSpace(port) ? "6379" : port)}/{db}";
        }

        if (engine.Equals("MongoDB", StringComparison.OrdinalIgnoreCase))
        {
            var credentials = string.IsNullOrWhiteSpace(username)
                ? string.Empty
                : string.IsNullOrWhiteSpace(password)
                    ? Uri.EscapeDataString(username) + "@"
                    : $"{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}@";

            var dbPath = string.IsNullOrWhiteSpace(database) ? string.Empty : "/" + Uri.EscapeDataString(database);
            return $"mongodb://{credentials}{host}:{(string.IsNullOrWhiteSpace(port) ? "27017" : port)}{dbPath}";
        }

        if (engine.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) ||
            engine.Equals("MySQL", StringComparison.OrdinalIgnoreCase) ||
            engine.Equals("MariaDB", StringComparison.OrdinalIgnoreCase))
        {
            var scheme = engine.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase)
                ? "postgresql"
                : engine.Equals("MariaDB", StringComparison.OrdinalIgnoreCase)
                    ? "mariadb"
                    : "mysql";

            var defaultPort = scheme == "postgresql" ? "5432" : "3306";
            var auth = string.IsNullOrWhiteSpace(username)
                ? string.Empty
                : string.IsNullOrWhiteSpace(password)
                    ? Uri.EscapeDataString(username) + "@"
                    : $"{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}@";

            var dbPath = string.IsNullOrWhiteSpace(database) ? string.Empty : "/" + Uri.EscapeDataString(database);
            return $"{scheme}://{auth}{host}:{(string.IsNullOrWhiteSpace(port) ? defaultPort : port)}{dbPath}";
        }

        return string.Empty;
    }

    private void LoadBuilderFromParameters(string engine, IReadOnlyDictionary<string, string> parameters)
    {
        string Get(string key, string fallback = "")
            => parameters.TryGetValue(key, out var value) ? value : fallback;

        BuilderHost = Get("host", BuilderHost);
        BuilderPort = Get("port", BuilderPort);
        BuilderDatabase = Get("database", BuilderDatabase);
        BuilderUsername = Get("username", BuilderUsername);
        BuilderPassword = Get("password", BuilderPassword);
        BuilderUseSsl = Get("ssl", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        BuilderRedisDb = Get("redisDb", BuilderRedisDb);
        BuilderElasticUrl = Get("elasticUrl", BuilderElasticUrl);
        BuilderElasticAuthMode = Get("elasticAuthMode", BuilderElasticAuthMode);
        BuilderElasticApiKey = Get("elasticApiKey", BuilderElasticApiKey);
        BuilderSshHost = Get("sshHost", BuilderSshHost);
        BuilderSshPort = Get("sshPort", BuilderSshPort);
        BuilderSshUsername = Get("sshUsername", BuilderSshUsername);
        BuilderSshAuthType = Get("sshAuthType", BuilderSshAuthType);
        BuilderSshPassword = Get("sshPassword", BuilderSshPassword);
        BuilderSshPrivateKeyPath = Get("sshPrivateKeyPath", BuilderSshPrivateKeyPath);
        BuilderSshPassphrase = Get("sshPassphrase", BuilderSshPassphrase);
        BuilderSshStartPath = Get("sshPath", BuilderSshStartPath);
    }

    private void ApplyEngineDefaults(string engine)
    {
        if (engine.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            BuilderPort = "5432";
            BuilderDatabase = "postgres";
            BuilderUsername = "postgres";
        }
        else if (engine.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            BuilderPort = "6379";
            BuilderRedisDb = "0";
        }
        else if (engine.Equals("ElasticSearch", StringComparison.OrdinalIgnoreCase))
        {
            BuilderElasticUrl = "http://127.0.0.1:9200";
            BuilderElasticAuthMode = "none";
        }
        else if (engine.Equals("SSH", StringComparison.OrdinalIgnoreCase))
        {
            BuilderSshPort = "22";
            BuilderSshAuthType = "password";
            BuilderSshStartPath = "/";
        }
        else if (engine.Equals("MongoDB", StringComparison.OrdinalIgnoreCase))
        {
            BuilderPort = "27017";
        }
        else
        {
            BuilderPort = "3306";
        }
    }

    private void RaiseBuilderVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsSshBuilderVisible));
        OnPropertyChanged(nameof(IsElasticBuilderVisible));
        OnPropertyChanged(nameof(IsRedisBuilderVisible));
        OnPropertyChanged(nameof(IsGenericBuilderVisible));
    }

    partial void OnBuilderHostChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderPortChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderDatabaseChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderUsernameChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderPasswordChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderUseSslChanged(bool value) => UpdateBuilderPreview();
    partial void OnBuilderRedisDbChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderElasticUrlChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderElasticAuthModeChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderElasticApiKeyChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderSshHostChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderSshPortChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderSshUsernameChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderSshAuthTypeChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderSshPasswordChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderSshPrivateKeyPathChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderSshPassphraseChanged(string value) => UpdateBuilderPreview();
    partial void OnBuilderSshStartPathChanged(string value) => UpdateBuilderPreview();

    private void UpdateBuilderPreview()
    {
        BuilderPreviewConnectionString = BuildConnectionString(SelectedEngine, BuildParameterMap());
    }

    private static async Task<bool> TestSshConnectionAsync(string connectionName, string connectionString, CancellationToken cancellationToken)
    {
        var settings = SshConnectionSettings.Parse(connectionName, connectionString);
        var connectionInfo = settings.BuildConnectionInfo();
        using var client = new SshClient(connectionInfo);

        await Task.Run(() => client.Connect(), cancellationToken);
        var connected = client.IsConnected;

        if (connected)
            client.Disconnect();

        return connected;
    }
}
