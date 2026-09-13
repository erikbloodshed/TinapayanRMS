using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Tests.Validators;

public class PackagingMaterialValidatorTests
{
    private readonly PackagingMaterialValidator _validator = new();

    [Fact]
    public void Accepts_a_valid_packaging_material()
    {
        var result = _validator.Validate(new PackagingMaterial
        {
            Name = "Satin Ribbon",
            PackagingCategoryId = 1,
            BaseUnitId = 1,
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_missing_base_unit()
    {
        var result = _validator.Validate(new PackagingMaterial
        {
            Name = "Invalid",
            PackagingCategoryId = 1,
            BaseUnitId = 0,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(PackagingMaterial.BaseUnitId));
    }

    [Fact]
    public void Rejects_a_missing_name()
    {
        var result = _validator.Validate(new PackagingMaterial
        {
            Name = "",
            PackagingCategoryId = 1,
            BaseUnitId = 1,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(PackagingMaterial.Name));
    }

    [Fact]
    public void Rejects_a_missing_category()
    {
        var result = _validator.Validate(new PackagingMaterial
        {
            Name = "Box",
            PackagingCategoryId = 0,
            BaseUnitId = 1,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(PackagingMaterial.PackagingCategoryId));
    }
}
