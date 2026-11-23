using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class ProductSubAssemblyUsageConfiguration : IEntityTypeConfiguration<ProductSubAssemblyUsage>
{
    public void Configure(EntityTypeBuilder<ProductSubAssemblyUsage> builder)
    {
        // Deleting the product takes its own usages with it.
        builder.HasOne(u => u.Product)
            .WithMany()
            .HasForeignKey(u => u.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting a sub-assembly that another product still uses is blocked instead.
        builder.HasOne(u => u.SubAssembly)
            .WithMany()
            .HasForeignKey(u => u.SubAssemblyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(u => new { u.ProductId, u.SubAssemblyId }).IsUnique();

        // The section it goes into whole is another of the same product's (ProductRules.EnsureValidFolds, in SetRecipeAsync),
        // which uses that sub-assembly too, so it can't be deleted from under it either.
        builder.HasOne<SubAssembly>()
            .WithMany()
            .HasForeignKey(u => u.GoesIntoSubAssemblyId)
            .OnDelete(DeleteBehavior.Restrict);

        // 0 means "not sized yet": a section goes into the recipe before how much of it a piece takes is
        // known (ProductRules.EnsureValidUsages). Only a negative piece size is refused.
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ProductSubAssemblyUsages_AuthoredValue", "authored_value >= 0");
            t.HasCheckConstraint(
                "CK_ProductSubAssemblyUsages_GoesIntoAnother",
                "goes_into_sub_assembly_id IS NULL OR goes_into_sub_assembly_id <> sub_assembly_id");
        });
    }
}
