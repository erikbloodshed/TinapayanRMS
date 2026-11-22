using System.Collections.ObjectModel;
using AwesomeAssertions;
using CsCheck;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// A product's packaging draft on its own, with no database and no screen (docs/adr/0006): random sequences of what a user
/// does to a saved product's packaging (lines typed, picked, given a unit and taken out, a set linked or a product's
/// packaging copied in, Make a Copy, Remove Link, Move to Sub-Assemblies, bulk lines and how many a pack holds, a set linked
/// for the bulk packaging, its Make a Copy and Remove Link), each an
/// undo step, with loads that bring fresh copies of the materials between them, their costs moved or not. Whatever the
/// sequence, the dirty check says whether the packaging shows differently from where it was loaded, packaging that shows
/// as loaded saves nothing, a load changes nothing it shows or saves, and undoing every step gives back where it started,
/// redoing them where it ended. CsCheck shrinks a failure to the shortest sequence.
/// </summary>
public class ProductPackagingDraftTests
{
    [Test]
    public void Any_sequence_of_edits_and_loads_undoes_back_to_where_it_started() =>
        Gen.Select(Gen.Bool, Steps.Array[0, 40]).Sample((linked, steps) =>
        {
            var world = new World(linked);
            var start = world.Draft.Capture();

            foreach (var step in steps)
            {
                world.Apply(step);
                world.History.IsDirty.Should().Be(!start.ShowsSameAs(world.Draft.Capture()), $"after {step}, the dirty check says whether it shows as loaded");
            }

            var end = world.Draft.Capture();
            while (world.TryUndo())
            {
            }

            world.Draft.Capture().ShowsSameAs(start).Should().BeTrue("undoing every step gives back where it started");
            world.History.IsDirty.Should().BeFalse();

            while (world.TryRedo())
            {
            }

            world.Draft.Capture().ShowsSameAs(end).Should().BeTrue("redoing every step gives back where it ended");
        }, iter: 300);

    [Test]
    public void Packaging_that_shows_as_loaded_saves_nothing() =>
        Gen.Select(Gen.Bool, Steps.Array[0, 40]).Sample((linked, steps) =>
        {
            var world = new World(linked);
            var start = world.Draft.Capture();
            world.Saved().Should().Be(Nothing);

            foreach (var step in steps)
            {
                world.Apply(step);
                if (world.Draft.Capture().ShowsSameAs(start))
                {
                    world.Saved().Should().Be(Nothing, $"after {step} it shows as loaded");
                }
            }

            while (world.TryUndo())
            {
            }

            world.Saved().Should().Be(Nothing, "undone back to how it was loaded");
        }, iter: 300);

    [Test]
    public void A_load_changes_nothing_the_draft_shows_or_saves() =>
        Gen.Select(Gen.Bool, Steps.Array[0, 30]).Sample((linked, steps) =>
        {
            var world = new World(linked);
            foreach (var step in steps)
            {
                world.Apply(step);
            }

            var before = world.Draft.Capture();
            var savedBefore = world.Saved();
            var wasDirty = world.History.IsDirty;
            world.Apply(new Load(CostsChanged: true));

            world.Draft.Capture().ShowsSameAs(before).Should().BeTrue("a load re-points the rows at fresh copies, by id");
            world.Saved().Should().Be(savedBefore);
            world.History.IsDirty.Should().Be(wasDirty);
        }, iter: 300);

    [Test]
    public void Putting_back_what_was_captured_changes_nothing() =>
        Gen.Select(Gen.Bool, Steps.Array[0, 30]).Sample((linked, steps) =>
        {
            var world = new World(linked);
            foreach (var step in steps)
            {
                world.Apply(step);
            }

            var captured = world.Draft.Capture();
            var saved = world.Saved();
            world.Draft.Restore(captured);

            world.Draft.Capture().ShowsSameAs(captured).Should().BeTrue();
            world.Saved().Should().Be(saved);
        }, iter: 300);

