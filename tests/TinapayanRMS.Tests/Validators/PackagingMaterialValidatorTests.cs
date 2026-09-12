using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain;

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
            StandardUnitId = 1,
            PurchaseUnitId = 2,
            PurchaseUnitQty = 1000m,
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_missing_standard_unit()
    {
        var result = _validator.Validate(new PackagingMaterial
        {
            Name = "Invalid",
            PackagingCategoryId = 1,
            StandardUnitId = 0,
            PurchaseUnitId = 2,
            PurchaseUnitQty = 1m,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(PackagingMaterial.StandardUnitId));
    }

    [Fact]
    public void Rejects_a_non_positive_purchase_unit_quantity()
    {
        var result = _validator.Validate(new PackagingMaterial
        {
            Name = "Box",
            PackagingCategoryId = 1,
            StandardUnitId = 1,
            PurchaseUnitId = 2,
            PurchaseUnitQty = 0m,
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_a_missing_category()
    {
        var result = _validator.Validate(new PackagingMaterial
        {
            Name = "Box",
            PackagingCategoryId = 0,
            StandardUnitId = 1,
            PurchaseUnitId = 2,
            PurchaseUnitQty = 100m,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(PackagingMaterial.PackagingCategoryId));
    }

    [Fact]
    public void Rejects_a_missing_purchase_unit()
    {
        var result = _validator.Validate(new PackagingMaterial
        {
            Name = "Box",
            PackagingCategoryId = 1,
            StandardUnitId = 1,
            PurchaseUnitId = 0,
            PurchaseUnitQty = 100m,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(PackagingMaterial.PurchaseUnitId));
    }
}
