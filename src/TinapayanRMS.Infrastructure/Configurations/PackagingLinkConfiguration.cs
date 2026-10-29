using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class PackagingLinkConfiguration : IEntityTypeConfiguration<PackagingLink>
{
    public void Configure(EntityTypeBuilder<PackagingLink> builder)
    {
        // Deleting the derived product takes its own link with it.
        builder.HasOne(l => l.Product)
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // A product whose packaging another product links to can't be deleted until that link is
        // repointed or unlinked first.
        builder.HasOne(l => l.LinkedProduct)
            .WithMany()
            .HasForeignKey(l => l.LinkedProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // A standalone packaging set a product links can't be deleted until that link is repointed or unlinked
        // first (SubAssemblyRepository.DeleteStandaloneAsync says which products).
        builder.HasOne(l => l.LinkedSubAssembly)
            .WithMany()
            .HasForeignKey(l => l.LinkedSubAssemblyId)
            .OnDelete(DeleteBehavior.Restrict);

        // A link points at a product or at a set, never both or neither.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_PackagingLinks_OneTarget",
            "(linked_product_id IS NOT NULL AND linked_sub_assembly_id IS NULL) OR (linked_product_id IS NULL AND linked_sub_assembly_id IS NOT NULL)"));

        // A product has one packaging, so at most one link.
        builder.HasIndex(l => l.ProductId).IsUnique();
    }
}
