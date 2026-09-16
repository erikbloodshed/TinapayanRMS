using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class UsageAddInLineConfiguration : IEntityTypeConfiguration<UsageAddInLine>
{
    public void Configure(EntityTypeBuilder<UsageAddInLine> builder)
    {
        // Taking the sub-assembly out of the recipe takes this product's add-ins to it with it. The only
        // cascade path in is Products → ProductSubAssemblyUsages → UsageAddInLines, a chain rather than a
        // second path, which SQL Server allows: a usage's own sub-assembly FK is Restrict.
        builder.HasOne(l => l.Usage)
            .WithMany()
            .HasForeignKey(l => l.UsageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting an ingredient that's in use as an add-in is blocked instead.
        builder.HasOne(l => l.Ingredient)
            .WithMany()
            .HasForeignKey(l => l.IngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.UsageId, l.IngredientId }).IsUnique();
    }
}
