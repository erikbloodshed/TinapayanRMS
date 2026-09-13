using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Tests.Validators;

public class UnitOfMeasureValidatorTests
{
    private readonly UnitOfMeasureValidator _validator = new();

    [Fact]
    public void Accepts_a_valid_unit()
    {
        var result = _validator.Validate(new UnitOfMeasure { Name = "bag", Abbreviation = "bag" });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_missing_name()
    {
        var result = _validator.Validate(new UnitOfMeasure { Name = "", Abbreviation = "bag" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UnitOfMeasure.Name));
    }

    [Fact]
    public void Rejects_a_missing_abbreviation()
    {
        var result = _validator.Validate(new UnitOfMeasure { Name = "bag", Abbreviation = "" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UnitOfMeasure.Abbreviation));
    }

    [Fact]
    public void Accepts_a_name_with_an_uppercase_letter()
    {
        // Casing is auto-corrected by the repository (UnitOfMeasureRules.NormalizeName),
        // not rejected here -- the validator only checks that a name is present at all.
        var result = _validator.Validate(new UnitOfMeasure { Name = "Bag", Abbreviation = "bag" });

        result.IsValid.Should().BeTrue();
    }
}
