using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Tests.Validators;

public class ProductCategoryValidatorTests
{
    private readonly ProductCategoryValidator _validator = new();

    [Test]
    public void Accepts_a_valid_category()
    {
        var result = _validator.Validate(new ProductCategory { Name = "Cakes" });

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public void Rejects_a_missing_name()
    {
        var result = _validator.Validate(new ProductCategory { Name = "" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(ProductCategory.Name));
    }
}
