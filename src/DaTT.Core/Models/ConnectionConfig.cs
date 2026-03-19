namespace DaTT.Core.Models;

public sealed class ConnectionConfig
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Engine { get; set; }
    public required string ConnectionString { get; set; }
    public Dictionary<string, string>? Parameters { get; set; }
    public string? ColorTag { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