    [Test]
    public void Move_to_Sub_Assemblies_needs_a_line_and_a_link_ends_it()
    {
        var world = new World(linked: false);
        world.Apply(new RemoveLine(0));
        world.Apply(new RemoveLine(0));

        world.Draft.Invoking(d => d.StartMoving()).Should().Throw<BusinessRuleException>()
            .WithMessage("Add the packaging materials one piece takes first: a packaging set needs at least one.");
        world.Draft.IsPackagingMoving.Should().BeFalse();

        world.Apply(new AddLine(1, 4));
        world.Apply(new MoveToSubAssemblies());
        world.Draft.IsPackagingMoving.Should().BeTrue();
        world.Draft.ChangedLink().Should().BeOfType<PackagingLinkDraft.NewSet>();
        world.Draft.ChangedLines().Should().ContainSingle();

        world.Apply(new LinkSet(0));
        world.Draft.IsPackagingMoving.Should().BeFalse("linking a set ends the move");
        world.Draft.ChangedLink().Should().Be(new PackagingLinkDraft.Set(SetId));
        world.Draft.ChangedLines().Should().BeNull("linked packaging is never saved as the product's own lines");
    }

    [Test]
    public void Make_a_Copy_saves_the_lines_the_link_showed_and_Remove_Link_keeps_the_bulk_packaging()
    {
        var copied = new World(linked: true);
        copied.Apply(new MakeCopy());

        copied.Draft.ChangedLink().Should().Be(new PackagingLinkDraft.None());
        copied.Draft.ChangedLines().Should().HaveCount(2, "the product had none of its own, so they're written though they match what the link showed");

        var removed = new World(linked: true);
        removed.Apply(new AddBulkLine(2, 3));
        removed.Apply(new TypeBulkQuantity(6));
        removed.Apply(new RemoveLink());

        removed.Draft.LineEdits.Should().BeEmpty();
        removed.Draft.BulkEdits.Should().ContainSingle();
        var bulk = removed.Draft.ChangedBulk()!;
        bulk.Lines.Should().Equal((63, (int?)null, PackagingLineAuthoredField.QtyPerUnit, 0.75m));
        bulk.Quantity.Should().Be(6);
    }

    [Test]
    public void Linked_bulk_packaging_saves_its_set_and_no_lines_and_Make_a_Copy_saves_the_lines_it_showed()
    {
        var linked = new World(linked: false);
        linked.Apply(new LinkBulkSet(0));

        linked.Draft.IsBulkLinked.Should().BeTrue();
        linked.Draft.BulkEdits.Should().ContainSingle();
        var bulk = linked.Draft.ChangedBulk()!;
        (bulk.SetId, bulk.Lines.Count, bulk.Quantity).Should().Be((BulkSetId, 0, 6), "the set's lines are the set's, and how many a pack holds stays the product's");

        linked.Apply(new MakeBulkCopy());
        bulk = linked.Draft.ChangedBulk()!;
        bulk.SetId.Should().BeNull();
        bulk.Lines.Should().Equal((61, (int?)null, PackagingLineAuthoredField.QtyPerUnit, 1m));

        var removed = new World(linked: false);
        removed.Apply(new LinkBulkSet(0));
        removed.Apply(new RemoveBulkLink());
        removed.Draft.BulkEdits.Should().BeEmpty();
        bulk = removed.Draft.ChangedBulk()!;
        (bulk.SetId, bulk.Lines.Count, bulk.Quantity).Should().Be(((int?)null, 0, (int?)null), "no bulk packaging, and so no quantity");
    }

    private const int SetId = 71;

    private const int BulkSetId = 81;

    private const string Nothing = "nothing";

    /// <summary>One thing a user does to the packaging, as one undo step, or a load between them.</summary>
    private abstract record Step;

    private sealed record AddLine(int Material, int Quarters) : Step;

    private sealed record TypeQuantity(int Row, int Quarters) : Step;

