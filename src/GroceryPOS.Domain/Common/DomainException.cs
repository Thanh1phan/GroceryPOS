namespace GroceryPOS.Domain.Common;

/// <summary>
/// Thrown when a domain invariant is violated. <see cref="Code"/> is a stable machine-readable key;
/// the message is user-facing (Vietnamese).
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string message) : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
