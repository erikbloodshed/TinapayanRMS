using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure.Configurations;

public class PackagingLineConfiguration : IEntityTypeConfiguration<PackagingLine>
{
    public void Configure(EntityTypeBuilder<PackagingLine> builder)
    {
        // Deleting the product takes its own packaging lines with it.
        builder.HasOne(l => l.Product)
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a packaging material that's in use by a product is blocked instead.
        builder.HasOne(l => l.PackagingMaterial)
            .WithMany()
            .HasForeignKey(l => l.PackagingMaterialId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(l => l.EntryUnit)
            .WithMany()
            .HasForeignKey(l => l.EntryUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.ProductId, l.PackagingMaterialId }).IsUnique();
    }
}
