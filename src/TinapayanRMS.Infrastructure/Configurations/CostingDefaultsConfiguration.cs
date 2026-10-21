using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Configurations;

public class CostingDefaultsConfiguration : IEntityTypeConfiguration<CostingDefaults>
{
    public void Configure(EntityTypeBuilder<CostingDefaults> builder)
    {
        // A settings record, not a list: the one row always has Id 1, so it's never an identity.
        builder.Property(d => d.Id).ValueGeneratedNever();

        // The database default is only for the row saved before this was: the column is added to it as Production. As on
        // Products.Stage, HasSentinel moves the "leave it to the database" value off Research and Development, the CLR
        // default, so EF always writes the stage picked.
        builder.Property(d => d.NewProductStage)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(CostingDefaults.DefaultNewProductStage)
            .HasSentinel((ProductStage)(-1));

        // The same again for the row saved before this was: it's given 10%. HasSentinel moves the "leave it to the database"
        // value off 0, so a buffer of none is written as typed rather than replaced by the store default.
        builder.Property(d => d.CostBufferPercent)
            .HasDefaultValue(CostingDefaults.DefaultCostBufferPercent)
            .HasSentinel(-1m);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_CostingDefaults_SingleRow", $"id = {CostingDefaults.SingletonId}");

            // A section's own range (CK_ProductSubAssemblyUsages_ShrinkagePercent): a new section starts at it.
            t.HasCheckConstraint("CK_CostingDefaults_ShrinkagePercent", "shrinkage_percent >= 0 AND shrinkage_percent < 100");

            // A material's own range (CK_RawMaterials_CostBufferPercent): a new one starts at it.
            t.HasCheckConstraint("CK_CostingDefaults_CostBufferPercent", "cost_buffer_percent >= 0 AND cost_buffer_percent <= 100");
        });
    }
}