    private sealed record PickMaterial(int Row, int Material) : Step;

    private sealed record PickUnit(int Row, bool Alternative) : Step;

    private sealed record RemoveLine(int Row) : Step;

    private sealed record AddBulkLine(int Material, int Quarters) : Step;

    private sealed record TypeBulkQuantityPerPack(int Row, int Quarters) : Step;

    private sealed record RemoveBulkLine(int Row) : Step;

    private sealed record TypeBulkQuantity(int? Products) : Step;

    private sealed record LinkSet(int Index) : Step;

    private sealed record CopyProduct(int Index) : Step;

    private sealed record MakeCopy : Step;

    private sealed record RemoveLink : Step;

    private sealed record MoveToSubAssemblies : Step;

    private sealed record LinkBulkSet(int Index) : Step;

    private sealed record MakeBulkCopy : Step;

    private sealed record RemoveBulkLink : Step;

    /// <summary>The view's load: fresh copies of the packaging materials, the costs moved or not.</summary>
    private sealed record Load(bool CostsChanged) : Step;

    private static readonly Gen<Step> Steps = Gen.OneOf<Step>(
        Gen.Select(Gen.Int[0, 2], Gen.Int[0, 12], (material, quarters) => (Step)new AddLine(material, quarters)),
        Gen.Select(Gen.Int[0, 9], Gen.Int[0, 12], (row, quarters) => (Step)new TypeQuantity(row, quarters)),
        Gen.Select(Gen.Int[0, 9], Gen.Int[0, 2], (row, material) => (Step)new PickMaterial(row, material)),
        Gen.Select(Gen.Int[0, 9], Gen.Bool, (row, alternative) => (Step)new PickUnit(row, alternative)),
        Gen.Int[0, 9].Select(row => (Step)new RemoveLine(row)),
        Gen.Select(Gen.Int[0, 2], Gen.Int[0, 12], (material, quarters) => (Step)new AddBulkLine(material, quarters)),
        Gen.Select(Gen.Int[0, 9], Gen.Int[0, 12], (row, quarters) => (Step)new TypeBulkQuantityPerPack(row, quarters)),
        Gen.Int[0, 9].Select(row => (Step)new RemoveBulkLine(row)),
        Gen.Int[0, 8].Select(products => (Step)new TypeBulkQuantity(products == 0 ? null : products)),
        Gen.Int[0, 1].Select(index => (Step)new LinkSet(index)),
        Gen.Int[0, 1].Select(index => (Step)new CopyProduct(index)),
        Gen.Const<Step>(new MakeCopy()),
        Gen.Const<Step>(new RemoveLink()),
        Gen.Const<Step>(new MoveToSubAssemblies()),
        Gen.Int[0, 1].Select(index => (Step)new LinkBulkSet(index)),
        Gen.Const<Step>(new MakeBulkCopy()),
        Gen.Const<Step>(new RemoveBulkLink()),
        Gen.Bool.Select(costsChanged => (Step)new Load(costsChanged)));

    /// <summary>
    /// A saved product's packaging as a form holds it, its own lines or linked to a set, with bulk packaging: the packaging
    /// materials it reads, and an undo history over it recording only the user's steps, measuring them as the form does.
    /// </summary>
    private sealed class World : IProductPackagingDraftHost
    {
        private bool _restoring;
        private int _loads;

        public World(bool linked)
        {
            Load(costsChanged: false);
            Draft = new ProductPackagingDraft(this, Materials);
            History = new UndoHistory<PackagingSnapshot>(
                Draft.Capture, (a, b) => a.ShowsSameAs(b), () => !_restoring, (a, b) => a.ShowsSameAs(b));
            Draft.Changed += History.RefreshDirty;

            // As the form loads a saved product's packaging (ProductForm.ShowDetails): the link, its lines, the bulk packaging.
            Draft.LoadLink(linked ? new PackagingLink { LinkedSubAssemblyId = SetId, LinkedSubAssembly = new SubAssembly { Id = SetId, Name = "Gift Box Set" } } : null);
            Draft.LoadLines([Saved(61, 30m, unitId: 611), Saved(62, 1m)]);
            Draft.LoadBulk(linked ? [] : [Saved(63, 5m)], linked ? null : 6);
            History.MarkClean();
        }

