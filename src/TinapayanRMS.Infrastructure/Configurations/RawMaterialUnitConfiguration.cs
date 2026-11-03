using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class RawMaterialUnitConfiguration : IEntityTypeConfiguration<RawMaterialUnit>
{
    public void Configure(EntityTypeBuilder<RawMaterialUnit> builder)
    {
        // Backstop for UnitConversionRules.EnsureValidMultiplier: a price in this unit is divided by it on every read.
        builder.ToTable(t => t.HasCheckConstraint("CK_RawMaterialUnits_BaseUnitsPerUnit", "base_units_per_unit > 0"));

        // Deleting the material takes its own units with it.
        builder.HasOne(u => u.RawMaterial)
            .WithMany(m => m.Units)
            .HasForeignKey(u => u.RawMaterialId)
            .OnDelete(DeleteBehavior.Cascade);

        // A unit of measure in use as some material's alternative unit can't be deleted.
        builder.HasOne(u => u.UnitOfMeasure)
            .WithMany()
            .HasForeignKey(u => u.UnitOfMeasureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(u => u.Description).HasMaxLength(200);

        builder.HasIndex(u => new { u.RawMaterialId, u.UnitOfMeasureId }).IsUnique();
    }
}
