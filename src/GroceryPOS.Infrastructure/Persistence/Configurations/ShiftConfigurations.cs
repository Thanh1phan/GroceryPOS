using GroceryPOS.Domain.Identity;
using GroceryPOS.Domain.Shifts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroceryPOS.Infrastructure.Persistence.Configurations;

internal sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.ToTable("Shifts");
        builder.HasDocumentCode();
        builder.References<Shift, User>(s => s.UserId);
        builder.References<Shift, User>(s => s.ClosedBy);

        // Totals frozen at closing live in the shift row (Totals_SalesCount, ...); null while the shift is open.
        builder.OwnsOne(s => s.Totals, t =>
        {
            t.Ignore(x => x.NetRevenue);
            t.Ignore(x => x.NetCash);
        });
        builder.Navigation(s => s.Totals).IsRequired(false);

        builder.HasMany(s => s.CashMovements).WithOne().HasForeignKey(m => m.ShiftId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.CashMovements).HasField("_cashMovements").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasIndex(s => new { s.UserId, s.Status });
        builder.HasIndex(s => s.OpenedAt);
        builder.HasAuditColumns();
    }
}

internal sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> builder)
    {
        builder.ToTable("CashMovements");
        builder.Property(m => m.Reason).HasMaxLength(200).IsRequired();
        builder.References<CashMovement, User>(m => m.UserId);
    }
}
