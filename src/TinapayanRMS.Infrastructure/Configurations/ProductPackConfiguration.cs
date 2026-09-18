using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class ProductPackConfiguration : IEntityTypeConfiguration<ProductPack>
{
    public void Configure(EntityTypeBuilder<ProductPack> builder)
    {
        // Deleting the product takes its packs with it. Their packaging lines are not cascaded from
        // here — see PackagingLineConfiguration for why, and ProductPackRepository for what removes them.
        builder.HasOne(p => p.Product)
            .WithMany()
            .HasForeignKey(p => p.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(p => p.Name).HasMaxLength(ProductPackValidator.MaxNameLength).IsRequired();

        builder.HasIndex(p => new { p.ProductId, p.Name }).IsUnique();

        builder.ToTable(t => t.HasCheckConstraint("CK_ProductPacks_PiecesPerPack", "[PiecesPerPack] > 0"));
    }
}
