using AwesomeAssertions;
using CsCheck;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// A product's costing draft on its own, with no database and no screen (docs/adr/0006): random sequences of what a user
/// does to a saved product's Costing tab (a percentage typed, or the amount it comes to typed in its place), each an undo
/// step, with what the costs are worked out from changing between them as the form changes it (a recipe or packaging edit
/// on another tab, the cost basis switched, Settings' defaults and rates read again by a load), and undos, redos and Saves
/// among them. Whatever the sequence, the figures are never stale (they're what the inputs and percentages come to now),
/// the percentages are where their history says, the dirty check and Save's <see cref="ProductCosting.IsChanged"/> say
/// whether they moved from what was last saved, and an amount typed shows as typed.
/// </summary>
public class ProductCostingTests
{
    [Test]
    public void The_figures_are_what_the_inputs_and_percentages_come_to_now() =>
        Steps.Array[0, 40].Sample(steps =>
        {
            var world = new World();
            foreach (var step in steps)
            {
                world.Apply(step);
                world.ShouldPriceAsNow($"after {step}");
            }
        }, iter: 300);

    [Test]
    public void Any_sequence_keeps_the_percentages_and_their_history_right_and_Save_sees_only_a_moved_percentage() =>
        Steps.Array[0, 40].Sample(steps =>
        {
            var world = new World();
            foreach (var step in steps)
            {
                world.Apply(step);
                world.ShouldBeRight($"after {step}");
            }

            // Every step left undone, then redone: the history walks back to what was last saved and forward again.
            while (world.Apply(new Undo()))
            {
                world.ShouldBeRight("undoing every step");
            }

            world.History.IsDirty.Should().BeFalse("every step undone gives back what was last saved");
            while (world.Apply(new Redo()))
            {
                world.ShouldBeRight("redoing every step");
            }
        }, iter: 300);

    [Test]
    public void An_amount_typed_shows_as_typed_and_sets_the_percentage_it_comes_to() =>
        Gen.Select(Edits.Array[0, 20], Gen.Int[0, 3], Gen.Int[1, 9999]).Sample((steps, metric, cents) =>
        {
            var world = new World();
            foreach (var step in steps)
            {
                world.Apply(step);
            }

            var ofCost = metric == 3 ? world.Costing.Costs.BaseCost : world.Costing.Costs.SubAssemblies;
            var before = world.Percent(metric);
            var amount = cents / 100m;
            world.Apply(new TypeAmount(metric, cents));

            if (ofCost == 0)
            {
                world.Percent(metric).Should().Be(before, "with nothing to take a percentage of, the percentage stays");
            }
            else
            {
                world.Percent(metric).Should().Be(Math.Round(CostingRules.PercentOf(amount, ofCost), 4, MidpointRounding.ToEven));
                world.Amount(metric).Should().Be(amount, "the box shows what was typed, not the hair off it worked back");
            }
        }, iter: 300);

    [Test]
    public void A_new_product_still_on_the_defaults_follows_every_change_to_them() =>
        Edits.Array[0, 40].Sample(steps =>
        {
            var world = new World(isNew: true);

            // The rule, as a model: new defaults land on the costing only while it shows the ones before them.
            var model = world.Costing.Percentages;
            foreach (var step in steps)
            {
                var defaultsBefore = world.DefaultsAsPercentages;
                world.Apply(step);
                model = step is ReadDefaults && model == defaultsBefore ? world.DefaultsAsPercentages : world.Costing.Percentages;

                world.Costing.Percentages.Should().Be(model, $"after {step}");
                world.History.IsDirty.Should().Be(world.Costing.Percentages != world.Baseline, $"after {step}, defaults landing aren't the user's change");
                world.ShouldPriceAsNow($"after {step}");
            }
        }, iter: 300);

    [Test]
    public void A_new_product_still_on_the_defaults_takes_new_ones_and_one_typed_into_keeps_its_costing()
    {
        var untouched = new ProductCosting(() => new ProductCostingInputs(new ProductCostInputs(), null, []));
        untouched.Load(null);
        untouched.SetDefaults(Defaults(labor: 35m));
        untouched.ApplyDefaults();

        untouched.TakeDefaults(Defaults(labor: 30m), isNewProduct: true).Should().BeTrue();
        untouched.EditingLaborPercent.Should().Be(30m);

        var typed = new ProductCosting(() => new ProductCostingInputs(new ProductCostInputs(), null, []));
        typed.Load(null);
        typed.SetDefaults(Defaults(labor: 35m));
        typed.ApplyDefaults();
        typed.EditingLaborPercent = 40m;

        typed.TakeDefaults(Defaults(labor: 30m), isNewProduct: true).Should().BeFalse();
        typed.EditingLaborPercent.Should().Be(40m, "what was typed stays");

        var saved = new ProductCosting(() => new ProductCostingInputs(new ProductCostInputs(), null, []));
        saved.SetDefaults(Defaults(labor: 35m));
        saved.Load(new CostingParameters { LaborPercent = 35m, OverheadPercent = 25m, AdministrativePercent = 15m, MarkupPercent = 40m });

        saved.TakeDefaults(Defaults(labor: 30m), isNewProduct: false).Should().BeFalse("a saved product's costing is its own");
        saved.EditingLaborPercent.Should().Be(35m);
        saved.IsChanged.Should().BeFalse();
    }

