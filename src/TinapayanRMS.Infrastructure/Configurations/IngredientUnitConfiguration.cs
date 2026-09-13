using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class IngredientUnitConfiguration : IEntityTypeConfiguration<IngredientUnit>
{
    public void Configure(EntityTypeBuilder<IngredientUnit> builder)
    {
        // Deleting the ingredient takes its own units with it.
        builder.HasOne(u => u.Ingredient)
            .WithMany(i => i.Units)
            .HasForeignKey(u => u.IngredientId)
            .OnDelete(DeleteBehavior.Cascade);

        // A unit of measure in use as some ingredient's alternative unit can't be deleted.
        builder.HasOne(u => u.UnitOfMeasure)
            .WithMany()
            .HasForeignKey(u => u.UnitOfMeasureId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(u => u.Description).HasMaxLength(200);

        builder.HasIndex(u => new { u.IngredientId, u.UnitOfMeasureId }).IsUnique();
    }
}
