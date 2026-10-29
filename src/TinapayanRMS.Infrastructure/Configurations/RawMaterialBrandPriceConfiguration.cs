using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class RawMaterialBrandPriceConfiguration : IEntityTypeConfiguration<RawMaterialBrandPrice>
{
    public void Configure(EntityTypeBuilder<RawMaterialBrandPrice> builder)
    {
        builder.Property(p => p.Brand).HasMaxLength(RawMaterialBrandPriceValidator.MaxBrandLength);

        builder.HasOne(p => p.RawMaterial)
            .WithMany()
            .HasForeignKey(p => p.RawMaterialId)
            .OnDelete(DeleteBehavior.Cascade);

        // A unit a brand is priced in can't be deleted while that price exists. Not Cascade: SQL
        // Server rejects a second cascade path from RawMaterials (through RawMaterialUnits) to this table.
        builder.HasOne(p => p.Unit)
            .WithMany()
            .HasForeignKey(p => p.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.RawMaterialId);

        builder.ToTable(t => t.HasCheckConstraint("CK_RawMaterialBrandPrices_Price", "price > 0"));
    }
}
