using System.Reactive.Linq;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>What the Scaling tab scales, as the Products screen holds it now: the recipe, and the products a composite holds.</summary>
/// <param name="ProductId">The product open, which a composite holding it can't hold (RecipeScaler); null for a new one.</param>
/// <param name="HeldProducts">A composite's products, whose saved recipes are read to scale them.</param>
public sealed record ScalingInput(ScalingRecipe Recipe, int? ProductId, IReadOnlyList<Product> HeldProducts);

/// <summary>
/// The Products screen's Scaling tab (ScalingCard, through ProductsViewModel.Scaling): the recipe as the form holds it,
/// scaled to a number of sellable units, and from an amount of one ingredient there is, how many units it makes. Worked out
/// while the tab shows (<see cref="IsShowing"/>), and again as the units, the amount or the recipe change; the screen says
/// when the recipe has (<see cref="Refresh"/>). A composite's products are scaled from their saved recipes, read the first
/// time they're needed.
/// </summary>
/// <param name="input">The recipe as the screen holds it now.</param>
/// <param name="ingredientsById">The ingredients as the screen last loaded them, by id: what a made ingredient opens into.</param>
/// <param name="costBasis">The screen's cost basis, buffered or not.</param>
/// <param name="savedProducts">Reads the saved recipes of a composite's products.</param>
/// <param name="showFailure">Shows a read nothing awaits that failed, on the screen.</param>
/// <param name="work">The screen's work nothing awaits, which counts the recipe reads (ViewModelBase.WhenIdleAsync).</param>
public sealed class ProductScaling : ReactiveObject
{
    private readonly Func<ScalingInput> _input;
    private readonly Func<IReadOnlyDictionary<int, RawMaterial>> _ingredientsById;
    private readonly Func<CostBasis> _costBasis;
    private readonly SavedProductReader _savedProducts;
    private readonly Action<Exception, string> _showFailure;
    private readonly BackgroundWork _work;

    /// <summary>Every made ingredient's BOM lines, by made ingredient, as the screen last loaded them: what a made ingredient opens into.</summary>
    private ILookup<int, IngredientBomLine> _bomLines = Array.Empty<IngredientBomLine>().ToLookup(l => l.IngredientId);

    /// <summary>
    /// The products a composite holds, as saved, read when the tab first needs them (ReadMissing), and again after the
    /// screen's loads (<see cref="TakeLoaded"/>) and for each product opened (<see cref="Forget"/>).
    /// </summary>
    private readonly Dictionary<int, (Product Product, ProductDetail Detail)> _products = [];

    /// <summary>Products whose recipe is being read, so each is read once however often it's asked for.</summary>
    private readonly HashSet<int> _reading = [];

    public ProductScaling(
        Func<ScalingInput> input,
        Func<IReadOnlyDictionary<int, RawMaterial>> ingredientsById,
        Func<CostBasis> costBasis,
        SavedProductReader savedProducts,
        Action<Exception, string> showFailure,
        BackgroundWork work)
    {
        _work = work;
        _input = input;
        _ingredientsById = ingredientsById;
        _costBasis = costBasis;
        _savedProducts = savedProducts;
        _showFailure = showFailure;

        // What the tab scales by is its own: the units, and the amount of an ingredient there is.
        this.WhenAnyValue(x => x.Units, x => x.IsShowing, x => x.HaveIngredient, x => x.HaveUnit, x => x.HaveAmount)
            .Subscribe(_ => Refresh());
        ScaleToHaveCommand = ReactiveCommand.Create(() => { Units = HaveMakes; }, this.WhenAnyValue(x => x.HaveMakes).Select(units => units > 0));
    }

    private bool _isShowing;
    /// <summary>Whether the tab shows: it's only worked out while it does.</summary>
    public bool IsShowing
    {
        get => _isShowing;
        set => this.RaiseAndSetIfChanged(ref _isShowing, value);
    }

    private int _units = 1;
    /// <summary>
    /// How many sellable units the recipe is scaled to: boxes for a Box of 8, pieces for a product sold by the piece. At
    /// least 1. It isn't part of the product, so it's no edit: nothing to undo or save, and it stays as it is from one
    /// product to the next until the app closes, as the cost basis does.
    /// </summary>
    public int Units
    {
        get => _units;
        set => this.RaiseAndSetIfChanged(ref _units, Math.Max(1, value));
    }

