using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class ProductComponentConfiguration : IEntityTypeConfiguration<ProductComponent>
{
    public void Configure(EntityTypeBuilder<ProductComponent> builder)
    {
        // Deleting the composite takes its lines with it; the products it held stay.
        builder.HasOne(c => c.Product)
            .WithMany()
            .HasForeignKey(c => c.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // A product in a composite can't be deleted until it's taken out there (ProductRepository.DeleteAsync names the
        // composites first). It couldn't cascade anyway: that would be a second cascade path from Products to this table,
        // which SQL Server refuses.
        builder.HasOne(c => c.ComponentProduct)
            .WithMany()
            .HasForeignKey(c => c.ComponentProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // One line per product: more of it is a bigger quantity on the same line.
        builder.HasIndex(c => new { c.ProductId, c.ComponentProductId }).IsUnique();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ProductComponents_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_ProductComponents_NotItself", "[ProductId] <> [ComponentProductId]");
        });
    }
}
