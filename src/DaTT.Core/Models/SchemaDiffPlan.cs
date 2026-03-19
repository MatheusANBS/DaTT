namespace DaTT.Core.Models;

public sealed record SchemaDiffPlan(
    IReadOnlyList<SchemaDiffItem> Items
)
{
    public bool HasChanges => Items.Count > 0;
}
