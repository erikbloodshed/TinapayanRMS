using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class SubAssemblyPackagingLineConfiguration : IEntityTypeConfiguration<SubAssemblyPackagingLine>
{
    public void Configure(EntityTypeBuilder<SubAssemblyPackagingLine> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("CK_SubAssemblyPackagingLines_QtyPerSet", "qty_per_set > 0"));

        // Deleting the set takes its own lines with it.
        builder.HasOne(l => l.SubAssembly)
            .WithMany()
            .HasForeignKey(l => l.SubAssemblyId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a packaging material that's in use by a set is blocked instead.
        builder.HasOne(l => l.PackagingMaterial)
            .WithMany()
            .HasForeignKey(l => l.PackagingMaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        // The unit the quantity is entered in, when it isn't the material's base unit. A unit a set line uses
        // can't be deleted (RawMaterialUnitRepository.DeleteAsync says which sets are on it).
        builder.HasOne(l => l.Unit)
            .WithMany()
            .HasForeignKey(l => l.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.SubAssemblyId, l.PackagingMaterialId }).IsUnique();
    }
}
