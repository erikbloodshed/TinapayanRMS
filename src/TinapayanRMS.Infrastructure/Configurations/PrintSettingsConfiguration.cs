using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Configurations;

public sealed class PrintSettingsConfiguration : IEntityTypeConfiguration<PrintSettings>
{
    public void Configure(EntityTypeBuilder<PrintSettings> builder)
    {
        // A settings record, not a list: the one row always has Id 1, so it's never an identity.
        builder.Property(s => s.Id).ValueGeneratedNever();

        // Stored by name, as RawMaterial.Type is. Each choice's first is the enum's default, so EF always writes it.
        builder.Property(s => s.PaperSize).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.Orientation).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.Margins).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_PrintSettings_SingleRow", $"id = {PrintSettings.SingletonId}");

            // Backstops for PrintRules.EnsureValid.
            t.HasCheckConstraint("CK_PrintSettings_PaperSize", "paper_size IN ('A4', 'Letter', 'LongBond', 'Legal')");
            t.HasCheckConstraint("CK_PrintSettings_Orientation", "orientation IN ('Portrait', 'Landscape')");
            t.HasCheckConstraint("CK_PrintSettings_Margins", "margins IN ('Normal', 'Narrow', 'Wide')");
        });
    }
}
