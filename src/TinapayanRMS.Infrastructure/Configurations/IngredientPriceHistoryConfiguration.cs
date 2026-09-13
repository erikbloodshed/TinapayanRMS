using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class IngredientPriceHistoryConfiguration : IEntityTypeConfiguration<IngredientPriceHistory>
{
    public void Configure(EntityTypeBuilder<IngredientPriceHistory> builder)
    {
        builder.HasOne(h => h.Ingredient)
            .WithMany()
            .HasForeignKey(h => h.IngredientId)
            .OnDelete(DeleteBehavior.Cascade);

        // A unit something was priced in can't be deleted while that price entry exists.
        // Not Cascade: SQL Server rejects a second cascade path from Ingredients (through
        // IngredientUnits) to this table.
        builder.HasOne(h => h.IngredientUnit)
            .WithMany()
            .HasForeignKey(h => h.IngredientUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => new { h.IngredientId, h.Date });
    }
}
