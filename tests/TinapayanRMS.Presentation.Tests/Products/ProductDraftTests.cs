using System.Collections.ObjectModel;
using AwesomeAssertions;
using CsCheck;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Pricing;
using TinapayanRMS.UseCases.Products;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// A product's draft on its own, with no database and no screen (docs/adr/0006): random sequences of what a user does to a
/// saved product's own fields (its name, description, quantity, current price and category) and, through its parts' drafts,
/// to its costing and packaging, each an undo step, with undos, redos and Saves among them, and loads that bring fresh
/// copies of the categories and packaging materials, renamed, repriced or with a category's own wastage moved, or not.
/// After every step the draft is where its history says it is, the dirty check says whether it would save differently from
/// what was last saved, and the costs follow the fields: the quantity they're for, the current price they're compared with,
/// the category's own wastage, and the packaging they add. Each part's draft has its own tests; these are what the
/// product's draft adds over them.
/// </summary>
public class ProductDraftTests
{
    [Test]
    public void Any_sequence_keeps_the_draft_and_its_history_right_and_the_costs_follow_the_fields() =>
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
    public void Save_writes_the_fields_as_typed_trimmed_and_filed_where_it_says()
    {
        var world = new World();
        world.Apply(new Rename(" Ube Loaf  "));
        world.Apply(new Describe(1));
        world.Apply(new TypeQuantity(4));
        world.Apply(new PickCategory(1));

        var saved = DateTime.Now;
        var product = world.Draft.ToProduct(9, ProductStage.Innovation, folderId: 3, saved);

        (product.Id, product.Name, product.Description, product.QuantityRequired, product.CategoryId, product.Stage, product.FolderId, product.UpdatedAt)
            .Should().Be((9, "Ube Loaf", null, 4, 62, ProductStage.Innovation, 3, saved));
        (product.IsComposite, product.IncludesComponentPackaging).Should().Be((false, false));
    }

    [Test]
    public void A_saved_product_shows_its_fields_a_new_one_clears_them_and_a_load_re_points_the_category()
    {
        var world = new World();
        var loaves = new ProductCategory { Id = 62, Name = "Loaves", WastagePercent = 5m };
        var product = new Product { Id = 7, Name = "Ube Loaf", Description = null, CategoryId = 62, QuantityRequired = 2, CurrentPrice = 95m };

        world.Draft.ShowFields(product, [loaves]);
        (world.Draft.EditingName, world.Draft.EditingDescription, world.Draft.EditingCategory, world.Draft.EditingQuantityRequired, world.Draft.EditingCurrentPrice, world.Draft.EditingCloneName)
            .Should().Be(("Ube Loaf", string.Empty, loaves, 2, 95m, "Ube Loaf Copy"));
        world.Draft.Costing.Costs.WastagePercent.Should().Be(5m, "the category's own wastage prices it at once");

        var freshLoaves = new ProductCategory { Id = 62, Name = "Loaves", WastagePercent = 4m };
        world.Draft.Rebase(62, [freshLoaves], new Dictionary<int, ComponentCosts>(), []);
        world.Draft.EditingCategory.Should().BeSameAs(freshLoaves, "a load re-points the category at its fresh copy, by id");
        world.Draft.Costing.Costs.WastagePercent.Should().Be(4m);

        world.Draft.EditingDescription = "Soft";
        world.Draft.Components.IncludesPackaging = true;
        world.Draft.ClearFields();
        world.Draft.EditingDescription.Should().BeEmpty();
        world.Draft.Components.IncludesPackaging.Should().BeFalse();
        world.Draft.EditingName.Should().BeEmpty();
        world.Draft.EditingCategory.Should().BeNull();
        world.Draft.EditingQuantityRequired.Should().Be(1);
        world.Draft.EditingCurrentPrice.Should().BeNull();
        world.Draft.EditingCloneName.Should().BeEmpty();
    }