    private IReadOnlyList<ScalingRow> _rows = [];
    /// <summary>The recipe scaled to <see cref="Units"/>, as a tree.</summary>
    public IReadOnlyList<ScalingRow> Rows
    {
        get => _rows;
        private set => this.RaiseAndSetIfChanged(ref _rows, value);
    }

    private decimal _totalCost;
    /// <summary>What everything in <see cref="Rows"/> costs, the extra pieces whole batches make included.</summary>
    public decimal TotalCost
    {
        get => _totalCost;
        private set => this.RaiseAndSetIfChanged(ref _totalCost, value);
    }

    private string _piecesText = string.Empty;
    /// <summary>What <see cref="Units"/> comes to in pieces, "= 160 pieces", when a unit holds more than one; empty otherwise.</summary>
    public string PiecesText
    {
        get => _piecesText;
        private set => this.RaiseAndSetIfChanged(ref _piecesText, value);
    }

    private IReadOnlyList<RawMaterial> _ingredients = [];
    /// <summary>The ingredients the recipe takes, wherever they are in it (RecipeScaler.IngredientsOf): what "from what you have" offers.</summary>
    public IReadOnlyList<RawMaterial> Ingredients
    {
        get => _ingredients;
        private set => this.RaiseAndSetIfChanged(ref _ingredients, value);
    }

