using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class RawMaterialPriceHistoryConfiguration : IEntityTypeConfiguration<RawMaterialPriceHistory>
{
    public void Configure(EntityTypeBuilder<RawMaterialPriceHistory> builder)
    {
        builder.Property(h => h.Brand).HasMaxLength(RawMaterialBrandPriceValidator.MaxBrandLength);

        builder.HasOne(h => h.RawMaterial)
            .WithMany()
            .HasForeignKey(h => h.RawMaterialId)
            .OnDelete(DeleteBehavior.Cascade);

        // A unit something was priced in can't be deleted while that price entry exists. Not
        // Cascade: that would be a second cascade path from RawMaterials (through RawMaterialUnits)
        // to this table, which SQL Server (the database until 2026-10-21) rejected.
        builder.HasOne(h => h.Unit)
            .WithMany()
            .HasForeignKey(h => h.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => new { h.RawMaterialId, h.Date });
    }
}
