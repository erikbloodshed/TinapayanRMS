using System.Collections.ObjectModel;
using AwesomeAssertions;
using CsCheck;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.SubAssemblies;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.UseCases.SubAssemblies;

namespace TinapayanRMS.Presentation.Tests.SubAssemblies;

/// <summary>
/// The Sub-Assemblies draft on its own, with no database and no screen (docs/adr/0006): random sequences of what a user
/// does to it (a name, a role, lines added, typed into, ticked as the base and taken out, a packaging set's lines), each an
/// undo step, with undos, redos and Saves among them, and loads that bring fresh copies of what it refers to, renamed or
/// repriced or not. After every step the draft is where its history says it is, the dirty check says whether it differs from
/// what was last saved, and its figures are what its lines give as they stand (docs/adr/0012). CsCheck shrinks a failure
/// to the shortest sequence that shows it.
/// </summary>
public class SubAssemblyDraftTests
{
    [Test]
    public void Any_sequence_keeps_the_draft_its_history_and_its_figures_right() =>
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

        world.Apply(new Load(CostsChanged: true, Renamed: false));

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

    /// <summary>One thing a user does to the draft, as one undo step; an undo, a redo or a Save; or a load between them.</summary>
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

    private sealed record Undo : Step;

    private sealed record Redo : Step;

    /// <summary>Save, as the form does it: refused when a rule refuses it, else written and shown again as saved.</summary>
    private sealed record Save : Step;

    /// <summary>
    /// The view's load: fresh copies of the roles and materials, as saved elsewhere since: repriced, renamed, both or
    /// neither.
    /// </summary>
    private sealed record Load(bool CostsChanged, bool Renamed) : Step;

    private static readonly string[] Names = ["Sweet Dough", "Ube Filling", " Glaze ", ""];

    private static readonly Gen<Step> Steps = Gen.Frequency(
        (12, Gen.OneOf<Step>(
            Gen.Int[0, 3].Select(index => (Step)new Rename(Names[index])),
            Gen.Int[0, 2].Select(index => (Step)new PickRole(index)),
            Gen.Select(Gen.Int[0, 3], Gen.Int[0, 1500], (ingredient, grams) => (Step)new AddLine(ingredient, grams)),
            Gen.Select(Gen.Int[0, 9], Gen.Int[0, 1500], (line, grams) => (Step)new TypeGrams(line, grams)),
            Gen.Select(Gen.Int[0, 9], Gen.Int[0, 150], (line, percent) => (Step)new TypePercent(line, percent)),
            Gen.Int[0, 9].Select(line => (Step)new TickBase(line)),
            Gen.Int[0, 9].Select(line => (Step)new RemoveLine(line)),
            Gen.Select(Gen.Int[0, 1], Gen.Int[1, 12], (material, quantity) => (Step)new AddPackaging(material, quantity)),
            Gen.Select(Gen.Int[0, 9], Gen.Int[0, 12], (line, quantity) => (Step)new TypeQuantity(line, quantity)),
            Gen.Int[0, 9].Select(line => (Step)new RemovePackaging(line)))),
        (3, Gen.Const<Step>(new Undo())),
        (2, Gen.Const<Step>(new Redo())),
        (1, Gen.Const<Step>(new Save())),
        (2, Gen.Select(Gen.Bool, Gen.Bool, (costs, renamed) => (Step)new Load(costs, renamed))));

    /// <summary>
    /// A draft as a form holds it: the view's lists it reads, an undo history over it recording only the user's steps, and
    /// beside them a model of that history: every state a step left, where the history stands among them, and what was
    /// last saved.
    /// </summary>
    private sealed class World : IUndoRecorder
    {
        private bool _restoring;
        private int _loads;
        private int _renames;
        private readonly UndoModel<SubAssemblyFormSnapshot> _model;

        public World()
        {
            Load(costsChanged: false, renamed: false);
            Draft = new SubAssemblyDraft(this, _ => { }, Roles, Ingredients, PackagingMaterials);
            History = new UndoHistory<SubAssemblyFormSnapshot>(() => Draft.Capture(0), (a, b) => a.ShowsSameAs(b), () => !_restoring);
            Draft.Changed += History.RefreshDirty;
            Draft.Clear(role: null);
            History.MarkClean();
            _model = new UndoModel<SubAssemblyFormSnapshot>(Draft.Capture(0), (a, b) => a.ShowsSameAs(b));
        }

        public ObservableCollection<SubAssemblyRole> Roles { get; } = [];

