using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Tests.Validators;

public class SubAssemblyValidatorTests
{
    private readonly SubAssemblyValidator _validator = new();

    [Test]
    public void Accepts_a_sub_assembly_with_a_name_and_role()
    {
        _validator.Validate(new SubAssembly { Name = "Bread Dough", RoleId = 1 }).IsValid.Should().BeTrue();
    }

    [Test]
    public void Rejects_a_missing_name()
    {
        _validator.Validate(new SubAssembly { Name = "", RoleId = 1 }).IsValid.Should().BeFalse();
    }

    [Test]
    public void Rejects_a_missing_role()
    {
        _validator.Validate(new SubAssembly { Name = "Bread Dough", RoleId = 0 }).IsValid.Should().BeFalse();
    }
}
