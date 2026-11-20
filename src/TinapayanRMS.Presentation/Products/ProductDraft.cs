using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Products;

/// <summary>What a product's draft (<see cref="ProductDraft"/>) asks of its form: which saved product it shows, if any.</summary>
public interface IProductDraftHost
{
    /// <summary>The product whose recipe, packaging, costing and links the form holds, or null for a new product.</summary>
    int? DetailProductId { get; }
}

/// <summary>The view's lists a product's draft reads, each merged into by every load: what its parts point at and offer.</summary>
public sealed record ProductLists(
    IReadOnlyList<RawMaterial> Ingredients,
    IReadOnlyList<SubAssemblyRole> Roles,
    IReadOnlyList<RawMaterial> PackagingMaterials,
    IReadOnlyList<Product> Products);

/// <summary>
/// A product as its form edits it (ADR 0002): its own fields (name, description, quantity, current price, category, kind
/// and the Create Variant name), its parts' drafts (the recipe's sections, the packaging, a composite's products and the
/// costing), what the costs are worked out from (<see cref="ProductCosting"/>'s inputs), the product Save writes
/// (<see cref="ToProduct"/>), what Print puts on paper (<see cref="ToPrintout"/>), and an undo step's share of all of it
/// (<see cref="Capture"/>, <see cref="Restore"/>). It hosts its parts' drafts: their cost basis is its costing's, and the
/// product's name and kind its own. It reads nothing and asks the form only which saved product it shows; the form keeps
/// the selection, the loads, Save's checks and questions, and the parts' tabs and commands.
/// </summary>
public sealed class ProductDraft : ObservableState, IDisposable, IProductPackagingDraftHost, IProductComponentsDraftHost, IProductRecipeDraftHost
{
    private readonly IProductDraftHost _host;

    /// <param name="host">The form: which saved product it shows.</param>
    /// <param name="lists">The view's lists the parts point at and offer.</param>
    public ProductDraft(IProductDraftHost host, ProductLists lists)
    {
        _host = host;

        // The costing first, as the parts read its cost basis. Its costs are worked out from the parts only as they're read.
        Costing = new ProductCosting(CostingInputs);
        Packaging = new ProductPackagingDraft(this, lists.PackagingMaterials);
        Components = new ProductComponentsDraft(this, lists.Products);
        Recipe = new ProductRecipeDraft(this, lists.Ingredients, lists.Roles, Components);
    }

    /// <summary>The costing and the costs worked out from the product as it stands.</summary>
    public ProductCosting Costing { get; }

    /// <summary>The packaging per piece and the bulk packaging.</summary>
    public ProductPackagingDraft Packaging { get; }

    /// <summary>A composite's products.</summary>
    public ProductComponentsDraft Components { get; }

    /// <summary>The recipe's sections.</summary>
    public ProductRecipeDraft Recipe { get; }

    private bool _isEditingComposite;
    /// <summary>
    /// The product is a composite: made of other products, its own packaging, and sections only in product-owned roles, with
    /// no Quantity Required of its own. Picked by New composite product, and fixed once it's saved.
    /// </summary>
    public bool IsEditingComposite
    {
        get => _isEditingComposite;
        internal set => RaiseAndSetIfChanged(ref _isEditingComposite, value);
    }

    private string _editingName = string.Empty;
    /// <summary>The name typed, which the header and the tab show.</summary>
    public string EditingName
    {
        get => _editingName;
        set => RaiseAndSetIfChanged(ref _editingName, value);
    }

    private string _editingDescription = string.Empty;
    public string EditingDescription
    {
        get => _editingDescription;
        set => RaiseAndSetIfChanged(ref _editingDescription, value);
    }

    private int _editingQuantityRequired = 1;
    /// <summary>
    /// How many finished pieces make one sellable unit (Product.QuantityRequired): 1 for a product sold by the piece, 8 for a
    /// "Box of 8". The recipe stays per piece; every cost and price is for one whole unit, so this multiplies the
    /// sub-assemblies' share of it.
    /// </summary>
    public int EditingQuantityRequired
    {
        get => _editingQuantityRequired;
        set
        {
            if (RaiseAndSetIfChanged(ref _editingQuantityRequired, value))
            {
                Costing.InputsChanged();
            }
        }
    }

