namespace GroceryPOS.Application.Catalog;

public sealed record PriceHistoryEntry(
    int Id,
    DateTimeOffset ChangedAt,
    decimal OldCostPrice,
    decimal NewCostPrice,
    decimal OldSellPrice,
    decimal NewSellPrice,
    string? ChangedByUsername,
    string SourceName,
    string? Note);
