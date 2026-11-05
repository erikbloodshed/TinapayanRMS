using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// Deleting a raw material (IRawMaterialRepository.DeleteAsync): with its units and prices, and refused while anything
/// uses it, naming each user, as deleting a product names what leans on it. Until 2026-11-05 the foreign keys refused it,
/// and the screen said only "This item is currently in use elsewhere and can't be deleted."
/// </summary>
[Collection(DatabaseCollection.A)]
public class RawMaterialDeletePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private RawMaterialRepository CreateRepository() => new(CreateDbContextFactory());

    private async Task<int> AddPackagingAsync(string name)
    {
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var categoryId = await AddPackagingCategoryAsync($"{name} Packaging");
        await using var dbContext = CreateDbContext();
        var material = new RawMaterial { Type = RawMaterialType.Packaging, Name = name, CategoryId = categoryId, BaseUnitId = pieceId };
        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return material.Id;
    }

    [Fact]
    public async Task An_unused_material_goes_with_its_prices()
    {
        var saltId = await AddIngredientAsync("Salt");
        await SetPriceAsync(saltId, null, 0.02m);

        await CreateRepository().DeleteAsync(saltId, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(m => m.Id == saltId, Cancellation)).Should().BeFalse();
        (await dbContext.RawMaterialBrandPrices.AnyAsync(p => p.RawMaterialId == saltId, Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task An_ingredient_in_use_stays_and_the_refusal_names_each_user_and_how_it_uses_it()
    {
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var vanillaId = await AddIngredientAsync("Vanilla", costPerGram: 1m);
        var doughRoleId = await AddRoleAsync("Dough");
        var (_, doughId) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [(flourId, 1000m)]);

        // Vanilla stirred into a product linking the dough, and into a made ingredient, as their own add-ins.
        var loafId = await AddProductAsync("Vanilla Loaf");
        await SaveRecipeAsync(loafId, new RecipeDraft(
            [], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 30m) { AddIns = [(vanillaId, 5m)] }]));
        var syrupId = await AddMadeIngredientAsync("Syrup", [(flourId, 100m)]);
        var rawMaterials = CreateRepository();
        await rawMaterials.SaveAsync((await rawMaterials.GetByIdAsync(syrupId, Cancellation))!, [], brandPrices: null, [(vanillaId, 10m)], Cancellation);

        var deleteFlour = () => rawMaterials.DeleteAsync(flourId, Cancellation);
        var deleteVanilla = () => rawMaterials.DeleteAsync(vanillaId, Cancellation);

        (await deleteFlour.Should().ThrowAsync<BusinessRuleException>()).Which.Message
            .Should().Be("Flour can't be deleted while it's used: Pandesal Dough (recipe), Syrup Base (recipe). Take it out of them first.");
        (await deleteVanilla.Should().ThrowAsync<BusinessRuleException>()).Which.Message
            .Should().Be("Vanilla can't be deleted while it's used: Syrup (add-in), Vanilla Loaf (add-in). Take it out of them first.");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.CountAsync(m => m.Id == flourId || m.Id == vanillaId, Cancellation)).Should().Be(2);
    }

    [Fact]
    public async Task Packaging_in_use_and_an_ingredient_others_are_linked_to_are_refused_by_name()
    {
        var boxId = await AddPackagingAsync("Box");
        var ribbonId = await AddPackagingAsync("Ribbon");
        var setRoleId = await AddRoleAsync("Packaging", isPackaging: true);
        await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = "Gift Set", RoleId = setRoleId }, [(ribbonId, null, 1m)], Cancellation);
        var cakeId = await AddProductAsync("Cake");
        await new PackagingLineRepository(CreateDbContextFactory()).SetLinesAsync(
            cakeId, [(boxId, null, PackagingLineAuthoredField.QtyPerUnit, 1m)], Cancellation);

        var eggId = await AddIngredientAsync("Egg", costPerGram: 0.1m);
        await using (var dbContext = CreateDbContext())
        {
            var category = await dbContext.RawMaterialCategories.FirstAsync(c => c.Name == "Ingredients", Cancellation);
            dbContext.RawMaterials.Add(new RawMaterial
            {
                Name = "Egg White", Category = category, Source = RawMaterialSource.Linked, LinkedToId = eggId, WeightSharePercent = 60m, CostSharePercent = 50m,
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var repository = CreateRepository();
        (await ((Func<Task>)(() => repository.DeleteAsync(boxId, Cancellation))).Should().ThrowAsync<BusinessRuleException>()).Which.Message
            .Should().Be("Box can't be deleted while it's used: Cake (packaging). Take it out of them first.");
        (await ((Func<Task>)(() => repository.DeleteAsync(ribbonId, Cancellation))).Should().ThrowAsync<BusinessRuleException>()).Which.Message
            .Should().Be("Ribbon can't be deleted while it's used: Gift Set (packaging set). Take it out of them first.");
        (await ((Func<Task>)(() => repository.DeleteAsync(eggId, Cancellation))).Should().ThrowAsync<BusinessRuleException>()).Which.Message
            .Should().Be("Egg can't be deleted while it's used: Egg White (linked to it). Take it out of them first.");
    }
}
