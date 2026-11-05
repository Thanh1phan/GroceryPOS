using GroceryPOS.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GroceryPOS.Infrastructure.Persistence.Configurations;

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSettings");
        builder.Property(s => s.Key).HasMaxLength(AppSetting.KeyMaxLength).IsRequired();
        builder.HasIndex(s => s.Key).IsUnique();
        builder.Property(s => s.Value).HasMaxLength(AppSetting.ValueMaxLength).IsRequired();
        builder.HasAuditColumns();
    }
}
