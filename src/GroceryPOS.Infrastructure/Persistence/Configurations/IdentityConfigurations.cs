using GroceryPOS.Domain.Auditing;
using GroceryPOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroceryPOS.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.Property(u => u.Username).HasMaxLength(50).IsRequired();
        builder.HasIndex(u => u.Username).IsUnique();
        builder.Property(u => u.FullName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        builder.HasOne(u => u.Role).WithMany().HasForeignKey(u => u.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasAuditColumns();
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.Property(r => r.Name).HasMaxLength(50).IsRequired();
        builder.HasIndex(r => r.Name).IsUnique();
        builder.Ignore(r => r.PermissionKeys);
        builder.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Permissions).HasField("_permissions").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.HasAuditColumns();
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(p => new { p.RoleId, p.Permission });
        builder.Property(p => p.Permission).HasMaxLength(64);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        // No foreign key to Users on purpose: the trail must survive user cleanup and records failed logins of unknown names.
        builder.ToTable("AuditLogs");
        builder.Property(a => a.Action).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Username).HasMaxLength(50);
        builder.Property(a => a.EntityName).HasMaxLength(64);
        builder.Property(a => a.EntityId).HasMaxLength(64);
        builder.Property(a => a.Details).HasMaxLength(4000);
        builder.HasIndex(a => a.Timestamp);
        builder.HasIndex(a => new { a.Action, a.Timestamp });
    }
}
