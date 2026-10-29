using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.Property(u => u.Name).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Abbreviation).HasMaxLength(10).IsRequired();

        builder.HasIndex(u => u.Name).IsUnique();
        builder.HasIndex(u => u.Abbreviation).IsUnique();
    }
}