        public ObservableCollection<RawMaterial> Materials { get; } = [];

        public ProductPackagingDraft Draft { get; }

        public UndoHistory<PackagingSnapshot> History { get; }

        public CostBasis CostBasis => CostBasis.Buffered;

        /// <summary>What Save writes of the packaging, as text to compare: each part, or the refusal a half-typed line meets.</summary>
        public string Saved()
        {
            try
            {
                var link = Draft.ChangedLink();
                var lines = Draft.ChangedLines();
                var bulk = Draft.ChangedBulk();
                return link is null && lines is null && bulk is null
                    ? Nothing
                    : $"link {link}; lines {(lines is null ? "none" : string.Join(", ", lines))}; bulk {(bulk is null ? "none" : $"{string.Join(", ", bulk.Lines)} set {bulk.SetId} x {bulk.Quantity}")}";
            }
            catch (BusinessRuleException refused)
            {
                return $"refused: {refused.Message}";
            }
        }

        public void Apply(Step step)
        {
            if (step is Load load)
            {
                // Not the user's: written as a load writes, recording nothing.
                Load(load.CostsChanged);
                _restoring = true;
                Draft.ResolveRows();
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
                // Refused, as the tab would refuse it: nothing changed.
            }

            History.End();
        }

        public bool TryUndo() => Move(History.TryUndo(out var snapshot), snapshot);

        public bool TryRedo() => Move(History.TryRedo(out var snapshot), snapshot);

        private bool Move(bool moved, PackagingSnapshot snapshot)
        {
            if (moved)
            {
                _restoring = true;
                try
                {
                    Draft.Restore(snapshot);
                }
                finally
                {
                    _restoring = false;
                }
            }

            return moved;
        }

        private void Edit(Step step)
        {
            var draft = Draft;
            var lines = draft.LineEdits;
            var bulk = draft.BulkEdits;
            switch (step)
            {
                // As the tables let them be typed: each only while its lines are the product's own.
                case AddLine(var material, var quarters) when draft.IsPackagingEditable:
                    AddRow(lines, material, quarters);
                    break;
                case TypeQuantity(var at, var quarters) when draft.IsPackagingEditable && lines.Count > 0:
                    lines[at % lines.Count].QtyPerUnit = quarters / 4m;
                    break;
                case PickMaterial(var at, var material) when draft.IsPackagingEditable && lines.Count > 0:
                    lines[at % lines.Count].PackagingMaterialId = Materials[material].Id;
                    break;
                case PickUnit(var at, var alternative) when draft.IsPackagingEditable && lines.Count > 0:
                    var row = lines[at % lines.Count];
                    if (row.UnitOptions.Count > 1)
                    {
                        row.UnitKey = alternative ? row.UnitOptions[1].Key : PackagingUnitChoice.BaseUnitKey;
                    }

                    break;
                case RemoveLine(var at) when draft.IsPackagingEditable && lines.Count > 0:
                    lines.RemoveAt(at % lines.Count);
                    break;
                case AddBulkLine(var material, var quarters) when draft.IsBulkEditable:
                    AddRow(bulk, material, quarters);
                    break;
                case TypeBulkQuantityPerPack(var at, var quarters) when draft.IsBulkEditable && bulk.Count > 0:
                    bulk[at % bulk.Count].QtyPerUnit = quarters / 4m;
                    break;
                case RemoveBulkLine(var at) when draft.IsBulkEditable && bulk.Count > 0:
                    bulk.RemoveAt(at % bulk.Count);
                    break;
                case TypeBulkQuantity(var products):
                    draft.BulkQuantity = products;
                    break;
                case LinkSet(var index):
                    // As the picker takes a set: its lines read, linked in place of the packaging per piece.
                    draft.Take([new PackagingLineRow(Materials[index], 1m + index)], new PackagingLinkTarget.Set(SetId + index, $"Set {index}"));
                    break;
                case CopyProduct(var index):
                    draft.Take([new PackagingLineRow(Materials[2 - index], 2m), new PackagingLineRow(Materials[index], 0.5m)], null);
                    break;
                case MakeCopy when draft.IsPackagingLinked:
                    draft.MakeCopy();
                    break;
                case RemoveLink when draft.IsPackagingLinked:
                    draft.RemoveLink();
                    break;
                case MoveToSubAssemblies when draft.CanMovePackaging:
                    draft.StartMoving();
                    break;
                case LinkBulkSet(var index):
                    // As the bulk packaging's picker takes a set: its lines read, linked in place of the bulk lines.
                    draft.TakeBulk([new PackagingLineRow(Materials[index], 1m + index)], new PackagingLinkTarget.Set(BulkSetId + index, $"Bulk Set {index}"));
                    break;
                case MakeBulkCopy when draft.IsBulkLinked:
                    draft.MakeBulkCopy();
                    break;
                case RemoveBulkLink when draft.IsBulkLinked:
                    draft.RemoveBulkLink();
                    break;
            }
        }

