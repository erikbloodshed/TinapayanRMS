using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure.Configurations;

public class ProductTabLinkConfiguration : IEntityTypeConfiguration<ProductTabLink>
{
    public void Configure(EntityTypeBuilder<ProductTabLink> builder)
    {
        // Deleting the derived product takes its own tab links with it.
        builder.HasOne(l => l.Product)
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // A product that's linked to by another product's tab can't be deleted until
        // every tab linking to it is repointed or unlinked first.
        builder.HasOne(l => l.LinkedProduct)
            .WithMany()
            .HasForeignKey(l => l.LinkedProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // A role that's referenced by a tab link can't be deleted while linked.
        builder.HasOne(l => l.SubAssemblyRole)
            .WithMany()
            .HasForeignKey(l => l.SubAssemblyRoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // One link per (product, role) among SubAssemblyRole tabs...
        builder.HasIndex(l => new { l.ProductId, l.SubAssemblyRoleId })
            .IsUnique()
            .HasFilter("[SubAssemblyRoleId] IS NOT NULL");

        // ...and one Packaging-tab link per product. SQL Server's default unique index
        // treats every NULL as distinct, so a plain unique index on (ProductId,
        // SubAssemblyRoleId) alone wouldn't stop two Packaging-tab rows (both NULL) for
        // the same product — this second, NULL-only filtered index closes that gap.
        builder.HasIndex(l => l.ProductId)
            .IsUnique()
            .HasFilter("[SubAssemblyRoleId] IS NULL");
    }
}
