using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static GroceryPOS.Infrastructure.Persistence.Configurations.ConfigurationExtensions;

namespace GroceryPOS.Infrastructure.Persistence.Configurations;

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("Sales");
        builder.HasDocumentCode();
        builder.Property(s => s.VoidReason).HasMaxLength(500);
        builder.HasDiscount(s => s.InvoiceDiscount, "InvoiceDiscount");
        builder.References<Sale, User>(s => s.CashierId);
        builder.References<Sale, User>(s => s.VoidedBy);
        builder.HasOne(s => s.Customer).WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Lines).WithOne().HasForeignKey(l => l.SaleId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.HasMany(s => s.Payments).WithOne().HasForeignKey(p => p.SaleId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Payments).HasField("_payments").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasIndex(s => new { s.Status, s.CompletedAt });
        builder.HasIndex(s => s.CashierId);
        builder.HasAuditColumns();
    }
}

internal sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> builder)
    {
        builder.ToTable("SaleLines");
        builder.Property(l => l.ProductCode).HasMaxLength(CodeLength).IsRequired();
        builder.Property(l => l.ProductName).HasMaxLength(NameLength).IsRequired();
        builder.Property(l => l.Barcode).HasMaxLength(Barcode.MaxLength);
        builder.Property(l => l.Unit).HasMaxLength(20).IsRequired();
        builder.Property(l => l.VatRate).HasPrecision(5, 2);
        builder.HasDiscount(l => l.Discount, "Discount");
        builder.References<SaleLine, Product>(l => l.ProductId);
        builder.HasIndex(l => l.ProductId);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("SalePayments");
        builder.Property(p => p.Reference).HasMaxLength(100);
    }
}

internal sealed class SaleReturnConfiguration : IEntityTypeConfiguration<SaleReturn>
{
    public void Configure(EntityTypeBuilder<SaleReturn> builder)
    {
        builder.ToTable("SaleReturns");
        builder.HasDocumentCode();
        builder.Property(r => r.SaleCode).HasMaxLength(CodeLength).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.References<SaleReturn, Sale>(r => r.SaleId);
        builder.References<SaleReturn, Customer>(r => r.CustomerId);
        builder.References<SaleReturn, User>(r => r.UserId);

        builder.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.SaleReturnId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasIndex(r => r.ReturnedAt);
        builder.HasAuditColumns();
    }
}

internal sealed class SaleReturnLineConfiguration : IEntityTypeConfiguration<SaleReturnLine>
{
    public void Configure(EntityTypeBuilder<SaleReturnLine> builder)
    {
        builder.ToTable("SaleReturnLines");
        builder.Property(l => l.ProductCode).HasMaxLength(CodeLength).IsRequired();
        builder.Property(l => l.ProductName).HasMaxLength(NameLength).IsRequired();
        builder.Property(l => l.Unit).HasMaxLength(20).IsRequired();
        builder.Property(l => l.VatRate).HasPrecision(5, 2);
        builder.References<SaleReturnLine, SaleLine>(l => l.SaleLineId);
        builder.References<SaleReturnLine, Product>(l => l.ProductId);
    }
}
