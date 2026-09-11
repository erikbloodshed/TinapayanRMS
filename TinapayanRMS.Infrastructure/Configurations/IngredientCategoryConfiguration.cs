using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure.Configurations;

public class IngredientCategoryConfiguration : IEntityTypeConfiguration<IngredientCategory>
{
    public void Configure(EntityTypeBuilder<IngredientCategory> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(c => c.Name).IsUnique();
    }
}
