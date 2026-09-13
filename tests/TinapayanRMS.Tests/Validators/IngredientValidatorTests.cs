using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Tests.Validators;

public class IngredientValidatorTests
{
    private readonly IngredientValidator _validator = new();

    [Fact]
    public void Accepts_a_valid_purchased_ingredient()
    {
        var result = _validator.Validate(new Ingredient
        {
            Name = "Sugar",
            IngredientCategoryId = 1,
            Source = IngredientSource.Purchased,
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Accepts_a_valid_made_ingredient()
    {
        var result = _validator.Validate(new Ingredient
        {
            Name = "Simple Syrup",
            IngredientCategoryId = 1,
            Source = IngredientSource.Made,
            WeightLossPercent = 20m,
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_missing_name()
    {
        var result = _validator.Validate(new Ingredient
        {
            Name = "",
            IngredientCategoryId = 1,
            Source = IngredientSource.Made,
            WeightLossPercent = 0m,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(Ingredient.Name));
    }

    [Fact]
    public void Rejects_a_made_ingredient_with_an_out_of_range_weight_loss_percent()
    {
        var result = _validator.Validate(new Ingredient
        {
            Name = "Simple Syrup",
            IngredientCategoryId = 1,
            Source = IngredientSource.Made,
            WeightLossPercent = 100m,
        });

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Rejects_a_made_ingredient_without_a_weight_loss_percent()
    {
        var result = _validator.Validate(new Ingredient
        {
            Name = "Simple Syrup",
            IngredientCategoryId = 1,
            Source = IngredientSource.Made,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(Ingredient.WeightLossPercent));
    }

    [Fact]
    public void Rejects_a_missing_category()
    {
        var result = _validator.Validate(new Ingredient
        {
            Name = "Sugar",
            IngredientCategoryId = 0,
            Source = IngredientSource.Purchased,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(Ingredient.IngredientCategoryId));
    }

    [Fact]
    public void Rejects_a_purchased_ingredient_with_a_weight_loss_percent()
    {
        var result = _validator.Validate(new Ingredient
        {
            Name = "Sugar",
            IngredientCategoryId = 1,
            Source = IngredientSource.Purchased,
            WeightLossPercent = 10m,
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(Ingredient.WeightLossPercent));
    }
}
