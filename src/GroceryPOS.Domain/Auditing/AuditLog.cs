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
    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserStatusChanged = "user.status_changed";
    public const string UserUnlocked = "user.unlocked";
    public const string RoleCreated = "role.created";
    public const string RoleUpdated = "role.updated";
    public const string RoleDeleted = "role.deleted";
    public const string SaleCompleted = "sale.completed";
    public const string SaleVoided = "sale.voided";
    public const string SaleReturned = "sale.returned";
    public const string ReceiptReprinted = "sale.receipt_reprinted";
    public const string PriceChanged = "product.price_changed";
    public const string CategoryCreated = "category.created";
    public const string CategoryUpdated = "category.updated";
    public const string CategoryStatusChanged = "category.status_changed";
    public const string ProductCreated = "product.created";
    public const string ProductUpdated = "product.updated";
    public const string ProductStatusChanged = "product.status_changed";
    public const string ProductImported = "product.imported";
    public const string ProductExported = "product.exported";
    public const string SupplierCreated = "supplier.created";
    public const string SupplierUpdated = "supplier.updated";
    public const string SupplierStatusChanged = "supplier.status_changed";
    public const string CustomerCreated = "customer.created";
    public const string CustomerUpdated = "customer.updated";
    public const string CustomerStatusChanged = "customer.status_changed";
    public const string StockAdjusted = "inventory.adjusted";
    public const string StockCounted = "inventory.counted";
    public const string GoodsReceived = "purchase.received";
    public const string GoodsReceiptCancelled = "purchase.cancelled";
    public const string SupplierPaid = "purchase.supplier_paid";
    public const string SupplierPaymentCancelled = "purchase.supplier_payment_cancelled";
    public const string SupplierReturned = "purchase.supplier_returned";
    public const string ShiftOpened = "shift.opened";
    public const string ShiftClosed = "shift.closed";
    public const string CashMoved = "shift.cash_movement";
}
