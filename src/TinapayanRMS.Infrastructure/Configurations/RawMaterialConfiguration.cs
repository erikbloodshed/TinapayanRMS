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

        // An ingredient another is linked to can't be deleted while the part links to it.
        builder.HasOne(m => m.LinkedTo)
            .WithMany()
            .HasForeignKey(m => m.LinkedToId)
            .OnDelete(DeleteBehavior.Restrict);

        // Backstops for RawMaterialRules.EnsureFieldsMatchType, EnsureFieldsMatchSource and
        // EnsureValidCostBuffer, which the repository checks first with a friendlier message: an
        // ingredient's base unit is the gram (no row), a packaging material has one of its own, only an
        // ingredient can be made or linked, a linked one has a link and both shares and nothing else
        // does, and a cost buffer is 0 to 100 percent.
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_RawMaterials_BaseUnitMatchesType",
                "([Type] = 'Ingredient' AND [BaseUnitId] IS NULL) OR ([Type] = 'Packaging' AND [BaseUnitId] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_RawMaterials_OnlyIngredientsAreMade",
                "[Type] = 'Ingredient' OR [Source] = 'Purchased'");
            table.HasCheckConstraint(
                "CK_RawMaterials_LinkMatchesSource",
                "([Source] = 'Linked' AND [LinkedToId] IS NOT NULL"
                + " AND [WeightSharePercent] > 0 AND [WeightSharePercent] <= 100"
                + " AND [CostSharePercent] > 0 AND [CostSharePercent] <= 100)"
                + " OR ([Source] <> 'Linked' AND [LinkedToId] IS NULL AND [WeightSharePercent] IS NULL AND [CostSharePercent] IS NULL)");
            table.HasCheckConstraint(
                "CK_RawMaterials_CostBufferPercent",
                "[CostBufferPercent] >= 0 AND [CostBufferPercent] <= 100");
        });
    }
}
