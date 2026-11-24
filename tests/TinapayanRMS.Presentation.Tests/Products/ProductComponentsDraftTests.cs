using System.Collections.ObjectModel;
using AwesomeAssertions;
using CsCheck;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// A composite's products draft on its own, with no database and no screen (docs/adr/0006): random sequences of what a user
/// does to a saved composite's Products table (lines added, a product picked from what the cell offers, a quantity typed,
/// a line taken out, the products' packaging ticked in or out), each an undo step, with undos, redos and Saves among them,
/// the costs a line wants arriving, and loads that bring fresh copies of the products, renamed or repriced or not. After
/// every step the draft is where its history says it is, the dirty check says whether it shows differently from what was
/// last saved, and the table's total is its lines' Costs; a load changes nothing it shows, and every line's Cost is its
/// quantity at what its product brings, as read, its packaging in only while it's ticked. A Save here is what the form's
/// dirty check sees of one: what was written is what's measured against, with nothing to undo. CsCheck shrinks a failure to
/// the shortest sequence.
/// </summary>
public class ProductComponentsDraftTests
{
    [Test]
    public void Any_sequence_keeps_the_draft_its_history_and_its_total_right() =>
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
    public void A_load_changes_nothing_the_draft_shows() =>
        Steps.Array[0, 30].Sample(steps =>
        {
            var world = new World();
            foreach (var step in steps)
            {
                world.Apply(step);
            }

            var before = world.Capture();
            var wasDirty = world.History.IsDirty;
            world.Apply(new Load(CostsChanged: true, Renamed: true));

            world.Capture().ShowsSameAs(before).Should().BeTrue("a load re-points the lines at fresh products and costs, by id");
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

            var captured = world.Capture();
            world.Restore(captured);

            world.Capture().ShowsSameAs(captured).Should().BeTrue();
        }, iter: 300);

    [Test]
    public void Every_line_costs_its_quantity_at_what_its_product_brings() =>
        Steps.Array[0, 40].Sample(steps =>
        {
            var world = new World();
            foreach (var step in steps)
            {
                world.Apply(step);
            }

            // Whatever was wanted and not read yet arrives, as the tab's read hands it over.
            world.Apply(new CostsArrive());

            foreach (var row in world.Draft.ComponentEdits.Where(r => r.Product is not null))
            {
                var costs = world.CostsOf(row.ComponentProductId!.Value);
                var quantity = row.Quantity ?? 0;
                row.Cost.Should().Be(quantity * (costs.RecipeBuffered + (world.Draft.IncludesPackaging ? costs.PackagingBuffered : 0m)), $"{row.Product!.Name} x {quantity}");
            }

            world.Draft.ComponentsCost.Should().Be(world.Draft.CostedLines.Sum(r => r.Cost));
        }, iter: 300);

    [Test]
    public void The_product_cell_never_offers_the_composite_nor_a_composite_holding_it()
    {
        var world = new World();

        world.Draft.ComponentProductOptions.Select(p => p.Id).Should().BeEquivalentTo([101, 102, 103],
            "Gift Box 2 holds this one, and Gift Box 3 holds Gift Box 2: either would end up holding itself");
    }

    [Test]
    public void A_cost_not_read_yet_is_wanted_once_the_product_is_picked_and_prices_the_line_when_it_arrives()
    {
        var world = new World();
        world.Apply(new AddLine(Product: 2, Quantity: 3));

        var line = world.Draft.ComponentEdits[^1];
        world.Wanted.Should().Contain(103);
        line.RecipeCost.Should().BeNull("Ube Roll's cost hasn't been read");

        world.Apply(new CostsArrive());

        line.RecipeCost.Should().Be(world.CostsOf(103).RecipeBuffered);
        line.Cost.Should().Be(3 * (world.CostsOf(103).RecipeBuffered + world.CostsOf(103).PackagingBuffered), "the composite takes its products' packaging too");
    }

