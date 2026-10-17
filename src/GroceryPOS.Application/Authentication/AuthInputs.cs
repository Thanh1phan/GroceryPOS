namespace GroceryPOS.Application.Authentication;

/// <summary>What the login form submits.</summary>
public sealed record LoginInput(string? Username, string? Password);

/// <summary>What the change-password dialog submits (the confirmation never reaches <see cref="IAuthService"/>).</summary>
public sealed record ChangePasswordInput(string? CurrentPassword, string? NewPassword, string? ConfirmPassword);
