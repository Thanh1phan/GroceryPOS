namespace GroceryPOS.Application.Partners;

public sealed record SupplierDto(
    int Id,
    string Code,
    string Name,
    string? ContactPerson,
    string? Phone,
    string? Email,
    string? Address,
    string? TaxCode,
    decimal DebtBalance,
    bool IsActive);

/// <summary>Create/update a supplier. <see cref="Code"/> is only used on create; leave it empty for the next <c>NCC0001</c> code.</summary>
public sealed record SaveSupplierRequest(
    string? Code,
    string Name,
    string? ContactPerson = null,
    string? Phone = null,
    string? Email = null,
    string? Address = null,
    string? TaxCode = null);

public sealed record CustomerDto(
    int Id,
    string Code,
    string Name,
    string? Phone,
    string? Email,
    string? Address,
    int LoyaltyPoints,
    decimal TotalSpent,
    bool IsActive);

/// <summary>Create/update a customer; the code (<c>KH000001</c>) is assigned on create.</summary>
public sealed record SaveCustomerRequest(
    string Name,
    string? Phone = null,
    string? Email = null,
    string? Address = null);

public enum LoyaltyEntryKind
{
    /// <summary>Points earned and/or redeemed on a completed invoice.</summary>
    Purchase,

    /// <summary>A voided invoice: earned points taken back, redeemed points given back.</summary>
    Void,

    /// <summary>A customer return: proportional points taken back, redeemed points restored on a full return.</summary>
    Return,

    /// <summary>Points that no sales document explains (e.g. carried over from an old system).</summary>
    Opening,
}

/// <summary>
/// One line of a customer's loyalty history (lịch sử tích điểm). <see cref="PointsChange"/> is the real effect on the
/// balance (earned − redeemed, or the reversal; reversals stop at zero), <see cref="BalanceAfter"/> the balance after it.
/// </summary>
public sealed record LoyaltyEntryDto(
    DateTimeOffset Date,
    LoyaltyEntryKind Kind,
    string DocumentCode,
    decimal Amount,
    int PointsEarned,
    int PointsRedeemed,
    int PointsChange,
    int BalanceAfter,
    string Description);

public sealed record CustomerLoyaltyHistoryDto(
    CustomerDto Customer,
    int InvoiceCount,
    IReadOnlyList<LoyaltyEntryDto> Entries);