        public ObservableCollection<RawMaterial> Ingredients { get; } = [];

        public ObservableCollection<RawMaterial> PackagingMaterials { get; } = [];

        public SubAssemblyDraft Draft { get; }

        public UndoHistory<SubAssemblyFormSnapshot> History { get; }

        /// <summary>Takes <paramref name="step"/>, the model with it. True when an undo or a redo moved, or a Save wrote.</summary>
        public bool Apply(Step step)
        {
            switch (step)
            {
                case Load load:
                    // Not the user's: written as a load writes, recording nothing.
                    var before = Draft.Capture(0);
                    Load(load.CostsChanged, load.Renamed);
                    Draft.Rebase(Draft.Role?.Id, newWithoutRole: false);
                    Draft.Capture(0).ShowsSameAs(before).Should().BeTrue($"{load} re-points the draft, changing nothing it saves");
                    ShouldReferToTheFreshCopies(load);
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
                    Edit(step);
                    History.End();
                    _model.Edited(was, Draft.Capture(0));
                    return false;
            }
        }

        /// <summary>The draft, its history and its figures, as the model and the lines say.</summary>
        public void ShouldBeRight(string when)
        {
            _model.ShouldMatch(History, Draft.Capture(0), when);

            // The figures, worked out afresh from the lines as they stand: a mark missed leaves them as they were.
            var lines = Draft.Batch.Lines;
            var weight = lines.Where(l => l.Ingredient is not null).Sum(l => l.Grams ?? 0m);
            var cost = lines.Sum(l => l.Cost);
            (Draft.Batch.BatchWeight, Draft.Batch.BatchCost, Draft.Batch.CostPerGram).Should().Be(
                (weight, cost, weight > 0 ? cost / weight : 0m), $"{when}, the batch's figures follow its lines");
            Draft.PackagingSet.Cost.Should().Be(Draft.PackagingSet.Lines.Sum(l => l.CostPerUnit), $"{when}, the set's cost follows its lines");
        }

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

        /// <summary>
        /// Save as the form does it (SubAssemblyForm.SaveAsync): a name, a role and lines, none half filled in, else refused
        /// with nothing changed; written, it's shown again as saved, with nothing to undo, and what a Save of it would write
        /// is what was just written.
        /// </summary>
        private bool TrySave()
        {
            SubAssemblySaveRequest request;
            try
            {
                request = Draft.ToSaveRequest(1, savedVersion: null);
            }
            catch (BusinessRuleException)
            {
                return false;
            }

            if (request.Name.Length == 0 || request.RoleId == 0 || request.Lines.Count + request.PackagingLines.Count == 0)
            {
                return false;
            }

            History.Clear();
            Draft.Show(request.Name, request.RoleId, new SubAssemblyContent(
                [.. request.Lines.Select((l, i) => new SubAssemblyLine
                {
                    IngredientId = l.Ingredient.Id, Ingredient = l.Ingredient, Grams = l.Grams, IsFormulaBase = l.IsFormulaBase,
                    AuthoredBakersPercent = l.AuthoredBakersPercent, SortOrder = i,
                })],
                [.. request.PackagingLines.Select((l, i) => new SubAssemblyPackagingLine
                {
                    PackagingMaterialId = l.PackagingMaterialId, UnitId = l.UnitId, QtyPerSet = l.QtyPerSet, SortOrder = i,
                })]));
            History.MarkClean();

            Written(Draft.ToSaveRequest(1, savedVersion: null)).Should().Be(Written(request), "the sub-assembly shown again as saved writes what was saved");
            _model.Saved(Draft.Capture(0));
            return true;
        }

        /// <summary>A Save's request as text, so two compare by what they'd write.</summary>
        private static string Written(SubAssemblySaveRequest request) =>
            $"{request.Name}|{request.RoleId}|{request.IsPackagingSet}|"
            + string.Join(";", request.Lines.Select(l => $"{l.Ingredient.Id}:{l.Grams}:{l.IsFormulaBase}:{l.AuthoredBakersPercent}"))
            + "|" + string.Join(";", request.PackagingLines.Select(l => $"{l.PackagingMaterialId}:{l.UnitId}:{l.QtyPerSet}"));

