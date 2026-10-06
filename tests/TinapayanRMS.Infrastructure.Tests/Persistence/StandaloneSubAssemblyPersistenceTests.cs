using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// Sub-assemblies as the Sub-Assemblies screen makes and edits them: saved and deleted on their own
/// (SubAssemblyRepository), linked or copied by products, kept linked by a clone, and repriced by the ingredient
/// cost cascade.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class StandaloneSubAssemblyPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private SubAssemblyRepository CreateRepository() => new(CreateDbContextFactory());

    private ProductSubAssemblyUsageRepository CreateUsageRepository() => new(CreateDbContextFactory());

    private async Task<SubAssembly> GetSubAssemblyAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == id, Cancellation);
    }

    private Task<int> SaveStandaloneAsync(string name, int roleId, IReadOnlyList<RecipeLine> lines, int id = 0) =>
        CreateRepository().SaveStandaloneAsync(new SubAssembly { Id = id, Name = name, RoleId = roleId }, lines, Cancellation);

    private Task LinkAsync(int productId, int subAssemblyId, decimal weightPerPiece) =>
        SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(subAssemblyId, UsageAuthoredField.WeightPerPiece, weightPerPiece)]));

    [Fact]
    public async Task Saving_a_new_sub_assembly_trims_its_name_and_computes_its_totals()
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var cocoaId = await AddIngredientAsync("Cocoa", costPerGram: 0.40m);

        var id = await SaveStandaloneAsync(" Chocolate Glaze ", roleId, [(sugarId, 800m), (cocoaId, 200m)]);

        var subAssembly = await GetSubAssemblyAsync(id);
        subAssembly.Name.Should().Be("Chocolate Glaze");
        subAssembly.TotalWeight.Should().Be(1000m);
        subAssembly.TotalCost.Should().Be(152m); // 800*0.09 + 200*0.40
        (await CreateRepository().GetStandaloneAsync(Cancellation)).Should().ContainSingle().Which.Role!.Name.Should().Be("Glaze");
    }

    [Fact]
    public async Task Resaving_renames_moves_and_relines_it()
    {
        var glazeRoleId = await AddRoleAsync("Glaze");
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 2);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var id = await SaveStandaloneAsync("Glaze", glazeRoleId, [(sugarId, 100m)]);

        await SaveStandaloneAsync("Sugar Topping", toppingRoleId, [(sugarId, 500m)], id);

        var subAssembly = await GetSubAssemblyAsync(id);
        subAssembly.Name.Should().Be("Sugar Topping");
        subAssembly.RoleId.Should().Be(toppingRoleId);
        subAssembly.TotalWeight.Should().Be(500m);
        subAssembly.TotalCost.Should().Be(45m);
        (await new SubAssemblyLineRepository(CreateDbContextFactory()).GetForSubAssembliesAsync([id], Cancellation))
            .Should().ContainSingle().Which.Grams.Should().Be(500m);
    }

    [Fact]
    public async Task Two_sub_assemblies_cant_share_a_name_whether_made_here_or_saved_from_a_product()
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        await SaveStandaloneAsync("Glaze", roleId, [(sugarId, 100m)]);
        await AddProductWithSubAssemblyAsync("Cream Loaf", roleId, [(sugarId, 100m)]);

        var act = () => SaveStandaloneAsync(" glaze ", roleId, [(sugarId, 50m)]);
        var productsName = () => SaveStandaloneAsync("cream loaf glaze", roleId, [(sugarId, 50m)]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("There's already a sub-assembly named 'glaze'*");
        await productsName.Should().ThrowAsync<InvalidOperationException>().WithMessage("There's already a sub-assembly named 'cream loaf glaze'*");
    }

    [Fact]
    public async Task A_new_role_for_a_sub_assembly_is_refused_only_where_a_product_linking_it_has_one_by_its_name()
    {
        var glazeRoleId = await AddRoleAsync("Glaze");
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 2);
        var sugarId = await AddIngredientAsync("Sugar");
        var glazeId = await SaveStandaloneAsync("Glaze", glazeRoleId, [(sugarId, 100m)]);
        var productId = await AddProductAsync("Cream Loaf");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(toppingRoleId, [(sugarId, 50m)], UsageAuthoredField.WeightPerPiece, 10m, SectionName: "Glaze")],
            [new LinkedUsageDraft(glazeId, UsageAuthoredField.WeightPerPiece, 5m)]));
        var toppingId = await SubAssemblyIdAsync(productId, toppingRoleId);

        // Cream Loaf's own Topping was saved as "Cream Loaf Topping: Glaze", which reads "Topping: Glaze" there.
        var act = () => SaveStandaloneAsync("Glaze", toppingRoleId, [(sugarId, 100m)], glazeId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cream Loaf, which links this sub-assembly, has two Topping sub-assemblies called Glaze*");
        (await GetSubAssemblyAsync(glazeId)).RoleId.Should().Be(glazeRoleId);

        // Renamed, Cream Loaf's Topping goes by its whole name, so Cream Loaf can tell them apart.
        await SaveStandaloneAsync("Cream Loaf Topping", toppingRoleId, [(sugarId, 50m)], toppingId);
        await SaveStandaloneAsync("Glaze", toppingRoleId, [(sugarId, 100m)], glazeId);
        (await GetSubAssemblyAsync(glazeId)).RoleId.Should().Be(toppingRoleId);
    }

    [Fact]
    public async Task A_standalone_sub_assembly_needs_a_line()
    {
        var roleId = await AddRoleAsync("Glaze");

        var act = () => SaveStandaloneAsync("Glaze", roleId, []);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*at least one line*");
        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.AnyAsync(Cancellation)).Should().BeFalse("a rejected save rolls back");
    }

    [Fact]
    public async Task A_product_links_a_standalone_sub_assembly_at_its_own_piece_size_and_follows_its_lines()
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var glazeId = await SaveStandaloneAsync("Glaze", roleId, [(sugarId, 100m)]);
        var productId = await AddProductAsync("Cream Loaf");

        await LinkAsync(productId, glazeId, 5m);
        await SaveStandaloneAsync("Glaze", roleId, [(sugarId, 200m)], glazeId);

        var usage = (await CreateUsageRepository().GetForProductAsync(productId, Cancellation)).Should().ContainSingle().Subject;
        usage.SubAssemblyId.Should().Be(glazeId);
        usage.AuthoredValue.Should().Be(5m);
        usage.SubAssembly!.TotalWeight.Should().Be(200m, "a link reads the sub-assembly as it's saved now");
        (await CreateRepository().GetStandaloneLinkedByAsync(Cancellation))[glazeId].Select(p => p.Name).Should().Equal("Cream Loaf");
    }

    [Fact]
    public async Task Used_By_names_each_linking_products_category_for_its_icon()
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var glazeId = await SaveStandaloneAsync("Glaze", roleId, [(sugarId, 100m)]);
        var creamLoafId = await AddProductAsync("Cream Loaf");
        var ubeRollId = await AddProductAsync("Ube Roll");
        await using (var dbContext = CreateDbContext())
        {
            var breads = new ProductCategory { Name = "Breads" };
            dbContext.ProductCategories.Add(breads);
            await dbContext.SaveChangesAsync(Cancellation);
            await dbContext.Products.Where(p => p.Id == creamLoafId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, breads.Id), Cancellation);
        }

        await LinkAsync(creamLoafId, glazeId, 5m);
        await LinkAsync(ubeRollId, glazeId, 5m);

        (await CreateRepository().GetStandaloneLinkedByAsync(Cancellation))[glazeId].Should().Equal(
            new LinkingProduct(creamLoafId, "Cream Loaf", "Breads"),
            new LinkingProduct(ubeRollId, "Ube Roll", null));
    }

    [Fact]
    public async Task Deleting_an_unused_standalone_sub_assembly_takes_its_lines()
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var glazeId = await SaveStandaloneAsync("Glaze", roleId, [(sugarId, 100m)]);

        await CreateRepository().DeleteStandaloneAsync(glazeId, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.SubAssemblies.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.SubAssemblyLines.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Deleting_a_linked_standalone_sub_assembly_is_refused_naming_the_product()
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var glazeId = await SaveStandaloneAsync("Glaze", roleId, [(sugarId, 100m)]);
        await LinkAsync(await AddProductAsync("Cream Loaf"), glazeId, 5m);

        var act = () => CreateRepository().DeleteStandaloneAsync(glazeId, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Cream Loaf links Glaze.*");
        (await GetSubAssemblyAsync(glazeId)).Name.Should().Be("Glaze");
    }

    [Fact]
    public async Task Deleting_a_product_permanently_that_links_a_standalone_sub_assembly_leaves_it()
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar");
        var glazeId = await SaveStandaloneAsync("Glaze", roleId, [(sugarId, 100m)]);
        var productId = await AddProductAsync("Cream Loaf");
        await LinkAsync(productId, glazeId, 5m);

        await new ProductRepository(CreateDbContextFactory()).DeletePermanentlyAsync(productId, Cancellation);

        (await GetSubAssemblyAsync(glazeId)).Name.Should().Be("Glaze");
        (await CreateRepository().GetStandaloneLinkedByAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task Linkable_sub_assemblies_are_listed_by_name()
    {
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour");
        await SaveStandaloneAsync("Sweet Dough", roleId, [(flourId, 1000m)]);
        await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 1000m)]);

        var linkable = await CreateRepository().GetLinkableAsync(Cancellation);

        linkable.Select(s => s.Name).Should().Equal("Pandesal Dough", "Sweet Dough");
    }

    [Fact]
    public async Task A_clone_links_every_sub_assembly_its_source_does()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var glazeRoleId = await AddRoleAsync("Glaze", sortOrder: 2);
        var flourId = await AddIngredientAsync("Flour");
        var sugarId = await AddIngredientAsync("Sugar");
        var glazeId = await SaveStandaloneAsync("Glaze", glazeRoleId, [(sugarId, 100m)]);
        var productId = await AddProductAsync("Cream Loaf");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 60m)],
            [new LinkedUsageDraft(glazeId, UsageAuthoredField.Yield, 20m)]));
        var doughId = await SubAssemblyIdAsync(productId, doughRoleId);

        var cloneId = await new ProductCloner(CreateDbContextFactory()).CloneAsync(productId, "Cream Loaf Mini", Cancellation);

        var usages = await CreateUsageRepository().GetForProductAsync(cloneId, Cancellation);
        usages.Select(u => (u.SubAssemblyId, u.AuthoredField, u.AuthoredValue)).Should().BeEquivalentTo(new[]
        {
            (doughId, UsageAuthoredField.WeightPerPiece, 60m),
            (glazeId, UsageAuthoredField.Yield, 20m),
        });
        var linkedBy = await CreateRepository().GetStandaloneLinkedByAsync(Cancellation);
        linkedBy[glazeId].Select(p => p.Name).Should().Equal("Cream Loaf", "Cream Loaf Mini");
        linkedBy[doughId].Select(p => p.Name).Should().Equal("Cream Loaf", "Cream Loaf Mini");
    }

    [Fact]
    public async Task A_clone_keeps_a_standalone_links_add_ins()
    {
        var glazeRoleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);
        var glazeId = await SaveStandaloneAsync("Glaze", glazeRoleId, [(sugarId, 100m)]);
        var productId = await AddProductAsync("Cream Loaf");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [],
            [new LinkedUsageDraft(glazeId, UsageAuthoredField.WeightPerPiece, 5m) { AddIns = [(ubeId, 20m)] }]));

        var cloneId = await new ProductCloner(CreateDbContextFactory()).CloneAsync(productId, "Cream Loaf Mini", Cancellation);

        // Still a link, so the extras stay extras: the clone puts the same ube in its own glaze.
        await using var dbContext = CreateDbContext();
        var addIns = await dbContext.UsageAddInLines.AsNoTracking()
            .Where(l => l.Usage!.ProductId == cloneId)
            .ToListAsync(Cancellation);
        addIns.Select(l => (l.IngredientId, l.Grams)).Should().Equal((ubeId, 20m));
        (await dbContext.SubAssemblyLines.CountAsync(l => l.SubAssemblyId == glazeId, Cancellation))
            .Should().Be(1, "the shared glaze itself is untouched");
    }

    [Fact]
    public async Task A_clone_keeps_the_add_ins_on_a_link_to_a_sub_assembly_another_product_saved()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var ubeId = await AddIngredientAsync("Ube Flavour", costPerGram: 0.8m);
        var (_, doughId) = await AddProductWithSubAssemblyAsync(
            "Cream Loaf", doughRoleId, [(flourId, 1000m)], weightPerPiece: 60m);
        var linkerId = await AddProductAsync("Cream Loaf Ube");
        await SaveRecipeAsync(linkerId, new RecipeDraft(
            [],
            [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m) { AddIns = [(ubeId, 20m)] }]));

        var cloneId = await new ProductCloner(CreateDbContextFactory()).CloneAsync(linkerId, "Cream Loaf Ube Mini", Cancellation);

        (await CreateUsageRepository().GetForProductAsync(cloneId, Cancellation)).Should().ContainSingle().Which.SubAssemblyId.Should().Be(doughId);
        await using var dbContext = CreateDbContext();
        (await dbContext.UsageAddInLines.AsNoTracking().Where(l => l.Usage!.ProductId == cloneId).Select(l => new { l.IngredientId, l.Grams }).ToListAsync(Cancellation))
            .Should().ContainSingle().Which.Should().Be(new { IngredientId = ubeId, Grams = 20m });
        (await dbContext.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == doughId, Cancellation))
            .TotalWeight.Should().Be(1000m, "the dough itself is untouched");
    }

    [Fact]
    public async Task Repricing_an_ingredient_reprices_a_standalone_sub_assembly()
    {
        var roleId = await AddRoleAsync("Glaze");
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var glazeId = await SaveStandaloneAsync("Glaze", roleId, [(sugarId, 1000m)]);

        await SetPriceAndRecalculateBufferAsync(sugarId, null, 0.18m);

        (await GetSubAssemblyAsync(glazeId)).TotalCost.Should().Be(180m);
    }
}