    private RawMaterial? _haveIngredient;
    /// <summary>The ingredient there's a limited amount of, which the units it's enough for are worked out from. Not saved.</summary>
    public RawMaterial? HaveIngredient
    {
        get => _haveIngredient;
        set
        {
            if (ReferenceEquals(_haveIngredient, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _haveIngredient, value);
            var previous = HaveUnit;
            HaveUnits = IngredientAmountUnit.For(value);
            HaveUnit = HaveUnits.FirstOrDefault(u => u.Name == previous?.Name) ?? HaveUnits[0];
        }
    }

    private IReadOnlyList<IngredientAmountUnit> _haveUnits = IngredientAmountUnit.For(null);
    /// <summary>What the amount can be typed in: grams, and the ingredient's own units (a kilo, a bag, a tray).</summary>
    public IReadOnlyList<IngredientAmountUnit> HaveUnits
    {
        get => _haveUnits;
        private set => this.RaiseAndSetIfChanged(ref _haveUnits, value);
    }

    private IngredientAmountUnit? _haveUnit = IngredientAmountUnit.Gram;
    public IngredientAmountUnit? HaveUnit
    {
        get => _haveUnit;
        set => this.RaiseAndSetIfChanged(ref _haveUnit, value);
    }

    private decimal? _haveAmount;
    /// <summary>How much of <see cref="HaveIngredient"/> there is, in <see cref="HaveUnit"/>.</summary>
    public decimal? HaveAmount
    {
        get => _haveAmount;
        set => this.RaiseAndSetIfChanged(ref _haveAmount, value);
    }

    private int _haveMakes;
    /// <summary>The most sellable units the amount there is makes (RecipeScaler.UnitsFrom); 0 for none, or nothing asked.</summary>
    public int HaveMakes
    {
        get => _haveMakes;
        private set
        {
            this.RaiseAndSetIfChanged(ref _haveMakes, value);
            this.RaisePropertyChanged(nameof(ScaleToHaveLabel));
            this.RaisePropertyChanged(nameof(HasHaveMakes));
        }
    }

    /// <summary>Whether the amount there is makes any: Scale to only shows when it does.</summary>
    public bool HasHaveMakes => _haveMakes > 0;

    /// <summary>The button that scales the tab to <see cref="HaveMakes"/>: "Scale to 12".</summary>
    public string ScaleToHaveLabel => $"Scale to {HaveMakes:#,0}";

    private string _haveText = string.Empty;
    /// <summary>What the amount there is makes, "Makes 12, using 2,400 g of the 2,500 g", or why it makes none; empty with nothing asked.</summary>
    public string HaveText
    {
        get => _haveText;
        private set => this.RaiseAndSetIfChanged(ref _haveText, value);
    }

    /// <summary>Sets <see cref="Units"/> to what the amount there is makes.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ScaleToHaveCommand { get; }

    /// <summary>
    /// Takes the screen's fresh load: the BOMs as they are now, and every product's saved recipe to be read again when the
    /// tab next shows it.
    /// </summary>
    public void TakeLoaded(ILookup<int, IngredientBomLine> bomLines)
    {
        _bomLines = bomLines;
        _products.Clear();
    }

    /// <summary>Forgets the saved recipes read for a composite's products: another product is open.</summary>
    public void Forget() => _products.Clear();

    /// <summary>Works the tab out again from the recipe as the screen holds it now, while it shows.</summary>
    public void Refresh()
    {
        if (!IsShowing)
        {
            return;
        }

        var input = _input();
        ReadMissing(input.HeldProducts);
        var costBasis = _costBasis();
        var scaler = new RecipeScaler(
            _bomLines,
            _ingredientsById(),
            productId => _products.TryGetValue(productId, out var read) ? SavedProductReader.Build(read.Product, read.Detail, costBasis) : null,
            costBasis);
        Rows = scaler.Scale(input.Recipe, Units, input.ProductId);
        TotalCost = Rows.Sum(r => r.Cost);
        PiecesText = input.Recipe.QuantityRequired > 1
            ? $"= {RecipeScalingRules.PiecesFor(Units, input.Recipe.QuantityRequired):#,0} pieces"
            : string.Empty;
        RefreshFromHave(scaler, input);
    }

    /// <summary>"From what you have": the ingredients the recipe takes, kept picked while it still takes it, and how many units the amount typed makes.</summary>
    private void RefreshFromHave(RecipeScaler scaler, ScalingInput input)
    {
        var ingredients = scaler.IngredientsOf(input.Recipe, input.ProductId);
        if (!ingredients.Select(i => i.Id).SequenceEqual(Ingredients.Select(i => i.Id)))
        {
            Ingredients = ingredients;
        }

        if (HaveIngredient is { } picked && ingredients.FirstOrDefault(i => i.Id == picked.Id) is var kept && !ReferenceEquals(kept, picked))
        {
            HaveIngredient = kept;
        }

        if (HaveIngredient is not { } ingredient || HaveAmount is not > 0m || HaveUnit is not { } unit)
        {
            HaveMakes = 0;
            HaveText = string.Empty;
            return;
        }

        var grams = HaveAmount.Value * unit.Grams;
        var makes = scaler.UnitsFrom(input.Recipe, input.ProductId, ingredient.Id, grams);
        HaveMakes = makes;
        HaveText = makes > 0
            ? $"Makes {RecipeScaler.UnitsText(makes, input.Recipe.QuantityRequired)}, using {RecipeScaler.GramsText(scaler.GramsOf(input.Recipe, makes, input.ProductId, ingredient.Id))} of the {RecipeScaler.GramsText(grams)}."
            : $"Not enough for one: one takes {RecipeScaler.GramsText(scaler.GramsOf(input.Recipe, 1m, input.ProductId, ingredient.Id))}.";
    }

    /// <summary>
    /// Reads the saved recipes of the products a composite holds that haven't been read yet, and those of the products any
    /// of them holds in turn: off the UI thread, once per product. The tab is worked out again when they arrive.
    /// </summary>
    private void ReadMissing(IEnumerable<Product> products)
    {
        List<Product> missing = [.. products.DistinctBy(p => p.Id).Where(p => !_products.ContainsKey(p.Id) && _reading.Add(p.Id))];
        if (missing.Count > 0)
        {
            _ = _work.RunAsync(() => ReadAsync(missing));
        }
    }

    private async Task ReadAsync(IReadOnlyList<Product> products)
    {
        List<(Product Product, ProductDetail Detail)> read = [];
        try
        {
            read = await Task.Run(async () =>
            {
                // A composite held in the composite holds products of its own, which the same read brings with it.
                var details = await _savedProducts.ReadDetailsAsync([.. products.Select(p => p.Id)], CancellationToken.None);
                Dictionary<int, Product> productsRead = products.ToDictionary(p => p.Id);
                foreach (var held in details.Values.SelectMany(d => d.Components).Select(c => c.Component.ComponentProduct!))
                {
                    productsRead.TryAdd(held.Id, held);
                }

                List<(Product Product, ProductDetail Detail)> found = [];
                foreach (var product in productsRead.Values)
                {
                    if (details.TryGetValue(product.Id, out var detail))
                    {
                        found.Add((product, detail));
                    }
                }

                return found;
            });
        }
        catch (Exception ex)
        {
            // Nothing awaits this read, so what went wrong is shown rather than lost. The products keep reading
            // "Reading…", and the next load reads them again.
            _showFailure(ex, "Couldn't read the recipe of a product in the Products table");
        }
        finally
        {
            foreach (var product in products)
            {
                _reading.Remove(product.Id);
            }
        }

        foreach (var (product, detail) in read)
        {
            _products[product.Id] = (product, detail);
        }

        if (read.Count > 0)
        {
            Refresh();
        }
    }
}
