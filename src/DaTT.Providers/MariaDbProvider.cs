using Microsoft.Extensions.Logging;

namespace DaTT.Providers;

public sealed class MariaDbProvider : MySqlProvider
{
    public MariaDbProvider(ILogger<MariaDbProvider> logger) : base(logger, isMariaDb: true) { }
}
