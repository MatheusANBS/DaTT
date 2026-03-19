using DaTT.Core.Models;

namespace DaTT.Core.Interfaces;

public interface IConnectionConfigService
{
    Task<IReadOnlyList<ConnectionConfig>> GetAllAsync();
    Task<ConnectionConfig?> GetByIdAsync(Guid id);
    Task SaveAsync(ConnectionConfig config);
    Task DeleteAsync(Guid id);
}
