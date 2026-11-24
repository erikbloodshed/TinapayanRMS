using System.Collections.ObjectModel;
using AwesomeAssertions;
using CsCheck;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.RawMaterials;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Tests.Fixtures;

namespace TinapayanRMS.Presentation.Tests.RawMaterials;

/// <summary>
/// The Raw Materials draft on its own, with no database and no screen (docs/adr/0006): random sequences of what a user does
/// to a new material (its name, type, source, category, weight loss, a link and its shares, a base unit, the cost buffer and
/// preferred brand, brand prices, alternative units, a made ingredient's sub-assembly and add-ins), each an undo step, with
/// undos, redos and Saves among them, loads that bring fresh copies of what it refers to, renamed or repriced or not, and
/// Settings' cost buffer for a new material read again, moved or not.
/// After every step the draft is where its history says it is, the dirty check says whether it would save differently from
/// what was last saved, and a made ingredient's totals are what its lines give as they stand (docs/adr/0012). CsCheck
/// shrinks a failure to the shortest sequence that shows it.
/// </summary>
public class RawMaterialDraftTests
{
    [Test]
    public void Any_sequence_keeps_the_draft_its_history_and_its_totals_right() =>
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
    public void A_load_changes_nothing_the_draft_saves() =>
        Steps.Array[0, 30].Sample(steps =>
        {
            var world = new World();
            foreach (var step in steps)
            {
                world.Apply(step);
            }

            var before = world.Draft.Capture(0);
            var wasDirty = world.History.IsDirty;
            world.Apply(new Load(CostsChanged: true, Renamed: true));

            world.Draft.Capture(0).SavesSameAs(before).Should().BeTrue("a load re-points the draft at fresh copies, by id");
            world.History.IsDirty.Should().Be(wasDirty);
        }, iter: 300);

    [Test]
    public void Putting_back_what_was_captured_changes_nothing() =>
        Steps.Array[0, 30].Sample(steps =>
        {
            var world = new World();
            foreach (var step in steps)
            {
                world.Apply(step);
            }

            var captured = world.Draft.Capture(0);
            world.Draft.Restore(captured);

            world.Draft.Capture(0).ShowsSameAs(captured).Should().BeTrue();
        }, iter: 300);

    [Test]
    public void A_packaging_material_is_always_purchased_and_a_made_one_starts_at_three_percent()
    {
        var world = new World();

        world.Apply(new PickSource(1));
        world.Draft.Source.Should().Be(RawMaterialSource.Made);
        world.Draft.WeightLossPercent.Should().Be(3m);

        world.Apply(new PickType(1));
        world.Draft.Type.Should().Be(RawMaterialType.Packaging);
        world.Draft.Source.Should().Be(RawMaterialSource.Purchased, "a packaging material is always bought");
        world.Draft.WeightLossPercent.Should().BeNull();
    }

    [Test]
    public void A_save_writes_only_what_the_type_and_source_take()
    {
        var world = new World();
        world.Apply(new Rename("  Sugar "));
        world.Apply(new PickCategory(0));
        world.Apply(new TypeWeightLoss(5));

        var material = world.Draft.ToMaterial(7, savedVersion: null);
        (material.Id, material.Name, material.Source, material.CategoryId).Should().Be((7, "Sugar", RawMaterialSource.Purchased, 11));
        material.WeightLossPercent.Should().BeNull("only a made ingredient keeps a weight loss");
        world.Draft.ToSaveRequest(material).AddIns.Should().BeNull("only a made ingredient's add-ins are written");
    }

    [Test]
    public void A_made_ingredients_totals_take_its_sub_assemblys_lines_less_the_weight_loss()
    {
        var world = new World();
        world.Apply(new PickSource(1));
        world.Apply(new PickSubAssembly(0));

        var (grams, _) = IngredientBomRules.CalculateCost([(500m, 0.42m * 1.1m), (100m, 0.09m * 1.1m)], 3m);
        world.Draft.Recipe.BomTotalWeight.Should().Be(grams);
        world.Draft.Recipe.BomTotalCost.Should().Be(500m * 0.42m * 1.1m + 100m * 0.09m * 1.1m);
    }

