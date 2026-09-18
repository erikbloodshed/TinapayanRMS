using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

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

        // Restrict, not Cascade: ProductPacks already cascades from Products, so cascading a pack's
        // lines from here as well would give SQL Server a second path Products -> PackagingLines,
        // which it refuses. ProductPackRepository deletes a removed pack's lines itself, just before
        // the pack, and deleting the product still takes every line with it through ProductId above.
        builder.HasOne(l => l.Pack)
            .WithMany(p => p.Lines)
            .HasForeignKey(l => l.PackId)
            .OnDelete(DeleteBehavior.Restrict);

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

        // A material can be on one per-piece line and on one line of each pack, so the pack is part of
        // what makes a line unique. The per-piece lines' NULL PackIds count as equal to each other, which
        // is what keeps the original "one line per material per product" rule for them.
        // HasFilter(null) is what makes that work: EF's SQL Server convention would otherwise add
        // "[PackId] IS NOT NULL" and drop every per-piece line out of the index, the same trap
        // SubAssemblyConfiguration documents.
        builder.HasIndex(l => new { l.ProductId, l.PackId, l.PackagingMaterialId }).IsUnique().HasFilter(null);
    }
}
