using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class CostingParametersConfiguration : IEntityTypeConfiguration<CostingParameters>
{
    public void Configure(EntityTypeBuilder<CostingParameters> builder)
    {
        // Backstop for CostingRules.EnsureValidParameters.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_CostingParameters_Percents",
            "labor_percent >= 0 AND overhead_percent >= 0 AND administrative_percent >= 0 AND margin_percent >= 0"));

        // One-to-one with Product: deleting the product takes its costing parameters with it.
        builder.HasOne(c => c.Product)
            .WithMany()
            .HasForeignKey(c => c.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.ProductId).IsUnique();
    }
}
