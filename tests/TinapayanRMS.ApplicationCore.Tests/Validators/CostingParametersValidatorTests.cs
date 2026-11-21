using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Tests.Validators;

public class CostingParametersValidatorTests
{
    private readonly CostingParametersValidator _validator = new();

    [Test]
    public void Accepts_zeroes()
    {
        _validator.Validate(new CostingParameters()).IsValid.Should().BeTrue();
    }

    [Test]
    [Arguments(-1, 0, 0, 0)]
    [Arguments(0, -1, 0, 0)]
    [Arguments(0, 0, -1, 0)]
    [Arguments(0, 0, 0, -1)]
    public void Rejects_any_negative_field(decimal laborPercent, decimal overheadPercent, decimal administrativePercent, decimal markupPercent)
    {
        var result = _validator.Validate(new CostingParameters
        {
            LaborPercent = laborPercent,
            OverheadPercent = overheadPercent,
            AdministrativePercent = administrativePercent,
            MarkupPercent = markupPercent,
        });

        result.IsValid.Should().BeFalse();
    }

    [Test]
    [Arguments(-1, 0, 0, 0)]
    [Arguments(0, 0, 0, -1)]
    public void Says_what_saving_would(decimal laborPercent, decimal overheadPercent, decimal administrativePercent, decimal markupPercent)
    {
        var saving = () => CostingRules.EnsureValidParameters(laborPercent, overheadPercent, administrativePercent, markupPercent);

        var result = _validator.Validate(new CostingParameters
        {
            LaborPercent = laborPercent,
            OverheadPercent = overheadPercent,
            AdministrativePercent = administrativePercent,
            MarkupPercent = markupPercent,
        });

        result.Errors.Should().ContainSingle().Which.ErrorMessage
            .Should().Be(saving.Should().Throw<BusinessRuleException>().Which.Message);
    }
}
