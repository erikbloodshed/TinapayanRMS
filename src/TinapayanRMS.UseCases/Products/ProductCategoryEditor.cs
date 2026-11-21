using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.UseCases.Settings;

namespace TinapayanRMS.UseCases.Products;

/// <summary>
/// What the Products forms' Category picker does, adding a category typed into it, and Settings' Product Categories tab
/// (<see cref="ILookupListEditor{TItem}"/>). A refusal is a <see cref="BusinessRuleException"/>, in words for the shop's
/// staff.
/// </summary>
public interface IProductCategoryEditor : ILookupListEditor<ProductCategory>
{
    /// <summary>Refuses a name a category can't take, before anything is written.</summary>
    void EnsureValid(string name);

    /// <summary>Adds a category named <paramref name="name"/>; the category as saved, with its id.</summary>
    Task<ProductCategory> AddAsync(string name, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IProductCategoryEditor"/>
public sealed class ProductCategoryEditor(IProductCategoryRepository categoryRepository, IValidator<ProductCategory> validator) : IProductCategoryEditor
{
    public void EnsureValid(string name) => Ensure(new ProductCategory { Name = name });

    public void EnsureValid(ProductCategory item) => Ensure(item);

    public Task<IReadOnlyList<ProductCategory>> GetAllAsync(CancellationToken cancellationToken) => categoryRepository.GetAllAsync(cancellationToken);

    public async Task<ProductCategory> AddAsync(string name, CancellationToken cancellationToken)
    {
        var added = new ProductCategory { Name = name };
        Ensure(added);

        // AddAsync fills in the saved id.
        await categoryRepository.AddAsync(added, cancellationToken);
        return added;
    }

    public async Task AddAsync(ProductCategory item, CancellationToken cancellationToken)
    {
        // Async, so a refusal fails the task rather than throwing as it's called.
        Ensure(item);
        await categoryRepository.AddAsync(item, cancellationToken);
    }

    public async Task UpdateAsync(ProductCategory item, CancellationToken cancellationToken)
    {
        Ensure(item);
        await categoryRepository.UpdateAsync(item, cancellationToken);
    }

    public Task DeleteAsync(int id, CancellationToken cancellationToken) => categoryRepository.DeleteAsync(id, cancellationToken);

    private void Ensure(ProductCategory category)
    {
        var validation = validator.Validate(category);
        if (!validation.IsValid)
        {
            throw new BusinessRuleException(validation.ToString(" "));
        }
    }
}
