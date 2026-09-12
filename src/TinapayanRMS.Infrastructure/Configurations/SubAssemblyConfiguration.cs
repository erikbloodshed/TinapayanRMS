using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Infrastructure.Configurations;

public class SubAssemblyConfiguration : IEntityTypeConfiguration<SubAssembly>
{
    public void Configure(EntityTypeBuilder<SubAssembly> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(s => s.Name).IsUnique();

        // Restrict, not Cascade: this is also how "a role can't be deleted while any
        // sub-assembly uses it" (PLAN.md's SubAssemblyRole rule) is enforced — no
        // separate application-level check needed.
        builder.HasOne(s => s.Role)
            .WithMany()
            .HasForeignKey(s => s.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
