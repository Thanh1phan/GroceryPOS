using GroceryPOS.Domain.Catalog;
using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Partners;
using GroceryPOS.Domain.Purchasing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static GroceryPOS.Infrastructure.Persistence.Configurations.ConfigurationExtensions;

namespace GroceryPOS.Infrastructure.Persistence.Configurations;

internal sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> builder)
    {
        builder.ToTable("GoodsReceipts");
        builder.HasDocumentCode();
        builder.Property(r => r.SupplierInvoiceNo).HasMaxLength(50);
        builder.References<GoodsReceipt, Supplier>(r => r.SupplierId);
        builder.References<GoodsReceipt, User>(r => r.CreatedById);
        builder.References<GoodsReceipt, User>(r => r.CompletedById);
        builder.References<GoodsReceipt, User>(r => r.CancelledById);

        builder.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.GoodsReceiptId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasIndex(r => new { r.Status, r.ReceiptDate });
        builder.HasIndex(r => r.SupplierId);
        builder.HasAuditColumns();
    }
}

internal sealed class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> builder)
    {
        builder.ToTable("GoodsReceiptLines");
        builder.Property(l => l.ProductCode).HasMaxLength(CodeLength).IsRequired();
        builder.Property(l => l.ProductName).HasMaxLength(NameLength).IsRequired();
        builder.Property(l => l.Unit).HasMaxLength(20).IsRequired();
        builder.References<GoodsReceiptLine, Product>(l => l.ProductId);
    }
}

internal sealed class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> builder)
    {
        builder.ToTable("SupplierPayments");
        builder.HasDocumentCode();
        builder.Property(p => p.Reference).HasMaxLength(50);
        builder.References<SupplierPayment, Supplier>(p => p.SupplierId);
        builder.References<SupplierPayment, User>(p => p.UserId);
        builder.References<SupplierPayment, User>(p => p.CancelledById);
        builder.HasIndex(p => new { p.SupplierId, p.PaidAt });
    }
}

internal sealed class SupplierReturnConfiguration : IEntityTypeConfiguration<SupplierReturn>
{
    public void Configure(EntityTypeBuilder<SupplierReturn> builder)
    {
        builder.ToTable("SupplierReturns");
        builder.HasDocumentCode();
        builder.Property(r => r.GoodsReceiptCode).HasMaxLength(CodeLength);
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.References<SupplierReturn, Supplier>(r => r.SupplierId);
        builder.References<SupplierReturn, GoodsReceipt>(r => r.GoodsReceiptId);
        builder.References<SupplierReturn, User>(r => r.UserId);

        builder.HasMany(r => r.Lines).WithOne().HasForeignKey(l => l.SupplierReturnId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasIndex(r => new { r.SupplierId, r.ReturnDate });
        builder.HasAuditColumns();
    }
}

internal sealed class SupplierReturnLineConfiguration : IEntityTypeConfiguration<SupplierReturnLine>
{
    public void Configure(EntityTypeBuilder<SupplierReturnLine> builder)
    {
        builder.ToTable("SupplierReturnLines");
        builder.Property(l => l.ProductCode).HasMaxLength(CodeLength).IsRequired();
        builder.Property(l => l.ProductName).HasMaxLength(NameLength).IsRequired();
        builder.Property(l => l.Unit).HasMaxLength(20).IsRequired();
        builder.References<SupplierReturnLine, Product>(l => l.ProductId);
    }
}
