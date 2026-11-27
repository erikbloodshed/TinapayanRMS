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

        builder.ToTable(t =>
        {
            // CK_CostingDefaults_WastagePercent's range (CostingRules.EnsureValidWastage), or none for the shop-wide one.
            t.HasCheckConstraint("CK_ProductCategories_WastagePercent", "wastage_percent IS NULL OR (wastage_percent >= 0 AND wastage_percent <= 100)");

            // A product's own costing's range (CostingRules.EnsureValidCategoryCosting) and a Warn Below %'s
            // (EnsureValidMarkupFloor), or none for Settings'.
            t.HasCheckConstraint(
                "CK_ProductCategories_CostingPercents",
                "(labor_percent IS NULL OR labor_percent >= 0) AND (overhead_percent IS NULL OR overhead_percent >= 0) "
                + "AND (administrative_percent IS NULL OR administrative_percent >= 0) AND (markup_percent IS NULL OR markup_percent >= 0) "
                + "AND (markup_floor_percent IS NULL OR markup_floor_percent >= 0)");
        });
    }
}
