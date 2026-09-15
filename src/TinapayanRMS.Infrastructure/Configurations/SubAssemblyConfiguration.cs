using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class SubAssemblyConfiguration : IEntityTypeConfiguration<SubAssembly>
{
    public void Configure(EntityTypeBuilder<SubAssembly> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();

        // Unique within its owner, not across products: a copy can keep its source's name. No filter:
        // EF would otherwise leave out rows with no owner (IS NOT NULL), and SQL Server's unique index
        // counts NULLs as equal, which keeps standalone names unique among themselves.
        builder.HasIndex(s => new { s.OwnerProductId, s.Name }).IsUnique().HasFilter(null);

        // Restrict, not Cascade: this is also how "a role can't be deleted while any
        // sub-assembly uses it" (PLAN.md's SubAssemblyRole rule) is enforced — no
        // separate application-level check needed.
        builder.HasOne(s => s.Role)
            .WithMany()
            .HasForeignKey(s => s.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deleting the owner takes its sub-assemblies (and their lines) with it. ProductRepository
        // refuses first while another product links one of them. A standalone one has no owner.
        builder.HasOne(s => s.OwnerProduct)
            .WithMany()
            .HasForeignKey(s => s.OwnerProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
