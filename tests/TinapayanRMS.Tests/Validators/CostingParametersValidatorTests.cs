using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Tests.Validators;

public class CostingParametersValidatorTests
{
    private readonly CostingParametersValidator _validator = new();

    [Fact]
    public void Accepts_zeroes()
    {
        _validator.Validate(new CostingParameters()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1, 0, 0, 0, 0)]
    [InlineData(0, -1, 0, 0, 0)]
    [InlineData(0, 0, -1, 0, 0)]
    [InlineData(0, 0, 0, -1, 0)]
    [InlineData(0, 0, 0, 0, -1)]
    public void Rejects_any_negative_field(decimal laborCost, decimal overheadCost, decimal administrativeCost, decimal marginPercent, decimal vatPercent)
    {
        var result = _validator.Validate(new CostingParameters
        {
            LaborCost = laborCost,
            OverheadCost = overheadCost,
            AdministrativeCost = administrativeCost,
            MarginPercent = marginPercent,
            VatPercent = vatPercent,
        });

        result.IsValid.Should().BeFalse();
    }
}
