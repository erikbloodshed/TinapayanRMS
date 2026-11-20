using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing.Versions;

namespace TinapayanRMS.UseCases.SubAssemblies;

/// <inheritdoc cref="ISubAssemblyEditor"/>
/// <param name="recipeImpact">What a Save of a recipe products link is checked with; none, and nothing is asked about.</param>
public sealed class SubAssemblyEditor(
    ISubAssemblyRepository subAssemblyRepository,
    IValidator<SubAssembly> validator,
    VersionedSaves versionedSaves,
    SharedRecipeImpact? recipeImpact = null) : ISubAssemblyEditor
{
    public void EnsureValid(string name, int? roleId)
    {
        var validation = validator.Validate(new SubAssembly { Name = name, RoleId = roleId ?? 0 });
        if (!validation.IsValid)
        {
            throw new BusinessRuleException(validation.ToString(" "));
        }
    }

    public async Task<IReadOnlyList<RecipeImpact>> CheckSaveAsync(SubAssemblySaveRequest request, CancellationToken cancellationToken)
    {
        EnsureHasLines(request);
        if (request.IsPackagingSet || request.Id == 0 || recipeImpact is null)
        {
            return [];
        }

        List<SubAssemblyLine> newLines =
        [
            .. request.Lines.Select((l, i) => new SubAssemblyLine
            {
                SubAssemblyId = request.Id, IngredientId = l.Ingredient.Id, Ingredient = l.Ingredient, Grams = l.Grams,
                IsFormulaBase = l.IsFormulaBase, AuthoredBakersPercent = l.AuthoredBakersPercent, SortOrder = i,
            }),
        ];
        return await recipeImpact.ReadAsync(request.Id, newLines, cancellationToken);
    }

    public async Task<SubAssemblySaved> SaveAsync(SubAssemblySaveRequest request, DateTime savedAt, CancellationToken cancellationToken)
    {
        // Async, so a refusal fails the task, as CheckSaveAsync's does, rather than throwing as it's called.
        EnsureValid(request.Name, request.RoleId);
        EnsureHasLines(request);

        var subAssembly = new SubAssembly { Id = request.Id, Name = request.Name, RoleId = request.RoleId, UpdatedAt = request.SavedVersion };
        Func<CancellationToken, Task<int>> save = request.IsPackagingSet
            ? token => subAssemblyRepository.SaveStandalonePackagingAsync(
                subAssembly, [.. request.PackagingLines.Select(l => (l.PackagingMaterialId, l.UnitId, l.QtyPerSet))], token)
            : token => subAssemblyRepository.SaveStandaloneAsync(
                subAssembly, [.. request.Lines.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))], token);

        return await versionedSaves.SaveSubAssemblyAsync(request.Id, request.Name, save, savedAt, cancellationToken);
    }

    public Task DeleteAsync(int subAssemblyId, CancellationToken cancellationToken) =>
        subAssemblyRepository.DeleteStandaloneAsync(subAssemblyId, cancellationToken);

    public Task MoveAsync(IReadOnlyCollection<SubAssemblyMove> moves, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken) =>
        subAssemblyRepository.MoveAsync(moves, checks, cancellationToken);

    public Task RenameAsync(int subAssemblyId, string name, IReadOnlyCollection<SavedVersionCheck> checks, CancellationToken cancellationToken) =>
        subAssemblyRepository.RenameAsync(subAssemblyId, name, checks, cancellationToken);

    public Task AddMadeIngredientAsync(int subAssemblyId, MadeIngredientDraft made, CancellationToken cancellationToken) =>
        subAssemblyRepository.AddMadeIngredientAsync(subAssemblyId, made, cancellationToken);

    /// <summary>A sub-assembly is saved with at least one line of the kind its role takes.</summary>
    private static void EnsureHasLines(SubAssemblySaveRequest request)
    {
        if (request.IsPackagingSet ? request.PackagingLines.Count == 0 : request.Lines.Count == 0)
        {
            throw new BusinessRuleException(request.IsPackagingSet
                ? $"Add the packaging materials that go into {request.Name}, per set."
                : $"Add the ingredients that go into {request.Name}, in grams.");
        }
    }
}
