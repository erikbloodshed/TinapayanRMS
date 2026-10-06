using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class CostingDefaultsConfiguration : IEntityTypeConfiguration<CostingDefaults>
{
    public void Configure(EntityTypeBuilder<CostingDefaults> builder)
    {
        // A settings record, not a list: the one row always has Id 1, so it's never an identity.
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_CostingDefaults_SingleRow", $"[Id] = {CostingDefaults.SingletonId}");

            // A section's own range (CK_ProductSubAssemblyUsages_ShrinkagePercent): a new section starts at it.
            t.HasCheckConstraint("CK_CostingDefaults_ShrinkagePercent", "[ShrinkagePercent] >= 0 AND [ShrinkagePercent] < 100");
        });
    }
}
