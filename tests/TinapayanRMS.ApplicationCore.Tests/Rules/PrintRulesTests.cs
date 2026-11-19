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
}