    [Test]
    public void A_buffer_typed_and_typed_back_before_the_default_moved_comes_back_at_the_new_one()
    {
        var world = new World();
        world.Apply(new TypeBuffer(12));
        world.Apply(new TypeDefaultBuffer());
        world.Apply(new MoveDefault(15));
        world.Buffer.Should().Be((15m, 15m), "it was showing the default, so it takes the new one");

        world.Apply(new Undo()).Should().BeTrue();
        world.Buffer.Should().Be((12m, 12m));
        world.Apply(new Undo()).Should().BeTrue();
        world.Buffer.Should().Be((15m, 15m), "the step before the typing showed the default, which is 15 now");
        world.History.IsDirty.Should().BeFalse("it's back where it started, so there's nothing for Save");

        world.Apply(new Redo()).Should().BeTrue();
        world.Apply(new Redo()).Should().BeTrue();
        world.Buffer.Should().Be((15m, 15m), "typed back to the default, which is 15 now");
        world.History.IsDirty.Should().BeFalse();
    }

    [Test]
    public void A_buffer_typed_before_the_default_moved_and_undone_after_comes_back_at_the_new_one_and_follows_it()
    {
        var world = new World();
        world.Apply(new TypeBuffer(12));
        world.Apply(new MoveDefault(15));
        world.Buffer.Should().Be((12m, 12m), "what was typed stays");

        world.Apply(new Undo()).Should().BeTrue();
        world.Buffer.Should().Be((15m, 15m), "the step before the typing showed the default, which is 15 now");
        world.History.IsDirty.Should().BeFalse("it's back where it started, so there's nothing for Save");

        world.Apply(new MoveDefault(18));
        world.Buffer.Should().Be((18m, 18m), "back on the default, it follows it again");
        world.History.IsDirty.Should().BeFalse();
    }

    [Test]
    public void A_buffer_typed_over_a_price_keeps_its_buffered_cost_when_the_default_moves()
    {
        var world = new World();
        world.Apply(new AddPricedBrand(0, 500));
        world.Apply(new TypeDefaultBuffer());
        world.Draft.PurchaseCost.EditingBufferedCost.Should().Be(0.055m, "₱0.05 a gram with the 10% on top, fixed");

        world.Apply(new MoveDefault(15));

        world.Buffer.Should().Be((10m, 10m), "the buffer typed over a price fixed a buffered cost of the user's, which the % follows");
        world.Draft.PurchaseCost.EditingBufferedCost.Should().Be(0.055m);
        world.Draft.PurchaseCost.IsBufferOffTarget.Should().BeFalse("the target is the % typed, not a default it was never on");
    }

    [Test]
    public void A_default_read_again_unchanged_or_on_a_saved_material_moves_nothing()
    {
        var world = new World();
        world.Draft.TakeDefaultCostBuffer(10m, 10m, isNewMaterial: true).Should().BeNull("nothing moved, so no step needs rewriting");
        world.Buffer.Should().Be((10m, 10m));

        // Saved unpriced at the default, it looks just like a new one still on it: only its being saved keeps it.
        world.Apply(new Rename("Sugar"));
        world.Apply(new PickCategory(0));
        world.Apply(new Save()).Should().BeTrue();
        world.Apply(new MoveDefault(15));
        world.Buffer.Should().Be((10m, 10m), "a saved material's buffer is its own");
        world.History.IsDirty.Should().BeFalse();
    }

    /// <summary>One thing a user does to the draft, as one undo step; an undo, a redo or a Save; or a load between them.</summary>
    private abstract record Step;

    private sealed record Rename(string Name) : Step;

    private sealed record PickType(int Index) : Step;

    private sealed record PickSource(int Index) : Step;

    private sealed record PickCategory(int Index) : Step;

    private sealed record TypeWeightLoss(int Percent) : Step;

    private sealed record PickLinkedTo(int Index) : Step;

    private sealed record TypeShares(int Weight, int Cost) : Step;

    private sealed record PickBaseUnit(int Index) : Step;

    private sealed record TypeBuffer(int Percent) : Step;

    /// <summary>The Cost Buffer % typed back to Settings' default, as it stands: a new material shows its default again.</summary>
    private sealed record TypeDefaultBuffer : Step;

    private sealed record TypePreferredBrand(int Index) : Step;

    /// <summary>A brand row with a price and no unit yet, which prices nothing.</summary>
    private sealed record AddBrandPrice(int Brand, int Price) : Step;

