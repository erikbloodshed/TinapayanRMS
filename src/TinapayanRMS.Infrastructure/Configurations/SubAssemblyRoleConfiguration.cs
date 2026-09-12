using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class SubAssemblyRoleConfiguration : IEntityTypeConfiguration<SubAssemblyRole>
{
    public void Configure(EntityTypeBuilder<SubAssemblyRole> builder)
    {
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(r => r.Name).IsUnique();

        builder.HasData(
            new SubAssemblyRole { Id = 1, Name = "Dough", SortOrder = 1 },
            new SubAssemblyRole { Id = 2, Name = "Filling", SortOrder = 2 },
            new SubAssemblyRole { Id = 3, Name = "Topping", SortOrder = 3 },
            new SubAssemblyRole { Id = 4, Name = "Inclusion", SortOrder = 4 },
            new SubAssemblyRole { Id = 5, Name = "Finishing", SortOrder = 5 });
    }
}