    [Test]
    public void A_load_says_what_it_changed_of_the_linked_packaging()
    {
        var world = new World();
        var set = new PackagingLinkTarget.Set(81, "Standard Wrap");
        PackagingLine Line(decimal qty) => new() { PackagingMaterialId = 71, PackagingMaterial = world.Material71, QtyPerUnit = qty, AuthoredField = PackagingLineAuthoredField.QtyPerUnit };
        world.Draft.Packaging.LoadLink(new PackagingLink { LinkedSubAssemblyId = 81, LinkedSubAssembly = new SubAssembly { Id = 81, Name = "Standard Wrap" } });
        world.Draft.Packaging.LoadLines([Line(1m)]);

        world.Draft.RebaseLinked(null, [], []).Should().BeNull("the linked packaging wasn't read again");
        world.Draft.RebaseLinked((set, null, new PackagingNow([Line(1m)], [])), [], []).Should().BeNull("it's as it was");

        var renamed = world.Draft.RebaseLinked((set with { Name = "Standard Wrap 10 x 14" }, null, new PackagingNow([Line(1m)], [])), [], []);
        renamed.Should().NotBeNull("the set was renamed where it's kept");
        world.Draft.Packaging.PackagingLinkedToText.Should().Contain("Standard Wrap 10 x 14");

        var moreLines = world.Draft.RebaseLinked((set with { Name = "Standard Wrap 10 x 14" }, null, new PackagingNow([Line(2m)], [])), [], []);
        moreLines!.Lines.Should().ContainSingle().Which.QtyPerUnit.Should().Be(2m, "the set's lines changed where it's kept");
    }

    /// <summary>One thing a user does to the product, as one undo step; an undo, a redo or a Save; or a load between them.</summary>
    private abstract record Step;

    private sealed record Undo : Step;

    private sealed record Redo : Step;

    /// <summary>Save, as the form does it: refused without a name, else written and its fields shown again as saved.</summary>
    private sealed record Save : Step;

    /// <summary>
    /// The view's load: fresh copies of the categories and packaging materials, as saved elsewhere since: the materials
    /// repriced and Loaves' own wastage moved, renamed, both or neither.
    /// </summary>
    private sealed record Load(bool Changed, bool Renamed) : Step;

    private sealed record Rename(string Name) : Step;

    private sealed record Describe(int Index) : Step;

    private sealed record TypeQuantity(int Quantity) : Step;

    private sealed record TypePrice(int? Pesos) : Step;

    private sealed record PickCategory(int Index) : Step;

    private sealed record TypeLabor(int Percent) : Step;

    private sealed record AddPackaging(int Material, int Quarters) : Step;

    private sealed record RemovePackaging(int Row) : Step;

    private static readonly string[] Names = ["Ube Loaf", " Cheese Roll ", ""];

    private static readonly string[] Descriptions = ["Soft, with macapuno", "   ", ""];

    private static readonly Gen<Step> Steps = Gen.Frequency(
        (12, Gen.OneOf<Step>(
            Gen.Int[0, 2].Select(index => (Step)new Rename(Names[index])),
            Gen.Int[0, 2].Select(index => (Step)new Describe(index)),
            Gen.Int[1, 12].Select(quantity => (Step)new TypeQuantity(quantity)),
            Gen.Int[0, 150].Select(pesos => (Step)new TypePrice(pesos == 0 ? null : pesos)),
            Gen.Int[0, 2].Select(index => (Step)new PickCategory(index)),
            Gen.Int[0, 60].Select(percent => (Step)new TypeLabor(percent)),
            Gen.Select(Gen.Int[0, 1], Gen.Int[1, 12], (material, quarters) => (Step)new AddPackaging(material, quarters)),
            Gen.Int[0, 9].Select(row => (Step)new RemovePackaging(row)))),
        (3, Gen.Const<Step>(new Undo())),
        (2, Gen.Const<Step>(new Redo())),
        (1, Gen.Const<Step>(new Save())),
        (2, Gen.Select(Gen.Bool, Gen.Bool, (changed, renamed) => (Step)new Load(changed, renamed))));

    /// <summary>
    /// A saved product's draft as its form holds it: the lists it reads (two packaging materials, three categories, one of
    /// them with its own wastage), the shop's defaults, and an undo history over it recording only the user's steps.
    /// </summary>
    private sealed class World : IProductDraftHost
    {
        /// <summary>The recipe part's share of a step, which the product's draft passes through and doesn't put back.</summary>
        private static readonly RecipeSnapshot Recipe = new()
        {
            Usages = [],
            SelectedTab = null,
            HasAddSubAssemblyTab = false,
            Form = new SectionFormSnapshot
            {
                IsOpen = false, IsAdding = false, Title = string.Empty, Mode = default, IsEditingLines = false, IsPickingSource = false,
                EditingUsageIndex = null, RoleId = null, Name = null, SourceKey = null, Lines = [], AddIns = [], FormulaBase = 0m,
                AuthoredField = UsageAuthoredField.WeightPerPiece, WeightPerPiece = 0m, Yield = 0m,
            },
        };