        /// <summary>As a table's add row: the row joins the table as its material is picked, then its quantity is typed.</summary>
        private void AddRow(ObservableCollection<PackagingLineRow> rows, int material, int quarters)
        {
            var row = Draft.NewRow();
            row.PackagingMaterialId = Materials[material].Id;
            rows.Add(row);
            row.QtyPerUnit = quarters / 4m;
        }

        /// <summary>A line as it was saved, its material the one the screen holds.</summary>
        private PackagingLine Saved(int materialId, decimal qtyPerUnit, int? unitId = null) => new()
        {
            PackagingMaterialId = materialId,
            PackagingMaterial = Materials.First(m => m.Id == materialId),
            UnitId = unitId,
            QtyPerUnit = qtyPerUnit,
            AuthoredField = PackagingLineAuthoredField.QtyPerUnit,
        };

        /// <summary>Fresh copies of the packaging materials, in place of the ones held: a ribbon by the centimetre (and by the roll), a box, tape.</summary>
        private void Load(bool costsChanged)
        {
            var factor = costsChanged ? 1m + (++_loads % 3) * 0.25m : 1m;
            var centimetre = new UnitOfMeasure { Id = 1, Name = "centimetre", Abbreviation = "cm" };
            var roll = new UnitOfMeasure { Id = 2, Name = "roll", Abbreviation = "roll" };
            var piece = new UnitOfMeasure { Id = 3, Name = "piece", Abbreviation = "pc" };

            Materials.Clear();
            Materials.Add(Material(61, "Satin Ribbon", centimetre, 0.24m * factor, new RawMaterialUnit { Id = 611, RawMaterialId = 61, UnitOfMeasureId = roll.Id, UnitOfMeasure = roll, BaseUnitsPerUnit = 2000m }));
            Materials.Add(Material(62, "Box 8 x 8", piece, 26.4m * factor));
            Materials.Add(Material(63, "Scotch Tape", centimetre, 0.0014m * factor));
        }

        private static RawMaterial Material(int id, string name, UnitOfMeasure baseUnit, decimal cost, params RawMaterialUnit[] units) => new()
        {
            Id = id, Name = name, Type = RawMaterialType.Packaging, Source = RawMaterialSource.Purchased,
            BaseUnitId = baseUnit.Id, BaseUnit = baseUnit, CostPerBaseUnit = cost, BufferedCostPerBaseUnit = cost * 1.1m, CostBufferPercent = 10m,
            Units = [.. units],
        };
    }
}
