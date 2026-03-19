using DaTT.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DaTT.Providers;

public sealed class ProviderFactory : IProviderFactory
{
    private readonly IServiceProvider _services;

    private static readonly IReadOnlyDictionary<string, string> SchemeToEngine =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mysql"]           = "MySQL",
            ["mariadb"]         = "MariaDB",
            ["postgresql"]      = "PostgreSQL",
            ["postgres"]        = "PostgreSQL",
            ["jdbc:oracle:thin"]= "Oracle",
            ["mongodb"]         = "MongoDB",
            ["mongodb+srv"]     = "MongoDB",
            ["jdbc:hive2"]      = "Hive",
            ["redis"]           = "Redis",
            ["elasticsearch"]   = "ElasticSearch",
            ["es"]              = "ElasticSearch",
            ["http"]            = "ElasticSearch",
            ["https"]           = "ElasticSearch",
        };

    public ProviderFactory(IServiceProvider services)
    {
        _services = services;
    }

    public IReadOnlyList<string> SupportedEngineNames
        => SchemeToEngine.Values.Distinct().ToList();

    public IDatabaseProvider CreateForConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var engineName = ResolveEngineName(connectionString)
            ?? throw new NotSupportedException(
                $"No provider found for connection string: '{TruncateForLog(connectionString)}'");

        return _services.GetRequiredKeyedService<IDatabaseProvider>(engineName);
    }

    private static string? ResolveEngineName(string connectionString)
    {
        foreach (var (scheme, engine) in SchemeToEngine)
        {
            if (connectionString.StartsWith(scheme + "://", StringComparison.OrdinalIgnoreCase) ||
                connectionString.StartsWith(scheme + ":", StringComparison.OrdinalIgnoreCase))
                return engine;
        }
        return null;
    }

    private static string TruncateForLog(string s) =>
        s.Length > 40 ? s[..40] + "..." : s;
}