    private decimal? _editingCurrentPrice;
    /// <summary>What the shop charges for one sellable unit now, with VAT (Product.CurrentPrice); null when not entered.</summary>
    public decimal? EditingCurrentPrice
    {
        get => _editingCurrentPrice;
        set
        {
            if (RaiseAndSetIfChanged(ref _editingCurrentPrice, value))
            {
                Costing.InputsChanged();
            }
        }
    }

    private ProductCategory? _editingCategory;
    /// <summary>The product's category, or null for none: a product doesn't need one. It can set the Wastage % the product is priced with.</summary>
    public ProductCategory? EditingCategory
    {
        get => _editingCategory;
        set
        {
            if (RaiseAndSetIfChanged(ref _editingCategory, value))
            {
                Costing.InputsChanged();
            }
        }
    }

    private string _editingCloneName = string.Empty;
    /// <summary>The name a clone or a variant made from the product will take (the Clone tab), which no Save writes.</summary>
    public string EditingCloneName
    {
        get => _editingCloneName;
        set => RaiseAndSetIfChanged(ref _editingCloneName, value);
    }

    /// <summary>
    /// The product as Save writes it, with <paramref name="id"/> (0 for a new one), filed in <paramref name="stage"/> and
    /// <paramref name="folderId"/> (only a new one's is written: a saved one's changes by moving it in the list), as last
    /// saved at <paramref name="savedVersion"/> when the form took it, so a save made since from another PC refuses this one.
    /// </summary>
    public Product ToProduct(int id, ProductStage stage, int? folderId, DateTime? savedVersion) => new()
    {
        Id = id,
        Name = EditingName.Trim(),
        Description = string.IsNullOrWhiteSpace(EditingDescription) ? null : EditingDescription.Trim(),
        CategoryId = EditingCategory?.Id,
        QuantityRequired = EditingQuantityRequired,
        CurrentPrice = EditingCurrentPrice,
        Stage = stage,
        FolderId = folderId,

        // A new one's to say; a saved one's is the kind it was saved as, which the form never changes.
        IsComposite = IsEditingComposite,
        IncludesComponentPackaging = IsEditingComposite && Components.IncludesPackaging,
        UpdatedAt = savedVersion,
    };

    /// <summary>
    /// What Print puts on paper: the product as it stands, its details, every section in recipe order, its packaging and
    /// its costing, filed in <paramref name="stageTitle"/>, last saved <paramref name="lastUpdatedText"/>, with
    /// <paramref name="note"/> when that isn't what's saved.
    /// </summary>
    public ProductPrintout ToPrintout(string stageTitle, string lastUpdatedText, string? note) => new(
        string.IsNullOrWhiteSpace(EditingName) ? (IsEditingComposite ? "New composite product" : "New product") : EditingName.Trim(),
        EditingCategory?.Name ?? "Uncategorized",
        stageTitle,
        Costing.CostUnitCaption,
        lastUpdatedText,
        note,
        DateTime.Now,
        [.. Recipe.UsageEdits.Select(u => PrintedSection.From(u.ToPriced(), Costing.CostBasis))],
        Packaging.LineEdits.Count > 0 || Packaging.BulkEdits.Count > 0
            ? PrintedPackaging.From(
                Packaging.LineEdits.Select(l => l.ToPriced()),
                Packaging.IsPackagingLinked ? Packaging.PackagingLinkedToText : null,
                Packaging.BulkEdits.Count == 0 ? null : new PricedBulkPackaging([.. Packaging.BulkEdits.Select(l => l.ToPriced())], Packaging.BulkQuantity))
            : null,
        Costing.Costs,
        IsEditingComposite ? PrintedComponent.From(Components.ComponentEdits) : null)
    {
        CostBasis = Costing.CostBasis,
    };

    /// <summary>
    /// The product as an undo step keeps it, for the form showing <paramref name="editingId"/>: its own fields, the costing,
    /// the packaging and a composite's products, with <paramref name="recipe"/>, the recipe part's share (its sections, its
    /// tabs and its section form).
    /// </summary>
    public ProductFormSnapshot Capture(int editingId, RecipeSnapshot recipe) => new()
    {
        EditingId = editingId,
        Name = EditingName,
        Description = EditingDescription,
        CurrentPrice = EditingCurrentPrice,
        QuantityRequired = EditingQuantityRequired,
        IncludesComponentPackaging = Components.IncludesPackaging,
        CategoryId = EditingCategory?.Id,
        CloneName = EditingCloneName,
        Costing = Costing.Percentages,
        Recipe = recipe,
        Packaging = Packaging.Capture(),
        Components = Components.Capture(),
    };

