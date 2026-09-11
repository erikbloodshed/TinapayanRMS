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
            StandardUnitId = WellKnownUnitOfMeasureIds.Centimeter,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitQty = 1000m,
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_gram_as_a_standard_unit()
    {
        var result = _validator.Validate(new PackagingMaterial
        {
            Name = "Invalid",
            PackagingCategoryId = 1,
            StandardUnitId = WellKnownUnitOfMeasureIds.Gram,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
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
            StandardUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitQty = 0m,
        });

        result.IsValid.Should().BeFalse();
    }
}
