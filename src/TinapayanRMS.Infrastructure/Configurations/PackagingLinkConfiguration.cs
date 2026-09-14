using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class PackagingLinkConfiguration : IEntityTypeConfiguration<PackagingLink>
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

        // A product has one packaging, so at most one link.
        builder.HasIndex(l => l.ProductId).IsUnique();
    }
}
