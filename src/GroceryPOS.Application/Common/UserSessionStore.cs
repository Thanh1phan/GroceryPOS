using GroceryPOS.Application.Abstractions;

namespace GroceryPOS.Application.Common;

/// <summary>In-memory session store for the single-user desktop app.</summary>
public sealed class UserSessionStore : IUserSessionStore
{
    private UserSession? _session;

    public event EventHandler? SessionChanged;

    public UserSession? Session => _session;

    public void SignIn(UserSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        if (_session is null)
        {
            return;
        }

        _session = null;
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}
