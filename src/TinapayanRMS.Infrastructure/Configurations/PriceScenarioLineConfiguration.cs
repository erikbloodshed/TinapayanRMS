using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class PriceScenarioLineConfiguration : IEntityTypeConfiguration<PriceScenarioLine>
{
    public void Configure(EntityTypeBuilder<PriceScenarioLine> builder)
    {
        builder.ToTable(t =>
        {
            // What was typed: a change in percent or a new price, never both and never neither (PriceScenarioRules).
            t.HasCheckConstraint("CK_PriceScenarioLines_OneTyped", "(change_percent IS NULL) <> (new_price IS NULL)");
            t.HasCheckConstraint("CK_PriceScenarioLines_ChangePercent", "change_percent > -100");
            t.HasCheckConstraint("CK_PriceScenarioLines_NewPrice", "new_price > 0");
        });

        // A scenario is only ever a trial, so deleting a material it tries a price on isn't blocked: its lines go instead.
        builder.HasOne(l => l.RawMaterial)
            .WithMany()
            .HasForeignKey(l => l.RawMaterialId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => new { l.PriceScenarioId, l.RawMaterialId }).IsUnique();
    }
}
