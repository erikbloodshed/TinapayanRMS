using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Infrastructure.Upgrades;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Upgrades;

/// <summary>
/// The one-time changes to the data a schema change needs (DataUpgrades): made once on a database as it's migrated, in
/// order, and recorded so they're never made again.
/// </summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class DataUpgradesTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task Migrating_a_database_makes_the_upgrades_it_hasnt_had_and_records_them()
    {
        await using var dbContext = CreateDbContext();

        await dbContext.Database.MigrateAsync(Cancellation);

        (await dbContext.DataUpgrades.Select(u => u.Id).ToListAsync(Cancellation)).Should().Contain(["2026-11-05 section names", "2026-11-05 target buffers", "2026-11-09 innovation stage"]);
    }

    [Test]
    public async Task The_section_names_upgrade_reads_each_products_own_label_from_its_name_once()
    {
        var fillingRoleId = await AddRoleAsync("Filling", isProductOwned: true);
        var productId = await AddProductAsync("Ensaymada");
        int namedId, plainId, numberedId, keptId, standaloneId;
        await using (var dbContext = CreateDbContext())
        {
            SubAssembly Add(string name, int? ownerId)
            {
                var subAssembly = new SubAssembly { Name = name, RoleId = fillingRoleId, OwnerProductId = ownerId };
                dbContext.SubAssemblies.Add(subAssembly);
                return subAssembly;
            }

            // As they were saved before section names were stored: the label only in the name.
            var named = Add("Ensaymada Filling: Ube", productId);
            var plain = Add("Ensaymada Filling", productId);
            var numbered = Add("Ensaymada Filling 2", productId);
            var kept = Add("Pan de Coco", productId);
            var standalone = Add("Ensaymada Filling: Cheese", null);
            await dbContext.SaveChangesAsync(Cancellation);
            (namedId, plainId, numberedId, keptId, standaloneId) = (named.Id, plain.Id, numbered.Id, kept.Id, standalone.Id);
        }

        await using (var dbContext = CreateDbContext())
        {
            await DataUpgrades.RunAsync(dbContext, Cancellation);
        }

        (await SectionNameOfAsync(namedId)).Should().Be("Ube");
        (await SectionNameOfAsync(plainId)).Should().BeNull();
        (await SectionNameOfAsync(numberedId)).Should().BeNull("the number only kept its name apart");
        (await SectionNameOfAsync(keptId)).Should().Be("Pan de Coco", "it went by all of its name, which it keeps");
        (await SectionNameOfAsync(standaloneId)).Should().BeNull("one on the Sub-Assemblies screen goes by its name");

        // Made once: a section name changed since stays as it is.
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.SubAssemblies.Where(s => s.Id == namedId).ExecuteUpdateAsync(set => set.SetProperty(s => s.SectionName, "Purple Yam"), Cancellation);
            await DataUpgrades.RunAsync(dbContext, Cancellation);
        }

        (await SectionNameOfAsync(namedId)).Should().Be("Purple Yam");
    }

    [Test]
    public async Task The_target_buffers_upgrade_gives_each_purchased_material_without_one_its_buffer_once()
    {
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m, bufferedCostPerGram: 0.055m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var syrupId = await AddMadeIngredientAsync("Syrup", [(sugarId, 1000m)]);
        await using (var dbContext = CreateDbContext())
        {
            // As saved before targets were kept: a buffer, and no target. Sugar has one of its own.
            await dbContext.RawMaterials.Where(m => m.Id == flourId).ExecuteUpdateAsync(
                set => set.SetProperty(m => m.CostBufferPercent, 10m).SetProperty(m => m.TargetCostBufferPercent, (decimal?)null), Cancellation);
            await dbContext.RawMaterials.Where(m => m.Id == sugarId).ExecuteUpdateAsync(
                set => set.SetProperty(m => m.CostBufferPercent, 5m).SetProperty(m => m.TargetCostBufferPercent, 8m), Cancellation);
            await DataUpgrades.RunAsync(dbContext, Cancellation);
        }

        (await TargetOfAsync(flourId)).Should().Be(10m, "what it was read as: its buffer as last saved");
        (await TargetOfAsync(sugarId)).Should().Be(8m, "one it has stays");
        (await TargetOfAsync(syrupId)).Should().BeNull("a made ingredient has no buffer of its own to aim at");

        // Made once: a buffer typed since doesn't move the target with it.
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.RawMaterials.Where(m => m.Id == flourId).ExecuteUpdateAsync(
                set => set.SetProperty(m => m.TargetCostBufferPercent, (decimal?)null).SetProperty(m => m.CostBufferPercent, 12m), Cancellation);
            await DataUpgrades.RunAsync(dbContext, Cancellation);
        }

        (await TargetOfAsync(flourId)).Should().BeNull();
    }

    [Test]
    public async Task The_innovation_stage_upgrade_renames_the_stage_stored_by_its_former_name()
    {
        var testLoafId = await AddProductAsync("Test Loaf");
        var pandesalId = await AddProductAsync("Pandesal");
        await new CostingDefaultsRepository(CreateDbContextFactory()).SetAsync(new CostingDefaults { NewProductStage = ProductStage.Innovation }, Cancellation);
        await using (var dbContext = CreateDbContext())
        {
            // As stored before the rename, or by a copy of the app built before it.
            await dbContext.Database.ExecuteSqlAsync($"UPDATE products SET stage = 'ResearchAndDevelopment' WHERE id = {testLoafId}", Cancellation);
            await dbContext.Database.ExecuteSqlAsync($"UPDATE products SET stage = 'Production' WHERE id = {pandesalId}", Cancellation);
            await dbContext.Database.ExecuteSqlAsync($"UPDATE costing_defaults SET new_product_stage = 'ResearchAndDevelopment'", Cancellation);
        }

        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.Products.SingleAsync(p => p.Id == testLoafId, Cancellation)).Stage.Should().Be(ProductStage.Innovation, "the former name still reads as Innovation");
            (await dbContext.CostingDefaults.SingleAsync(Cancellation)).NewProductStage.Should().Be(ProductStage.Innovation);
            await DataUpgrades.RunAsync(dbContext, Cancellation);
        }

        (await StoredAsync($"SELECT stage AS \"Value\" FROM products WHERE id = {testLoafId}")).Should().Be("Innovation");
        (await StoredAsync($"SELECT stage AS \"Value\" FROM products WHERE id = {pandesalId}")).Should().Be("Production");
        (await StoredAsync($"SELECT new_product_stage AS \"Value\" FROM costing_defaults")).Should().Be("Innovation");
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.Products.CountAsync(p => p.Stage == ProductStage.Innovation, Cancellation)).Should().Be(1, "EF's queries find it by its new name");
        }
    }

    /// <summary>The one text <paramref name="sql"/> reads, as stored.</summary>
    private async Task<string> StoredAsync(FormattableString sql)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.Database.SqlQuery<string>(sql).SingleAsync(Cancellation);
    }

    private async Task<decimal?> TargetOfAsync(int materialId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterials.Where(m => m.Id == materialId).Select(m => m.TargetCostBufferPercent).SingleAsync(Cancellation);
    }

    private async Task<string?> SectionNameOfAsync(int subAssemblyId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblies.Where(s => s.Id == subAssemblyId).Select(s => s.SectionName).SingleAsync(Cancellation);
    }
}
