using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.SubAssemblies;
using TinapayanRMS.UseCases.Tests.Fixtures;

namespace TinapayanRMS.UseCases.Tests.SubAssemblies;

/// <summary>What the Sub-Assemblies list does to a role: added after the others, renamed in place, refused with why.</summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class SubAssemblyRoleEditorTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task A_role_added_comes_back_with_its_id()
    {
        var added = await CreateEditor().AddAsync("Filling", Cancellation);

        added.Id.Should().BePositive();
        (await new SubAssemblyRoleRepository(CreateDbContextFactory()).GetAllAsync(Cancellation)).Should().ContainSingle(r => r.Id == added.Id && r.Name == "Filling");
    }

    [Test]
    public async Task A_name_the_roles_cannot_take_is_refused()
    {
        await CreateEditor().Awaiting(e => e.AddAsync("", Cancellation)).Should().ThrowAsync<BusinessRuleException>();
    }

    [Test]
    public async Task A_rename_keeps_the_roles_place_and_ownership()
    {
        var roleId = await AddRoleAsync("Dough", sortOrder: 3, isProductOwned: true);
        var repository = new SubAssemblyRoleRepository(CreateDbContextFactory());
        var role = (await repository.GetAllAsync(Cancellation)).Single(r => r.Id == roleId);

        await CreateEditor().RenameAsync(role, "Bread Dough", Cancellation);

        var renamed = (await repository.GetAllAsync(Cancellation)).Single(r => r.Id == roleId);
        (renamed.Name, renamed.SortOrder, renamed.IsProductOwned).Should().Be(("Bread Dough", 3, true));
    }

    private SubAssemblyRoleEditor CreateEditor() =>
        new(new SubAssemblyRoleRepository(CreateDbContextFactory()), new SubAssemblyRoleValidator());
}
