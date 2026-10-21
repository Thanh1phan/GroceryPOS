using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static GroceryPOS.Infrastructure.Persistence.Configurations.ConfigurationExtensions;

namespace GroceryPOS.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.Name);
        builder.HasAuditColumns();
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.Property(p => p.Code).HasMaxLength(CodeLength).IsRequired();
        builder.HasIndex(p => p.Code).IsUnique();
        builder.Property(p => p.Barcode).HasMaxLength(Barcode.MaxLength);
        builder.HasIndex(p => p.Barcode).IsUnique();
        builder.Property(p => p.Name).HasMaxLength(NameLength).IsRequired();
        builder.HasIndex(p => p.Name);
        builder.Property(p => p.Unit).HasMaxLength(20).IsRequired();
        builder.Property(p => p.VatRate).HasPrecision(5, 2);
        builder.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(p => p.IsLowStock);
        builder.Ignore(p => p.UnitMargin);
        builder.HasAuditColumns();
    }
}

internal sealed class PriceHistoryConfiguration : IEntityTypeConfiguration<PriceHistory>
{
    public void Configure(EntityTypeBuilder<PriceHistory> builder)
    {
        builder.ToTable("PriceHistories");
        builder.HasOne(h => h.Product).WithMany().HasForeignKey(h => h.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.References<PriceHistory, User>(h => h.ChangedByUserId);
        builder.Property(h => h.Note).HasMaxLength(500);
        builder.HasIndex(h => new { h.ProductId, h.ChangedAt });
    }
}

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.Property(c => c.Code).HasMaxLength(CodeLength).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();
        builder.Property(c => c.Name).HasMaxLength(NameLength).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(15);
        builder.HasIndex(c => c.Phone);
        builder.Property(c => c.Email).HasMaxLength(100);
        builder.HasAuditColumns();
    }
}

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");
        builder.Property(s => s.Code).HasMaxLength(CodeLength).IsRequired();
        builder.HasIndex(s => s.Code).IsUnique();
        builder.Property(s => s.Name).HasMaxLength(NameLength).IsRequired();
        builder.Property(s => s.ContactPerson).HasMaxLength(100);
        builder.Property(s => s.Phone).HasMaxLength(15);
        builder.Property(s => s.Email).HasMaxLength(100);
        builder.Property(s => s.TaxCode).HasMaxLength(20);
        builder.HasAuditColumns();
    }
}
