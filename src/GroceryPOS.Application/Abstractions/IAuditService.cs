namespace GroceryPOS.Application.Abstractions;

/// <summary>
/// Records important business actions. Entries are added to the current unit of work,
/// so they are saved atomically with the change they describe.
/// </summary>
public interface IAuditService
{
    void Record(string action, string? entityName = null, string? entityId = null, string? details = null);

    /// <summary>Records an action on behalf of a specific (possibly not signed-in) user, e.g. failed logins.</summary>
    void RecordFor(int? userId, string? username, string action, string? details = null);
}
