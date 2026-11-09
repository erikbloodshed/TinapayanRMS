using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class CostingDefaultsConfiguration : IEntityTypeConfiguration<CostingDefaults>
{
    public void Configure(EntityTypeBuilder<CostingDefaults> builder)
    {
        // A settings record, not a list: the one row always has Id 1, so it's never an identity.
        builder.Property(d => d.Id).ValueGeneratedNever();

        // The database default is only for the row saved before this was: the column is added to it as Production. As on
        // Products.Stage, HasSentinel moves the "leave it to the database" value off Innovation, the CLR
        // default, so EF always writes the stage picked. It's stored by name, reading Innovation's former name too
        // (ProductStageConverter).
        builder.Property(d => d.NewProductStage)
            .HasConversion<ProductStageConverter>()
            .HasMaxLength(30)
            .HasDefaultValue(CostingDefaults.DefaultNewProductStage)
            .HasSentinel((ProductStage)(-1));

        // The same again for the row saved before this was: it's given 10%. HasSentinel moves the "leave it to the database"
        // value off 0, so a buffer of none is written as typed rather than replaced by the store default.
        builder.Property(d => d.CostBufferPercent)
            .HasDefaultValue(CostingDefaults.DefaultCostBufferPercent)
            .HasSentinel(-1m);

        // The same for the shop-wide wastage: the row saved before it was is given 3%, and 0 is written as typed.
        builder.Property(d => d.WastagePercent)
            .HasDefaultValue(CostingDefaults.DefaultWastagePercent)
            .HasSentinel(-1m);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_CostingDefaults_SingleRow", $"id = {CostingDefaults.SingletonId}");

            // A share of the ingredient cost added on top of it (CostingRules.EnsureValidWastage).
            t.HasCheckConstraint("CK_CostingDefaults_WastagePercent", "wastage_percent >= 0 AND wastage_percent <= 100");

            // Backstop for CostingRules.EnsureValidVat: every product's current price has the VAT taken out by dividing by
            // 1 + VAT%, on every read.
            t.HasCheckConstraint("CK_CostingDefaults_VatPercent", "vat_percent >= 0");

            // A material's own range (CK_RawMaterials_CostBufferPercent): a new one starts at it.
            t.HasCheckConstraint("CK_CostingDefaults_CostBufferPercent", "cost_buffer_percent >= 0 AND cost_buffer_percent <= 100");

            // None, or a step CostingRules.PriceRoundingSteps offers.
            t.HasCheckConstraint("CK_CostingDefaults_PriceRoundingStep", "price_rounding_step IN (0, 0.25, 0.5, 1, 5, 10)");
        });
    }
}
