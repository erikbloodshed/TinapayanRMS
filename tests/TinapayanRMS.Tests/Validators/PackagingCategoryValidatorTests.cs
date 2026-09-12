using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Tests.Validators;

public class PackagingCategoryValidatorTests
{
    private readonly PackagingCategoryValidator _validator = new();

    [Fact]
    public void Accepts_a_valid_category()
    {
        var result = _validator.Validate(new PackagingCategory { Name = "Ribbons" });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_missing_name()
    {
        var result = _validator.Validate(new PackagingCategory { Name = "" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(PackagingCategory.Name));
    }
}