    /// <summary>A brand row priced per base unit, in ten-thousandths: the material is costed at it, and a % typed fixes a buffered cost.</summary>
    private sealed record AddPricedBrand(int Brand, int Price) : Step;

    private sealed record RemoveBrandPrice(int Row) : Step;

    private sealed record AddUnit(int Unit, int Multiplier) : Step;

    private sealed record RemoveUnit(int Row) : Step;

    private sealed record PickSubAssembly(int Index) : Step;

    private sealed record AddAddIn(int Ingredient, int Grams) : Step;

    private sealed record TypeAddInGrams(int Row, int Grams) : Step;

    private sealed record RemoveAddIn(int Row) : Step;

    private sealed record Undo : Step;

    private sealed record Redo : Step;

    /// <summary>Save, as the form does it: refused when a rule refuses it, else what was saved is what's measured against.</summary>
    private sealed record Save : Step;

    /// <summary>
    /// The view's load: fresh copies of the categories, materials, units and sub-assemblies, as saved elsewhere since:
    /// repriced, renamed, both or neither.
    /// </summary>
    private sealed record Load(bool CostsChanged, bool Renamed) : Step;

    /// <summary>
    /// A load reading Settings' cost buffer for a new material, moved or not, and the form taking it
    /// (RawMaterialForm.RefreshDefaultCostBuffer): not the user's change.
    /// </summary>
    private sealed record MoveDefault(int Percent) : Step;

    private static readonly string[] Names = ["Sugar", "Egg Yolk", " Ribbon ", ""];

    private static readonly string?[] Brands = [null, "Sugarland", "Victory"];

    private static readonly Gen<Step> Steps = Gen.Frequency(
        (12, Gen.OneOf<Step>(
            Gen.Int[0, 3].Select(index => (Step)new Rename(Names[index])),
            Gen.Int[0, 1].Select(index => (Step)new PickType(index)),
            Gen.Int[0, 2].Select(index => (Step)new PickSource(index)),
            Gen.Int[0, 5].Select(index => (Step)new PickCategory(index)),
            Gen.Int[0, 20].Select(percent => (Step)new TypeWeightLoss(percent)),
            Gen.Int[0, 2].Select(index => (Step)new PickLinkedTo(index)),
            Gen.Select(Gen.Int[1, 100], Gen.Int[1, 100], (weight, cost) => (Step)new TypeShares(weight, cost)),
            Gen.Int[0, 2].Select(index => (Step)new PickBaseUnit(index)),
            Gen.Int[0, 50].Select(percent => (Step)new TypeBuffer(percent)),
            Gen.Const<Step>(new TypeDefaultBuffer()),
            Gen.Int[0, 2].Select(index => (Step)new TypePreferredBrand(index)),
            Gen.Select(Gen.Int[0, 2], Gen.Int[1, 2000], (brand, price) => (Step)new AddBrandPrice(brand, price)),
            Gen.Select(Gen.Int[0, 2], Gen.Int[1, 2000], (brand, price) => (Step)new AddPricedBrand(brand, price)),
            Gen.Int[0, 9].Select(row => (Step)new RemoveBrandPrice(row)),
            Gen.Select(Gen.Int[0, 2], Gen.Int[1, 1000], (unit, multiplier) => (Step)new AddUnit(unit, multiplier)),
            Gen.Int[0, 9].Select(row => (Step)new RemoveUnit(row)),
            Gen.Int[0, 1].Select(index => (Step)new PickSubAssembly(index)),
            Gen.Select(Gen.Int[0, 2], Gen.Int[0, 500], (ingredient, grams) => (Step)new AddAddIn(ingredient, grams)),
            Gen.Select(Gen.Int[0, 9], Gen.Int[0, 500], (row, grams) => (Step)new TypeAddInGrams(row, grams)),
            Gen.Int[0, 9].Select(row => (Step)new RemoveAddIn(row)))),
        (3, Gen.Const<Step>(new Undo())),
        (2, Gen.Const<Step>(new Redo())),
        (1, Gen.Const<Step>(new Save())),
        (2, Gen.Select(Gen.Bool, Gen.Bool, (costs, renamed) => (Step)new Load(costs, renamed))),
        (2, Gen.Int[5, 15].Select(percent => (Step)new MoveDefault(percent))));

