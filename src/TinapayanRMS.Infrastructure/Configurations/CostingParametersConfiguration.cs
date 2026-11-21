using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class CostingParametersConfiguration : IEntityTypeConfiguration<CostingParameters>
{
    public void Configure(EntityTypeBuilder<CostingParameters> builder)
    {
        // The markup on cost, stored as margin_percent, its name before 2026-11-22: the Recipe API, installed apart from the
        // app, reads the column too, and would stop reading costs until reinstalled if the app renamed it.
        builder.Property(c => c.MarkupPercent).HasColumnName("margin_percent");

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
