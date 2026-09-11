using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure.Configurations;

public class IngredientPriceHistoryConfiguration : IEntityTypeConfiguration<IngredientPriceHistory>
{
    public void Configure(EntityTypeBuilder<IngredientPriceHistory> builder)
    {
        builder.HasOne(h => h.Ingredient)
            .WithMany()
            .HasForeignKey(h => h.IngredientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(h => new { h.IngredientId, h.Date });
    }
}
