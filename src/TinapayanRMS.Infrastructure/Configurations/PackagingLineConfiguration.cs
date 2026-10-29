using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class PackagingLineConfiguration : IEntityTypeConfiguration<PackagingLine>
{
    public void Configure(EntityTypeBuilder<PackagingLine> builder)
    {
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
