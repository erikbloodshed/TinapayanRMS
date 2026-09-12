using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class IngredientConfiguration : IEntityTypeConfiguration<Ingredient>
{
    public void Configure(EntityTypeBuilder<Ingredient> builder)
    {
        builder.Property(i => i.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(i => i.Name).IsUnique();

        builder.Property(i => i.Source)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(i => i.IngredientCategory)
            .WithMany()
            .HasForeignKey(i => i.IngredientCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.PurchaseUnit)
            .WithMany()
            .HasForeignKey(i => i.PurchaseUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
