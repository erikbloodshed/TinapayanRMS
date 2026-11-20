using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Presentation.Shared;

/// <summary>
/// One row of an ingredient lines table that is typed into directly: the sub-assembly form's lines and a
/// made ingredient's Bill of Materials. The grid's ingredient cell sets <see cref="IngredientId"/>; the
/// screen points <see cref="Ingredient"/> at that ingredient (<see cref="Resolve"/>), which prices the row.
/// A row can be half filled in: <see cref="CompleteLines"/> checks them all before they're used.
/// </summary>
public sealed class IngredientLineRow : ObservableRow
{
    /// <summary>Looks up an ingredient by id as the screen has it; null for a row the screen resolves itself.</summary>
    private readonly Func<int, RawMaterial?>? _findIngredient;

    public IngredientLineRow()
    {
    }

    /// <summary>
    /// A new row that prices itself as soon as its ingredient is picked. The table's add row edits one of these
    /// (LineGrid's NewItem): until it's committed it isn't in the screen's lines, so the screen
    /// can't resolve it, and its Cost would stay 0 while its grams are typed.
    /// </summary>
    /// <param name="baseWeight">The formula's base as the table has it, so a percentage can be typed into the add row too.</param>
    public IngredientLineRow(Func<int, RawMaterial?> findIngredient, decimal baseWeight = 0m)
    {
        _findIngredient = findIngredient;
        _baseWeight = baseWeight;
    }

    /// <param name="authoredBakersPercent">The percentage typed for the line, or null when its grams were.</param>
    public IngredientLineRow(RawMaterial ingredient, decimal grams, bool isFormulaBase = false, decimal? authoredBakersPercent = null)
    {
        _ingredientId = ingredient.Id;
        _ingredient = ingredient;
        _grams = grams;
        _isFormulaBase = isFormulaBase;
        _authoredBakersPercent = isFormulaBase ? null : authoredBakersPercent;
    }

    /// <summary>
    /// A row put back exactly as it stood, which is what an undo needs (ProductsState's history). The
    /// public setters can't do it: typing grams or ticking the base lets a typed percentage go, and
    /// <see cref="Percent"/> refuses to take one while there's no base to read it against — a state a row is
    /// allowed to be in, and the one case where restoring through the setters would lose the figure silently.
    /// The row is left half filled in if that's how it stood, ingredient and all, which the other constructors
    /// can't express either. <see cref="BaseWeight"/> and <see cref="BatchWeight"/> aren't taken: the caller's
    /// single <see cref="ApplyFormulaBase(IEnumerable{IngredientLineRow}, decimal, decimal)"/> pass sets both.
    /// </summary>
    public static IngredientLineRow Restore(
        Func<int, RawMaterial?> findIngredient,
        decimal baseWeight,
        int? ingredientId,
        decimal? grams,
        bool isFormulaBase,
        decimal? authoredBakersPercent)
    {
        var row = new IngredientLineRow(findIngredient, baseWeight);
        row._ingredientId = ingredientId;
        row._ingredient = ingredientId is { } id ? findIngredient(id) : null;
        row._grams = grams;
        row._isFormulaBase = isFormulaBase;
        row._authoredBakersPercent = authoredBakersPercent;
        return row;
    }

    private int? _ingredientId;
    public int? IngredientId
    {
        get => _ingredientId;
        set
        {
            RaiseAndSetIfChanged(ref _ingredientId, value);
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
            RaiseAndSetIfChanged(ref _ingredient, value);
            RaisePropertyChanged(nameof(CostPerGram));
            RaisePropertyChanged(nameof(Cost));
        }
    }

    private CostBasis _costBasis;
    /// <summary>
    /// Which of the ingredient's costs the row is priced at: buffered, as every screen prices a recipe, unless the
    /// Products screen has been switched to its unbuffered costs.
    /// </summary>
    public CostBasis CostBasis
    {
        get => _costBasis;
        set
        {
            if (RaiseAndSetIfChanged(ref _costBasis, value))
            {
                RaisePropertyChanged(nameof(CostPerGram));
                RaisePropertyChanged(nameof(Cost));
            }
        }
    }

    /// <summary>What a gram of this line's ingredient costs at <see cref="CostBasis"/>, buffered unless switched; 0 until one is picked.</summary>
    public decimal CostPerGram => Ingredient is { } ingredient ? RawMaterialRules.CostPerBaseUnitAt(ingredient, CostBasis) : 0m;

    private decimal? _grams;
    /// <summary>
    /// The line's grams; null until they're typed, so the grid's cell is empty rather than 0.00 (AllowNullValue).
    /// Typing them makes them the figure that stays put, so any typed percentage is let go.
    /// </summary>
    public decimal? Grams
    {
        get => _grams;
        set
        {
            AuthoredBakersPercent = null;
            SetGrams(value);
        }
    }

