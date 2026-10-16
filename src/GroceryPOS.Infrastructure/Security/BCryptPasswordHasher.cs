using GroceryPOS.Application.Abstractions;

namespace GroceryPOS.Infrastructure.Security;

/// <summary>BCrypt (salted, adaptive) password hashing. Work factor 11 ≈ 0.1–0.2 s per login on a store PC.</summary>
internal sealed class BCryptPasswordHasher : IPasswordHasher
{
    public const int DefaultWorkFactor = 11;

    private readonly int _workFactor;

    public BCryptPasswordHasher(int workFactor = DefaultWorkFactor)
    {
        _workFactor = workFactor;
    }

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return BCrypt.Net.BCrypt.EnhancedHashPassword(password, _workFactor);
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.EnhancedVerify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // A malformed hash in the database must not crash the login screen.
            return false;
        }
    }
}