        private readonly ObservableCollection<ProductCategory> _categories = [];
        private readonly ObservableCollection<RawMaterial> _materials = [];
        private readonly UndoModel<ProductFormSnapshot> _model;
        private bool _restoring;
        private int _loads;
        private int _renames;

        public World()
        {
            Load(changed: false, renamed: false);
            Draft = new ProductDraft(this, new ProductLists([], [], _materials, []));
            Draft.Costing.SetDefaults(new CostingDefaults { WastagePercent = 3m, VatPercent = 12m, PriceRoundingStep = 1m });
            Draft.Costing.Load(new CostingParameters { LaborPercent = 35m, OverheadPercent = 25m, AdministrativePercent = 15m, MarkupPercent = 60m });
            Draft.EditingName = "Ube Loaf";
            Draft.EditingCategory = _categories[0];
            Draft.EditingCurrentPrice = 95m;

            History = new UndoHistory<ProductFormSnapshot>(Capture, (a, b) => a.ShowsSameAs(b), () => !_restoring, (a, b) => a.SavesSameAs(b));
            History.MarkClean();
            _model = new UndoModel<ProductFormSnapshot>(Capture(), (a, b) => a.ShowsSameAs(b), (a, b) => a.SavesSameAs(b));
        }

        public ProductDraft Draft { get; }

        /// <summary>The cello wrapper, as the screen holds it.</summary>
        public RawMaterial Material71 => _materials[0];

        public UndoHistory<ProductFormSnapshot> History { get; }

        public int? DetailProductId => 7;

        public ProductFormSnapshot Capture() => Draft.Capture(7, Recipe);

        /// <summary>The costs are for the product's fields as they stand now, whatever was typed and undone.</summary>
        public void ShouldPriceTheFields(string when)
        {
            var costs = Draft.Costing.Costs;
            costs.QuantityRequired.Should().Be(Draft.EditingQuantityRequired, when);
            costs.CurrentPrice.Should().Be(Draft.EditingCurrentPrice, when);
            costs.WastagePercent.Should().Be(Draft.EditingCategory?.WastagePercent ?? 3m, $"{when}, the category's own wastage, else the shop's");
            costs.LaborPercent.Should().Be(Draft.Costing.EditingLaborPercent, when);
            costs.Packaging.Should().Be(Draft.Packaging.LineEdits.Sum(l => l.CostPerUnit), $"{when}, the packaging lines as they stand");
        }

        /// <summary>The draft, its history and its costs, as the model and the fields say.</summary>
        public void ShouldBeRight(string when)
        {
            _model.ShouldMatch(History, Capture(), when);
            ShouldPriceTheFields(when);
        }

