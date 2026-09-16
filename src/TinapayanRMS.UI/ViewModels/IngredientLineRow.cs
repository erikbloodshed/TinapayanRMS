using ReactiveUI;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// One row of an ingredient lines table that is typed into directly: the sub-assembly form's lines and a
/// made ingredient's Bill of Materials. The grid's ingredient cell sets <see cref="IngredientId"/>; the
/// screen points <see cref="Ingredient"/> at that ingredient (<see cref="Resolve"/>), which prices the row.
/// A row can be half filled in: <see cref="CompleteLines"/> checks them all before they're used.
/// </summary>
public sealed class IngredientLineRow : ReactiveObject
{
    /// <summary>Looks up an ingredient by id as the screen has it; null for a row the screen resolves itself.</summary>
    private readonly Func<int, RawMaterial?>? _findIngredient;

    public IngredientLineRow()
    {
    }

    /// <summary>
    /// A new row that prices itself as soon as its ingredient is picked. The grid's add row edits one of these
    /// (controls:GridAddNewRow.ItemFactory): until it's committed it isn't in the screen's lines, so the screen
    /// can't resolve it, and its Cost would stay 0 while its grams are typed.
    /// </summary>
    public IngredientLineRow(Func<int, RawMaterial?> findIngredient)
    {
        _findIngredient = findIngredient;
    }

    public IngredientLineRow(RawMaterial ingredient, decimal grams)
    {
        _ingredientId = ingredient.Id;
        _ingredient = ingredient;
        _grams = grams;
    }

    private int? _ingredientId;
    public int? IngredientId
    {
        get => _ingredientId;
        set
        {
            this.RaiseAndSetIfChanged(ref _ingredientId, value);
            if (_findIngredient is not null)
            {
                Ingredient = value is { } id ? _findIngredient(id) : null;
            }
        }
    }

    private RawMaterial? _ingredient;
    /// <summary>The ingredient <see cref="IngredientId"/> names, as the screen last loaded it.</summary>
    public RawMaterial? Ingredient
    {
        get => _ingredient;
        private set
        {
            this.RaiseAndSetIfChanged(ref _ingredient, value);
            this.RaisePropertyChanged(nameof(CostPerGram));
            this.RaisePropertyChanged(nameof(Cost));
        }
    }

    /// <summary>What a gram of this line's ingredient costs, buffered; 0 until one is picked.</summary>
    public decimal CostPerGram => Ingredient?.BufferedCostPerBaseUnit ?? 0m;

    private decimal _grams;
    public decimal Grams
    {
        get => _grams;
        set
        {
            this.RaiseAndSetIfChanged(ref _grams, value);
            this.RaisePropertyChanged(nameof(Cost));
        }
    }

    /// <summary>What this line adds to the batch's cost, at the ingredient's buffered cost per gram.</summary>
    public decimal Cost => Grams * (Ingredient?.BufferedCostPerBaseUnit ?? 0m);

    /// <summary>Nothing typed into it: the grid's add row left as it was, which isn't a line at all.</summary>
    private bool IsBlank => IngredientId is null && Grams == 0m;

    /// <summary>
    /// Points each row at its ingredient in <paramref name="ingredientsById"/> (the screen's current copies,
    /// so a refreshed cost shows). A row whose ingredient isn't there keeps the one it has, if it's the same.
    /// </summary>
    public static void Resolve(IEnumerable<IngredientLineRow> rows, IReadOnlyDictionary<int, RawMaterial> ingredientsById)
    {
        foreach (var row in rows)
        {
            var resolved = row.IngredientId is { } id
                ? ingredientsById.GetValueOrDefault(id) ?? (row.Ingredient?.Id == id ? row.Ingredient : null)
                : null;
            if (!ReferenceEquals(resolved, row.Ingredient))
            {
                row.Ingredient = resolved;
            }
        }
    }

    /// <summary>
    /// The rows as lines, leaving out blank ones. A row with only an ingredient or only grams is refused, and so
    /// is an ingredient on two rows, each with a message naming it and <paramref name="madeInto"/>.
    /// </summary>
    public static IReadOnlyList<(RawMaterial Ingredient, decimal Grams)> CompleteLines(IEnumerable<IngredientLineRow> rows, string madeInto)
    {
        List<(RawMaterial Ingredient, decimal Grams)> lines = [];
        foreach (var row in rows.Where(r => !r.IsBlank))
        {
            if (row.Ingredient is not { } ingredient)
            {
                throw new InvalidOperationException($"Pick the ingredient for the {row.Grams:#,0.##} g line of {madeInto}, or delete that line.");
            }

            if (row.Grams <= 0m)
            {
                throw new InvalidOperationException($"Enter how many grams of {ingredient.Name} go into {madeInto}, or delete its line.");
            }

            if (lines.Any(l => l.Ingredient.Id == ingredient.Id))
            {
                throw new InvalidOperationException($"{ingredient.Name} is on two lines of {madeInto}. Put its total on one line and delete the other.");
            }

            lines.Add((ingredient, row.Grams));
        }

        return lines;
    }
}
