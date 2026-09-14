using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class RawMaterialConfiguration : IEntityTypeConfiguration<RawMaterial>
{
    public void Configure(EntityTypeBuilder<RawMaterial> builder)
    {
        // Ingredients and packaging share one list, so a name is unique across both types.
        builder.Property(m => m.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(m => m.Name).IsUnique();

        builder.Property(m => m.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.Source)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(m => m.Category)
            .WithMany()
            .HasForeignKey(m => m.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // A unit of measure in use as a packaging material's base unit can't be deleted.
        builder.HasOne(m => m.BaseUnit)
            .WithMany()
            .HasForeignKey(m => m.BaseUnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Backstops for RawMaterialRules.EnsureFieldsMatchType and EnsureValidCostBuffer, which the
        // repository checks first with a friendlier message: an ingredient's base unit is the gram
        // (no row), a packaging material has one of its own, only an ingredient can be made, and a
        // cost buffer is 0 to 100 percent.
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_RawMaterials_BaseUnitMatchesType",
                "([Type] = 'Ingredient' AND [BaseUnitId] IS NULL) OR ([Type] = 'Packaging' AND [BaseUnitId] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_RawMaterials_OnlyIngredientsAreMade",
                "[Type] = 'Ingredient' OR [Source] = 'Purchased'");
            table.HasCheckConstraint(
                "CK_RawMaterials_CostBufferPercent",
                "[CostBufferPercent] >= 0 AND [CostBufferPercent] <= 100");
        });
    }
}
