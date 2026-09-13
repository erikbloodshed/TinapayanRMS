using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class RawMaterialPriceHistoryConfiguration : IEntityTypeConfiguration<RawMaterialPriceHistory>
{
    public void Configure(EntityTypeBuilder<RawMaterialPriceHistory> builder)
    {
        builder.HasOne(h => h.RawMaterial)
            .WithMany()
            .HasForeignKey(h => h.RawMaterialId)
            .OnDelete(DeleteBehavior.Cascade);

        // A unit something was priced in can't be deleted while that price entry exists. Not
        // Cascade: SQL Server rejects a second cascade path from RawMaterials (through
        // RawMaterialUnits) to this table.
        builder.HasOne(h => h.Unit)
            .WithMany()
            .HasForeignKey(h => h.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => new { h.RawMaterialId, h.Date });
    }
}