    /// <summary>The lines each of the sub-assemblies a made ingredient can be made from has, as saved: (ingredient, grams).</summary>
    private static readonly Dictionary<int, (int Ingredient, decimal Grams)[]> SubAssemblyLines = new()
    {
        [41] = [(23, 500m), (22, 100m)],
        [42] = [(22, 300m), (21, 50m)],
    };

    /// <summary>
    /// A new material's draft as a form holds it: the view's lists it reads, the sub-assembly's lines read as a pick or a load
    /// shows another (RawMaterialForm.LoadRecipeLinesAsync), an undo history over it recording only the user's steps and
    /// measuring what a Save would write as the form does (SavesSameAs), and beside it a model of that history.
    /// </summary>
    private sealed class World : IRawMaterialDraftHost
    {
        private readonly List<SubAssembly> _subAssemblies = [];
        private readonly UndoModel<RawMaterialFormSnapshot> _model;
        private bool _restoring;
        private int _loads;
        private int _renames;

        /// <summary>Settings' cost buffer for a new material, as the form last read it (RawMaterialForm's _defaultCostBufferPercent).</summary>
        private decimal _defaultBuffer = 10m;

        /// <summary>The material isn't saved yet: once it is, it's the saved material's form, and Settings' buffer isn't its.</summary>
        private bool _isNew = true;

        public World()
        {
            Load(costsChanged: false, renamed: false);
            Draft = new RawMaterialDraft(
                this,
                new RawMaterialLists(Categories, Ingredients, UnitsOfMeasure),
                new RawMaterialRowChecks(_ => { }, _ => { }));
            History = new UndoHistory<RawMaterialFormSnapshot>(
                () => Draft.Capture(0), (a, b) => a.ShowsSameAs(b), () => !_restoring, (a, b) => a.SavesSameAs(b));
            Draft.Changed += History.RefreshDirty;

            // As the form reads the recipe's lines: whenever the pick shows another sub-assembly, and on every load.
            Draft.Recipe.SubAssemblies.Subscribe(ReadRecipeLines);

            Draft.Rebase(null, null, null, null, _subAssemblies);
            Draft.Clear(RawMaterialType.Ingredient, categoryId: null, RawMaterialSource.Purchased, _defaultBuffer);
            History.MarkClean();
            _model = new UndoModel<RawMaterialFormSnapshot>(Draft.Capture(0), (a, b) => a.ShowsSameAs(b), (a, b) => a.SavesSameAs(b));
        }

        public ObservableCollection<RawMaterialCategory> Categories { get; } = [];

        public ObservableCollection<RawMaterial> Ingredients { get; } = [];

        public ObservableCollection<UnitOfMeasure> UnitsOfMeasure { get; } = [];

        public RawMaterialDraft Draft { get; }

        public UndoHistory<RawMaterialFormSnapshot> History { get; }

        /// <summary>The Cost Buffer % and its target, as the form shows them.</summary>
        public (decimal? Percent, decimal? Target) Buffer => (Draft.PurchaseCost.EditingCostBufferPercent, Draft.PurchaseCost.EditingTargetCostBufferPercent);

