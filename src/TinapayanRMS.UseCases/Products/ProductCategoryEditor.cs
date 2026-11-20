using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UseCases.Products;

/// <summary>
/// What the Products forms' Category picker does: adds a category typed into it. A refusal is a
/// <see cref="BusinessRuleException"/>, in words for the shop's staff.
/// </summary>
public interface IProductCategoryEditor
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

    public async Task<ProductCategory> AddAsync(string name, CancellationToken cancellationToken)
    {
        var added = new ProductCategory { Name = name };
        Ensure(added);

        // AddAsync fills in the saved id.
        await categoryRepository.AddAsync(added, cancellationToken);
        return added;
    }

    private void Ensure(ProductCategory category)
    {
        var validation = validator.Validate(category);
        if (!validation.IsValid)
        {
            throw new BusinessRuleException(validation.ToString(" "));
        }
    }
}
