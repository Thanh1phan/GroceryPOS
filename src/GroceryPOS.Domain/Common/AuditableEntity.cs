namespace GroceryPOS.Domain.Common;

/// <summary>Entity that tracks who created/modified it and when. Populated by the persistence layer.</summary>
public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
