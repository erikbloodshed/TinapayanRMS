using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class RawMaterialConfiguration : IEntityTypeConfiguration<RawMaterial>
{
    public void Configure(EntityTypeBuilder<RawMaterial> builder)
    {
        // Ingredients and packaging share one list, so a name is unique across both types.
        builder.Property(m => m.Name).HasMaxLength(100).IsRequired();

        // A brand as its prices name it (RawMaterialBrandPrice.Brand).
        builder.Property(m => m.PreferredBrand).HasMaxLength(100);
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

        // A sub-assembly a made ingredient is made from can't be deleted while it is.
        builder.HasOne(m => m.SubAssembly)
            .WithMany()
            .HasForeignKey(m => m.SubAssemblyId)
            .OnDelete(DeleteBehavior.Restrict);

        // The costs per base unit keep 20 decimals rather than the usual 4, so a buffered cost typed per bag (₱1,297.37 for
        // 25 kg is ₱0.0518948 a gram) comes back as typed, and screens round only for show. 28 digits in all is as many as
        // a .NET decimal holds, so any stored value reads back without overflowing.
        builder.Property(m => m.CostPerBaseUnit).HasPrecision(28, 20);
        builder.Property(m => m.BufferedCostPerBaseUnit).HasPrecision(28, 20);

        // Backstops for RawMaterialRules.EnsureFieldsMatchType, EnsureFieldsMatchSource and
        // EnsureValidCostBuffer, which the repository checks first with a friendlier message: an
        // ingredient's base unit is the gram (no row), a packaging material has one of its own, only an
        // ingredient can be made or linked, a linked one has a link and both shares and nothing else
        // does, a made one has the sub-assembly it's made from and nothing else does, and a cost buffer is 0 to 100 percent.
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_RawMaterials_BaseUnitMatchesType",
                "(type = 'Ingredient' AND base_unit_id IS NULL) OR (type = 'Packaging' AND base_unit_id IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_RawMaterials_OnlyIngredientsAreMade",
                "type = 'Ingredient' OR source = 'Purchased'");
            table.HasCheckConstraint(
                "CK_RawMaterials_LinkMatchesSource",
                "(source = 'Linked' AND linked_to_id IS NOT NULL"
                + " AND weight_share_percent > 0 AND weight_share_percent <= 100"
                + " AND cost_share_percent > 0 AND cost_share_percent <= 100)"
                + " OR (source <> 'Linked' AND linked_to_id IS NULL AND weight_share_percent IS NULL AND cost_share_percent IS NULL)");
            table.HasCheckConstraint(
                "CK_RawMaterials_SubAssemblyMatchesSource",
                "(source = 'Made') = (sub_assembly_id IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_RawMaterials_CostBufferPercent",
                "cost_buffer_percent >= 0 AND cost_buffer_percent <= 100");

            // The same range as the buffer itself, which it's typed as.
            table.HasCheckConstraint(
                "CK_RawMaterials_TargetCostBufferPercent",
                "target_cost_buffer_percent IS NULL OR (target_cost_buffer_percent >= 0 AND target_cost_buffer_percent <= 100)");

            // A made ingredient's weight loss, which RawMaterialRules.EnsureFieldsMatchSource keeps at 0 or more and under 100:
            // what the recipe makes is its grams less that, and the cost per gram divides by it, so at 100% or more the
            // ingredient would cost nothing (IngredientBomRules.CalculateCost), with every product using it under-costed.
            table.HasCheckConstraint(
                "CK_RawMaterials_WeightLossPercent",
                "weight_loss_percent IS NULL OR (weight_loss_percent >= 0 AND weight_loss_percent < 100)");
        });
    }
}
