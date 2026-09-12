using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Tests.Validators;

public class SubAssemblyRoleValidatorTests
{
    private readonly SubAssemblyRoleValidator _validator = new();

    [Fact]
    public void Accepts_a_role_with_a_name()
    {
        _validator.Validate(new SubAssemblyRole { Name = "Glaze", SortOrder = 6 }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_missing_name()
    {
        _validator.Validate(new SubAssemblyRole { Name = "", SortOrder = 1 }).IsValid.Should().BeFalse();
    }
}
