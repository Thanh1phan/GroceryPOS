using GroceryPOS.Domain.Common;

namespace GroceryPOS.Domain.Auditing;

/// <summary>Immutable record of an important business action (login, sale, price change, stock adjustment...).</summary>
public sealed class AuditLog : Entity
{
    private AuditLog()
    {
    }

    public AuditLog(DateTimeOffset timestamp, string action, int? userId, string? username,
        string? entityName = null, string? entityId = null, string? details = null)
    {
        Timestamp = timestamp;
        Action = Guard.NotEmpty(action, "audit.action", "Thiếu hành động", 64);
        UserId = userId;
        Username = username;
        EntityName = entityName;
        EntityId = entityId;
        Details = details;
    }

    public DateTimeOffset Timestamp { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public int? UserId { get; private set; }
    public string? Username { get; private set; }
    public string? EntityName { get; private set; }
    public string? EntityId { get; private set; }
    public string? Details { get; private set; }
}

/// <summary>Well-known audit action keys.</summary>
public static class AuditActions
{
    public const string LoginSucceeded = "auth.login";
    public const string LoginFailed = "auth.login_failed";
    public const string AccountLocked = "auth.locked";
    public const string Logout = "auth.logout";
    public const string PasswordChanged = "auth.password_changed";
    public const string PasswordReset = "auth.password_reset";
    public const string SaleCompleted = "sale.completed";
    public const string SaleVoided = "sale.voided";
    public const string PriceChanged = "product.price_changed";
    public const string StockAdjusted = "inventory.adjusted";
}