    [Test]
    public void A_product_taken_out_and_picked_again_after_a_load_is_priced_as_it_is_now()
    {
        var world = new World();
        world.Apply(new RemoveLine(1));
        world.Apply(new Load(CostsChanged: true, Renamed: false));

        world.Apply(new AddLine(Product: 1, Quantity: 1));
        world.Apply(new CostsArrive());

        var pandesal = world.Draft.ComponentEdits.Single(r => r.ComponentProductId == 102);
        pandesal.RecipeCost.Should().Be(world.CostsOf(102).RecipeBuffered, "the cost read before the load is gone with the line, and read again as it's picked");
    }

    [Test]
    public void A_cost_read_that_a_load_overtook_is_let_go()
    {
        var world = new World();
        world.Apply(new AddLine(Product: 2, Quantity: 1));
        var line = world.Draft.ComponentEdits[^1];
        var readBefore = world.Draft.Generation;
        var costBefore = world.CostsOf(103);

        // The load brings Ube Roll's cost as it is now, moved; then the read started before it lands.
        world.Apply(new Load(CostsChanged: true, Renamed: false));
        world.Draft.TakeCosts(new Dictionary<int, ComponentCosts> { [103] = costBefore }, readBefore).Should().BeFalse();

        line.RecipeCost.Should().Be(world.CostsOf(103).RecipeBuffered, "the load's cost stands");
    }

    [Test]
    public void The_add_row_keeps_its_cost_through_a_load_and_wants_it_read_again()
    {
        var world = new World();
        var adding = world.Draft.NewRow();
        adding.ComponentProductId = 103;
        world.Apply(new CostsArrive());
        var shown = adding.RecipeCost;
        shown.Should().NotBeNull();

        world.Apply(new Load(CostsChanged: true, Renamed: false));
        world.Draft.Reprice();

        adding.RecipeCost.Should().Be(shown, "nothing it shows goes blank while it's typed");
        world.Wanted.Should().Contain(103, "its cost went with the load, so it's read again");
        world.Apply(new CostsArrive());
        adding.RecipeCost.Should().Be(world.CostsOf(103).RecipeBuffered);
    }

    private const int CompositeId = 100;

    /// <summary>One thing a user does to the table, as one undo step, or something that arrives between them.</summary>
    private abstract record Step;

    private sealed record AddLine(int Product, int Quantity) : Step;

    private sealed record PickProduct(int Row, int Product) : Step;

    private sealed record TypeQuantity(int Row, int Quantity) : Step;

    private sealed record RemoveLine(int Row) : Step;

    private sealed record TickPackaging(bool Ticked) : Step;

    /// <summary>The costs the lines wanted, read and handed over (ProductComponents.FetchCostsAsync).</summary>
    private sealed record CostsArrive : Step;

    private sealed record Undo : Step;

    private sealed record Redo : Step;

    /// <summary>Save: what was written is what the dirty check measures against.</summary>
    private sealed record Save : Step;

    /// <summary>The view's load: fresh copies of the products and what they bring, as saved elsewhere since: repriced, renamed, both or neither.</summary>
    private sealed record Load(bool CostsChanged, bool Renamed) : Step;

    private static readonly Gen<Step> Steps = Gen.Frequency(
        (12, Gen.OneOf<Step>(
            Gen.Select(Gen.Int[0, 2], Gen.Int[0, 6], (product, quantity) => (Step)new AddLine(product, quantity)),
            Gen.Select(Gen.Int[0, 9], Gen.Int[0, 2], (row, product) => (Step)new PickProduct(row, product)),
            Gen.Select(Gen.Int[0, 9], Gen.Int[0, 6], (row, quantity) => (Step)new TypeQuantity(row, quantity)),
            Gen.Int[0, 9].Select(row => (Step)new RemoveLine(row)),
            Gen.Bool.Select(ticked => (Step)new TickPackaging(ticked)))),
        (2, Gen.Const<Step>(new CostsArrive())),
        (3, Gen.Const<Step>(new Undo())),
        (2, Gen.Const<Step>(new Redo())),
        (1, Gen.Const<Step>(new Save())),
        (2, Gen.Select(Gen.Bool, Gen.Bool, (costs, renamed) => (Step)new Load(costs, renamed))));

