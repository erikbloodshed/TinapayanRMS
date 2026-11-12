using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class ProductFolderConfiguration : IEntityTypeConfiguration<ProductFolder>
{
    public void Configure(EntityTypeBuilder<ProductFolder> builder)
    {
        builder.Property(f => f.Name).HasMaxLength(ProductFolderRules.MaxNameLength).IsRequired();

        // Stored by name, as a product's stage is (ProductStageConverter). Always written: no database default.
        builder.Property(f => f.Stage).HasConversion<ProductStageConverter>().HasMaxLength(30);

        // A name is unique beside the folders in the same place, the top of a stage included (a null parent counts as one
        // place, not as each its own); the collation makes it so whatever the case. The repository checks first, to say
        // where (ProductFolderRules.NameTaken).
        builder.HasIndex(f => new { f.Stage, f.ParentId, f.Name }).IsUnique().AreNullsDistinct(false);

        // A folder is deleted with what it holds moved up first (ProductFolderRepository.DeleteAsync), so one still
        // holding folders is refused rather than taking them with it.
        builder.HasOne<ProductFolder>()
            .WithMany()
            .HasForeignKey(f => f.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint("CK_ProductFolders_NotOwnParent", "parent_id IS NULL OR parent_id <> id"));
    }
}
