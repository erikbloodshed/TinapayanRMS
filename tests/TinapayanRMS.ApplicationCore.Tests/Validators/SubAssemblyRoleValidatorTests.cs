using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Tests.Validators;

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

    [Fact]
    public void Rejects_a_product_owned_packaging_role_and_accepts_any_other()
    {
        _validator.Validate(new SubAssemblyRole { Name = "Packaging", IsPackaging = true, IsProductOwned = true }).IsValid.Should().BeFalse();
        _validator.Validate(new SubAssemblyRole { Name = "Filling", IsProductOwned = true }).IsValid.Should().BeTrue();
    }
}
