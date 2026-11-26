using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
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
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class DataUpgradesTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task Migrating_a_database_makes_the_upgrades_it_hasnt_had_and_records_them()
    {
        await using var dbContext = CreateDbContext();

        await dbContext.Database.MigrateAsync(Cancellation);

        (await dbContext.DataUpgrades.Select(u => u.Id).ToListAsync(Cancellation)).Should().Contain(
            ["2026-11-05 section names", "2026-11-05 target buffers", "2026-11-09 innovation stage", "2026-11-12 product folders", "2026-11-26 roles into other sections"]);
    }

    [Test]
    public async Task The_roles_into_other_sections_upgrade_ticks_the_roles_already_on_anothers_lines_once()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1, isProductOwned: true);
        var spongeRoleId = await AddRoleAsync("Sponge", sortOrder: 2, isProductOwned: true, canGoIntoOtherSections: true);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 3, isProductOwned: true);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await AddProductAsync("Spanish Bread");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [
                new NewSubAssemblyDraft(doughRoleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 60m)
                {
                    SectionLines = [new SectionLineDraft(RecipeDraft.SectionIdOf(0, 1), 300m, null, 1)],
                },
                new NewSubAssemblyDraft(spongeRoleId, [(flourId, 400m)], UsageAuthoredField.WeightPerPiece, 0m),
                new NewSubAssemblyDraft(fillingRoleId, [(flourId, 100m)], UsageAuthoredField.WeightPerPiece, 10m),
            ],
            []));
        await using (var dbContext = CreateDbContext())
        {
            // As the column came in: unticked everywhere.
            await dbContext.SubAssemblyRoles.ExecuteUpdateAsync(set => set.SetProperty(r => r.CanGoIntoOtherSections, false), Cancellation);
            await DataUpgrades.RunAsync(dbContext, Cancellation);
        }

        (await TickedRolesAsync()).Should().Equal("Sponge");

        // Made once: a role ticked or unticked since stays as it is.
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.SubAssemblyRoles.Where(r => r.Id == fillingRoleId).ExecuteUpdateAsync(set => set.SetProperty(r => r.CanGoIntoOtherSections, true), Cancellation);
            await dbContext.SubAssemblyRoles.Where(r => r.Id == spongeRoleId).ExecuteUpdateAsync(set => set.SetProperty(r => r.CanGoIntoOtherSections, false), Cancellation);
            await DataUpgrades.RunAsync(dbContext, Cancellation);
        }

        (await TickedRolesAsync()).Should().Equal("Filling");
    }

    private async Task<List<string>> TickedRolesAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblyRoles.Where(r => r.CanGoIntoOtherSections).OrderBy(r => r.Name).Select(r => r.Name).ToListAsync(Cancellation);
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

    [Test]
    public async Task The_product_folders_upgrade_files_the_products_in_a_folder_per_category_with_products_in_each_stage()
    {
        await using (var dbContext = CreateDbContext())
        {
            // As saved before folders were the user's own: each category a folder in each stage.
            var breads = new ProductCategory { Name = "Breads" };
            var cakes = new ProductCategory { Name = "Cakes" };
            var empty = new ProductCategory { Name = "Pastries" };
            dbContext.ProductCategories.AddRange(breads, cakes, empty);
            var deletedAt = new DateTime(2026, 11, 1, 9, 0, 0);
            dbContext.Products.AddRange(
                new Product { Name = "Pandesal", Stage = ProductStage.Production, Category = breads },
                new Product { Name = "Ensaymada", Stage = ProductStage.Production, Category = breads },
                new Product { Name = "Test Bun", Stage = ProductStage.Innovation, Category = breads },
                new Product { Name = "Mamon", Stage = ProductStage.Production, Category = cakes },
                new Product { Name = "Loaf", Stage = ProductStage.Production },
                new Product { Name = "Old Bread", Stage = ProductStage.Production, Category = breads, DeletedAt = deletedAt },
                new Product { Name = "Old Cake", Stage = ProductStage.Innovation, Category = cakes, DeletedAt = deletedAt });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await using (var dbContext = CreateDbContext())
        {
            await DataUpgrades.RunAsync(dbContext, Cancellation);
        }

        await using (var dbContext = CreateDbContext())
        {
            var folders = await dbContext.ProductFolders.OrderBy(f => f.Stage).ThenBy(f => f.Name).ToListAsync(Cancellation);
            folders.Select(f => (f.Stage, f.Name)).Should().Equal(
                [(ProductStage.Innovation, "Breads"), (ProductStage.Production, "Breads"), (ProductStage.Production, "Cakes")],
                "a folder for each category with a product in that stage: none for Pastries, none in Innovation for a deleted cake alone");
            folders.Should().OnlyContain(f => f.ParentId == null);

            var products = await dbContext.Products.ToListAsync(Cancellation);
            string? FolderOf(string name) =>
                folders.FirstOrDefault(f => f.Id == products.Single(p => p.Name == name).FolderId) is { } folder ? $"{folder.Stage}/{folder.Name}" : null;

            FolderOf("Pandesal").Should().Be("Production/Breads");
            FolderOf("Ensaymada").Should().Be("Production/Breads");
            FolderOf("Test Bun").Should().Be("Innovation/Breads");
            FolderOf("Mamon").Should().Be("Production/Cakes");
            FolderOf("Loaf").Should().BeNull("an uncategorized product stays at the top of its stage");
            FolderOf("Old Bread").Should().Be("Production/Breads", "a deleted one goes in its category's folder when there's one, for its restore");
            FolderOf("Old Cake").Should().BeNull();
            (await dbContext.DataUpgrades.Select(u => u.Id).ToListAsync(Cancellation)).Should().Contain("2026-11-12 product folders");
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
