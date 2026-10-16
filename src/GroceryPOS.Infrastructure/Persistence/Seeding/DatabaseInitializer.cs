using GroceryPOS.Application.Abstractions;
using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GroceryPOS.Infrastructure.Persistence.Seeding;

/// <summary>Bound from the <c>Seed</c> section of appsettings.json.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public string AdminUsername { get; set; } = "admin";

    /// <summary>Initial password; the admin must change it on first login.</summary>
    public string AdminPassword { get; set; } = "admin123";

    public string AdminFullName { get; set; } = "Quản trị viên";

    public string DefaultCategoryName { get; set; } = "Hàng tạp hóa";
}

public interface IDatabaseInitializer
{
    /// <summary>Applies pending migrations, then seeds what a fresh store needs. Safe to run on every start.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Idempotent start-up seeding: missing system roles (Admin, Quản lý, Thu ngân, Thủ kho) with their default
/// permissions, permissions added in later versions granted to Admin, a default admin account when the store has
/// no users, and one default category when there is none. Existing users, roles and edited permissions are kept.
/// </summary>
internal sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly GroceryDbContext _context;
    private readonly IPasswordHasher _hasher;
    private readonly SeedOptions _options;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        GroceryDbContext context,
        IPasswordHasher hasher,
        SeedOptions options,
        ILogger<DatabaseInitializer>? logger = null)
    {
        _context = context;
        _hasher = hasher;
        _options = options;
        _logger = logger ?? NullLogger<DatabaseInitializer>.Instance;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsRelational())
        {
            await _context.Database.MigrateAsync(cancellationToken);
        }

        var roles = await SeedRolesAsync(cancellationToken);
        await SeedAdminAsync(roles[SystemRoles.Admin], cancellationToken);
        await SeedCategoryAsync(cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dictionary<string, Role>> SeedRolesAsync(CancellationToken cancellationToken)
    {
        var existing = await _context.Roles.ToDictionaryAsync(r => r.Name, StringComparer.Ordinal, cancellationToken);
        foreach (var role in SystemRoles.CreateDefaults())
        {
            if (!existing.ContainsKey(role.Name))
            {
                _context.Roles.Add(role);
                existing[role.Name] = role;
                _logger.LogInformation("Seeded system role {Role}", role.Name);
            }
        }

        // Admin always holds every permission, including ones introduced after the database was created.
        var admin = existing[SystemRoles.Admin];
        foreach (var permission in Permissions.All.Where(p => !admin.HasPermission(p)))
        {
            admin.Grant(permission);
            _logger.LogInformation("Granted new permission {Permission} to Admin", permission);
        }

        return existing;
    }

    private async Task SeedAdminAsync(Role adminRole, CancellationToken cancellationToken)
    {
        if (await _context.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var admin = new User(_options.AdminUsername, _options.AdminFullName, _hasher.Hash(_options.AdminPassword), adminRole);
        admin.SetPasswordHash(admin.PasswordHash, mustChange: true);
        _context.Users.Add(admin);
        _logger.LogWarning("Seeded default administrator {Username}; the password must be changed on first login", admin.Username);
    }

    private async Task SeedCategoryAsync(CancellationToken cancellationToken)
    {
        if (!await _context.Categories.AnyAsync(cancellationToken))
        {
            _context.Categories.Add(new Category(_options.DefaultCategoryName));
        }
    }
}
