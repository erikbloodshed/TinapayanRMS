using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class SubAssemblyLineConfiguration : IEntityTypeConfiguration<SubAssemblyLine>
{
    public void Configure(EntityTypeBuilder<SubAssemblyLine> builder)
    {
        // Deleting the sub-assembly takes its own lines with it.
        builder.HasOne(l => l.SubAssembly)
            .WithMany()
            .HasForeignKey(l => l.SubAssemblyId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting an ingredient that's in use as a line is blocked instead.
        builder.HasOne(l => l.Ingredient)
            .WithMany()
            .HasForeignKey(l => l.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        // An ingredient's unit can't be deleted while a line is entered in it.
        builder.HasOne(l => l.Unit)
            .WithMany()
            .HasForeignKey(l => l.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.SubAssemblyId, l.IngredientId }).IsUnique();
    }
}
