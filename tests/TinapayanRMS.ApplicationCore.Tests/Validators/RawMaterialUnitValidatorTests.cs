using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Tests.Validators;

public class RawMaterialUnitValidatorTests
{
    private readonly RawMaterialUnitValidator _validator = new();

    [Test]
    [Arguments(25000)] // a 25 kg bag of flour
    [Arguments(1000)] // a 10 m roll of ribbon, in centimeters
    public void Accepts_a_unit_with_a_positive_multiplier(decimal baseUnitsPerUnit)
    {
        _validator.Validate(new RawMaterialUnit { UnitOfMeasureId = 1, BaseUnitsPerUnit = baseUnitsPerUnit }).IsValid.Should().BeTrue();
    }

    [Test]
    public void Rejects_a_missing_unit()
    {
        var result = _validator.Validate(new RawMaterialUnit { UnitOfMeasureId = 0, BaseUnitsPerUnit = 25000m });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RawMaterialUnit.UnitOfMeasureId));
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public void Rejects_a_multiplier_that_is_not_positive(decimal baseUnitsPerUnit)
    {
        var result = _validator.Validate(new RawMaterialUnit { UnitOfMeasureId = 1, BaseUnitsPerUnit = baseUnitsPerUnit });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RawMaterialUnit.BaseUnitsPerUnit));
    }
}
