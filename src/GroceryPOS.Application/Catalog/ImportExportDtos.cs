namespace GroceryPOS.Application.Catalog;

public enum ImportRowOutcome
{
    Created,
    Updated,
    Failed,
}

public sealed record ImportRowResult(int RowNumber, string Code, ImportRowOutcome Outcome, string? Message);

public sealed record ImportSummary(int Created, int Updated, int Failed, IReadOnlyList<ImportRowResult> Rows);