        /// <summary>Takes <paramref name="step"/>, the model with it. True when an undo or a redo moved, or a Save wrote.</summary>
        public bool Apply(Step step)
        {
            switch (step)
            {
                case Load load:
                    // Not the user's: written as a load writes, recording nothing.
                    var draft = Draft;
                    var before = draft.Capture(0);
                    Load(load.CostsChanged, load.Renamed);
                    _restoring = true;
                    draft.Rebase(draft.Category?.Id, draft.LinkedTo?.Id, draft.Recipe.SubAssembly?.Id, draft.BaseUnit?.Id, _subAssemblies);

                    // The form reads the picked sub-assembly's lines on every load (RawMaterialForm's load), not only on a new pick.
                    ReadRecipeLines(draft.Recipe.SubAssembly);
                    _restoring = false;
                    draft.Capture(0).SavesSameAs(before).Should().BeTrue($"{load} re-points the draft, changing nothing it saves");
                    ShouldReferToTheFreshCopies(load);
                    return false;

                case MoveDefault(var percent):
                    // As the form takes it (RawMaterialForm.RefreshDefaultCostBuffer), within its refresh: nothing recorded.
                    var from = _defaultBuffer;
                    _restoring = true;
                    if (Draft.TakeDefaultCostBuffer(from, percent, isNewMaterial: _isNew) is { } moved)
                    {
                        History.RewriteAll(moved.Carry);
                    }

                    _restoring = false;
                    _defaultBuffer = percent;

                    // The model: on a new material, wherever the old default was held (the form, what Save measures against, any
                    // step), the new one is now, whether or not the form showed it, so no undo or redo brings the old one back and
                    // one landing on it follows Settings again. A buffer on the old default is the % and its target at it, with no
                    // fixed buffered cost (one typed over a price, or as an amount), which the % would follow instead.
                    if (_isNew && from != percent)
                    {
                        _model.Rewritten(s => s.CostBufferPercent == from && s.TargetCostBufferPercent == from && s.BufferedCost is not > 0m
                            ? s with { CostBufferPercent = percent, TargetCostBufferPercent = percent }
                            : s);
                    }

                    return false;

                case Undo:
                    var undid = Move(History.TryUndo(out var undone), undone);
                    _model.Undone(undid);
                    return undid;

                case Redo:
                    var redid = Move(History.TryRedo(out var redone), redone);
                    _model.Redone(redid);
                    return redid;

                case Save:
                    return TrySave();

                default:
                    var was = Draft.Capture(0);
                    History.Begin();
                    try
                    {
                        Edit(step);
                    }
                    catch (BusinessRuleException)
                    {
                        // Refused, as the screen would refuse it: the step keeps whatever it changed before, as there.
                    }

                    History.End();
                    _model.Edited(was, Draft.Capture(0));
                    return false;
            }
        }

        /// <summary>The draft, its history and a made ingredient's totals, as the model and the lines say.</summary>
        public void ShouldBeRight(string when)
        {
            _model.ShouldMatch(History, Draft.Capture(0), when);

            // The totals, worked out afresh from both tables as they stand and the Weight Loss %: a mark missed leaves them as they were.
            var recipe = Draft.Recipe;
            List<IngredientLineRow> rows = [.. recipe.Lines, .. recipe.AddIns];
            var (grams, _) = IngredientBomRules.CalculateCost(
                [.. rows.Where(l => l.Ingredient is not null).Select(l => (l.Grams ?? 0m, l.Ingredient!.BufferedCostPerBaseUnit))],
                Draft.WeightLossPercent ?? 0m);
            (recipe.BomTotalWeight, recipe.BomTotalCost).Should().Be((grams, rows.Sum(l => l.Cost)), $"{when}, the totals follow the lines and the weight loss");
        }

        /// <summary>The picked sub-assembly's lines as saved, each at the screen's copy of its ingredient, into the recipe (RawMaterialForm.LoadRecipeLinesAsync).</summary>
        private void ReadRecipeLines(SubAssembly? subAssembly) =>
            Draft.Recipe.ShowLines(subAssembly is null ? [] : [.. SubAssemblyLines[subAssembly.Id].Select(l => new SubAssemblyLine
            {
                SubAssemblyId = subAssembly.Id, IngredientId = l.Ingredient, Ingredient = Ingredients.Single(m => m.Id == l.Ingredient), Grams = l.Grams,
            })]);

        private bool Move(bool moved, RawMaterialFormSnapshot snapshot)
        {
            if (moved)
            {
                Draft.Restore(snapshot, put =>
                {
                    _restoring = true;
                    try
                    {
                        put();
                    }
                    finally
                    {
                        _restoring = false;
                    }
                });
            }

            return moved;
        }

        /// <summary>
        /// Save as the form does it: a named material in a category, with nothing half filled in, else refused with nothing
        /// changed; written, what was saved is what the dirty check measures against, with nothing to undo. The form's reload
        /// of what was saved is the screen's own, tested on the database (RawMaterialsState's tests).
        /// </summary>
        private bool TrySave()
        {
            if (Draft.Name.Trim().Length == 0 || Draft.Category is null)
            {
                return false;
            }

            try
            {
                Draft.ToSaveRequest(Draft.ToMaterial(1, savedVersion: null));
            }
            catch (BusinessRuleException)
            {
                return false;
            }

            History.Clear();
            History.MarkClean();
            _model.Saved(Draft.Capture(0));
            _isNew = false;
            return true;
        }

