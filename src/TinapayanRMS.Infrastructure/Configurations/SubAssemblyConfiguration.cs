using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class SubAssemblyConfiguration : IEntityTypeConfiguration<SubAssembly>
{
    public void Configure(EntityTypeBuilder<SubAssembly> builder)
    {
        // A standalone sub-assembly always has a name; a product's own goes by its role, and has one only to tell it
        // apart from others in that role.
        builder.Property(s => s.Name).HasMaxLength(100);

        // Standalone names are unique among themselves: rows with no owner are kept in the index, where SQL
        // Server's unique index counts their NULL owners as equal. A product's own names are unique within their
        // product; an unnamed one is left out (the filter), or its NULL names would clash too.
        builder.HasIndex(s => new { s.OwnerProductId, s.Name }).IsUnique().HasFilter("[Name] IS NOT NULL");

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
