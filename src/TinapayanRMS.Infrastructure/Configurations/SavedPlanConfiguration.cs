using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class SavedPlanConfiguration : IEntityTypeConfiguration<SavedPlan>
{
    public void Configure(EntityTypeBuilder<SavedPlan> builder)
    {
        builder.Property(p => p.Name).HasMaxLength(SavedPlanRules.NameMaxLength).IsRequired();
        builder.HasIndex(p => p.Name).IsUnique();

        // Deleting a plan takes its lines with it.
        builder.HasMany(p => p.Lines)
            .WithOne(l => l.SavedPlan)
            .HasForeignKey(l => l.SavedPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
