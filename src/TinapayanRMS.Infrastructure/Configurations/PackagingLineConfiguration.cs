using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class PackagingLineConfiguration : IEntityTypeConfiguration<PackagingLine>
{
    public void Configure(EntityTypeBuilder<PackagingLine> builder)
    {
        // Backstop for ProductRules.EnsureValidPackagingLines, on the figure that was typed (authored_field 0 is QtyPerUnit,
        // 1 UnitsPerPackagingUnit): reading a line divides by it. The other is worked out from it, and can round to 0 at this
        // column's 4 places, as 1 / 30,000 does, so it's left alone. Applies to the value as stored, after that rounding.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_PackagingLines_AuthoredQuantity",
            "(authored_field = 0 AND qty_per_unit > 0) OR (authored_field = 1 AND units_per_packaging_unit > 0)"));

        // Deleting the product takes its packaging lines with it.
        builder.HasOne(l => l.Product)
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a packaging material that's in use by a product is blocked instead.
        builder.HasOne(l => l.PackagingMaterial)
            .WithMany()
            .HasForeignKey(l => l.PackagingMaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        // A material's unit can't be deleted while a line is entered in it.
        builder.HasOne(l => l.Unit)
            .WithMany()
            .HasForeignKey(l => l.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // One line per material per product: a material used twice belongs on a single line.
        builder.HasIndex(l => new { l.ProductId, l.PackagingMaterialId }).IsUnique();
    }
}
