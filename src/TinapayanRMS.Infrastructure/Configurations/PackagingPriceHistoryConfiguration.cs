using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class PackagingPriceHistoryConfiguration : IEntityTypeConfiguration<PackagingPriceHistory>
{
    public void Configure(EntityTypeBuilder<PackagingPriceHistory> builder)
    {
        builder.HasOne(h => h.PackagingMaterial)
            .WithMany()
            .HasForeignKey(h => h.PackagingMaterialId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(h => new { h.PackagingMaterialId, h.Date });
    }
}
