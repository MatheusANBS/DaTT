using System.Text;
using DaTT.App.ViewModels;
using DaTT.Core.Models;
using DaTT.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DaTT.Tests;

public sealed class ConnectionConfigServiceTests : IDisposable
{
    private readonly string _tempRoot;

    public ConnectionConfigServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "datt-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    [Trait("Phase", "A")]
    public async Task PhaseA_SaveAndLoad_ShouldRoundTripConnections()
    {
        var filePath = Path.Combine(_tempRoot, "connections.dat");
        var sut = new ConnectionConfigService(filePath, NullLogger<ConnectionConfigService>.Instance);

        var cfg = new ConnectionConfig
        {
            Name = "Local PG",
            Engine = "PostgreSQL",
            ConnectionString = "postgresql://postgres:secret@localhost:5432/postgres"
        };

        await sut.SaveAsync(cfg);
        var all = await sut.GetAllAsync();

        Assert.Single(all);
        Assert.Equal(cfg.Name, all[0].Name);
        Assert.Equal(cfg.Engine, all[0].Engine);
        Assert.Equal(cfg.ConnectionString, all[0].ConnectionString);
    }

    [Fact]
    public async Task PersistedFile_ShouldNotContainPlaintextConnectionString()
    {
        var filePath = Path.Combine(_tempRoot, "connections.dat");
        var sut = new ConnectionConfigService(filePath, NullLogger<ConnectionConfigService>.Instance);

        var cfg = new ConnectionConfig
        {
            Name = "Local MySQL",
            Engine = "MySQL",
            ConnectionString = "mysql://root:secret@localhost:3306/test"
        };

        await sut.SaveAsync(cfg);
        var bytes = await File.ReadAllBytesAsync(filePath);
        var asText = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("mysql://root:secret@localhost:3306/test", asText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ConnectionString\"", asText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorruptedFile_ShouldReturnEmptyList()
    {
        var filePath = Path.Combine(_tempRoot, "connections.dat");
        await File.WriteAllBytesAsync(filePath, [0x00, 0x11, 0x22, 0x33, 0x44]);

        var sut = new ConnectionConfigService(filePath, NullLogger<ConnectionConfigService>.Instance);
        var all = await sut.GetAllAsync();

        Assert.Empty(all);
    }

    [Fact]
    public async Task Delete_ShouldRemoveConnection()
    {
        var filePath = Path.Combine(_tempRoot, "connections.dat");
        var sut = new ConnectionConfigService(filePath, NullLogger<ConnectionConfigService>.Instance);

        var cfg = new ConnectionConfig
        {
            Name = "Local Mongo",
            Engine = "MongoDB",
            ConnectionString = "mongodb://localhost:27017"
        };

        await sut.SaveAsync(cfg);
        await sut.DeleteAsync(cfg.Id);

        var all = await sut.GetAllAsync();
        Assert.Empty(all);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
        }
        catch
        {
            // Best-effort cleanup for temp test directory.
        }
    }
}

public static class QueryEditorPhaseBTests
{
    [Fact]
    [Trait("Phase", "B")]
    public static void PhaseB_ExtractCurrentStatement_ShouldReturnStatementAtCaret()
    {
        const string sql = "SELECT 1; SELECT * FROM customers WHERE name = 'ana'; UPDATE customers SET active = true;";
        var caret = sql.IndexOf("customers WHERE", StringComparison.Ordinal);

        var statement = QueryEditorTabViewModel.ExtractCurrentStatement(sql, caret);

        Assert.NotNull(statement);
        Assert.Contains("SELECT * FROM customers", statement, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE customers", statement, StringComparison.OrdinalIgnoreCase);
    }
}

public static class SchemaDiffServicePhaseCTests
{
    [Fact]
    [Trait("Phase", "C")]
    public static void PhaseC_BuildPlan_ShouldGenerateAddColumnAndIndexStatements()
    {
        var sourceColumns = new ColumnMeta[]
        {
            new("id", "integer", IsNullable: false, Key: "PRI", OrdinalPosition: 1),
            new("email", "varchar(120)", IsNullable: false, OrdinalPosition: 2)
        };
        var sourceIndexes = new IndexMeta[]
        {
            new("idx_users_email", ["email"], IsUnique: true, IsPrimaryKey: false)
        };

        var destinationColumns = new ColumnMeta[]
        {
            new("id", "integer", IsNullable: false, Key: "PRI", OrdinalPosition: 1)
        };
        var destinationIndexes = Array.Empty<IndexMeta>();

        var plan = SchemaDiffService.BuildPlan(
            "users", sourceColumns, sourceIndexes,
            destinationColumns, destinationIndexes,
            "PostgreSQL", includeDrops: false);

        Assert.True(plan.HasChanges);
        Assert.Contains(plan.Items, i => i.Kind == "AddColumn" && i.ObjectName == "email");
        Assert.Contains(plan.Items, i => i.Kind == "CreateIndex" && i.ObjectName == "idx_users_email");
    }
}
