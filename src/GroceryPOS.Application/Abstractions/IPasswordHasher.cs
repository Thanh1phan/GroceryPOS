namespace GroceryPOS.Application.Abstractions;

/// <summary>Password hashing abstraction (BCrypt in Infrastructure).</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}
