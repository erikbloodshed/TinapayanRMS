using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Configurations;

public class RawMaterialCategoryConfiguration : IEntityTypeConfiguration<RawMaterialCategory>
{
    public void Configure(EntityTypeBuilder<RawMaterialCategory> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.Name).IsUnique();

        // Stored by name, as RawMaterial.Type is. The default puts the categories saved before types came in with the
        // ingredients (the enum's default too, so EF has no sentinel to tell apart); packaging ones are set in Settings.
        builder.Property(c => c.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(RawMaterialType.Ingredient);
    }
}
