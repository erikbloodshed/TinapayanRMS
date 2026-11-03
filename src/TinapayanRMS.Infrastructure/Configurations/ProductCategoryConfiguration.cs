using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.Name).IsUnique();

        // CK_CostingDefaults_WastagePercent's range (CostingRules.EnsureValidWastage), or none for the shop-wide one.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_ProductCategories_WastagePercent", "wastage_percent IS NULL OR (wastage_percent >= 0 AND wastage_percent <= 100)"));
    }
}
