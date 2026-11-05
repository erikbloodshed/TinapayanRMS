using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Tests.Validators;

public class CostingParametersValidatorTests
{
    private readonly CostingParametersValidator _validator = new();

    [Fact]
    public void Accepts_zeroes()
    {
        _validator.Validate(new CostingParameters()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void Rejects_any_negative_field(decimal laborPercent, decimal overheadPercent, decimal administrativePercent, decimal marginPercent)
    {
        var result = _validator.Validate(new CostingParameters
        {
            LaborPercent = laborPercent,
            OverheadPercent = overheadPercent,
            AdministrativePercent = administrativePercent,
            MarginPercent = marginPercent,
        });

        result.IsValid.Should().BeFalse();
    }
}
