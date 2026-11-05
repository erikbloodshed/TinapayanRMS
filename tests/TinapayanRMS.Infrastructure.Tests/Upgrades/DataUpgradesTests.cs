using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Infrastructure.Upgrades;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Upgrades;

/// <summary>
/// The one-time changes to the data a schema change needs (DataUpgrades): made once on a database as it's migrated, in
/// order, and recorded so they're never made again.
/// </summary>
[Collection(DatabaseCollection.A)]
public class DataUpgradesTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task Migrating_a_database_makes_the_upgrades_it_hasnt_had_and_records_them()
    {
        await using var dbContext = CreateDbContext();

        await dbContext.Database.MigrateAsync(Cancellation);

        (await dbContext.DataUpgrades.Select(u => u.Id).ToListAsync(Cancellation)).Should().Contain("2026-11-05 section names");
    }

    [Fact]
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

    private async Task<string?> SectionNameOfAsync(int subAssemblyId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblies.Where(s => s.Id == subAssemblyId).Select(s => s.SectionName).SingleAsync(Cancellation);
    }
}
