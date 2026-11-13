using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// The checks every write of a recipe's or a packaging's lines makes of what they name, before they're written, on the
/// caller's context: a sub-assembly's lines, a usage's add-ins, a made ingredient's add-ins, a product's packaging lines
/// and a packaging set's.
/// </summary>
internal static class LineChecks
{
    /// <summary>
    /// The names of the ingredients <paramref name="ingredientIds"/> names, by id, once each is checked in turn: refused
    /// when one doesn't exist, or isn't one a recipe can use (RawMaterialRules.EnsureUsableInRecipes).
    /// </summary>
    public static async Task<IReadOnlyDictionary<int, string>> UsableIngredientNamesAsync(
        TinapayanRMSDbContext dbContext, IReadOnlyList<int> ingredientIds, CancellationToken cancellationToken)
    {
        List<int> ids = [.. ingredientIds.Distinct()];
        var ingredients = await dbContext.RawMaterials
            .Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.Type })
            .ToDictionaryAsync(m => m.Id, cancellationToken);
        foreach (var id in ingredientIds)
        {
            var ingredient = ingredients.GetValueOrDefault(id) ?? throw new BusinessRuleException($"Ingredient {id} does not exist.");
            RawMaterialRules.EnsureUsableInRecipes(ingredient.Type, ingredient.Name);
        }

        return ingredients.ToDictionary(e => e.Key, e => e.Value.Name);
    }

    /// <summary>
    /// Refuses packaging lines whose material doesn't exist or isn't packaging (RawMaterialRules.EnsureUsableAsPackaging),
    /// or whose unit isn't one of its own material's alternative units; an empty one means its base unit.
    /// <paramref name="line"/> is what the refusal of a unit calls a line: "packaging line", "packaging set line".
    /// </summary>
    public static async Task EnsureUsablePackagingAsync(
        TinapayanRMSDbContext dbContext, IReadOnlyList<(int MaterialId, int? UnitId)> lines, string line, CancellationToken cancellationToken)
    {
        List<int> materialIds = [.. lines.Select(l => l.MaterialId).Distinct()];
        var materials = await dbContext.RawMaterials
            .Where(m => materialIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.Type })
            .ToDictionaryAsync(m => m.Id, cancellationToken);
        foreach (var (materialId, _) in lines)
        {
            var material = materials.GetValueOrDefault(materialId)
                ?? throw new BusinessRuleException($"Packaging material {materialId} does not exist.");
            RawMaterialRules.EnsureUsableAsPackaging(material.Type, material.Name);
        }

        List<int> unitIds = [.. lines.Where(l => l.UnitId is not null).Select(l => l.UnitId!.Value).Distinct()];
        var materialIdByUnitId = await dbContext.RawMaterialUnits
            .Where(u => unitIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.RawMaterialId, cancellationToken);
        foreach (var (materialId, unitId) in lines)
        {
            if (unitId is { } id && (!materialIdByUnitId.TryGetValue(id, out var unitMaterialId) || unitMaterialId != materialId))
            {
                throw new BusinessRuleException($"A {line}'s unit must be one of that material's own units.");
            }
        }
    }
}
