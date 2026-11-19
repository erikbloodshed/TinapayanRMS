using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

/// <summary>Settings → Printing's page: each paper's size, turned for landscape, less its margins.</summary>
public class PrintRulesTests
{
    [Test]
    public void Each_paper_is_its_size_upright_in_millimetres()
    {
        PrintRules.PaperSizeOf(PaperSize.A4).Should().Be(new PageArea(210m, 297m));
        PrintRules.PaperSizeOf(PaperSize.Letter).Should().Be(new PageArea(215.9m, 279.4m), "8.5 × 11 in");
        PrintRules.PaperSizeOf(PaperSize.LongBond).Should().Be(new PageArea(215.9m, 330.2m), "8.5 × 13 in");
        PrintRules.PaperSizeOf(PaperSize.Legal).Should().Be(new PageArea(215.9m, 355.6m), "8.5 × 14 in");
    }

    [Test]
    public void Before_anything_is_set_the_page_is_A4_portrait_with_14_mm_margins_and_a_16_mm_foot()
    {
        var settings = new PrintSettings();

        PrintRules.PaperOf(settings).Should().Be(new PageArea(210m, 297m));
        PrintRules.MarginsOf(settings.Margins).Should().Be(new PageMarginWidths(14m, 14m, 16m));
        PrintRules.PrintedAreaOf(settings).Should().Be(new PageArea(182m, 267m), "what print.css had before it was a setting");
    }

    [Test]
    public void Landscape_turns_the_paper_on_its_side_and_the_margins_come_off_what_is_left()
    {
        var settings = new PrintSettings { PaperSize = PaperSize.LongBond, Orientation = PaperOrientation.Landscape, Margins = PageMargins.Wide };

        PrintRules.PaperOf(settings).Should().Be(new PageArea(330.2m, 215.9m));
        PrintRules.PrintedAreaOf(settings).Should().Be(new PageArea(290.2m, 173.9m), "20 mm off each side, 20 mm off the top and 22 mm off the foot");
    }

    [Test]
    public void A_paper_orientation_or_margins_the_lists_do_not_offer_is_refused()
    {
        var paper = () => PrintRules.EnsureValid(new PrintSettings { PaperSize = (PaperSize)99 });
        var orientation = () => PrintRules.EnsureValid(new PrintSettings { Orientation = (PaperOrientation)99 });
        var margins = () => PrintRules.EnsureValid(new PrintSettings { Margins = (PageMargins)99 });

        paper.Should().Throw<BusinessRuleException>().WithMessage("Choose a paper size from the list.");
        orientation.Should().Throw<BusinessRuleException>().WithMessage("Choose portrait or landscape.");
        margins.Should().Throw<BusinessRuleException>().WithMessage("Choose margins from the list.");
        PrintRules.EnsureValid(new PrintSettings { PaperSize = PaperSize.Legal, Margins = PageMargins.Narrow });
    }

    [Test]
    public void Compact_type_is_a_tenth_smaller_and_large_a_fifth_larger()
    {
        PrintRules.TypeScaleOf(PrintTypeSize.Normal).Should().Be(1m);
        PrintRules.TypeScaleOf(PrintTypeSize.Compact).Should().Be(0.9m);
        PrintRules.TypeScaleOf(PrintTypeSize.Large).Should().Be(1.2m);
        var other = () => PrintRules.TypeScaleOf((PrintTypeSize)99);
        other.Should().Throw<BusinessRuleException>().WithMessage("Choose a type size from the list.");
    }

    [Test]
    public void A_logo_is_a_PNG_or_JPEG_by_its_first_bytes_and_at_most_half_a_megabyte()
    {
        PrintRules.LogoContentTypeOf([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A]).Should().Be("image/png");
        PrintRules.LogoContentTypeOf([0xFF, 0xD8, 0xFF, 0xE0]).Should().Be("image/jpeg");

        var gif = () => PrintRules.LogoContentTypeOf("GIF89a"u8.ToArray());
        var tooBig = () => PrintRules.LogoContentTypeOf([0x89, 0x50, 0x4E, 0x47, .. new byte[PrintRules.MaxLogoBytes]]);

        gif.Should().Throw<BusinessRuleException>().WithMessage("A logo must be a PNG or JPEG picture.");
        tooBig.Should().Throw<BusinessRuleException>().WithMessage("That picture is 513 KB: a logo can be at most 512 KB. Save a smaller copy of it.");
    }

    [Test]
    public void A_name_address_or_note_is_kept_without_spaces_around_it_and_too_long_a_one_is_refused()
    {
        PrintRules.Tidy("  Tinapayan Festival Bakeshoppe ").Should().Be("Tinapayan Festival Bakeshoppe");
        PrintRules.Tidy("   ").Should().BeNull();
        PrintRules.Tidy(null).Should().BeNull();

        var name = () => PrintRules.EnsureValid(new PrintSettings { ShopName = new string('a', 101) });
        var note = () => PrintRules.EnsureValid(new PrintSettings { FooterNote = new string('a', 151) });

        name.Should().Throw<BusinessRuleException>().WithMessage("The shop's name can be at most 100 characters.");
        note.Should().Throw<BusinessRuleException>().WithMessage("The note at the foot of each page can be at most 150 characters.");
    }

    [Test]
    public void A_logo_goes_with_its_own_type_or_not_at_all()
    {
        var mismatched = () => PrintRules.EnsureValid(new PrintSettings { Logo = [0xFF, 0xD8, 0xFF], LogoContentType = "image/png" });
        var typeAlone = () => PrintRules.EnsureValid(new PrintSettings { LogoContentType = "image/png" });

        mismatched.Should().Throw<BusinessRuleException>().WithMessage("The logo's picture and its type don't match.");
        typeAlone.Should().Throw<BusinessRuleException>().WithMessage("A logo's type was given with no picture.");
        PrintRules.EnsureValid(new PrintSettings { Logo = [0xFF, 0xD8, 0xFF], LogoContentType = "image/jpeg" });
    }
}
