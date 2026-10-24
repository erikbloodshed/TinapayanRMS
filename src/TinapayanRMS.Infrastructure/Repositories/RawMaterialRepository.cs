using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Queries;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Repositories;

public class RawMaterialRepository(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory) : IRawMaterialRepository
{
    public Task<IReadOnlyList<RawMaterial>> GetAllAsync(CancellationToken cancellationToken) =>
        dbContextFactory.ReadAsync(
            dbContext => RawMaterialReads.GetWithDetailsAsync(dbContext, "ORDER BY m.name", null, cancellationToken), cancellationToken);

    public async Task<RawMaterial?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        (await dbContextFactory.ReadAsync(
            dbContext => RawMaterialReads.GetWithDetailsAsync(dbContext, "WHERE m.id = @id", new { id }, cancellationToken), cancellationToken))
        .SingleOrDefault();

    public async Task<IReadOnlyList<LinkingProduct>> GetUsedByAsync(int id, CancellationToken cancellationToken) =>
        [
            .. (await dbContextFactory.QueryAsync<UsedByRow>(UsedBySql, new { id }, cancellationToken))
                .Select(p => new LinkingProduct(p.Id, ProductRules.ListedName(p.Name, p.DeletedAt is not null), p.CategoryName, p.IsComposite))
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase),
        ];

    /// <summary>A product GetUsedByAsync finds. One in the Deleted folder still holds the material, and is named as deleted.</summary>
    private sealed record UsedByRow(int Id, string Name, string? CategoryName, DateTime? DeletedAt, bool IsComposite);

    /// <summary>
    /// GetUsedByAsync's products. feeds is the material and every ingredient whose cost it feeds: a made one with it in its
    /// BOM, a linked one cut from it, and on up (both kinds are rejected when they'd close a loop). PostgreSQL lets a
    /// recursive query name itself only once in its recursive part, so feeds follows both kinds of link through one union
    /// of them. A recipe takes an
    /// ingredient through a sub-assembly's lines or a usage's add-ins, and a composite through the products it holds, and
    /// on up (a composite can't end up containing itself). Packaging is read as a product sees it: its own lines unless
    /// its packaging links, a set's lines when it links a set, and another product's packaging when it links that
    /// product, followed down the chain (PackagingLineRepository.ResolveEffectiveAsync). A composite's products bring
    /// their packaging only when it includes theirs (products.includes_component_packaging), and then all of it, so
    /// packaging_costs climbs from every product whose packaging cost the material is in to each composite like that
    /// holding it, and on up. A packaging link takes only the lines, so packaging_users doesn't climb through composites.
    /// </summary>
    private const string UsedBySql = """
        WITH RECURSIVE feeds AS (
            SELECT @id AS id
            UNION ALL
            SELECT e.id
            FROM feeds f
            JOIN (
                SELECT b.component_ingredient_id AS source_id, b.ingredient_id AS id FROM ingredient_bom_lines b
                UNION ALL
                SELECT m.linked_to_id, m.id FROM raw_materials m WHERE m.linked_to_id IS NOT NULL
            ) e ON e.source_id = f.id
        ),
        section_users AS (
            SELECT u.product_id
            FROM product_sub_assembly_usages u
            JOIN sub_assembly_lines l ON l.sub_assembly_id = u.sub_assembly_id
            WHERE l.ingredient_id IN (SELECT id FROM feeds)
            UNION
            SELECT u.product_id
            FROM product_sub_assembly_usages u
            JOIN usage_add_in_lines a ON a.usage_id = u.id
            WHERE a.ingredient_id IN (SELECT id FROM feeds)
        ),
        recipe_users AS (
            SELECT product_id FROM section_users
            UNION ALL
            SELECT c.product_id FROM product_components c JOIN recipe_users r ON r.product_id = c.component_product_id
        ),
        packaging_users AS (
            SELECT l.product_id
            FROM packaging_lines l
            WHERE l.packaging_material_id = @id
                AND NOT EXISTS (SELECT 1 FROM packaging_links k WHERE k.product_id = l.product_id)
            UNION ALL
            SELECT k.product_id
            FROM packaging_links k
            WHERE EXISTS (SELECT 1 FROM sub_assembly_packaging_lines s WHERE s.sub_assembly_id = k.linked_sub_assembly_id AND s.packaging_material_id = @id)
            UNION ALL
            SELECT k.product_id FROM packaging_links k JOIN packaging_users p ON p.product_id = k.linked_product_id
        ),
        packaging_costs AS (
            SELECT product_id FROM packaging_users
            UNION ALL
            SELECT c.product_id
            FROM product_components c
            JOIN packaging_costs p ON p.product_id = c.component_product_id
            JOIN products h ON h.id = c.product_id
            WHERE h.includes_component_packaging
        )
        SELECT p.id, p.name, c.name AS category_name, p.deleted_at, p.is_composite
        FROM products p
        LEFT JOIN product_categories c ON c.id = p.category_id
        WHERE p.id IN (SELECT product_id FROM recipe_users) OR p.id IN (SELECT product_id FROM packaging_costs)
        """;

    public async Task AddAsync(RawMaterial material, CancellationToken cancellationToken)
    {
        // The material and its cascade commit together.
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (await AddAsync(dbContext, material, cancellationToken))
        {
            await IngredientCostPropagator.RecomputeAndPropagateAsync(dbContext, material.Id, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateAsync(RawMaterial material, CancellationToken cancellationToken)
    {
        // The material and its cascade commit together.
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (await UpdateAsync(dbContext, material, cancellationToken))
        {
            await IngredientCostPropagator.RecomputeAndPropagateAsync(dbContext, material.Id, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task MoveAsync(IReadOnlyCollection<RawMaterialMove> moves, CancellationToken cancellationToken)
    {
        if (moves.Count == 0)
        {
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var materialIds = moves.Select(m => m.RawMaterialId).Distinct().ToList();
        var materials = await dbContext.RawMaterials.Where(m => materialIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);
        if (materialIds.Where(id => !materials.ContainsKey(id)).ToList() is [var missingId, ..])
        {
            throw new BusinessRuleException($"Raw material {missingId} does not exist.");
        }

        // Checked rather than left to the foreign key, whose refusal would read as a duplicate name: a move undone after
        // the category it came from was deleted.
        var categoryIds = moves.Select(m => m.CategoryId).Distinct().ToList();
        var categories = await dbContext.RawMaterialCategories
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
        if (categories.Count < categoryIds.Count)
        {
            throw new BusinessRuleException("The category to move it to no longer exists.");
        }

        var now = DateTime.Now;
        foreach (var move in moves)
        {
            var material = materials[move.RawMaterialId];
            var category = categories[move.CategoryId];
            RawMaterialRules.EnsureCategoryMatchesType(material.Type, category.Type, category.Name);
            material.CategoryId = move.CategoryId;
            material.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RenameAsync(int id, string name, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await dbContext.RawMaterials.FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new BusinessRuleException($"Raw material {id} does not exist.");

        existing.Name = name;
        existing.UpdatedAt = DateTime.Now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> SaveAsync(
        RawMaterial material,
        IReadOnlyList<RawMaterialUnit> units,
        IReadOnlyList<(string? Brand, int? UnitOfMeasureId, decimal Price)>? brandPrices,
        IReadOnlyList<(int ComponentIngredientId, decimal Grams)>? bomLines,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // One transaction for every part and the cost cascade they cause, so a part that's rejected,
        // or a cascade that fails, leaves nothing half-saved. Each step saves as it goes, so the checks
        // of the steps after it see what it wrote. The cascade runs once, after the last step.
        int id;
        var costChanged = false;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // A copy of the form's fields: the caller's entity is never attached, and never given an
            // id by a save that then rolls back.
            var fields = new RawMaterial
            {
                Id = material.Id,
                Name = material.Name,
                Type = material.Type,
                CategoryId = material.CategoryId,
                Source = material.Source,
                WeightLossPercent = material.WeightLossPercent,
                LinkedToId = material.LinkedToId,
                WeightSharePercent = material.WeightSharePercent,
                CostSharePercent = material.CostSharePercent,
                BaseUnitId = material.BaseUnitId,
                CostBufferPercent = material.CostBufferPercent,
                BufferedCostPerBaseUnit = material.BufferedCostPerBaseUnit,
            };

            if (fields.Id == 0)
            {
                costChanged = await AddAsync(dbContext, fields, cancellationToken);
            }
            else
            {
                costChanged = await UpdateAsync(dbContext, fields, cancellationToken);
            }

            id = fields.Id;

            // The cost the brand prices are compared against, whatever the steps below do to it on the
            // way, so a Save logs at most one price, and only when it leaves the cost changed.
            var costBefore = await dbContext.RawMaterials.Where(m => m.Id == id).Select(m => m.CostPerBaseUnit).SingleAsync(cancellationToken);

            // The old brand prices go before the units do, so a unit only they were in can be removed.
            if (brandPrices is not null)
            {
                await RawMaterialBrandPriceRepository.RemoveAllAsync(dbContext, id, cancellationToken);
            }

            costChanged |= await SaveUnitsAsync(dbContext, id, units, cancellationToken);

            if (brandPrices is not null)
            {
                var rows = new List<RawMaterialBrandPrice>();
                foreach (var (brand, unitOfMeasureId, price) in brandPrices)
                {
                    rows.Add(new RawMaterialBrandPrice
                    {
                        Brand = brand,
                        UnitId = await FindUnitIdAsync(dbContext, id, unitOfMeasureId, cancellationToken),
                        Price = price,
                    });
                }

                var saved = await dbContext.RawMaterials.FirstAsync(m => m.Id == id, cancellationToken);

                // No buffered cost sent: it's worked out from the new prices, not the old ones UpdateAsync had.
                if (fields.Source == RawMaterialSource.Purchased && material.BufferedCostPerBaseUnit == 0m)
                {
                    saved.BufferedCostPerBaseUnit = 0m;
                }

                await RawMaterialBrandPriceRepository.AddAllAsync(dbContext, saved, rows, cancellationToken);
                costChanged |= await RawMaterialBrandPriceRepository.RepriceAsync(dbContext, saved, costBefore, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            if (bomLines is not null)
            {
                await IngredientBomLineRepository.SetLinesAsync(dbContext, id, bomLines, cancellationToken);
                costChanged = true;
            }

            if (costChanged)
            {
                await IngredientCostPropagator.RecomputeAndPropagateAsync(dbContext, id, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        return id;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await dbContext.RawMaterials.Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Adds <paramref name="material"/> and saves it, rejecting fields that don't fit its type or source, or a
    /// link that doesn't fit. A linked ingredient takes the buffer of the one it's linked to, whatever it was
    /// given, and a made one is always stored at 0 (RawMaterialRules.StoredCostBufferPercent) — which only this
    /// method can do for a new made ingredient, since it has no BOM yet and so never cascades. A new material has
    /// no cost yet, so its buffered cost only stands if one was typed for a purchased
    /// material; otherwise its first brand price or the cascade works it out. Returns whether the caller must
    /// cascade its cost: a linked ingredient's is worked out from the one it's linked to.
    /// </summary>
    internal static async Task<bool> AddAsync(TinapayanRMSDbContext dbContext, RawMaterial material, CancellationToken cancellationToken)
    {
        RawMaterialRules.EnsureFieldsMatchType(material);
        RawMaterialRules.EnsureFieldsMatchSource(material);
        RawMaterialRules.EnsureValidCostBuffer(material.CostBufferPercent);
        RawMaterialRules.EnsureValidBufferedCost(material.BufferedCostPerBaseUnit);
        await EnsureCategoryMatchesAsync(dbContext, material, cancellationToken);
        var inheritedBuffer = await EnsureValidLinkAsync(dbContext, material, cancellationToken);
        material.CostBufferPercent = RawMaterialRules.StoredCostBufferPercent(material.Source, inheritedBuffer, material.CostBufferPercent);

        material.BufferedCostPerBaseUnit = material.Source == RawMaterialSource.Purchased
            ? RawMaterialRules.StoredCost(material.BufferedCostPerBaseUnit)
            : 0m;

        material.UpdatedAt = DateTime.Now;
        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(cancellationToken);

        return material.Source == RawMaterialSource.Linked;
    }

    /// <summary>A material goes in a category of its own type. A category that doesn't exist is left to the foreign key.</summary>
    private static async Task EnsureCategoryMatchesAsync(TinapayanRMSDbContext dbContext, RawMaterial material, CancellationToken cancellationToken)
    {
        var category = await dbContext.RawMaterialCategories
            .Where(c => c.Id == material.CategoryId)
            .Select(c => new { c.Name, c.Type })
            .FirstOrDefaultAsync(cancellationToken);
        if (category is not null)
        {
            RawMaterialRules.EnsureCategoryMatchesType(material.Type, category.Type, category.Name);
        }
    }

    /// <summary>
    /// Copies the form's fields onto the stored material and saves it. Returns whether the caller must
    /// cascade its cost: a made ingredient's weight loss percent, a linked ingredient's link or shares,
    /// or an ingredient's buffer or fixed buffered cost may have just changed.
    /// </summary>
    internal static async Task<bool> UpdateAsync(TinapayanRMSDbContext dbContext, RawMaterial material, CancellationToken cancellationToken)
    {
        RawMaterialRules.EnsureFieldsMatchType(material);
        RawMaterialRules.EnsureFieldsMatchSource(material);
        RawMaterialRules.EnsureValidCostBuffer(material.CostBufferPercent);
        RawMaterialRules.EnsureValidBufferedCost(material.BufferedCostPerBaseUnit);

        var existing = await dbContext.RawMaterials.FirstOrDefaultAsync(m => m.Id == material.Id, cancellationToken)
            ?? throw new BusinessRuleException($"Raw material {material.Id} does not exist.");

        RawMaterialRules.EnsureTypeUnchanged(existing.Type, material.Type);
        RawMaterialRules.EnsureSourceUnchanged(existing.Source, material.Source);
        await EnsureCategoryMatchesAsync(dbContext, material, cancellationToken);
        var inheritedBuffer = await EnsureValidLinkAsync(dbContext, material, cancellationToken);

        // Only a packaging material can get here: an ingredient's is always null (EnsureFieldsMatchType).
        if (existing.BaseUnitId != material.BaseUnitId)
        {
            RawMaterialRules.EnsureBaseUnitCanChange(
                hasUnits: await dbContext.RawMaterialUnits.AnyAsync(u => u.RawMaterialId == existing.Id, cancellationToken),
                hasPrices: await dbContext.RawMaterialBrandPrices.AnyAsync(p => p.RawMaterialId == existing.Id, cancellationToken)
                    || await dbContext.RawMaterialPriceHistories.AnyAsync(h => h.RawMaterialId == existing.Id, cancellationToken),
                hasLines: await dbContext.PackagingLines.AnyAsync(l => l.PackagingMaterialId == existing.Id, cancellationToken)
                    || await dbContext.SubAssemblyPackagingLines.AnyAsync(l => l.PackagingMaterialId == existing.Id, cancellationToken));
        }

        // Field by field, not a blind Update(): CostPerBaseUnit and BufferedCostPerBaseUnit are owned
        // by the price-history and BOM rollups, and the units by RawMaterialUnitRepository, not by this form.
        existing.Name = material.Name;
        existing.CategoryId = material.CategoryId;
        existing.WeightLossPercent = material.WeightLossPercent;
        existing.LinkedToId = material.LinkedToId;
        existing.WeightSharePercent = material.WeightSharePercent;
        existing.CostSharePercent = material.CostSharePercent;
        existing.BaseUnitId = material.BaseUnitId;
        // The form saved it; its cost has its own date, stamped by the DbContext only when a cost really moves.
        existing.UpdatedAt = DateTime.Now;

        // A purchased material's buffered cost is the fixed amount the form sends, which the buffer percent
        // doesn't move (typing the percent on the screen works the amount out). A made one's is a BOM rollup of
        // its components' buffered costs, which the cascade redoes, and it adds no buffer of its own, so its
        // percent is stored as 0. A linked one's buffer is the linked-to ingredient's, not the form's.
        var costBufferPercent = RawMaterialRules.StoredCostBufferPercent(existing.Source, inheritedBuffer, material.CostBufferPercent);
        var bufferChanged = existing.CostBufferPercent != costBufferPercent;
        existing.CostBufferPercent = costBufferPercent;
        // None sent (0) is worked out afresh from the cost and the percent, as for a first price.
        // Stored to the column's places, so a buffered cost sent again unchanged compares equal and doesn't cascade.
        if (existing.Source == RawMaterialSource.Purchased)
        {
            var bufferedCost = RawMaterialRules.StoredCost(material.BufferedCostPerBaseUnit > 0
                ? material.BufferedCostPerBaseUnit
                : RawMaterialRules.BufferedCost(existing.CostPerBaseUnit, existing.CostBufferPercent));
            bufferChanged = existing.BufferedCostPerBaseUnit != bufferedCost;
            existing.BufferedCostPerBaseUnit = bufferedCost;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        // WeightLossPercent, or the link and its shares, may have just changed; the cascade recomputes the
        // cost from its current BOM or link. A new buffer on an ingredient reaches the made and linked
        // ingredients and sub-assemblies using it; a packaging material's needs nothing, since products
        // cost their packaging lines on read.
        return existing.Source is RawMaterialSource.Made or RawMaterialSource.Linked
            || (bufferChanged && existing.Type == RawMaterialType.Ingredient);
    }

    /// <summary>
    /// A linked ingredient's link must be to another ingredient, must leave room among that ingredient's other
    /// parts (together at most 100% of its weight and of its cost), and must not close a loop through BOMs
    /// and links. Nothing to check for any other source. Call after EnsureFieldsMatchSource. Returns the
    /// linked-to ingredient's cost buffer, which a linked ingredient inherits, or null when it isn't linked.
    /// </summary>
    private static async Task<decimal?> EnsureValidLinkAsync(TinapayanRMSDbContext dbContext, RawMaterial material, CancellationToken cancellationToken)
    {
        if (material is not { Source: RawMaterialSource.Linked, LinkedToId: { } linkedToId, WeightSharePercent: { } weightShare, CostSharePercent: { } costShare })
        {
            return null;
        }

        var linkedTo = await dbContext.RawMaterials
            .Where(m => m.Id == linkedToId)
            .Select(m => new { m.Name, m.Type, m.CostBufferPercent })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException($"Ingredient {linkedToId} does not exist.");

        RawMaterialRules.EnsureValidLinkTarget(material.Id, linkedToId, linkedTo.Type, linkedTo.Name);

        var otherParts = await dbContext.RawMaterials
            .Where(m => m.LinkedToId == linkedToId && m.Id != material.Id)
            .OrderBy(m => m.Name)
            .Select(m => new { m.Name, m.WeightSharePercent, m.CostSharePercent })
            .ToListAsync(cancellationToken);

        RawMaterialRules.EnsureLinkSharesFit(
            material.Name,
            linkedTo.Name,
            weightShare,
            costShare,
            [.. otherParts.Select(p => (p.Name, p.WeightSharePercent ?? 0m, p.CostSharePercent ?? 0m))]);

        // A new ingredient has nothing depending on it yet, so it can't close a loop.
        if (material.Id != 0)
        {
            var dependencies = await IngredientDependencies.LoadAsync(dbContext, material.Id, cancellationToken);
            IngredientBomRules.EnsureNoCycle(material.Id, [linkedToId], dependencies);
        }

        return linkedTo.CostBufferPercent;
    }

    /// <summary>
    /// Makes the material's alternative units match <paramref name="units"/>: removes the saved ones
    /// missing from it, then updates those with an id, then adds those without. Removing first lets a
    /// removed unit of measure come back as a new row. Returns whether a corrected multiplier changed an ingredient's cost.
    /// </summary>
    private static async Task<bool> SaveUnitsAsync(
        TinapayanRMSDbContext dbContext, int rawMaterialId, IReadOnlyList<RawMaterialUnit> units, CancellationToken cancellationToken)
    {
        var keptIds = units.Where(u => u.Id != 0).Select(u => u.Id).ToList();
        var removedIds = await dbContext.RawMaterialUnits
            .Where(u => u.RawMaterialId == rawMaterialId && !keptIds.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        foreach (var removedId in removedIds)
        {
            await RawMaterialUnitRepository.DeleteAsync(dbContext, removedId, cancellationToken);
        }

        var costChanged = false;
        foreach (var unit in units.Where(u => u.Id != 0))
        {
            costChanged |= await RawMaterialUnitRepository.UpdateAsync(dbContext, ForMaterial(unit, rawMaterialId), cancellationToken);
        }

        foreach (var unit in units.Where(u => u.Id == 0))
        {
            await RawMaterialUnitRepository.AddAsync(dbContext, ForMaterial(unit, rawMaterialId), cancellationToken);
        }

        return costChanged;

        // The caller's units are never changed: a new material's didn't know its id yet.
        static RawMaterialUnit ForMaterial(RawMaterialUnit unit, int rawMaterialId) => new()
        {
            Id = unit.Id,
            RawMaterialId = rawMaterialId,
            UnitOfMeasureId = unit.UnitOfMeasureId,
            BaseUnitsPerUnit = unit.BaseUnitsPerUnit,
            Description = unit.Description,
        };
    }

    /// <summary>
    /// The id of the material's unit in <paramref name="unitOfMeasureId"/>, or null for a price per base
    /// unit. A new price names its unit by unit of measure because a unit added in the same save had no
    /// id yet; a material has each unit of measure at most once.
    /// </summary>
    private static async Task<int?> FindUnitIdAsync(
        TinapayanRMSDbContext dbContext, int rawMaterialId, int? unitOfMeasureId, CancellationToken cancellationToken)
    {
        if (unitOfMeasureId is null)
        {
            return null;
        }

        return await dbContext.RawMaterialUnits
            .Where(u => u.RawMaterialId == rawMaterialId && u.UnitOfMeasureId == unitOfMeasureId)
            .Select(u => (int?)u.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new BusinessRuleException("A price's unit must be one of that material's own units.");
    }
}