    private static CostingDefaults Defaults(decimal labor, decimal wastage = 3m, decimal vat = 12m, decimal rounding = 1m) => new()
    {
        LaborPercent = labor,
        OverheadPercent = 25m,
        AdministrativePercent = 15m,
        MarkupPercent = 40m,
        WastagePercent = wastage,
        VatPercent = vat,
        PriceRoundingStep = rounding,
    };

    /// <summary>One thing a user does to the Costing tab, as one undo step, or a change the form makes to its inputs.</summary>
    private abstract record Step;

    /// <summary>A metric's percentage typed: labor, overhead, admin, markup (0 to 3).</summary>
    private sealed record TypePercent(int Metric, int Percent) : Step;

    /// <summary>A metric's amount typed in pesos, in its value box.</summary>
    private sealed record TypeAmount(int Metric, int Cents) : Step;

    /// <summary>A recipe or packaging edit on another tab: what the costs are worked out from changes (ProductForm.InvalidateCosts).</summary>
    private sealed record EditRecipe(int Part, int Cents) : Step;

    /// <summary>The product's category or quantity changed on the form: the category's own wastage, or the pieces a unit holds.</summary>
    private sealed record EditProduct(int CategoryWastage, int Quantity) : Step;

    private sealed record ToggleCostBasis : Step;

    /// <summary>Butter priced past its buffer or not, and the dough's piece size taken out or not: the header's warnings.</summary>
    private sealed record EditMaterials(bool ButterBelowCost, bool DoughUnsized) : Step;

    /// <summary>A load read Settings' defaults and the shop's rates again, moved or not.</summary>
    private sealed record ReadDefaults(int Labor, int Vat, int Rounding) : Step;

    private sealed record Undo : Step;

    private sealed record Redo : Step;

    /// <summary>Save: the percentages written, and loaded again as saved, as the form's reload does.</summary>
    private sealed record Save : Step;

    /// <summary>
    /// Every step, undos, redos and Saves among the edits. On a saved product only: a new one takes Settings' defaults
    /// as they land, with no undo step, which the edits alone test (A_new_product_still_on_the_defaults_follows_every_change_to_them).
    /// </summary>
    private static readonly Gen<Step> Steps = Gen.Frequency(
        (12, EditsOf()),
        (3, Gen.Const<Step>(new Undo())),
        (2, Gen.Const<Step>(new Redo())),
        (1, Gen.Const<Step>(new Save())));

    /// <summary>The edits, and the changes the form makes to the costing's inputs between them.</summary>
    private static readonly Gen<Step> Edits = EditsOf();

    private static Gen<Step> EditsOf() => Gen.OneOf<Step>(
        Gen.Select(Gen.Int[0, 3], Gen.Int[0, 120], (metric, percent) => (Step)new TypePercent(metric, percent)),
        Gen.Select(Gen.Int[0, 3], Gen.Int[0, 9999], (metric, cents) => (Step)new TypeAmount(metric, cents)),
        Gen.Select(Gen.Int[0, 2], Gen.Int[0, 5000], (part, cents) => (Step)new EditRecipe(part, cents)),
        Gen.Select(Gen.Int[-1, 10], Gen.Int[1, 12], (wastage, quantity) => (Step)new EditProduct(wastage, quantity)),
        Gen.Const<Step>(new ToggleCostBasis()),
        Gen.Select(Gen.Bool, Gen.Bool, (below, unsized) => (Step)new EditMaterials(below, unsized)),
        Gen.Select(Gen.Int[0, 50], Gen.Int[0, 20], Gen.Int[0, 5], (labor, vat, rounding) => (Step)new ReadDefaults(labor, vat, rounding)));

    /// <summary>The percentages an undo step keeps (ProductFormSnapshot.Costing), as a class for the undo history.</summary>
    private sealed record Snapshot((decimal Labor, decimal Overhead, decimal Administrative, decimal Margin) Percentages);