    /// <summary>Grams worked out rather than typed: from a typed percentage, which stays the figure that's kept.</summary>
    private void SetGrams(decimal? value)
    {
        RaiseAndSetIfChanged(ref _grams, value, nameof(Grams));
        RaisePropertyChanged(nameof(Percent));
        RaisePropertyChanged(nameof(Cost));
    }

    private bool _isFormulaBase;
    /// <summary>
    /// Ticked as part of the formula's 100% base — usually the flour. Every line's <see cref="Percent"/>
    /// is read against the ticked lines' total, so ticking one changes the whole table: the screen sums them
    /// and pushes the total back through <see cref="ApplyFormulaBase(IReadOnlyList{IngredientLineRow})"/>.
    /// </summary>
    public bool IsFormulaBase
    {
        get => _isFormulaBase;
        set
        {
            // The base is 100% between its lines, so a line ticked into it keeps its grams and lets its percentage go.
            if (value)
            {
                AuthoredBakersPercent = null;
            }

            RaiseAndSetIfChanged(ref _isFormulaBase, value);
            RaisePropertyChanged(nameof(CanTypeBakersPercent));
        }
    }

    private decimal _baseWeight;
    /// <summary>The formula's base weight, as the screen last worked it out. Not the row's own business to know.</summary>
    public decimal BaseWeight
    {
        get => _baseWeight;
        private set
        {
            // Only a base that moved changes what reads it: the formula's base is set on every line each time it's worked
            // out (ApplyFormulaBase), every load of every open tab included, and each raise runs every subscription to the row.
            if (RaiseAndSetIfChanged(ref _baseWeight, value))
            {
                RaisePropertyChanged(nameof(Percent));
                RaisePropertyChanged(nameof(CanTypeBakersPercent));
                RaisePropertyChanged(nameof(IsPercentTyped));
            }
        }
    }

    private decimal _batchWeight;
    /// <summary>
    /// The whole batch's weight, as the screen last worked it out: what <see cref="Percent"/> is read against while
    /// nothing is ticked as the base. On a linked recipe that's the recipe and its add-ins together.
    /// </summary>
    public decimal BatchWeight
    {
        get => _batchWeight;
        private set
        {
            if (RaiseAndSetIfChanged(ref _batchWeight, value))
            {
                RaisePropertyChanged(nameof(Percent));
            }
        }
    }

    private decimal? _authoredBakersPercent;
    /// <summary>
    /// The percentage typed for the line, which is then the figure that stays put: when the base changes, its grams are
    /// worked out again from it. Null when its grams were typed, as a base line's always are. Saved with the line.
    /// </summary>
    public decimal? AuthoredBakersPercent
    {
        get => _authoredBakersPercent;
        private set
        {
            RaiseAndSetIfChanged(ref _authoredBakersPercent, value);
            RaisePropertyChanged(nameof(IsPercentTyped));
        }
    }

    /// <summary>
    /// The line's percentage, not its grams, is what was typed. Only while there's a base, since the figure showing
    /// without one is the absolute percentage, which is never typed.
    /// </summary>
    public bool IsPercentTyped => AuthoredBakersPercent is not null && BaseWeight > 0;

    /// <summary>
    /// Whether a percentage can be typed for the line: not for a base line, which is 100% between its lines by
    /// definition and given in grams, and not while nothing is ticked, when the absolute percentage showing is
    /// only a reading of the grams.
    /// </summary>
    public bool CanTypeBakersPercent => !IsFormulaBase && BaseWeight > 0;

    /// <summary>
    /// The line's percentage column. With a base ticked it's baker's percentage: the line's weight against the base, so
    /// the base comes to 100 and 650 g of water on 1,000 g of flour comes to 65. With nothing ticked (a glaze or a filling
    /// needn't have a base) it's absolute percentage instead: the line's weight against the whole batch, so the lines come
    /// to 100 between them (SubAssemblyRules.CalculateAbsolutePercent), and the column's header says which it is. Only a
    /// baker's percentage can be typed: that works out the grams (SubAssemblyRules.GramsFromBakersPercent) and makes the
    /// percentage the figure that stays put; clearing it keeps the grams and makes those the figure instead.
    /// </summary>
    public decimal? Percent
    {
        get => AuthoredBakersPercent is { } authored && BaseWeight > 0
            ? authored
            : Grams is not { } grams
                ? null
                : BaseWeight <= 0
                    ? SubAssemblyRules.CalculateAbsolutePercent(grams, BatchWeight)
                    : SubAssemblyRules.CalculateBakersPercent(grams, BaseWeight);
        set
        {
            // The table's cell is read-only where it can't be typed (IngredientLinesGrid); this is the backstop. Checked
            // first, so a line whose typed baker's percentage is waiting for a base to come back doesn't lose it.
            if (!CanTypeBakersPercent)
            {
                RaisePropertyChanged(nameof(Percent));
                return;
            }

            if (value is not > 0m)
            {
                AuthoredBakersPercent = null;
                RaisePropertyChanged(nameof(Percent));
                return;
            }

            AuthoredBakersPercent = value;
            SetGrams(SubAssemblyRules.GramsFromBakersPercent(value.Value, BaseWeight));
        }
    }