    /// <summary>
    /// Puts back what <paramref name="snapshot"/> kept of the product, in the order it has to go back in, for the form's
    /// undo, which has closed the section form and let go of the rows picked first, and puts the recipe back after (the
    /// recipe part's Restore). The category goes back by id, as the reloaded <paramref name="categories"/>' own copy; the
    /// packaging's link as saved is left alone, as it's what Save compares against.
    /// </summary>
    public void Restore(ProductFormSnapshot snapshot, IReadOnlyList<ProductCategory> categories)
    {
        // The product's own fields.
        EditingName = snapshot.Name;
        EditingDescription = snapshot.Description;
        EditingCurrentPrice = snapshot.CurrentPrice;
        EditingQuantityRequired = snapshot.QuantityRequired;
        Components.IncludesPackaging = snapshot.IncludesComponentPackaging;
        EditingCloneName = snapshot.CloneName;
        _editingCategory = snapshot.CategoryId is { } categoryId ? categories.FirstOrDefault(c => c.Id == categoryId) : null;
        RaisePropertyChanged(nameof(EditingCategory));
        Costing.InputsChanged();

        // The costing, then the packaging, then a composite's lines, each finding its product and the recipe cost read for it
        // again (a cost not read yet is wanted now, and prices the line when it arrives).
        Costing.Percentages = snapshot.Costing;
        Packaging.Restore(snapshot.Packaging);
        Components.Restore(snapshot.Components);
    }

    /// <summary>
    /// What the costs are worked out from, as the product stands (ProductCosting.Costs reads it): the recipe and packaging,
    /// the product's fields, and every material they use, for the below-cost warning.
    /// </summary>
    private ProductCostingInputs CostingInputs()
    {
        // A composite's recipe cost is what its products bring, each line its quantity at the product's recipe cost (a
        // line still being filled in brings nothing yet), then its own sections, each per whole composite.
        List<ProductComponentRow> lines = IsEditingComposite ? [.. Components.CostedLines] : [];
        List<(string Name, decimal Cost)> components = [.. lines.Select(r => (r.LineName, r.RecipeCostTotal))];

        // With the box ticked, each product's own packaging for one sellable unit, times its quantity, joins the
        // composite's packaging, not its recipe cost (a product whose costs haven't been read yet brings none until they are).
        decimal? componentPackaging = IsEditingComposite && Components.IncludesPackaging
            ? lines.Sum(r => r.PackagingCostTotal)
            : null;
        var usages = Recipe.UsageEdits;
        return new ProductCostingInputs(
            new ProductCostInputs
            {
                Parts = [.. components, .. usages.Select(u => (u.SectionName, u.CostPerPieceAt(Costing.CostBasis)))],
                QuantityRequired = EditingQuantityRequired,
                PackagingCosts = [.. Packaging.LineEdits.Select(l => l.CostPerUnit), .. Packaging.BulkEdits.Count > 0 ? [Packaging.BulkCostPerUnit] : Array.Empty<decimal>()],
                ComponentPackaging = componentPackaging,
                CurrentPrice = EditingCurrentPrice,
                IsComposite = IsEditingComposite,
                HasOwnSections = IsEditingComposite && usages.Count > 0,
            },
            EditingCategory?.WastagePercent,
            [.. usages.SelectMany(u => u.Lines.Concat(u.AddIns)).Select(l => l.Ingredient)
                .Concat(Packaging.AllLineEdits.Select(l => l.PackagingMaterial).OfType<RawMaterial>())])
        {
            SectionsWithoutPieceSize = [.. usages.Where(u => !ProductRules.HasPieceSize(u.AuthoredValue)).Select(u => u.SectionName)],
        };
    }

    // The parts' drafts ask it: their cost basis is the costing's, and the product it's for its own.
    CostBasis IProductPackagingDraftHost.CostBasis => Costing.CostBasis;

    CostBasis IProductComponentsDraftHost.CostBasis => Costing.CostBasis;

    int? IProductRecipeDraftHost.DetailProductId => _host.DetailProductId;

    public void Dispose()
    {
        Packaging.Dispose();
        Components.Dispose();
    }
}
