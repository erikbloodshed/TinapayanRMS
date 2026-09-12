using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class CostingParametersConfiguration : IEntityTypeConfiguration<CostingParameters>
{
    public void Configure(EntityTypeBuilder<CostingParameters> builder)
    {
        // One-to-one with Product: deleting the product takes its costing parameters with it.
        builder.HasOne(c => c.Product)
            .WithMany()
            .HasForeignKey(c => c.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.ProductId).IsUnique();
    }
}
