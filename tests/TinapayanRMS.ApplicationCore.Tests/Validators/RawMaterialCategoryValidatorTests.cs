using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Tests.Validators;

public class RawMaterialCategoryValidatorTests
{
    private readonly RawMaterialCategoryValidator _validator = new();

    [Test]
    public void Accepts_a_valid_category()
    {
        var result = _validator.Validate(new RawMaterialCategory { Name = "Flour" });

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public void Rejects_a_missing_name()
    {
        var result = _validator.Validate(new RawMaterialCategory { Name = "" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RawMaterialCategory.Name));
    }
}