        /// <summary>After a load, what the draft refers to is the load's copies, so their names and costs are the ones saved now.</summary>
        private void ShouldReferToTheFreshCopies(Load load)
        {
            var draft = Draft;
            if (draft.Category is { } category)
            {
                category.Should().BeSameAs(Categories.Single(c => c.Id == category.Id), $"after {load}, the category is the load's copy");
            }

            if (draft.LinkedTo is { } linkedTo)
            {
                linkedTo.Should().BeSameAs(Ingredients.Single(m => m.Id == linkedTo.Id), $"after {load}, the linked ingredient is the load's copy");
            }

            // The picker's options keep a sub-assembly whose values didn't change (CollectionMerge.MergeFrom), so it's known by
            // what it shows: the name saved now.
            if (draft.Recipe.SubAssembly is { } subAssembly)
            {
                (subAssembly.Name, subAssembly.Role?.Name).Should().Be(
                    (_subAssemblies.Single(s => s.Id == subAssembly.Id).Name, _subAssemblies.Single(s => s.Id == subAssembly.Id).Role?.Name),
                    $"after {load}, the sub-assembly shows as it's saved now");
            }

            foreach (var line in draft.Recipe.Lines.Concat(draft.Recipe.AddIns).Where(l => l.IngredientId is not null))
            {
                line.Ingredient.Should().BeSameAs(Ingredients.Single(m => m.Id == line.IngredientId), $"after {load}, a line refers to the load's copy");
            }
        }

        private void Edit(Step step)
        {
            var draft = Draft;
            var brands = draft.PurchaseCost.BrandPriceRows;
            var units = draft.Units.Rows;
            var addIns = draft.Recipe.AddIns;
            switch (step)
            {
                case Rename(var name):
                    draft.Name = name;
                    break;
                case PickType(var index):
                    draft.ChangeType(index == 0 ? RawMaterialType.Ingredient : RawMaterialType.Packaging);
                    break;
                case PickSource(var index) when draft.Type == RawMaterialType.Ingredient:
                    draft.ChangeSource((RawMaterialSource)index);
                    break;
                case PickCategory(var index):
                    var kinds = Categories.Where(c => c.Type == draft.CategoryType).ToList();
                    draft.Category = kinds.Count > 0 ? kinds[index % kinds.Count] : null;
                    break;
                case TypeWeightLoss(var percent) when draft.IsEditingMade:
                    draft.WeightLossPercent = percent;
                    break;
                case PickLinkedTo(var index) when draft.IsEditingLinked:
                    draft.LinkedTo = Ingredients[index];
                    break;
                case TypeShares(var weight, var cost) when draft.IsEditingLinked:
                    draft.WeightSharePercent = weight;
                    draft.CostSharePercent = cost;
                    break;
                case PickBaseUnit(var index) when draft.IsEditingPackaging:
                    draft.ChangeBaseUnit(UnitsOfMeasure[index]);
                    break;
                case TypeBuffer(var percent) when draft.ShowPurchaseSetup:
                    draft.PurchaseCost.TypedCostBufferPercent = percent;
                    break;
                case TypeDefaultBuffer when draft.ShowPurchaseSetup:
                    draft.PurchaseCost.TypedCostBufferPercent = _defaultBuffer;
                    break;
                case TypePreferredBrand(var index) when draft.ShowPurchaseSetup:
                    draft.PurchaseCost.EditingPreferredBrand = Brands[index];
                    break;
                case AddBrandPrice(var brand, var price) when draft.ShowPurchaseSetup:
                    // As the table's add row: the row joins the table as its price is typed, then its brand.
                    var row = draft.PurchaseCost.NewBrandPriceRow();
                    row.Price = price / 100m;
                    brands.Add(row);
                    row.Brand = Brands[brand];
                    break;
                case AddPricedBrand(var brand, var price) when draft.ShowPurchaseSetup:
                    // As the add row with its base unit picked: priced at once, so the material is costed at it.
                    var priced = draft.PurchaseCost.NewBrandPriceRow();
                    priced.UnitKey = PurchaseUnitChoice.BaseUnitKey;
                    priced.Price = price / 10000m;
                    brands.Add(priced);
                    priced.Brand = Brands[brand];
                    break;
                case RemoveBrandPrice(var at) when brands.Count > 0:
                    brands.RemoveAt(at % brands.Count);
                    break;
                case AddUnit(var unit, var multiplier):
                    var unitRow = draft.Units.NewRow();
                    unitRow.UnitOfMeasureId = UnitsOfMeasure[unit].Id;
                    units.Add(unitRow);

                    // As the table lets it be typed: a standard unit's (a kilogram's, for an ingredient in grams) is fixed.
                    if (unitRow.CanTypeMultiplier)
                    {
                        unitRow.BaseUnitsPerUnit = multiplier;
                    }

                    break;
                case RemoveUnit(var at) when units.Count > 0:
                    units.RemoveAt(at % units.Count);
                    break;
                case PickSubAssembly(var index) when draft.IsEditingMade:
                    draft.Recipe.PickSubAssembly(draft.Recipe.SubAssemblyOptions[index]);
                    break;
                case AddAddIn(var ingredient, var grams) when draft.IsEditingMade:
                    var line = draft.Recipe.NewAddIn();
                    line.IngredientId = Ingredients[ingredient].Id;
                    addIns.Add(line);
                    line.Grams = grams;
                    break;
                case TypeAddInGrams(var at, var grams) when addIns.Count > 0:
                    addIns[at % addIns.Count].Grams = grams;
                    break;
                case RemoveAddIn(var at) when addIns.Count > 0:
                    addIns.RemoveAt(at % addIns.Count);
                    break;
            }
        }

