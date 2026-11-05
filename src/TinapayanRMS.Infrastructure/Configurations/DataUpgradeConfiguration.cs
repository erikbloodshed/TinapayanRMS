using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Infrastructure.Upgrades;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class DataUpgradeConfiguration : IEntityTypeConfiguration<DataUpgrade>
{
    public void Configure(EntityTypeBuilder<DataUpgrade> builder)
    {
        builder.ToTable("data_upgrades");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasMaxLength(100);
    }
}
