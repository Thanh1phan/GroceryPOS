namespace GroceryPOS.Domain.Identity;

/// <summary>How many consecutive failed logins lock an account, and for how long.</summary>
public sealed record LockoutPolicy(int MaxFailedAttempts, TimeSpan LockoutDuration)
{
    public static LockoutPolicy Default { get; } = new(5, TimeSpan.FromMinutes(15));
}
