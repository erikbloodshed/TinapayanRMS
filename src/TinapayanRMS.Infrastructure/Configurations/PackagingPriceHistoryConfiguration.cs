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

        // A unit something was priced in can't be deleted while that price entry exists. Not
        // Cascade: SQL Server rejects a second cascade path from PackagingMaterials (through
        // PackagingMaterialUnits) to this table.
        builder.HasOne(h => h.PackagingMaterialUnit)
            .WithMany()
            .HasForeignKey(h => h.PackagingMaterialUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => new { h.PackagingMaterialId, h.Date });
    }
}
