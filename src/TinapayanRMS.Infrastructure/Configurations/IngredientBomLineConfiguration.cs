using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class IngredientBomLineConfiguration : IEntityTypeConfiguration<IngredientBomLine>
{
    public void Configure(EntityTypeBuilder<IngredientBomLine> builder)
    {
        // Deleting the made ingredient takes its own BOM lines with it.
        builder.HasOne(l => l.Ingredient)
            .WithMany()
            .HasForeignKey(l => l.IngredientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a component that's in use is blocked instead — and SQL Server
        // rejects two Cascade paths from the same table to the same parent table anyway.
        builder.HasOne(l => l.ComponentIngredient)
            .WithMany()
            .HasForeignKey(l => l.ComponentIngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        // A component's unit can't be deleted while a line is entered in it.
        builder.HasOne(l => l.Unit)
            .WithMany()
            .HasForeignKey(l => l.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.IngredientId, l.ComponentIngredientId }).IsUnique();
    }
}