        /// <summary>Takes <paramref name="step"/>, the model with it. True when an undo or a redo moved, or a Save wrote.</summary>
        public bool Apply(Step step)
        {
            switch (step)
            {
                case Load load:
                    // As the form's load (ProductForm.TakeLoad): the category back on its fresh copy, the packaging rows at
                    // their fresh materials, and the costs told that what they're worked out from changed.
                    var before = Capture();
                    Load(load.Changed, load.Renamed);
                    _restoring = true;
                    Draft.Rebase(Draft.EditingCategory?.Id, _categories, new Dictionary<int, ComponentCosts>(), []);
                    Draft.Packaging.ResolveRows();
                    Draft.Costing.InputsChanged();
                    _restoring = false;
                    History.RefreshDirty();
                    Capture().SavesSameAs(before).Should().BeTrue($"{load} re-points the draft, changing nothing it saves");
                    if (Draft.EditingCategory is { } category)
                    {
                        category.Should().BeSameAs(_categories.Single(c => c.Id == category.Id), $"after {load}, the category is the load's copy");
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
            }

            var was = Capture();
            History.Begin();
            switch (step)
            {
                case Rename(var name):
                    Draft.EditingName = name;
                    break;
                case Describe(var index):
                    Draft.EditingDescription = Descriptions[index];
                    break;
                case TypeQuantity(var quantity):
                    Draft.EditingQuantityRequired = quantity;
                    break;
                case TypePrice(var pesos):
                    Draft.EditingCurrentPrice = pesos;
                    break;
                case PickCategory(var index):
                    Draft.EditingCategory = _categories[index];
                    break;
                case TypeLabor(var percent):
                    Draft.Costing.EditingLaborPercent = percent;
                    break;
                // The packaging's lines are a table the form watches: it tells the costing (PackagingLinesEdited), as here.
                // The fields tell it themselves, so nothing else does here.
                case AddPackaging(var material, var quarters):
                    var row = Draft.Packaging.NewRow();
                    row.PackagingMaterialId = 71 + material;
                    Draft.Packaging.LineEdits.Add(row);
                    row.QtyPerUnit = quarters / 4m;
                    Draft.Costing.InputsChanged();
                    break;
                case RemovePackaging(var at) when Draft.Packaging.LineEdits.Count > 0:
                    Draft.Packaging.LineEdits.RemoveAt(at % Draft.Packaging.LineEdits.Count);
                    Draft.Costing.InputsChanged();
                    break;
            }

            History.End();

            // As the form's watchers do on every change to what it holds (the fields, the tables): Save's dirty check is told.
            History.RefreshDirty();
            _model.Edited(was, Capture());
            return false;
        }

        private bool Move(bool moved, ProductFormSnapshot snapshot)
        {
            if (moved)
            {
                // The restore marks the costs stale itself: nothing here does, so one that stopped would fail the pricing checks.
                _restoring = true;
                Draft.Restore(snapshot, _categories);
                _restoring = false;
                History.RefreshDirty();
            }

            return moved;
        }

        /// <summary>
        /// Save, as the form does it: refused without a name; written, the product's fields are shown again as saved
        /// (ProductDraft.ShowFields), with nothing to undo, and what a Save of them would write is what was just written.
        /// </summary>
        private bool TrySave()
        {
            if (Draft.EditingName.Trim().Length == 0)
            {
                return false;
            }

            var written = Draft.ToProduct(7, ProductStage.Production, folderId: null, DateTime.Now);
            History.Clear();
            _restoring = true;
            Draft.ShowFields(written, _categories);
            _restoring = false;
            History.MarkClean();

            Fields(Draft.ToProduct(7, ProductStage.Production, folderId: null, written.UpdatedAt)).Should().Be(Fields(written), "the product shown again as saved writes what was saved");
            _model.Saved(Capture());
            return true;
        }

        private static string Fields(Product product) =>
            $"{product.Name}|{product.Description}|{product.QuantityRequired}|{product.CurrentPrice}|{product.CategoryId}|{product.IsComposite}|{product.IncludesComponentPackaging}";

        /// <summary>Fresh copies of the categories and packaging materials, in place of the ones held, as a load brings them.</summary>
        private void Load(bool changed, bool renamed)
        {
            var factor = changed ? 1m + (++_loads % 3) * 0.25m : 1m;
            var suffix = renamed ? $" ({++_renames})" : string.Empty;
            _categories.Clear();
            _categories.Add(new ProductCategory { Id = 61, Name = "Breads" + suffix });
            _categories.Add(new ProductCategory { Id = 62, Name = "Loaves" + suffix, WastagePercent = changed ? 5m + _loads % 3 : 5m });
            _categories.Add(new ProductCategory { Id = 63, Name = "Cakes" + suffix });

            _materials.Clear();
            _materials.Add(Material(71, "Cello Wrapper" + suffix, 0.7m * factor));
            _materials.Add(Material(72, "Sticker Label" + suffix, 0.4m * factor));
        }

        private static RawMaterial Material(int id, string name, decimal cost) => new()
        {
            Id = id, Name = name, Type = RawMaterialType.Packaging, Source = RawMaterialSource.Purchased,
            BaseUnit = new UnitOfMeasure { Id = 3, Name = "piece", Abbreviation = "pc" }, BaseUnitId = 3,
            CostPerBaseUnit = cost, BufferedCostPerBaseUnit = cost, CostBufferPercent = 0m,
        };
    }
}