    /// <summary>The table's share of an undo step, as the form keeps it: the checkbox, and the lines (ProductFormSnapshot).</summary>
    private sealed record Snapshot(bool IncludesPackaging, IReadOnlyList<ComponentLineSnapshot> Lines)
    {
        public bool ShowsSameAs(Snapshot other) => IncludesPackaging == other.IncludesPackaging && Lines.SequenceEqual(other.Lines);
    }

    /// <summary>
    /// A saved composite's Products table as a form holds it, two products in it and its packaging ticked: the products and
    /// what each brings, the composites holding others, and an undo history over it recording only the user's steps.
    /// </summary>
    private sealed class World : IProductComponentsDraftHost
    {
        private readonly Dictionary<int, ComponentCosts> _costs = [];
        private readonly UndoModel<Snapshot> _model;
        private bool _restoring;
        private int _loads;
        private int _renames;

        public World()
        {
            Load(costsChanged: false, renamed: false);
            Draft = new ProductComponentsDraft(this, Products);
            Draft.CostWanted += id => Wanted.Add(id);
            History = new UndoHistory<Snapshot>(Capture, (a, b) => a.ShowsSameAs(b), () => !_restoring, (a, b) => a.ShowsSameAs(b));
            Draft.Changed += History.RefreshDirty;

            // As the form shows a saved composite (ProductForm.ShowDetails, then the load's merge): the checkbox, its
            // lines with what their products bring, then what every composite holds, for the cell's choices.
            Draft.IncludesPackaging = true;
            Draft.Load([Saved(101, 2), Saved(102, 4)]);
            Draft.TakeFresh(new Dictionary<int, ComponentCosts>(), Links());
            Draft.RefreshOptions(CompositeId, Products);
            History.MarkClean();
            _model = new UndoModel<Snapshot>(Capture(), (a, b) => a.ShowsSameAs(b));
        }

        public ObservableCollection<Product> Products { get; } = [];

        public ProductComponentsDraft Draft { get; }

        public UndoHistory<Snapshot> History { get; }

        /// <summary>The products whose costs the lines wanted and haven't been handed.</summary>
        public HashSet<int> Wanted { get; } = [];

        public CostBasis CostBasis => CostBasis.Buffered;

        public ComponentCosts CostsOf(int productId) => _costs[productId];

        public Snapshot Capture() => new(Draft.IncludesPackaging, Draft.Capture());

        /// <summary>As the form's undo puts the table back: the checkbox first, then the lines.</summary>
        public void Restore(Snapshot snapshot)
        {
            Draft.IncludesPackaging = snapshot.IncludesPackaging;
            Draft.Restore(snapshot.Lines);
        }

        /// <summary>Takes <paramref name="step"/>, the model with it. True when an undo or a redo moved, or a Save wrote.</summary>
        public bool Apply(Step step)
        {
            switch (step)
            {
                // Not the user's: written as a load or a read writes, recording nothing.
                case Load load:
                    var before = Capture();
                    Load(load.CostsChanged, load.Renamed);
                    Unrecorded(() =>
                    {
                        Draft.TakeFresh(_costs.Where(c => Draft.ProductIds.Contains(c.Key)).ToDictionary(), Links());
                        Draft.RefreshOptions(CompositeId, Products);
                    });
                    Capture().ShowsSameAs(before).Should().BeTrue($"{load} re-points the lines, changing nothing they show");
                    foreach (var row in Draft.ComponentEdits.Where(r => r.Product is not null))
                    {
                        row.Product!.Name.Should().Be(Products.Single(p => p.Id == row.ComponentProductId).Name, $"after {load}, a line shows its product as it's called now");
                    }

                    return false;
                case CostsArrive:
                    var arrived = Wanted.ToDictionary(id => id, id => _costs[id]);
                    Wanted.Clear();
                    Unrecorded(() => Draft.TakeCosts(arrived, Draft.Generation));
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
                    History.Clear();
                    History.MarkClean();
                    _model.Saved(Capture());
                    return true;
            }

            var was = Capture();
            History.Begin();
            Edit(step);
            History.End();
            _model.Edited(was, Capture());
            return false;
        }

