using System.Collections.ObjectModel;
using AwesomeAssertions;
using CsCheck;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.SubAssemblies;

namespace TinapayanRMS.Presentation.Tests.SubAssemblies;

/// <summary>
/// The Sub-Assemblies draft on its own, with no database and no screen (docs/adr/0006): random sequences of what a user
/// does to it (a name, a role, lines added, typed into, ticked as the base and taken out, a packaging set's lines), each an
/// undo step, with loads that bring fresh copies of what it refers to between them. Whatever the sequence, the dirty check
/// says whether it differs from where it started, a load changes nothing that's saved, and undoing every step gives back
/// where it started, redoing them where it ended. CsCheck shrinks a failure to the shortest sequence that shows it.
/// </summary>
public class SubAssemblyDraftTests
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
                var now = world.Draft.Capture(0);
                world.History.IsDirty.Should().Be(!start.ShowsSameAs(now), $"after {step}, the dirty check says whether the draft differs from where it started");
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

            world.Draft.Capture(0).ShowsSameAs(before).Should().BeTrue("a load re-points the draft at fresh copies, by id");
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
    public void A_load_prices_the_lines_from_the_fresh_copies()
    {
        var world = new World();
        world.Apply(new AddLine(0, 1000));

        world.Apply(new Load(CostsChanged: true));

        world.Draft.Batch.Lines.Single().Ingredient.Should().BeSameAs(world.Ingredients[0], "the line now refers to the load's copy");
        world.Draft.Batch.BatchCost.Should().Be(1000m * world.Ingredients[0].BufferedCostPerBaseUnit);
    }

    [Test]
    public void A_save_request_holds_the_lines_filled_in_and_refuses_one_half_filled()
    {
        var world = new World();
        world.Apply(new Rename("  Sweet Dough "));
        world.Apply(new AddLine(0, 1000));

        var request = world.Draft.ToSaveRequest(7, savedVersion: null);
        (request.Id, request.Name, request.RoleId, request.IsPackagingSet).Should().Be((7, "Sweet Dough", world.Roles[0].Id, false));
        request.Lines.Select(l => (l.Ingredient.Id, l.Grams)).Should().Equal((world.Ingredients[0].Id, 1000m));

        world.Draft.Batch.Lines.Add(new IngredientLineRow { Grams = 50m });
        world.Draft.Invoking(d => d.ToSaveRequest(7, null)).Should().Throw<BusinessRuleException>().WithMessage("Pick the ingredient for the 50 g line of Sweet Dough*");
    }

    /// <summary>One thing a user does to the draft, as one undo step, or a load between them.</summary>
    private abstract record Step;

    private sealed record Rename(string Name) : Step;

    private sealed record PickRole(int Index) : Step;

    private sealed record AddLine(int Ingredient, int Grams) : Step;

    private sealed record TypeGrams(int Line, int Grams) : Step;

    private sealed record TypePercent(int Line, int Percent) : Step;

    private sealed record TickBase(int Line) : Step;

    private sealed record RemoveLine(int Line) : Step;

    private sealed record AddPackaging(int Material, int Quantity) : Step;

    private sealed record TypeQuantity(int Line, int Quantity) : Step;

    private sealed record RemovePackaging(int Line) : Step;

    /// <summary>The view's load: fresh copies of the roles and materials, the costs moved or not.</summary>
    private sealed record Load(bool CostsChanged) : Step;

    private static readonly string[] Names = ["Sweet Dough", "Ube Filling", " Glaze ", ""];

    private static readonly Gen<Step> Steps = Gen.OneOf<Step>(
        Gen.Int[0, 3].Select(index => (Step)new Rename(Names[index])),
        Gen.Int[0, 2].Select(index => (Step)new PickRole(index)),
        Gen.Select(Gen.Int[0, 3], Gen.Int[0, 1500], (ingredient, grams) => (Step)new AddLine(ingredient, grams)),
        Gen.Select(Gen.Int[0, 9], Gen.Int[0, 1500], (line, grams) => (Step)new TypeGrams(line, grams)),
        Gen.Select(Gen.Int[0, 9], Gen.Int[0, 150], (line, percent) => (Step)new TypePercent(line, percent)),
        Gen.Int[0, 9].Select(line => (Step)new TickBase(line)),
        Gen.Int[0, 9].Select(line => (Step)new RemoveLine(line)),
        Gen.Select(Gen.Int[0, 1], Gen.Int[1, 12], (material, quantity) => (Step)new AddPackaging(material, quantity)),
        Gen.Select(Gen.Int[0, 9], Gen.Int[0, 12], (line, quantity) => (Step)new TypeQuantity(line, quantity)),
        Gen.Int[0, 9].Select(line => (Step)new RemovePackaging(line)),
        Gen.Bool.Select(costsChanged => (Step)new Load(costsChanged)));

    /// <summary>A draft as a form holds it: the view's lists it reads, and an undo history over it, recording only the user's steps.</summary>
    private sealed class World : IUndoRecorder
    {
        private bool _restoring;
        private int _loads;

        public World()
        {
            Load(costsChanged: false);
            Draft = new SubAssemblyDraft(this, _ => { }, Roles, Ingredients, PackagingMaterials);
            History = new UndoHistory<SubAssemblyFormSnapshot>(() => Draft.Capture(0), (a, b) => a.ShowsSameAs(b), () => !_restoring);
            Draft.Changed += History.RefreshDirty;
            Draft.Clear(role: null);
            History.MarkClean();
        }

        public ObservableCollection<SubAssemblyRole> Roles { get; } = [];

        public ObservableCollection<RawMaterial> Ingredients { get; } = [];

        public ObservableCollection<RawMaterial> PackagingMaterials { get; } = [];

        public SubAssemblyDraft Draft { get; }

        public UndoHistory<SubAssemblyFormSnapshot> History { get; }

        public void Apply(Step step)
        {
            if (step is Load load)
            {
                // Not the user's: written as a load writes, recording nothing.
                Load(load.CostsChanged);
                Draft.Rebase(Draft.Role?.Id, newWithoutRole: false);
                return;
            }

            History.Begin();
            Edit(step);
            History.End();
        }

        public bool TryUndo() => Move(History.TryUndo(out var snapshot), snapshot);

        public bool TryRedo() => Move(History.TryRedo(out var snapshot), snapshot);

        private bool Move(bool moved, SubAssemblyFormSnapshot snapshot)
        {
            if (moved)
            {
                _restoring = true;
                Draft.Restore(snapshot);
                _restoring = false;
            }

            return moved;
        }

        private void Edit(Step step)
        {
            var lines = Draft.Batch.Lines;
            var packaging = Draft.PackagingSet.Lines;
            switch (step)
            {
                case Rename(var name):
                    Draft.Name = name;
                    break;
                case PickRole(var index):
                    Draft.Role = Roles[index];
                    break;
                case AddLine(var ingredient, var grams):
                    // As the table's add row: the line joins the lines as its ingredient is picked, then its grams are typed.
                    var line = Draft.Batch.NewLine();
                    line.IngredientId = Ingredients[ingredient].Id;
                    lines.Add(line);
                    line.Grams = grams;
                    break;
                case TypeGrams(var at, var grams) when lines.Count > 0:
                    lines[at % lines.Count].Grams = grams;
                    break;
                case TypePercent(var at, var percent) when lines.Count > 0:
                    lines[at % lines.Count].Percent = percent;
                    break;
                case TickBase(var at) when lines.Count > 0:
                    lines[at % lines.Count].IsFormulaBase = !lines[at % lines.Count].IsFormulaBase;
                    break;
                case RemoveLine(var at) when lines.Count > 0:
                    lines.RemoveAt(at % lines.Count);
                    break;
                case AddPackaging(var material, var quantity):
                    var row = Draft.PackagingSet.NewLine();
                    row.PackagingMaterialId = PackagingMaterials[material].Id;
                    packaging.Add(row);
                    row.QtyPerUnit = quantity;
                    break;
                case TypeQuantity(var at, var quantity) when packaging.Count > 0:
                    packaging[at % packaging.Count].QtyPerUnit = quantity;
                    break;
                case RemovePackaging(var at) when packaging.Count > 0:
                    packaging.RemoveAt(at % packaging.Count);
                    break;
            }
        }

        /// <summary>Fresh copies of the roles and materials, in place of the ones held, as a load brings them.</summary>
        private void Load(bool costsChanged)
        {
            var factor = costsChanged ? 1m + (++_loads % 3) * 0.25m : 1m;
            Roles.Clear();
            Roles.Add(new SubAssemblyRole { Id = 1, Name = "Dough", SortOrder = 1 });
            Roles.Add(new SubAssemblyRole { Id = 2, Name = "Filling", SortOrder = 2 });
            Roles.Add(new SubAssemblyRole { Id = 3, Name = "Packaging", SortOrder = 3, IsPackaging = true });

            Ingredients.Clear();
            foreach (var (id, name, cost) in new[] { (11, "Bread Flour", 0.05m), (12, "Water", 0m), (13, "Sugar", 0.09m), (14, "Butter", 0.42m) })
            {
                Ingredients.Add(new RawMaterial
                {
                    Id = id, Name = name, Type = RawMaterialType.Ingredient,
                    CostPerBaseUnit = cost * factor, BufferedCostPerBaseUnit = cost * factor,
                });
            }

            var piece = new UnitOfMeasure { Id = 1, Name = "Piece", Abbreviation = "pc" };
            PackagingMaterials.Clear();
            foreach (var (id, name, cost) in new[] { (21, "Box", 4.5m), (22, "Sticker", 0.35m) })
            {
                PackagingMaterials.Add(new RawMaterial
                {
                    Id = id, Name = name, Type = RawMaterialType.Packaging, BaseUnitId = piece.Id, BaseUnit = piece,
                    CostPerBaseUnit = cost * factor, BufferedCostPerBaseUnit = cost * factor,
                });
            }
        }

        void IUndoRecorder.BeginUndoStep() => History.Begin();

        void IUndoRecorder.EndUndoStep() => History.End();

        bool IUndoRecorder.IsEditPending { get; set; }

        int IUndoRecorder.FormVersion => 0;
    }
}
