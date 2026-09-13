using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class PackagingMaterialUnitConfiguration : IEntityTypeConfiguration<PackagingMaterialUnit>
{
    public void Configure(EntityTypeBuilder<PackagingMaterialUnit> builder)
    {
        // Deleting the material takes its own units with it.
        builder.HasOne(u => u.PackagingMaterial)
            .WithMany(m => m.Units)
            .HasForeignKey(u => u.PackagingMaterialId)
            .OnDelete(DeleteBehavior.Cascade);

        // A unit of measure in use as some material's alternative unit can't be deleted.
        builder.HasOne(u => u.UnitOfMeasure)
            .WithMany()
            .HasForeignKey(u => u.UnitOfMeasureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(u => u.Description).HasMaxLength(200);

        builder.HasIndex(u => new { u.PackagingMaterialId, u.UnitOfMeasureId }).IsUnique();
    }
}
