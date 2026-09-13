using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Tests.Validators;

public class IngredientUnitValidatorTests
{
    private readonly IngredientUnitValidator _validator = new();

    [Fact]
    public void Accepts_a_unit_with_a_positive_multiplier()
    {
        _validator.Validate(new IngredientUnit { UnitOfMeasureId = 1, GramsPerUnit = 25000m }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_missing_unit()
    {
        var result = _validator.Validate(new IngredientUnit { UnitOfMeasureId = 0, GramsPerUnit = 25000m });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(IngredientUnit.UnitOfMeasureId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_multiplier_that_is_not_positive(decimal gramsPerUnit)
    {
        var result = _validator.Validate(new IngredientUnit { UnitOfMeasureId = 1, GramsPerUnit = gramsPerUnit });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(IngredientUnit.GramsPerUnit));
    }
}
