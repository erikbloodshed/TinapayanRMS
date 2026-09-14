using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Tests.Validators;

public class RawMaterialValidatorTests
{
    private readonly RawMaterialValidator _validator = new();

    [Fact]
    public void Accepts_a_valid_purchased_ingredient()
    {
        var result = _validator.Validate(new RawMaterial
        {
            Type = RawMaterialType.Ingredient,
            Name = "Sugar",
            CategoryId = 1,
            Source = RawMaterialSource.Purchased,
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Accepts_a_valid_made_ingredient()
    {
        var result = _validator.Validate(new RawMaterial
        {
            Type = RawMaterialType.Ingredient,
            Name = "Simple Syrup",
            CategoryId = 1,
            Source = RawMaterialSource.Made,
            WeightLossPercent = 20m,
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Accepts_a_valid_packaging_material()
    {
        var result = _validator.Validate(new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = "Satin Ribbon",
            CategoryId = 1,
            BaseUnitId = 1,
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_missing_name()
    {
        var result = _validator.Validate(new RawMaterial
        {
            Name = "",
            CategoryId = 1,
            Source = RawMaterialSource.Made,
            WeightLossPercent = 0m,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RawMaterial.Name));
    }

    [Fact]
    public void Rejects_a_missing_category()
    {
        var result = _validator.Validate(new RawMaterial
        {
            Name = "Sugar",
            CategoryId = 0,
            Source = RawMaterialSource.Purchased,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RawMaterial.CategoryId));
    }

    [Fact]
    public void Rejects_a_made_ingredient_with_an_out_of_range_weight_loss_percent()
    {
        var result = _validator.Validate(new RawMaterial
        {
            Name = "Simple Syrup",
            CategoryId = 1,
            Source = RawMaterialSource.Made,
            WeightLossPercent = 100m,
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_a_made_ingredient_without_a_weight_loss_percent()
    {
        var result = _validator.Validate(new RawMaterial
        {
            Name = "Simple Syrup",
            CategoryId = 1,
            Source = RawMaterialSource.Made,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RawMaterial.WeightLossPercent));
    }

    [Fact]
    public void Rejects_a_purchased_material_with_a_weight_loss_percent()
    {
        var result = _validator.Validate(new RawMaterial
        {
            Name = "Sugar",
            CategoryId = 1,
            Source = RawMaterialSource.Purchased,
            WeightLossPercent = 10m,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RawMaterial.WeightLossPercent));
    }

    [Fact]
    public void Rejects_an_ingredient_with_a_base_unit_since_it_is_always_the_gram()
    {
        var result = _validator.Validate(new RawMaterial
        {
            Type = RawMaterialType.Ingredient,
            Name = "Sugar",
            CategoryId = 1,
            BaseUnitId = 1,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RawMaterial.BaseUnitId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Rejects_a_packaging_material_without_a_base_unit(int? baseUnitId)
    {
        var result = _validator.Validate(new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = "Box",
            CategoryId = 1,
            BaseUnitId = baseUnitId,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(RawMaterial.BaseUnitId))
            .Which.ErrorMessage.Should().Be("Base unit is required.");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Rejects_a_cost_buffer_outside_0_to_100_percent(decimal bufferPercent)
    {
        var result = _validator.Validate(new RawMaterial
        {
            Type = RawMaterialType.Ingredient,
            Name = "Flour",
            CategoryId = 1,
            Source = RawMaterialSource.Purchased,
            CostBufferPercent = bufferPercent,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(RawMaterial.CostBufferPercent));
    }

    [Fact]
    public void Accepts_a_cost_buffer_of_100_percent()
    {
        var result = _validator.Validate(new RawMaterial
        {
            Type = RawMaterialType.Ingredient,
            Name = "Flour",
            CategoryId = 1,
            Source = RawMaterialSource.Purchased,
            CostBufferPercent = 100m,
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_made_packaging_material()
    {
        var result = _validator.Validate(new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = "Box",
            CategoryId = 1,
            BaseUnitId = 1,
            Source = RawMaterialSource.Made,
            WeightLossPercent = 0m,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RawMaterial.Source));
    }
}