        /// <summary>The draft, its history and the table's total, as the model and the lines say.</summary>
        public void ShouldBeRight(string when)
        {
            _model.ShouldMatch(History, Capture(), when);
            Draft.ComponentsCost.Should().Be(Draft.CostedLines.Sum(r => r.Cost), $"{when}, the total is its lines'");
        }

        private bool Move(bool moved, Snapshot snapshot)
        {
            if (moved)
            {
                Unrecorded(() => Restore(snapshot));
            }

            return moved;
        }

        private void Unrecorded(Action write)
        {
            _restoring = true;
            try
            {
                write();
            }
            finally
            {
                _restoring = false;
            }
        }

        private void Edit(Step step)
        {
            var lines = Draft.ComponentEdits;
            var options = Draft.ComponentProductOptions;
            switch (step)
            {
                // As the table lets it be typed: a product from what the cell offers, then how many.
                case AddLine(var product, var quantity) when options.Count > 0:
                    var row = Draft.NewRow();
                    row.ComponentProductId = options[product % options.Count].Id;
                    lines.Add(row);
                    row.Quantity = quantity == 0 ? null : quantity;
                    break;
                case PickProduct(var at, var product) when lines.Count > 0 && options.Count > 0:
                    lines[at % lines.Count].ComponentProductId = options[product % options.Count].Id;
                    break;
                case TypeQuantity(var at, var quantity) when lines.Count > 0:
                    lines[at % lines.Count].Quantity = quantity == 0 ? null : quantity;
                    break;
                case RemoveLine(var at) when lines.Count > 0:
                    lines.RemoveAt(at % lines.Count);
                    break;
                case TickPackaging(var ticked):
                    Draft.IncludesPackaging = ticked;
                    break;
            }
        }

        /// <summary>A line as it was saved, its product the one the screen holds, read with what it brings.</summary>
        private SavedComponent Saved(int productId, int quantity) => new(
            new ProductComponent { ProductId = CompositeId, ComponentProductId = productId, ComponentProduct = Products.First(p => p.Id == productId), Quantity = quantity },
            _costs[productId]);

        /// <summary>Gift Box 2 holds this composite, and Gift Box 3 holds Gift Box 2.</summary>
        private static List<ProductComponent> Links() =>
        [
            new() { ProductId = CompositeId, ComponentProductId = 101, Quantity = 2 },
            new() { ProductId = CompositeId, ComponentProductId = 102, Quantity = 4 },
            new() { ProductId = 104, ComponentProductId = CompositeId, Quantity = 1 },
            new() { ProductId = 105, ComponentProductId = 104, Quantity = 1 },
        ];

        /// <summary>Fresh copies of the products and what each brings, in place of the ones held, the costs moved or not.</summary>
        private void Load(bool costsChanged, bool renamed)
        {
            var factor = costsChanged ? 1m + (++_loads % 3) * 0.25m : 1m;
            var suffix = renamed ? $" ({++_renames})" : string.Empty;
            Products.Clear();
            foreach (var (id, name, isComposite) in new[]
            {
                (101, "Cheese Roll", false), (102, "Pandesal", false), (103, "Ube Roll", false),
                (CompositeId, "Gift Box", true), (104, "Gift Box 2", true), (105, "Gift Box 3", true),
            })
            {
                Products.Add(new Product { Id = id, Name = name + suffix, IsComposite = isComposite, QuantityRequired = 1 });
            }

            foreach (var (id, recipe, packaging) in new[] { (101, 12.5m, 1.25m), (102, 3.2m, 0.4m), (103, 9.75m, 1.1m) })
            {
                _costs[id] = new ComponentCosts(recipe * factor, recipe * factor * 0.9m, packaging * factor, packaging * factor * 0.9m);
            }
        }
    }
}