    /// <summary>
    /// A saved product's costing as its form holds it: the inputs the form gathers (three sections' costs per piece, two
    /// packaging lines, a category, the pieces a unit holds, a material priced past its buffer), Settings' rates, and an undo
    /// history over the percentages recording only the user's steps.
    /// </summary>
    private sealed class World
    {
        private readonly decimal[] _parts = [12.86m, 10.81m, 21.36m];
        private decimal? _categoryWastage;
        private int _quantity = 1;
        private readonly bool _isNew;
        private CostingDefaults _defaults = Defaults(labor: 35m);
        private bool _butterBelowCost = true;
        private bool _doughUnsized;
        private bool _restoring;
        private readonly UndoModel<Snapshot> _model;

        /// <param name="isNew">A new product, on Settings' defaults, as New starts one; else a saved one, at its own costing.</param>
        public World(bool isNew = false)
        {
            _isNew = isNew;
            Costing = new ProductCosting(Inputs);
            Costing.SetDefaults(_defaults);
            if (isNew)
            {
                Costing.Load(null);
                Costing.ApplyDefaults();
            }
            else
            {
                Costing.Load(new CostingParameters { LaborPercent = 25m, OverheadPercent = 20m, AdministrativePercent = 15m, MarkupPercent = 60m });
            }

            History = new UndoHistory<Snapshot>(
                () => new Snapshot(Costing.Percentages), (a, b) => a.Percentages == b.Percentages, () => !_restoring);
            Costing.PropertyChanged += (_, _) => History.RefreshDirty();
            History.MarkClean();
            Baseline = Costing.Percentages;
            _model = new UndoModel<Snapshot>(new Snapshot(Costing.Percentages), (a, b) => a.Percentages == b.Percentages);
        }

        public ProductCosting Costing { get; }

        public UndoHistory<Snapshot> History { get; }

        /// <summary>What the dirty check measures against: as loaded, or the defaults that landed on a new product since.</summary>
        public (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin) Baseline { get; private set; }

        public (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin) DefaultsAsPercentages =>
            (_defaults.LaborPercent, _defaults.OverheadPercent, _defaults.AdministrativePercent, _defaults.MarkupPercent);

        public decimal Percent(int metric) => metric switch
        {
            0 => Costing.EditingLaborPercent,
            1 => Costing.EditingOverheadPercent,
            2 => Costing.EditingAdministrativePercent,
            _ => Costing.EditingMarkupPercent,
        };

        public decimal Amount(int metric) => metric switch
        {
            0 => Costing.EditingLaborAmount,
            1 => Costing.EditingOverheadAmount,
            2 => Costing.EditingAdministrativeAmount,
            _ => Costing.EditingMarkupAmount,
        };

        /// <summary>The figures the draft shows are a fresh pricing of the inputs and percentages as they are now.</summary>
        public void ShouldPriceAsNow(string when)
        {
            var now = new ProductCostSummary(Recipe() with
            {
                WastagePercent = _categoryWastage ?? _defaults.WastagePercent,
                VatPercent = _defaults.VatPercent,
                PriceRoundingStep = _defaults.PriceRoundingStep,
                LaborPercent = Costing.EditingLaborPercent,
                OverheadPercent = Costing.EditingOverheadPercent,
                AdministrativePercent = Costing.EditingAdministrativePercent,
                MarkupPercent = Costing.EditingMarkupPercent,
            });
            var shown = Costing.Costs;
            (shown.SubAssemblies, shown.Packaging, shown.Wastage, shown.Labor, shown.Overhead, shown.Administrative, shown.BaseCost, shown.SellingPrice, shown.RoundedSellingPrice)
                .Should().Be((now.SubAssemblies, now.Packaging, now.Wastage, now.Labor, now.Overhead, now.Administrative, now.BaseCost, now.SellingPrice, now.RoundedSellingPrice), when);
            Costing.CostUnitCaption.Should().Be(_quantity == 1 ? "Per piece" : $"Per unit of {_quantity} pieces", when);
            (Costing.BelowCostText?.Contains("Butter", StringComparison.Ordinal) ?? false).Should().Be(_butterBelowCost, when);
            (Costing.BelowCostText is null).Should().Be(!_butterBelowCost, when);
            (Costing.NoPieceSizeText?.StartsWith("Dough has no weight per piece", StringComparison.Ordinal) ?? false).Should().Be(_doughUnsized, when);
            (Costing.NoPieceSizeText is null).Should().Be(!_doughUnsized, when);
        }

        /// <summary>The percentages, their history and the figures, as the model and the inputs say: on a saved product.</summary>
        public void ShouldBeRight(string when)
        {
            _model.ShouldMatch(History, new Snapshot(Costing.Percentages), when);
            Costing.IsChanged.Should().Be(Costing.Percentages != Baseline, $"{when}, Save sends the costing only when it moved");
            ShouldPriceAsNow(when);
        }

