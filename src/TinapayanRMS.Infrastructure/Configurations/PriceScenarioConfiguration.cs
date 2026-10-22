using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class PriceScenarioConfiguration : IEntityTypeConfiguration<PriceScenario>
{
    public void Configure(EntityTypeBuilder<PriceScenario> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(PriceScenarioRules.NameMaxLength).IsRequired();
        builder.HasIndex(s => s.Name).IsUnique();

        // Deleting a scenario takes its lines with it.
        builder.HasMany(s => s.Lines)
            .WithOne(l => l.PriceScenario)
            .HasForeignKey(l => l.PriceScenarioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
