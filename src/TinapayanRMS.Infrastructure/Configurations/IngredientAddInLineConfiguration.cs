using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class IngredientAddInLineConfiguration : IEntityTypeConfiguration<IngredientAddInLine>
{
    public void Configure(EntityTypeBuilder<IngredientAddInLine> builder)
    {
        // Deleting the made ingredient takes its own add-ins with it.
        builder.HasOne(l => l.Ingredient)
            .WithMany()
            .HasForeignKey(l => l.IngredientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting an ingredient that's in use as an add-in is blocked instead.
        builder.HasOne(l => l.AddInIngredient)
            .WithMany()
            .HasForeignKey(l => l.AddInIngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.IngredientId, l.AddInIngredientId }).IsUnique();
    }
}
