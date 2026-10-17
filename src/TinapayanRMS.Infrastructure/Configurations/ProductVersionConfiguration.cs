using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class ProductVersionConfiguration : IEntityTypeConfiguration<ProductVersion>
{
    public void Configure(EntityTypeBuilder<ProductVersion> builder)
    {
        builder.Property(v => v.Note).HasMaxLength(ProductVersionRules.MaxNoteLength);

        // The snapshot points at nothing, so it has no length to cap: a long recipe makes a long one.
        builder.Property(v => v.Content).IsRequired();

        // A product deleted for good takes its history with it; the Deleted folder keeps it whole.
        builder.HasOne(v => v.Product)
            .WithMany()
            .HasForeignKey(v => v.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(v => new { v.ProductId, v.Number }).IsUnique();

        builder.ToTable(t => t.HasCheckConstraint("CK_ProductVersions_Number", "[Number] >= 1"));
    }
}
