using GroceryPOS.Domain.Common;
using GroceryPOS.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroceryPOS.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    public const int CodeLength = 32;
    public const int NameLength = 200;
    public const int UserLength = 50;

    /// <summary>Audit columns shared by every <see cref="AuditableEntity"/> (filled by <see cref="AuditableEntityInterceptor"/>).</summary>
    public static void HasAuditColumns<T>(this EntityTypeBuilder<T> builder) where T : AuditableEntity
    {
        builder.Property(e => e.CreatedBy).HasMaxLength(UserLength);
        builder.Property(e => e.UpdatedBy).HasMaxLength(UserLength);
    }

    /// <summary>A document code such as <c>HD261016-0001</c>: required, short, unique.</summary>
    public static void HasDocumentCode<T>(this EntityTypeBuilder<T> builder, string property = "Code")
        where T : class
    {
        builder.Property<string>(property).HasMaxLength(CodeLength).IsRequired();
        builder.HasIndex(property).IsUnique();
    }

    /// <summary>Maps a <see cref="Discount"/> value object to two columns (<c>{prefix}Kind</c>, <c>{prefix}Value</c>).</summary>
    public static void HasDiscount<T>(this EntityTypeBuilder<T> builder, System.Linq.Expressions.Expression<Func<T, Discount>> discount, string prefix)
        where T : class =>
        builder.ComplexProperty(discount, d =>
        {
            d.IsRequired();
            d.Property(x => x.Kind).HasColumnName(prefix + "Kind");
            d.Property(x => x.Value).HasColumnName(prefix + "Value");
        });

    /// <summary>A required reference to another aggregate by id only; deleting the target is blocked.</summary>
    public static ReferenceCollectionBuilder<TPrincipal, T> References<T, TPrincipal>(
        this EntityTypeBuilder<T> builder, System.Linq.Expressions.Expression<Func<T, object?>> foreignKey)
        where T : class
        where TPrincipal : class =>
        builder.HasOne<TPrincipal>().WithMany().HasForeignKey(foreignKey).OnDelete(DeleteBehavior.Restrict);
}
