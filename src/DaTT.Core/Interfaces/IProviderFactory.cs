using DaTT.Core.Interfaces;

namespace DaTT.Core.Interfaces;

public interface IProviderFactory
{
    IDatabaseProvider CreateForConnectionString(string connectionString);
    IReadOnlyList<string> SupportedEngineNames { get; }
}