    /// <summary>What this line adds to the batch's cost, at <see cref="CostPerGram"/>.</summary>
    public decimal Cost => (Grams ?? 0m) * CostPerGram;

    /// <summary>Nothing typed into it: the grid's add row left as it was, which isn't a line at all.</summary>
    private bool IsBlank => IngredientId is null && Grams is null or 0m;

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
    /// Works out the formula's base from <paramref name="rows"/> — the ticked lines' total weight — and gives
    /// it to every row, with the rows' batch weight, so each can read its own percentage. Returns the base, which the
    /// screen also puts in its caption. Call it wherever <see cref="Resolve"/> is called: a tick, a gram or a line
    /// coming or going all move it.
    /// </summary>
    public static decimal ApplyFormulaBase(IReadOnlyList<IngredientLineRow> rows) =>
        ApplyFormulaBase(rows, BatchWeightOf(rows));

    /// <summary>The same, against a batch weight worked out elsewhere: a linked recipe's, which its add-ins are part of.</summary>
    public static decimal ApplyFormulaBase(IReadOnlyList<IngredientLineRow> rows, decimal batchWeight)
    {
        // A typed percentage is never on a base line, so reworking those lines' grams can't move the base itself.
        var baseWeight = SubAssemblyRules.FormulaBaseWeight([.. rows.Select(r => (r.Grams ?? 0m, r.IsFormulaBase))]);
        ApplyFormulaBase(rows, baseWeight, batchWeight);
        return baseWeight;
    }

    /// <summary>
    /// The same, against a base and a batch worked out elsewhere: a linked recipe's add-ins are read against the recipe's
    /// base, not against their own weight, since an add-in is never part of the base.
    /// </summary>
    public static void ApplyFormulaBase(IEnumerable<IngredientLineRow> rows, decimal baseWeight, decimal batchWeight)
    {
        foreach (var row in rows)
        {
            row.BaseWeight = baseWeight;
            row.BatchWeight = batchWeight;

            // A line typed as a percentage keeps it: its grams follow the base. With no base there's nothing to take a
            // share of, so it keeps its last grams, and its percentage waits for a base to come back.
            if (row.AuthoredBakersPercent is { } percent && baseWeight > 0)
            {
                row.SetGrams(SubAssemblyRules.GramsFromBakersPercent(percent, baseWeight));
            }
        }
    }

    /// <summary>The batch the rows make: the grams of every row with its ingredient picked, as the screens' batch weight counts them.</summary>
    public static decimal BatchWeightOf(IEnumerable<IngredientLineRow> rows) =>
        rows.Where(r => r.Ingredient is not null).Sum(r => r.Grams ?? 0m);

    /// <summary>The percentage column's header: "Baker's %" while there's a base, "Absolute %" while nothing is ticked.</summary>
    public static string PercentHeader(decimal baseWeight) => baseWeight > 0 ? "Baker's %" : "Absolute %";

    /// <summary>
    /// The rows as lines, leaving out blank ones. A row with only an ingredient or only grams is refused, and so
    /// is an ingredient on two rows, each with a message naming it and <paramref name="madeInto"/>.
    /// </summary>
    public static IReadOnlyList<(RawMaterial Ingredient, decimal Grams, bool IsFormulaBase, decimal? AuthoredBakersPercent)> CompleteLines(
        IEnumerable<IngredientLineRow> rows, string madeInto)
    {
        List<(RawMaterial Ingredient, decimal Grams, bool IsFormulaBase, decimal? AuthoredBakersPercent)> lines = [];
        foreach (var row in rows.Where(r => !r.IsBlank))
        {
            if (row.Ingredient is not { } ingredient)
            {
                throw new BusinessRuleException($"Pick the ingredient for the {row.Grams:#,0.##} g line of {madeInto}, or delete that line.");
            }

            if (row.Grams is not > 0m)
            {
                throw new BusinessRuleException($"Enter how many grams of {ingredient.Name} go into {madeInto}, or delete its line.");
            }

            if (lines.Any(l => l.Ingredient.Id == ingredient.Id))
            {
                throw new BusinessRuleException($"{ingredient.Name} is on two lines of {madeInto}. Put its total on one line and delete the other.");
            }

            lines.Add((ingredient, row.Grams.Value, row.IsFormulaBase, row.AuthoredBakersPercent));
        }

        return lines;
    }
}

/// <summary>
/// One ingredient line as <see cref="IngredientLineRow.Restore"/> can put it back. Its ingredient travels as
/// an id rather than the object: a LoadCommand landing mid-gesture swaps in freshly priced RawMaterials, and
/// comparing those by reference would read as an edit the user never made.
/// </summary>
public readonly record struct IngredientLineSnapshot(int? IngredientId, decimal? Grams, bool IsFormulaBase, decimal? AuthoredBakersPercent);
