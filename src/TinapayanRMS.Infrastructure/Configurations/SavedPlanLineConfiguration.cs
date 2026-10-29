using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class SavedPlanLineConfiguration : IEntityTypeConfiguration<SavedPlanLine>
{
    public void Configure(EntityTypeBuilder<SavedPlanLine> builder)
    {
        // Something to make (SavedPlanRules).
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_SavedPlanLines_Units", "units > 0");

            // What was made and left over (2026-10-29): none below 0, and no more left over than was made.
            t.HasCheckConstraint("CK_SavedPlanLines_MadeUnits", "made_units IS NULL OR made_units >= 0");
            t.HasCheckConstraint(
                "CK_SavedPlanLines_LeftoverUnits",
                "leftover_units IS NULL OR (made_units IS NOT NULL AND leftover_units >= 0 AND leftover_units <= made_units)");
        });

        // A saved plan only lists what to make, so deleting a product for good isn't blocked by one: its lines go instead.
        builder.HasOne(l => l.Product)
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
