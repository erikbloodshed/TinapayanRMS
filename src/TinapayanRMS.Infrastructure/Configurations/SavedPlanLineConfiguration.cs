using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public class SavedPlanLineConfiguration : IEntityTypeConfiguration<SavedPlanLine>
{
    public void Configure(EntityTypeBuilder<SavedPlanLine> builder)
    {
        // Something to make (SavedPlanRules).
        builder.ToTable(t => t.HasCheckConstraint("CK_SavedPlanLines_Units", "units > 0"));

        // A saved plan only lists what to make, so deleting a product for good isn't blocked by one: its lines go instead.
        builder.HasOne(l => l.Product)
            .WithMany()
            .HasForeignKey(l => l.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