        /// <summary>Takes <paramref name="step"/>, the model with it. True when an undo or a redo moved, or a Save wrote.</summary>
        public bool Apply(Step step)
        {
            switch (step)
            {
                case TypePercent(var metric, var percent):
                    Recorded(() => SetPercent(metric, percent));
                    break;
                case TypeAmount(var metric, var cents):
                    Recorded(() => SetAmount(metric, cents / 100m));
                    break;
                case Undo:
                    var undid = Move(History.TryUndo(out var undone), undone);
                    _model.Undone(undid);
                    return undid;
                case Redo:
                    var redid = Move(History.TryRedo(out var redone), redone);
                    _model.Redone(redid);
                    return redid;
                case Save:
                    // As the form's reload after a Save: the costing loaded as written, with nothing to undo.
                    var written = Costing.Percentages;
                    Costing.Load(new CostingParameters
                    {
                        LaborPercent = written.Labor, OverheadPercent = written.Overhead, AdministrativePercent = written.Administrative, MarkupPercent = written.Margin,
                    });
                    History.Clear();
                    History.MarkClean();
                    Baseline = Costing.Percentages;
                    _model.Saved(new Snapshot(Costing.Percentages));
                    return true;

                // Not this tab's: what the form changes, telling the costing as it does (InvalidateCosts).
                case EditRecipe(var part, var cents):
                    _parts[part] = cents / 100m;
                    Costing.InputsChanged();
                    break;
                case EditProduct(var wastage, var quantity):
                    _categoryWastage = wastage < 0 ? null : wastage;
                    _quantity = quantity;
                    Costing.InputsChanged();
                    break;
                case ToggleCostBasis:
                    Costing.CostBasis = Costing.CostBasis == CostBasis.Buffered ? CostBasis.Unbuffered : CostBasis.Buffered;
                    break;
                case EditMaterials(var below, var unsized):
                    _butterBelowCost = below;
                    _doughUnsized = unsized;
                    Costing.InputsChanged();
                    break;
                case ReadDefaults(var labor, var vat, var rounding):
                    _defaults = Defaults(labor, vat: vat, rounding: rounding);
                    if (Costing.TakeDefaults(_defaults, isNewProduct: _isNew))
                    {
                        // As the form does (RefreshCostingDefaults): the defaults landing aren't the user's change, so what
                        // the dirty check measures against moves with them.
                        History.Rebase((settled, now) => settled with { Percentages = now.Percentages });
                        Baseline = Costing.Percentages;
                    }

                    break;
            }

            return false;
        }

        private bool Move(bool moved, Snapshot snapshot)
        {
            if (moved)
            {
                _restoring = true;
                Costing.Percentages = snapshot.Percentages;
                _restoring = false;
            }

            return moved;
        }

        private void Recorded(Action edit)
        {
            var was = new Snapshot(Costing.Percentages);
            History.Begin();
            edit();
            History.End();
            _model.Edited(was, new Snapshot(Costing.Percentages));
        }

        private void SetPercent(int metric, decimal percent)
        {
            switch (metric)
            {
                case 0: Costing.EditingLaborPercent = percent; break;
                case 1: Costing.EditingOverheadPercent = percent; break;
                case 2: Costing.EditingAdministrativePercent = percent; break;
                default: Costing.EditingMarkupPercent = percent; break;
            }
        }

        private void SetAmount(int metric, decimal amount)
        {
            switch (metric)
            {
                case 0: Costing.EditingLaborAmount = amount; break;
                case 1: Costing.EditingOverheadAmount = amount; break;
                case 2: Costing.EditingAdministrativeAmount = amount; break;
                default: Costing.EditingMarkupAmount = amount; break;
            }
        }

        /// <summary>What the form gathers for the costs (ProductForm.CostingInputs): the sections and packaging at the basis shown.</summary>
        private ProductCostingInputs Inputs() => new(
            Recipe(),
            _categoryWastage,
            [new RawMaterial { Id = 1, Name = "Butter", CostPerBaseUnit = 0.6m, BufferedCostPerBaseUnit = _butterBelowCost ? 0.5m : 0.66m }])
        {
            SectionsWithoutPieceSize = _doughUnsized ? ["Dough"] : [],
        };

        private ProductCostInputs Recipe()
        {
            var factor = Costing.CostBasis == CostBasis.Unbuffered ? 0.9m : 1m;
            return new ProductCostInputs
            {
                Parts = [("Dough", _parts[0] * factor), ("Topping", _parts[1] * factor), ("Filling", _parts[2] * factor)],
                PackagingCosts = [0.85m * factor, 0.42m * factor],
                QuantityRequired = _quantity,
                CurrentPrice = 95m,
            };
        }
    }
}
