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

    [Fact]
    public void Names_the_packaging_role_by_its_own_name_as_the_save_does()
    {
        // The validator had its own copy of the message, which called every such role "the Packaging role".
        var result = _validator.Validate(new SubAssemblyRole { Name = "Boxes", IsPackaging = true, IsProductOwned = true });

        result.Errors.Should().ContainSingle().Which.ErrorMessage
            .Should().Be("The Boxes role holds packaging sets from the Sub-Assemblies screen, so it can't be product-owned.");
    }
}
