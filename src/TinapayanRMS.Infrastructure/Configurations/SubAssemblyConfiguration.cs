using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class SubAssemblyConfiguration : IEntityTypeConfiguration<SubAssembly>
{
    public void Configure(EntityTypeBuilder<SubAssembly> builder)
    {
        // Products pick sub-assemblies by name, so every one has a name no other has.
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(s => s.Name).IsUnique();

        // Restrict, not Cascade: this is also how "a role can't be deleted while any
        // sub-assembly uses it" (PLAN.md's SubAssemblyRole rule) is enforced — no
        // separate application-level check needed.
        builder.HasOne(s => s.Role)
            .WithMany()
            .HasForeignKey(s => s.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deleting a product takes its own sub-assemblies (and their lines) with it. Nothing but the owner uses one: other
        // products can copy it, not link it. One on the Sub-Assemblies screen has no owner.
        builder.HasOne(s => s.OwnerProduct)
            .WithMany()
            .HasForeignKey(s => s.OwnerProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
