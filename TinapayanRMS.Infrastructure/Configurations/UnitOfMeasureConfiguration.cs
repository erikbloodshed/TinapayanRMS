using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure.Configurations;

public class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.Property(u => u.Name).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Abbreviation).HasMaxLength(10).IsRequired();

        builder.HasIndex(u => u.Name).IsUnique();
        builder.HasIndex(u => u.Abbreviation).IsUnique();

        builder.HasData(
            new UnitOfMeasure { Id = WellKnownUnitOfMeasureIds.Millimeter, Name = "Millimeter", Abbreviation = "mm" },
            new UnitOfMeasure { Id = WellKnownUnitOfMeasureIds.Centimeter, Name = "Centimeter", Abbreviation = "cm" },
            new UnitOfMeasure { Id = WellKnownUnitOfMeasureIds.Inch, Name = "Inch", Abbreviation = "in" },
            new UnitOfMeasure { Id = WellKnownUnitOfMeasureIds.Meter, Name = "Meter", Abbreviation = "m" },
            new UnitOfMeasure { Id = 5, Name = "Gram", Abbreviation = "g" },
            new UnitOfMeasure { Id = 6, Name = "Piece", Abbreviation = "pc" });
    }
}
