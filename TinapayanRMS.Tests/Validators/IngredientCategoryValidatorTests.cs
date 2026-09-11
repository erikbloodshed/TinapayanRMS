using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Tests.Validators;

public class IngredientCategoryValidatorTests
{
    private readonly IngredientCategoryValidator _validator = new();

    [Fact]
    public void Accepts_a_valid_category()
    {
        var result = _validator.Validate(new IngredientCategory { Name = "Flour" });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_missing_name()
    {
        var result = _validator.Validate(new IngredientCategory { Name = "" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(IngredientCategory.Name));
    }
}
