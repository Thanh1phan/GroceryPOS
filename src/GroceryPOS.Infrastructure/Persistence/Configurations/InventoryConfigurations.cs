using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static GroceryPOS.Infrastructure.Persistence.Configurations.ConfigurationExtensions;

namespace GroceryPOS.Infrastructure.Persistence.Configurations;

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");
        builder.HasOne(m => m.Product).WithMany().HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.References<StockMovement, User>(m => m.UserId);
        builder.Property(m => m.ReferenceCode).HasMaxLength(CodeLength);
        builder.HasIndex(m => new { m.ProductId, m.Timestamp });
        builder.HasIndex(m => m.ReferenceCode);
    }
}