        /// <summary>Fresh copies of the categories, materials, units of measure and sub-assemblies, in place of the ones held.</summary>
        private void Load(bool costsChanged, bool renamed)
        {
            var factor = costsChanged ? 1m + (++_loads % 3) * 0.25m : 1m;
            var suffix = renamed ? $" ({++_renames})" : string.Empty;

            Categories.Clear();
            Categories.Add(new RawMaterialCategory { Id = 11, Name = "Dry goods" + suffix, Type = RawMaterialCategoryType.Ingredient });
            Categories.Add(new RawMaterialCategory { Id = 12, Name = "Dairy" + suffix, Type = RawMaterialCategoryType.Ingredient });
            Categories.Add(new RawMaterialCategory { Id = 13, Name = "In-House" + suffix, Type = RawMaterialCategoryType.Made });
            Categories.Add(new RawMaterialCategory { Id = 14, Name = "Boxes" + suffix, Type = RawMaterialCategoryType.Packaging });

            UnitsOfMeasure.Clear();
            UnitsOfMeasure.Add(new UnitOfMeasure { Id = 1, Name = "kilogram", Abbreviation = "kg" });
            UnitsOfMeasure.Add(new UnitOfMeasure { Id = 2, Name = "piece" + suffix, Abbreviation = "pc" });
            UnitsOfMeasure.Add(new UnitOfMeasure { Id = 3, Name = "bag" + suffix, Abbreviation = "bag" });

            Ingredients.Clear();
            foreach (var (id, name, cost) in new[] { (21, "Bread Flour", 0.05m), (22, "Egg", 0.09m), (23, "Butter", 0.42m) })
            {
                Ingredients.Add(new RawMaterial
                {
                    Id = id, Name = name + suffix, Type = RawMaterialType.Ingredient, Source = RawMaterialSource.Purchased, CategoryId = 11,
                    CostPerBaseUnit = cost * factor, BufferedCostPerBaseUnit = cost * factor * 1.1m, CostBufferPercent = 10m,
                });
            }

            _subAssemblies.Clear();
            var filling = new SubAssemblyRole { Id = 31, Name = "Filling" + suffix };
            _subAssemblies.Add(new SubAssembly { Id = 41, Name = "Butter Icing Base" + suffix, RoleId = filling.Id, Role = filling });
            _subAssemblies.Add(new SubAssembly { Id = 42, Name = "Custard Base" + suffix, RoleId = filling.Id, Role = filling });
        }

        RawMaterial? IRawMaterialDraftHost.SelectedRawMaterial => null;

        bool IRawMaterialDraftHost.AreDetailsReady => true;

        bool IRawMaterialDraftHost.IsBusyApplying => _restoring;

        void IUndoRecorder.BeginUndoStep() => History.Begin();

        void IUndoRecorder.EndUndoStep() => History.End();

        bool IUndoRecorder.IsEditPending { get; set; }

        int IUndoRecorder.FormVersion => 0;
    }
}