        /// <summary>After a load, the role and every line refer to the load's copies, so their names and costs are the ones saved now.</summary>
        private void ShouldReferToTheFreshCopies(Load load)
        {
            if (Draft.Role is { } role)
            {
                role.Should().BeSameAs(Roles.Single(r => r.Id == role.Id), $"after {load}, the role is the load's copy");
            }

            foreach (var line in Draft.Batch.Lines.Where(l => l.IngredientId is not null))
            {
                line.Ingredient.Should().BeSameAs(Ingredients.Single(m => m.Id == line.IngredientId), $"after {load}, a line refers to the load's copy");
            }

            foreach (var line in Draft.PackagingSet.Lines.Where(l => l.PackagingMaterialId is not null))
            {
                line.PackagingMaterial.Should().BeSameAs(PackagingMaterials.Single(m => m.Id == line.PackagingMaterialId), $"after {load}, a line refers to the load's copy");
            }
        }

        /// <summary>A step only as the screen lets the user take it: a recipe's lines on a recipe, a set's lines on a packaging set.</summary>
        private void Edit(Step step)
        {
            var lines = Draft.Batch.Lines;
            var packaging = Draft.PackagingSet.Lines;
            var isRecipe = !Draft.IsPackaging;
            switch (step)
            {
                case Rename(var name):
                    Draft.Name = name;
                    break;
                case PickRole(var index):
                    Draft.Role = Roles[index];
                    break;
                case AddLine(var ingredient, var grams) when isRecipe:
                    // As the table's add row: the line joins the lines as its ingredient is picked, then its grams are typed.
                    var line = Draft.Batch.NewLine();
                    line.IngredientId = Ingredients[ingredient].Id;
                    lines.Add(line);
                    line.Grams = grams;
                    break;
                case TypeGrams(var at, var grams) when isRecipe && lines.Count > 0:
                    lines[at % lines.Count].Grams = grams;
                    break;
                case TypePercent(var at, var percent) when isRecipe && lines.Count > 0:
                    lines[at % lines.Count].Percent = percent;
                    break;
                case TickBase(var at) when isRecipe && lines.Count > 0:
                    lines[at % lines.Count].IsFormulaBase = !lines[at % lines.Count].IsFormulaBase;
                    break;
                case RemoveLine(var at) when isRecipe && lines.Count > 0:
                    lines.RemoveAt(at % lines.Count);
                    break;
                case AddPackaging(var material, var quantity) when !isRecipe:
                    var row = Draft.PackagingSet.NewLine();
                    row.PackagingMaterialId = PackagingMaterials[material].Id;
                    packaging.Add(row);
                    row.QtyPerUnit = quantity;
                    break;
                case TypeQuantity(var at, var quantity) when !isRecipe && packaging.Count > 0:
                    packaging[at % packaging.Count].QtyPerUnit = quantity;
                    break;
                case RemovePackaging(var at) when !isRecipe && packaging.Count > 0:
                    packaging.RemoveAt(at % packaging.Count);
                    break;
            }
        }

        /// <summary>Fresh copies of the roles and materials, in place of the ones held, as a load brings them.</summary>
        private void Load(bool costsChanged, bool renamed)
        {
            var factor = costsChanged ? 1m + (++_loads % 3) * 0.25m : 1m;
            var suffix = renamed ? $" ({++_renames})" : string.Empty;
            Roles.Clear();
            Roles.Add(new SubAssemblyRole { Id = 1, Name = "Dough" + suffix, SortOrder = 1 });
            Roles.Add(new SubAssemblyRole { Id = 2, Name = "Filling" + suffix, SortOrder = 2 });
            Roles.Add(new SubAssemblyRole { Id = 3, Name = "Packaging" + suffix, SortOrder = 3, IsPackaging = true });

            Ingredients.Clear();
            foreach (var (id, name, cost) in new[] { (11, "Bread Flour", 0.05m), (12, "Water", 0m), (13, "Sugar", 0.09m), (14, "Butter", 0.42m) })
            {
                Ingredients.Add(new RawMaterial
                {
                    Id = id, Name = name + suffix, Type = RawMaterialType.Ingredient,
                    CostPerBaseUnit = cost * factor, BufferedCostPerBaseUnit = cost * factor,
                });
            }

            var piece = new UnitOfMeasure { Id = 1, Name = "Piece", Abbreviation = "pc" };
            PackagingMaterials.Clear();
            foreach (var (id, name, cost) in new[] { (21, "Box", 4.5m), (22, "Sticker", 0.35m) })
            {
                PackagingMaterials.Add(new RawMaterial
                {
                    Id = id, Name = name + suffix, Type = RawMaterialType.Packaging, BaseUnitId = piece.Id, BaseUnit = piece,
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
