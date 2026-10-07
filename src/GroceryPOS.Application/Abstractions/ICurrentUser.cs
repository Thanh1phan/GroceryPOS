namespace GroceryPOS.Application.Abstractions;

/// <summary>Snapshot of the signed-in user taken at login.</summary>
public sealed record UserSession(
    int UserId,
    string Username,
    string FullName,
    string RoleName,
    IReadOnlySet<string> Permissions,
    DateTimeOffset SignedInAt,
    bool MustChangePassword = false);

/// <summary>Read-only access to the current user, used by services and presenters for permission checks.</summary>
public interface ICurrentUser
{
    UserSession? Session { get; }

    bool IsAuthenticated => Session is not null;

    bool HasPermission(string permission) => Session?.Permissions.Contains(permission) ?? false;
}

/// <summary>Mutable session holder (singleton in a desktop app).</summary>
public interface IUserSessionStore : ICurrentUser
{
    event EventHandler? SessionChanged;

    void SignIn(UserSession session);

    void SignOut();
}
