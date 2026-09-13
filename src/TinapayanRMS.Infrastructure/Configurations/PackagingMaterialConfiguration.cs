using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

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

        builder.HasOne(p => p.BaseUnit)
            .WithMany()
            .HasForeignKey(p => p.BaseUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
