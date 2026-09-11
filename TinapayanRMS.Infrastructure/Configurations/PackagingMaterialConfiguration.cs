using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure.Configurations;

public class PackagingMaterialConfiguration : IEntityTypeConfiguration<PackagingMaterial>
{
    public void Configure(EntityTypeBuilder<PackagingMaterial> builder)
    {
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(p => p.Name).IsUnique();

        builder.HasOne(p => p.PackagingCategory)
            .WithMany()
            .HasForeignKey(p => p.PackagingCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.StandardUnit)
            .WithMany()
            .HasForeignKey(p => p.StandardUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.PurchaseUnit)
            .WithMany()
            .HasForeignKey(p => p.PurchaseUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
