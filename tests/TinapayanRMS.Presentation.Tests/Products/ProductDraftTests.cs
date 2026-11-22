using System.Collections.ObjectModel;
using AwesomeAssertions;
using CsCheck;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Pricing;
using TinapayanRMS.UseCases.Products;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// A product's draft on its own, with no database and no screen (docs/adr/0006): random sequences of what a user does to a
/// saved product's own fields (its name, description, quantity, current price and category) and, through its parts' drafts,
/// to its costing and packaging, each an undo step. Whatever the sequence, undoing every step gives back where it started
/// and redoing them where it ended, and the costs always follow the fields: the quantity they're for, the current price
/// they're compared with, the category's own wastage, and the packaging they add. Each part's draft has its own tests;
/// these are what the product's draft adds over them.
/// </summary>
public class ProductDraftTests
{
    [Test]
    public void Any_sequence_undoes_back_to_where_it_started_and_the_costs_follow_the_fields() =>
        Steps.Array[0, 40].Sample(steps =>
        {
            var world = new World();
            var start = world.Capture();

            foreach (var step in steps)
            {
                world.Apply(step);
                world.ShouldPriceTheFields($"after {step}");
            }

            var end = world.Capture();
            while (world.TryUndo())
            {
            }

            world.Capture().ShowsSameAs(start).Should().BeTrue("undoing every step gives back where it started");
            world.ShouldPriceTheFields("undone");

            while (world.TryRedo())
            {
            }

            world.Capture().ShowsSameAs(end).Should().BeTrue("redoing every step gives back where it ended");
            world.ShouldPriceTheFields("redone");
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

        world.Draft.RebaseLinked(null, [], []).LinkedPackagingNow.Should().BeNull("the linked packaging wasn't read again");
        world.Draft.RebaseLinked((set, null, new PackagingNow([Line(1m)], [])), [], []).LinkedPackagingNow.Should().BeNull("it's as it was");

        var renamed = world.Draft.RebaseLinked((set with { Name = "Standard Wrap 10 x 14" }, null, new PackagingNow([Line(1m)], [])), [], []).LinkedPackagingNow;
        renamed.Should().NotBeNull("the set was renamed where it's kept");
        world.Draft.Packaging.PackagingLinkedToText.Should().Contain("Standard Wrap 10 x 14");

        var moreLines = world.Draft.RebaseLinked((set with { Name = "Standard Wrap 10 x 14" }, null, new PackagingNow([Line(2m)], [])), [], []).LinkedPackagingNow;
        moreLines!.Lines.Should().ContainSingle().Which.QtyPerUnit.Should().Be(2m, "the set's lines changed where it's kept");
    }

    /// <summary>One thing a user does to the product, as one undo step.</summary>
    private abstract record Step;

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

    private static readonly Gen<Step> Steps = Gen.OneOf<Step>(
        Gen.Int[0, 2].Select(index => (Step)new Rename(Names[index])),
        Gen.Int[0, 2].Select(index => (Step)new Describe(index)),
        Gen.Int[1, 12].Select(quantity => (Step)new TypeQuantity(quantity)),
        Gen.Int[0, 150].Select(pesos => (Step)new TypePrice(pesos == 0 ? null : pesos)),
        Gen.Int[0, 2].Select(index => (Step)new PickCategory(index)),
        Gen.Int[0, 60].Select(percent => (Step)new TypeLabor(percent)),
        Gen.Select(Gen.Int[0, 1], Gen.Int[1, 12], (material, quarters) => (Step)new AddPackaging(material, quarters)),
        Gen.Int[0, 9].Select(row => (Step)new RemovePackaging(row)));

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
        private bool _restoring;

        public World()
        {
            _categories.Add(new ProductCategory { Id = 61, Name = "Breads" });
            _categories.Add(new ProductCategory { Id = 62, Name = "Loaves", WastagePercent = 5m });
            _categories.Add(new ProductCategory { Id = 63, Name = "Cakes" });

            Material71 = Material(71, "Cello Wrapper", 0.7m);
            Draft = new ProductDraft(this, new ProductLists(
                [],
                [],
                [Material71, Material(72, "Sticker Label", 0.4m)],
                []));
            Draft.Costing.SetDefaults(new CostingDefaults { WastagePercent = 3m, VatPercent = 12m, PriceRoundingStep = 1m });
            Draft.Costing.Load(new CostingParameters { LaborPercent = 35m, OverheadPercent = 25m, AdministrativePercent = 15m, MarkupPercent = 60m });
            Draft.EditingName = "Ube Loaf";
            Draft.EditingCategory = _categories[0];
            Draft.EditingCurrentPrice = 95m;

            History = new UndoHistory<ProductFormSnapshot>(Capture, (a, b) => a.ShowsSameAs(b), () => !_restoring, (a, b) => a.SavesSameAs(b));
            History.MarkClean();
        }

        public ProductDraft Draft { get; }

        /// <summary>The cello wrapper, as the screen holds it.</summary>
        public RawMaterial Material71 { get; }

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

        public void Apply(Step step)
        {
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
        }

        public bool TryUndo() => Move(History.TryUndo(out var snapshot), snapshot);

        public bool TryRedo() => Move(History.TryRedo(out var snapshot), snapshot);

        private bool Move(bool moved, ProductFormSnapshot snapshot)
        {
            if (moved)
            {
                // The restore marks the costs stale itself: nothing here does, so one that stopped would fail the pricing checks.
                _restoring = true;
                Draft.Restore(snapshot, _categories);
                _restoring = false;
            }

            return moved;
        }

        private static RawMaterial Material(int id, string name, decimal cost) => new()
        {
            Id = id, Name = name, Type = RawMaterialType.Packaging, Source = RawMaterialSource.Purchased,
            BaseUnit = new UnitOfMeasure { Id = 3, Name = "piece", Abbreviation = "pc" }, BaseUnitId = 3,
            CostPerBaseUnit = cost, BufferedCostPerBaseUnit = cost, CostBufferPercent = 0m,
        };
    }
}
