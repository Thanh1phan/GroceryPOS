namespace GroceryPOS.Domain.Common;

/// <summary>Base type for all persisted entities with an integer identity.</summary>
public abstract class Entity
{
    public int Id { get; protected set; }

    public bool IsTransient => Id == 0;
}
