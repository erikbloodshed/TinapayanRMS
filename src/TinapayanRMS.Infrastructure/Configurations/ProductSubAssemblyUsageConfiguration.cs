using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class ProductSubAssemblyUsageConfiguration : IEntityTypeConfiguration<ProductSubAssemblyUsage>
{
    public void Configure(EntityTypeBuilder<ProductSubAssemblyUsage> builder)
    {
        // Deleting the product takes its own usages with it.
        builder.HasOne(u => u.Product)
            .WithMany()
            .HasForeignKey(u => u.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a sub-assembly that another product still uses is blocked instead.
        builder.HasOne(u => u.SubAssembly)
            .WithMany()
            .HasForeignKey(u => u.SubAssemblyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => new { u.ProductId, u.SubAssemblyId }).IsUnique();

        builder.ToTable(t => t.HasCheckConstraint("CK_ProductSubAssemblyUsages_AuthoredValue", "[AuthoredValue] > 0"));
    }
}
