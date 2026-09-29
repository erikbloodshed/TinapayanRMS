using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(p => p.Name).IsUnique();

        builder.Property(p => p.Description).HasMaxLength(ProductValidator.MaxDescriptionLength);

        // One piece unless the product says otherwise. The database default matters as much as the CLR one:
        // without it the column is added to existing rows as 0, and the check constraint below would then
        // refuse to apply. HasSentinel moves the "leave it to the database" value off 0, which is what EF
        // would take it to be: a product explicitly saved as 0 pieces has to reach the check constraint and
        // be refused, on an insert as on an update, rather than being quietly stored as 1.
        builder.Property(p => p.QuantityRequired).HasDefaultValue(1).HasSentinel(-1);

        // A composite is one sellable unit: its products say how many of each go in (CompositeProductRules.EnsureOneUnit).
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Products_QuantityRequired", "[QuantityRequired] > 0");
            t.HasCheckConstraint("CK_Products_CompositeIsOneUnit", "[IsComposite] = 0 OR [QuantityRequired] = 1");
        });

        // The database default is only for the products there before stages were: the column is added to them as
        // Production. A new one is Research and Development, the CLR default, so HasSentinel moves the "leave it to
        // the database" value off that: EF always writes the stage the product holds.
        builder.Property(p => p.Stage)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(ProductStage.Production)
            .HasSentinel((ProductStage)(-1));

        // A category is optional, so deleting one leaves its products uncategorized rather than being refused.
        builder.HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
