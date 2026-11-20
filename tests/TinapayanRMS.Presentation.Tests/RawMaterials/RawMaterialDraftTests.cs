using System.Collections.ObjectModel;
using AwesomeAssertions;
using CsCheck;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.RawMaterials;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Tests.RawMaterials;

/// <summary>
/// The Raw Materials draft on its own, with no database and no screen (docs/adr/0006): random sequences of what a user does
/// to a new material (its name, type, source, category, weight loss, a link and its shares, a base unit, the cost buffer and
/// preferred brand, brand prices, alternative units, a made ingredient's sub-assembly and add-ins), each an undo step, with
/// loads that bring fresh copies of what it refers to between them, their costs moved or not. Whatever the sequence, the
/// dirty check says whether it would save differently from where it started, a load changes nothing it saves, and undoing
/// every step gives back where it started, redoing them where it ended. CsCheck shrinks a failure to the shortest sequence.
/// </summary>
public class RawMaterialDraftTests
{
    [Test]
    public void Any_sequence_of_edits_and_loads_undoes_back_to_where_it_started() =>
        Steps.Array[0, 40].Sample(steps =>
        {
            var world = new World();
            var start = world.Draft.Capture(0);

            foreach (var step in steps)
            {
                world.Apply(step);
                world.History.IsDirty.Should().Be(!start.SavesSameAs(world.Draft.Capture(0)), $"after {step}, the dirty check says whether a Save would write anything");
            }

            var end = world.Draft.Capture(0);
            while (world.TryUndo())
            {
            }

            world.Draft.Capture(0).ShowsSameAs(start).Should().BeTrue("undoing every step gives back where it started");
            world.History.IsDirty.Should().BeFalse();

            while (world.TryRedo())
            {
            }

            world.Draft.Capture(0).ShowsSameAs(end).Should().BeTrue("redoing every step gives back where it ended");
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
            world.Apply(new Load(CostsChanged: true));

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

    /// <summary>One thing a user does to the draft, as one undo step, or a load between them.</summary>
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

    private sealed record TypePreferredBrand(int Index) : Step;

    private sealed record AddBrandPrice(int Brand, int Price) : Step;

    private sealed record RemoveBrandPrice(int Row) : Step;

    private sealed record AddUnit(int Unit, int Multiplier) : Step;

    private sealed record RemoveUnit(int Row) : Step;

    private sealed record PickSubAssembly(int Index) : Step;

    private sealed record AddAddIn(int Ingredient, int Grams) : Step;

    private sealed record TypeAddInGrams(int Row, int Grams) : Step;

    private sealed record RemoveAddIn(int Row) : Step;

    /// <summary>The view's load: fresh copies of the categories, materials, units and sub-assemblies, the costs moved or not.</summary>
    private sealed record Load(bool CostsChanged) : Step;

    private static readonly string[] Names = ["Sugar", "Egg Yolk", " Ribbon ", ""];

    private static readonly string?[] Brands = [null, "Sugarland", "Victory"];

    private static readonly Gen<Step> Steps = Gen.OneOf<Step>(
        Gen.Int[0, 3].Select(index => (Step)new Rename(Names[index])),
        Gen.Int[0, 1].Select(index => (Step)new PickType(index)),
        Gen.Int[0, 2].Select(index => (Step)new PickSource(index)),
        Gen.Int[0, 5].Select(index => (Step)new PickCategory(index)),
        Gen.Int[0, 20].Select(percent => (Step)new TypeWeightLoss(percent)),
        Gen.Int[0, 2].Select(index => (Step)new PickLinkedTo(index)),
        Gen.Select(Gen.Int[1, 100], Gen.Int[1, 100], (weight, cost) => (Step)new TypeShares(weight, cost)),
        Gen.Int[0, 2].Select(index => (Step)new PickBaseUnit(index)),
        Gen.Int[0, 50].Select(percent => (Step)new TypeBuffer(percent)),
        Gen.Int[0, 2].Select(index => (Step)new TypePreferredBrand(index)),
        Gen.Select(Gen.Int[0, 2], Gen.Int[1, 2000], (brand, price) => (Step)new AddBrandPrice(brand, price)),
        Gen.Int[0, 9].Select(row => (Step)new RemoveBrandPrice(row)),
        Gen.Select(Gen.Int[0, 2], Gen.Int[1, 1000], (unit, multiplier) => (Step)new AddUnit(unit, multiplier)),
        Gen.Int[0, 9].Select(row => (Step)new RemoveUnit(row)),
        Gen.Int[0, 1].Select(index => (Step)new PickSubAssembly(index)),
        Gen.Select(Gen.Int[0, 2], Gen.Int[0, 500], (ingredient, grams) => (Step)new AddAddIn(ingredient, grams)),
        Gen.Select(Gen.Int[0, 9], Gen.Int[0, 500], (row, grams) => (Step)new TypeAddInGrams(row, grams)),
        Gen.Int[0, 9].Select(row => (Step)new RemoveAddIn(row)),
        Gen.Bool.Select(costsChanged => (Step)new Load(costsChanged)));

    /// <summary>
    /// A new material's draft as a form holds it: the view's lists it reads, and an undo history over it recording only the
    /// user's steps, measuring what a Save would write as the form does (SavesSameAs).
    /// </summary>
    private sealed class World : IRawMaterialDraftHost
    {
        private readonly List<SubAssembly> _subAssemblies = [];
        private bool _restoring;
        private int _loads;

        public World()
        {
            Load(costsChanged: false);
            Draft = new RawMaterialDraft(
                this,
                new RawMaterialLists(Categories, Ingredients, UnitsOfMeasure),
                new RawMaterialRowChecks(_ => { }, _ => { }));
            History = new UndoHistory<RawMaterialFormSnapshot>(
                () => Draft.Capture(0), (a, b) => a.ShowsSameAs(b), () => !_restoring, (a, b) => a.SavesSameAs(b));
            Draft.Changed += History.RefreshDirty;
            Draft.Rebase(null, null, null, null, _subAssemblies);
            Draft.Clear(RawMaterialType.Ingredient, categoryId: null, RawMaterialSource.Purchased, defaultCostBufferPercent: 10m);
            History.MarkClean();
        }

        public ObservableCollection<RawMaterialCategory> Categories { get; } = [];

        public ObservableCollection<RawMaterial> Ingredients { get; } = [];

        public ObservableCollection<UnitOfMeasure> UnitsOfMeasure { get; } = [];

        public RawMaterialDraft Draft { get; }

        public UndoHistory<RawMaterialFormSnapshot> History { get; }

        public void Apply(Step step)
        {
            if (step is Load load)
            {
                // Not the user's: written as a load writes, recording nothing.
                var draft = Draft;
                Load(load.CostsChanged);
                _restoring = true;
                draft.Rebase(draft.Category?.Id, draft.LinkedTo?.Id, draft.Recipe.SubAssembly?.Id, draft.BaseUnit?.Id, _subAssemblies);
                _restoring = false;
                return;
            }

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
        }

        public bool TryUndo() => Move(History.TryUndo(out var snapshot), snapshot);

        public bool TryRedo() => Move(History.TryRedo(out var snapshot), snapshot);

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
        private void Load(bool costsChanged)
        {
            var factor = costsChanged ? 1m + (++_loads % 3) * 0.25m : 1m;

            Categories.Clear();
            Categories.Add(new RawMaterialCategory { Id = 11, Name = "Dry goods", Type = RawMaterialCategoryType.Ingredient });
            Categories.Add(new RawMaterialCategory { Id = 12, Name = "Dairy", Type = RawMaterialCategoryType.Ingredient });
            Categories.Add(new RawMaterialCategory { Id = 13, Name = "In-House", Type = RawMaterialCategoryType.Made });
            Categories.Add(new RawMaterialCategory { Id = 14, Name = "Boxes", Type = RawMaterialCategoryType.Packaging });

            UnitsOfMeasure.Clear();
            UnitsOfMeasure.Add(new UnitOfMeasure { Id = 1, Name = "kilogram", Abbreviation = "kg" });
            UnitsOfMeasure.Add(new UnitOfMeasure { Id = 2, Name = "piece", Abbreviation = "pc" });
            UnitsOfMeasure.Add(new UnitOfMeasure { Id = 3, Name = "bag", Abbreviation = "bag" });

            Ingredients.Clear();
            foreach (var (id, name, cost) in new[] { (21, "Bread Flour", 0.05m), (22, "Egg", 0.09m), (23, "Butter", 0.42m) })
            {
                Ingredients.Add(new RawMaterial
                {
                    Id = id, Name = name, Type = RawMaterialType.Ingredient, Source = RawMaterialSource.Purchased, CategoryId = 11,
                    CostPerBaseUnit = cost * factor, BufferedCostPerBaseUnit = cost * factor * 1.1m, CostBufferPercent = 10m,
                });
            }

            _subAssemblies.Clear();
            var filling = new SubAssemblyRole { Id = 31, Name = "Filling" };
            _subAssemblies.Add(new SubAssembly { Id = 41, Name = "Butter Icing Base", RoleId = filling.Id, Role = filling });
            _subAssemblies.Add(new SubAssembly { Id = 42, Name = "Custard Base", RoleId = filling.Id, Role = filling });
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
