using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class UsageSectionLineConfiguration : IEntityTypeConfiguration<UsageSectionLine>
{
    public void Configure(EntityTypeBuilder<UsageSectionLine> builder)
    {
        // Backstop for ProductRules.EnsureValidSectionLines.
        builder.ToTable(t => t.HasCheckConstraint("CK_UsageSectionLines_Grams", "grams > 0"));

        // Taking the section out of the recipe takes its lines with it, as it takes its add-ins: a Save deletes every usage
        // of the product before it writes them again (ProductSubAssemblyUsageRepository.SetRecipeAsync).
        builder.HasOne(l => l.Usage)
            .WithMany()
            .HasForeignKey(l => l.UsageId)
            .OnDelete(DeleteBehavior.Cascade);

        // The section it uses is another of the same product's, whose usage (Restrict) keeps its sub-assembly from being
        // deleted from under it; a Save deletes the usages, and these with them, before any sub-assembly it drops. Only
        // deleting the product for good reaches this, deleting its own sub-assemblies and its usages in one statement by
        // their cascades: Restrict, or no action, refused it there, checked before the usages' cascade had taken the lines.
        builder.HasOne<SubAssembly>()
            .WithMany()
            .HasForeignKey(l => l.SectionSubAssemblyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(l => new { l.UsageId, l.SectionSubAssemblyId }).IsUnique();
    }
}
