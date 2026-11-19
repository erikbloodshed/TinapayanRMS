using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

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

        // Normal for the row saved before it was; Normal is the enum's default too, so leaving it to the database writes
        // the same.
        builder.Property(s => s.TypeSize)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(PrintTypeSize.Normal);

        builder.Property(s => s.ShopName).HasMaxLength(PrintRules.MaxShopNameLength);
        builder.Property(s => s.ShopAddress).HasMaxLength(PrintRules.MaxShopAddressLength);
        builder.Property(s => s.FooterNote).HasMaxLength(PrintRules.MaxFooterNoteLength);
        builder.Property(s => s.LogoContentType).HasMaxLength(30);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_PrintSettings_SingleRow", $"id = {PrintSettings.SingletonId}");

            // Backstops for PrintRules.EnsureValid.
            t.HasCheckConstraint("CK_PrintSettings_PaperSize", "paper_size IN ('A4', 'Letter', 'LongBond', 'Legal')");
            t.HasCheckConstraint("CK_PrintSettings_Orientation", "orientation IN ('Portrait', 'Landscape')");
            t.HasCheckConstraint("CK_PrintSettings_Margins", "margins IN ('Normal', 'Narrow', 'Wide')");
            t.HasCheckConstraint("CK_PrintSettings_TypeSize", "type_size IN ('Normal', 'Compact', 'Large')");

            // A logo is a picture with its type, or neither, and no bigger than PrintRules.MaxLogoBytes. The type's IS NOT
            // NULL is needed: a NULL type's IN would be NULL, which a check lets through.
            t.HasCheckConstraint(
                "CK_PrintSettings_LogoWhole",
                "(logo IS NULL AND logo_content_type IS NULL) OR (logo IS NOT NULL AND logo_content_type IS NOT NULL AND logo_content_type IN ('image/png', 'image/jpeg'))");
            t.HasCheckConstraint("CK_PrintSettings_LogoSize", $"logo IS NULL OR octet_length(logo) <= {PrintRules.MaxLogoBytes}");
        });
    }
}
