using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Tests.Validators;

public class PackagingMaterialUnitValidatorTests
{
    private readonly PackagingMaterialUnitValidator _validator = new();

    [Fact]
    public void Accepts_a_unit_with_a_positive_multiplier()
    {
        _validator.Validate(new PackagingMaterialUnit { UnitOfMeasureId = 1, BaseUnitsPerUnit = 1000m }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_missing_unit()
    {
        var result = _validator.Validate(new PackagingMaterialUnit { UnitOfMeasureId = 0, BaseUnitsPerUnit = 1000m });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(PackagingMaterialUnit.UnitOfMeasureId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_multiplier_that_is_not_positive(decimal baseUnitsPerUnit)
    {
        var result = _validator.Validate(new PackagingMaterialUnit { UnitOfMeasureId = 1, BaseUnitsPerUnit = baseUnitsPerUnit });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(PackagingMaterialUnit.BaseUnitsPerUnit));
    }
}
