using System.Collections.ObjectModel;
using AwesomeAssertions;
using CsCheck;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// A product's recipe draft on its own, with no database and no screen (docs/adr/0006): random sequences of what the recipe
/// part does to a saved product's sections (a section of its own added, a sub-assembly linked, a linked one copied or
/// unlinked, one of its own moved to Sub-Assemblies, its lines or a linked one's piece size changed, a section taken out),
/// each an undo step, with undos, redos and Saves among them, and loads that bring fresh copies of the ingredients and the
/// sub-assemblies, their costs moved or not, and what was saved elsewhere since. After every step the sections are where
/// their history says, in recipe order and told apart, each knows whether others share its role, and the dirty check says
/// whether a Save would write the recipe differently from what was last saved; a load changes nothing Save writes, a
/// recipe that saves as loaded is as saved, and an undo shows the costs the last load brought. A Save here is what the
/// form's dirty check sees of one: what was written is what's measured against, with nothing to undo; the form's reload of
/// it is tested on the database.
/// </summary>
public class ProductRecipeDraftTests
{
    [Test]
    public void Any_sequence_keeps_the_sections_and_their_history_right_and_in_order() =>
        Steps.Array[0, 40].Sample(steps =>
        {
            var world = new World();
            foreach (var step in steps)
            {
                world.Apply(step);
                world.ShouldBeRight($"after {step}");
            }

            // Every step left undone, at the costs the last load brought: a step keeps the ingredients it was taken with, and
            // an undo puts its sections back at the ones the screen holds now (ProductRecipeDraft.Restore).
            while (world.Apply(new Undo()))
            {
                world.ShouldBeRight("undoing every step");
            }

            world.ShouldShowTheLastLoadsCosts("undone");
            world.History.IsDirty.Should().BeFalse("every step undone gives back what was last saved");
            if (!steps.Any(s => s is ChangedElsewhere(3) or Save))
            {
                // Unless the load made a linked section the product's own, which Save then writes as such, or a Save moved
                // what's measured against from what was loaded.
                world.Draft.IsAsSaved(world.Draft.CurrentDraft()).Should().BeTrue("undone back to how it was loaded, Save needn't write it");
            }

            while (world.Apply(new Redo()))
            {
                world.ShouldBeRight("redoing every step");
            }
        }, iter: 300);

    [Test]
    public void A_load_changes_nothing_Save_writes() =>
        Steps.Array[0, 30].Sample(steps =>
        {
            var world = new World();
            foreach (var step in steps)
            {
                world.Apply(step);
            }

            var before = Describe(world.Draft.CurrentDraft());
            var wasDirty = world.History.IsDirty;
            world.Apply(new Load(CostsChanged: true));

            Describe(world.Draft.CurrentDraft()).Should().Be(before, "a load re-points the sections at fresh copies, by id");
            world.History.IsDirty.Should().Be(wasDirty);
            world.ShouldHoldItsRules("after the load");
        }, iter: 300);

    [Test]
    public void A_change_saved_elsewhere_never_moves_what_lines_changed_here_were_changed_from() =>
        Gen.Select(Steps.Array[0, 30], Gen.Int[0, 3]).Sample((steps, kind) =>
        {
            var world = new World();
            foreach (var step in steps)
            {
                world.Apply(step);
            }

            var before = world.Draft.CurrentDraft().Linked.Where(l => l.Lines is not null).ToDictionary(l => l.SubAssemblyId);
            world.Apply(new ChangedElsewhere(kind));

            // One whose lines are now what's kept there has nothing left to write, so nothing to check.
            foreach (var now in world.Draft.CurrentDraft().Linked.Where(l => l.Lines is not null))
            {
                if (before.TryGetValue(now.SubAssemblyId, out var was))
                {
                    now.LinesTaken.Should().Equal(was.LinesTaken!, "the Save that writes them is refused over what was saved since");
                }
            }
        }, iter: 300);

    [Test]
    public void A_section_changed_here_stays_as_changed_when_its_ownership_changes_elsewhere() =>
        Gen.Select(Steps.Array[0, 30], Gen.Int[4, 5]).Sample((steps, kind) =>
        {
            var world = new World();
            foreach (var step in steps)
            {
                world.Apply(step);
            }

            // The dough of its own released (4), or the icing it links made its own (5).
            var id = kind == 4 ? 51 : 41;
            var before = world.Capture().FirstOrDefault(u => u.SubAssemblyId == id);
            var start = world.Start.FirstOrDefault(u => u.SubAssemblyId == id);
            world.Apply(new ChangedElsewhere(kind));

            if (before is not null && start is not null && Saved(before) != Saved(start))
            {
                Saved(world.Capture().Single(u => u.SubAssemblyId == id)).Should().Be(Saved(before), "a load changes nothing typed (ADR 0007)");
            }
        }, iter: 300);

    [Test]
    public void A_recipe_that_saves_as_loaded_is_as_saved() =>
        Steps.Array[0, 40].Sample(steps =>
        {
            var world = new World();
            var loaded = Describe(world.Draft.CurrentDraft());

            // What's as saved is measured from the load; a Save's reload, which would move it, is the form's.
            foreach (var step in steps.Where(s => s is not Save))
            {
                world.Apply(step);
                var now = world.Draft.CurrentDraft();
                world.Draft.IsAsSaved(now).Should().Be(Describe(now) == loaded, $"after {step}");
            }
        }, iter: 300);

    [Test]
    public void An_undo_across_a_load_that_moved_the_costs_shows_the_costs_the_load_brought()
    {
        var world = new World();
        var dough = world.Draft.UsageEdits.Single(u => !u.IsLinked);
        var costBefore = dough.TotalCostAt(CostBasis.Buffered);
        world.Apply(new EditLines(Row: world.Draft.UsageEdits.IndexOf(dough), Ingredient: 0, Grams: 500));
        world.Apply(new Load(CostsChanged: true));

        world.Apply(new Undo()).Should().BeTrue();

        var undone = world.Draft.UsageEdits.Single(u => !u.IsLinked);
        undone.Lines.Select(l => (l.Ingredient.Id, l.Grams)).Should().Equal(dough.Lines.Select(l => (l.Ingredient.Id, l.Grams)), "the edit is undone");
        undone.TotalCostAt(CostBasis.Buffered).Should().BeGreaterThan(costBefore, "the load moved every cost up, and the undone dough shows it");
        world.ShouldShowTheLastLoadsCosts("undone");
    }

    [Test]
    public void A_linked_section_copied_becomes_a_new_one_of_its_lines_and_add_ins_at_its_piece_size()
    {
        var world = new World();
        var linked = world.Draft.UsageEdits.Single(u => u.IsLinked);

        var copy = ProductRecipeDraft.CopyOfLinked(linked, isOwn: true);

        copy.IsNew.Should().BeTrue();
        copy.IsOwned.Should().BeTrue("unlinked, it's the product's own whatever its role");
        copy.Lines.Select(l => (l.Ingredient.Id, l.Grams)).Should().Equal(linked.Lines.Concat(linked.AddIns).Select(l => (l.Ingredient.Id, l.Grams)));
        (copy.AuthoredField, copy.AuthoredValue).Should().Be((linked.AuthoredField, linked.AuthoredValue));
    }

    private const int ProductId = 7;

    /// <summary>What a Save would write, as text: the new and own sections, the linked ones, in order.</summary>
    private static string Describe(RecipeDraft draft) =>
        string.Join(" | ", draft.New.Select(n => $"new {n.Id} {n.Placement} role {n.RoleId} '{n.SectionName}' {n.AuthoredField} {n.AuthoredValue} [{string.Join(", ", n.Lines)}]"))
        + " || "
        + string.Join(" | ", draft.Linked.Select(l => $"linked {l.SubAssemblyId} {l.AuthoredField} {l.AuthoredValue} add-ins [{string.Join(", ", l.AddIns)}] lines [{(l.Lines is null ? "as kept" : string.Join(", ", l.Lines))}]"));

    /// <summary>Two recipes as an undo step keeps them, section for section, looking the same, costs and all.</summary>
    private static bool ShowsSameAs(IReadOnlyList<ProductUsageEdit> a, IReadOnlyList<ProductUsageEdit> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => pair.First.ShowsSameAs(pair.Second));

    /// <summary>Two recipes a Save would write the same: as ShowsSameAs, but whatever the ingredients cost.</summary>
    private static bool SavesSameAs(IReadOnlyList<ProductUsageEdit> a, IReadOnlyList<ProductUsageEdit> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => Saved(pair.First) == Saved(pair.Second));

    private static string Saved(ProductUsageEdit u) =>
        $"{u.SubAssemblyId} {u.IsOwned} {u.IsReleasing} {u.RoleId} {u.LinkedName} '{u.Name}' {u.AuthoredField} {u.AuthoredValue} {u.AreLinkedLinesEdited} "
        + $"[{string.Join(", ", u.Lines.Select(l => (l.Ingredient.Id, l.Grams, l.IsFormulaBase)))}] [{string.Join(", ", u.AddIns.Select(l => (l.Ingredient.Id, l.Grams)))}]";

    /// <summary>One thing the recipe part does to the sections, as one undo step, or a load between them.</summary>
    private abstract record Step;

    private sealed record AddOwn(int Role, int Name, int Ingredient, int Grams) : Step;

    private sealed record Link(int SubAssembly) : Step;

    private sealed record CopyLinked(int Row, bool IsOwn) : Step;

    private sealed record MoveToSubAssemblies(int Row) : Step;

    private sealed record EditLines(int Row, int Ingredient, int Grams) : Step;

    private sealed record PieceSize(int Row, bool Yield, int Value) : Step;

    private sealed record Remove(int Row) : Step;

    /// <summary>The view's load: fresh copies of the ingredients, the roles and the sub-assemblies, the costs moved or not.</summary>
    private sealed record Load(bool CostsChanged) : Step;

    /// <summary>
    /// A load bringing what was saved elsewhere since: the butter icing's lines changed (0), the ube filling renamed (1) or put
    /// in the Topping role (2), or the cheese filling's role made product-owned, so this product owns it (3). Only where
    /// a test says: the dough's role no longer product-owned (4), or the butter icing's made so (5).
    /// </summary>
    private sealed record ChangedElsewhere(int Kind) : Step;

    private sealed record Undo : Step;

    private sealed record Redo : Step;

    /// <summary>Save: what was written is what the dirty check measures against.</summary>
    private sealed record Save : Step;

    private static readonly string?[] Names = [null, "Ube", "Pandan"];

    private static readonly Gen<Step> Steps = Gen.Frequency(
        (12, Gen.OneOf<Step>(
            Gen.Select(Gen.Int[0, 2], Gen.Int[0, 2], Gen.Int[0, 3], Gen.Int[1, 900], (role, name, ingredient, grams) => (Step)new AddOwn(role, name, ingredient, grams)),
            Gen.Int[0, 2].Select(subAssembly => (Step)new Link(subAssembly)),
            Gen.Select(Gen.Int[0, 9], Gen.Bool, (row, isOwn) => (Step)new CopyLinked(row, isOwn)),
            Gen.Int[0, 9].Select(row => (Step)new MoveToSubAssemblies(row)),
            Gen.Select(Gen.Int[0, 9], Gen.Int[0, 3], Gen.Int[1, 900], (row, ingredient, grams) => (Step)new EditLines(row, ingredient, grams)),
            Gen.Select(Gen.Int[0, 9], Gen.Bool, Gen.Int[1, 120], (row, yield, value) => (Step)new PieceSize(row, yield, value)),
            Gen.Int[0, 9].Select(row => (Step)new Remove(row)))),
        (3, Gen.Const<Step>(new Undo())),
        (2, Gen.Const<Step>(new Redo())),
        (1, Gen.Const<Step>(new Save())),
        (2, Gen.Bool.Select(costsChanged => (Step)new Load(costsChanged))),
        (1, Gen.Int[0, 3].Select(kind => (Step)new ChangedElsewhere(kind))));

    /// <summary>The sections as an undo step keeps them (RecipeSnapshot.Usages), as a class for the undo history.</summary>
    private sealed class Snapshot(IReadOnlyList<ProductUsageEdit> usages)
    {
        public IReadOnlyList<ProductUsageEdit> Usages { get; } = usages;
    }

    /// <summary>
    /// A saved product's recipe as its form holds it, a dough of its own and a topping linked from the Sub-Assemblies
    /// screen: the ingredients, roles and sub-assemblies it reads, and an undo history over the sections recording only the
    /// part's steps, measuring them as the form does, by what a Save would write.
    /// </summary>
    private sealed class World : IProductRecipeDraftHost, IProductComponentsDraftHost
    {
        private readonly List<SubAssembly> _kept = [];
        private readonly List<SubAssemblyLine> _keptLines = [];
        private readonly HashSet<int> _changedElsewhere = [];
        private readonly UndoModel<Snapshot> _model;
        private bool _restoring;
        private int _loads;

        public World()
        {
            Load(costsChanged: false);
            Draft = new ProductRecipeDraft(this, Ingredients, Roles, new ProductComponentsDraft(this, []));
            History = new UndoHistory<Snapshot>(
                () => new Snapshot(Capture()), (a, b) => ShowsSameAs(a.Usages, b.Usages), () => !_restoring, (a, b) => SavesSameAs(a.Usages, b.Usages));
            Draft.UsageEdits.CollectionChanged += (_, _) => History.RefreshDirty();

            // As the form shows a saved product (ProductForm.ShowDetails): its usages, their sub-assemblies' lines, its add-ins.
            Draft.Show(
                [Usage(81, 51, UsageAuthoredField.WeightPerPiece, 280m), Usage(82, 41, UsageAuthoredField.WeightPerPiece, 15m)],
                [.. _keptLines.Where(l => l.SubAssemblyId is 51 or 41)],
                [new UsageAddInLine { UsageId = 82, IngredientId = 24, Ingredient = Ingredients.First(i => i.Id == 24), Grams = 5m }]);
            History.MarkClean();
            Start = Capture();
            _model = new UndoModel<Snapshot>(
                new Snapshot(Start),
                (a, b) => ShowsSameAs(a.Usages, b.Usages),
                (a, b) => SavesSameAs(a.Usages, b.Usages),
                (a, b) => SavesSameAs(a.Usages, b.Usages));
        }

        /// <summary>Where the history started, as it keeps it: rewritten, as every step is, when a load brings what was saved elsewhere.</summary>
        public IReadOnlyList<ProductUsageEdit> Start { get; private set; }

        public ObservableCollection<RawMaterial> Ingredients { get; } = [];

        public ObservableCollection<SubAssemblyRole> Roles { get; } = [];

        public ProductRecipeDraft Draft { get; }

        public UndoHistory<Snapshot> History { get; }

        public bool IsEditingComposite => false;

        public int? DetailProductId => ProductId;

        public string EditingName => "Ube Loaf";

        public CostBasis CostBasis => CostBasis.Buffered;

        public IReadOnlyList<ProductUsageEdit> Capture() => [.. Draft.UsageEdits];

        /// <summary>Every section's lines and add-ins at the ingredients' costs as the last load brought them.</summary>
        public void ShouldShowTheLastLoadsCosts(string when)
        {
            var now = Ingredients.ToDictionary(i => i.Id);
            foreach (var section in Draft.UsageEdits)
            {
                foreach (var line in section.Lines.Concat(section.AddIns))
                {
                    (line.Ingredient.CostPerBaseUnit, line.Ingredient.BufferedCostPerBaseUnit)
                        .Should().Be((now[line.Ingredient.Id].CostPerBaseUnit, now[line.Ingredient.Id].BufferedCostPerBaseUnit),
                            $"{when}, {section.DisplayName}'s {line.Ingredient.Name} is at the last load's cost");
                }
            }
        }

        /// <summary>What holds of the sections whatever was done to them.</summary>
        public void ShouldHoldItsRules(string when)
        {
            var sections = Draft.UsageEdits;
            sections.Should().Equal(sections.Order(ProductUsageEdit.RecipeOrder), $"{when}, the sections are in recipe order");
            FluentActions.Invoking(() => SubAssemblyRules.EnsureSectionsDistinct([.. sections.Select(u => (u.RoleId, u.RoleName, u.Label))]))
                .Should().NotThrow($"{when}, the sections are told apart");
            foreach (var section in sections)
            {
                section.IsRoleRepeated.Should().Be(sections.Count(u => u.RoleId == section.RoleId) > 1, $"{when}, {section.DisplayName} knows whether others share its role");
            }
        }

        /// <summary>
        /// The sections, their history and their rules, as the model says, and the recipe's figures what its sections give as
        /// they stand, folded afresh as the draft folds them (ProductRecipeDraft.Priced): a mark missed leaves them as they were.
        /// </summary>
        public void ShouldBeRight(string when)
        {
            _model.ShouldMatch(History, new Snapshot(Capture()), when);
            ShouldHoldItsRules(when);

            List<ProductUsageEdit> sections = [.. Draft.UsageEdits];
            int? IndexOf(long? key) => key is { } k && sections.FindIndex(u => u.Key == k) is var i and >= 0 ? i : null;
            var fresh = SectionFolds.Apply([.. sections.Select(u => u.ToPriced())], [.. sections.Select(u => IndexOf(u.GoesInto))]);
            Draft.Priced().Select(p => p.Figures).Should().Equal(fresh.Select(p => p.Figures), $"{when}, the recipe's figures follow its sections");
        }

        /// <summary>Takes <paramref name="step"/>, the model with it. True when an undo or a redo moved, or a Save wrote.</summary>
        public bool Apply(Step step)
        {
            switch (step)
            {
                case ProductRecipeDraftTests.Load or ChangedElsewhere:
                    // Not the part's: written as a load writes, recording nothing.
                    if (step is ChangedElsewhere(var kind))
                    {
                        _changedElsewhere.Add(kind);
                    }

                    Load(step is ProductRecipeDraftTests.Load { CostsChanged: true });
                    _restoring = true;
                    if (Draft.RefreshRows(_kept, _keptLines))
                    {
                        // As the form does (ProductForm.TakeLoad): what came in from where it's kept goes into every step too, so
                        // an undo never puts back a linked sub-assembly's lines as they were before they were saved there.
                        History.RewriteAll(snapshot => new Snapshot(AsSavedNow(snapshot.Usages)));
                        _model.Rewritten(snapshot => new Snapshot(AsSavedNow(snapshot.Usages)));
                        Start = AsSavedNow(Start);
                    }

                    _restoring = false;
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
                    _model.Saved(new Snapshot(Capture()));
                    return true;
            }

            var was = new Snapshot(Capture());
            History.Begin();
            try
            {
                Edit(step);
            }
            catch (BusinessRuleException)
            {
                // Refused, as the part would refuse it: nothing changed.
            }

            History.End();
            _model.Edited(was, new Snapshot(Capture()));
            return false;
        }

        private bool Move(bool moved, Snapshot snapshot)
        {
            if (moved)
            {
                // As the form's undo does (ProductRecipe.RestoreSections).
                _restoring = true;
                Draft.Restore(snapshot.Usages);
                _restoring = false;
            }

            return moved;
        }

        private void Edit(Step step)
        {
            var sections = Draft.UsageEdits;
            ProductUsageEdit? At(int row) => sections.Count == 0 ? null : sections[row % sections.Count];
            switch (step)
            {
                // As the section form's Apply adds one: checked against the others first, then put where it sorts.
                case AddOwn(var role, var name, var ingredient, var grams):
                    var own = new ProductUsageEdit(0, Roles[role], [new SectionLine(Ingredients[ingredient], grams)], UsageAuthoredField.WeightPerPiece, 60m, linkedName: null, name: Names[name]);
                    Draft.EnsureSectionsDistinct(own, replacing: null);
                    Draft.InsertInRecipeOrder(own);
                    break;

                // A sub-assembly the recipe doesn't hold yet, linked as the form links one, from its lines as kept.
                case Link(var index) when _kept.Where(s => s.OwnerProductId is null).ElementAtOrDefault(index) is { } subAssembly && sections.All(u => u.SubAssemblyId != subAssembly.Id):
                    var linkRole = Roles.First(r => r.Id == subAssembly.RoleId);
                    var linked = new ProductUsageEdit(
                        subAssembly.Id, linkRole, [.. KeptLines(subAssembly.Id)], UsageAuthoredField.Yield, 1m,
                        linkedName: subAssembly.Name, name: Draft.SectionLabelOf(subAssembly.Name, linkRole));
                    Draft.EnsureSectionsDistinct(linked, replacing: null);
                    Draft.InsertInRecipeOrder(linked);
                    break;
                case CopyLinked(var row, var isOwn) when At(row) is { IsLinked: true } usage:
                    Draft.ReplaceInRecipeOrder(usage, ProductRecipeDraft.CopyOfLinked(usage, isOwn));
                    break;
                case MoveToSubAssemblies(var row) when At(row) is { IsOwned: true, IsReleasing: false } usage:
                    Draft.ReplaceInRecipeOrder(usage, usage.Released());
                    break;
                case EditLines(var row, var ingredient, var grams) when At(row) is { } usage:
                    IReadOnlyList<SectionLine> lines = [.. usage.Lines.Where(l => l.Ingredient.Id != Ingredients[ingredient].Id), new SectionLine(Ingredients[ingredient], grams)];
                    Draft.ReplaceInRecipeOrder(usage, usage.IsLinked ? usage.WithLinkedForm(lines, usage.AuthoredField, usage.AuthoredValue, usage.AddIns, usage.GoesInto) : usage.With(usage.Role, lines));
                    break;
                case PieceSize(var row, var yield, var value) when At(row) is { IsLinked: true } usage:
                    Draft.ReplaceInRecipeOrder(usage, usage.WithLinkedForm(usage.Lines, yield ? UsageAuthoredField.Yield : UsageAuthoredField.WeightPerPiece, value, usage.AddIns, usage.GoesInto));
                    break;
                case Remove(var row) when At(row) is { } usage:
                    sections.Remove(usage);
                    break;
            }
        }

        /// <summary>
        /// An undo step's sections as the sub-assemblies are kept now, as the form rewrites every step
        /// (ProductRecipeDraft.StepAsSavedNow), the section form closed.
        /// </summary>
        private IReadOnlyList<ProductUsageEdit> AsSavedNow(IReadOnlyList<ProductUsageEdit> usages) =>
            Draft.StepAsSavedNow(new RecipeSnapshot { Usages = usages, SelectedTab = null, HasAddSubAssemblyTab = false, Form = ClosedForm }, _kept, _keptLines).Usages;

        private static readonly SectionFormSnapshot ClosedForm = new()
        {
            IsOpen = false, IsAdding = false, Title = string.Empty, Mode = default, IsEditingLines = false, IsPickingSource = false,
            EditingUsageIndex = null, RoleId = null, Name = null, SourceKey = null, Lines = [], AddIns = [], FormulaBase = 0m,
            AuthoredField = UsageAuthoredField.WeightPerPiece, WeightPerPiece = 0m, Yield = 0m,
        };

        private IEnumerable<SectionLine> KeptLines(int subAssemblyId) =>
            _keptLines.Where(l => l.SubAssemblyId == subAssemblyId).Select(l => new SectionLine(Ingredients.First(i => i.Id == l.IngredientId), l.Grams));

        private ProductSubAssemblyUsage Usage(int id, int subAssemblyId, UsageAuthoredField field, decimal value) => new()
        {
            Id = id,
            ProductId = ProductId,
            SubAssemblyId = subAssemblyId,
            SubAssembly = _kept.First(s => s.Id == subAssemblyId),
            AuthoredField = field,
            AuthoredValue = value,
        };

        /// <summary>
        /// Fresh copies of the ingredients, the roles and the sub-assemblies with their lines as kept, in place of the ones
        /// held: the product's own dough, and on the Sub-Assemblies screen a butter icing and two fillings.
        /// </summary>
        private void Load(bool costsChanged)
        {
            var factor = costsChanged ? 1m + (++_loads % 3) * 0.25m : 1m;

            Ingredients.Clear();
            foreach (var (id, name, cost) in new[] { (21, "Bread Flour", 0.05m), (22, "Sugar", 0.07m), (23, "Butter", 0.42m), (24, "Cheese", 0.31m) })
            {
                Ingredients.Add(new RawMaterial
                {
                    Id = id, Name = name, Type = RawMaterialType.Ingredient, Source = RawMaterialSource.Purchased,
                    CostPerBaseUnit = cost * factor, BufferedCostPerBaseUnit = cost * factor * 1.1m, CostBufferPercent = 10m,
                });
            }

            Roles.Clear();
            Roles.Add(new SubAssemblyRole { Id = 31, Name = "Dough", SortOrder = 1 });
            Roles.Add(new SubAssemblyRole { Id = 32, Name = "Filling", SortOrder = 2 });
            Roles.Add(new SubAssemblyRole { Id = 33, Name = "Topping", SortOrder = 3 });

            // What was saved elsewhere since stays saved: each later load reads it too.
            var ubeMoved = _changedElsewhere.Contains(2);
            _kept.Clear();
            int? doughOwner = _changedElsewhere.Contains(4) ? null : ProductId;
            int? icingOwner = _changedElsewhere.Contains(5) ? ProductId : null;
            _kept.Add(new SubAssembly { Id = 51, Name = "Ube Loaf Dough", RoleId = 31, Role = Roles[0], OwnerProductId = doughOwner });
            _kept.Add(new SubAssembly { Id = 41, Name = "Butter Icing", RoleId = 33, Role = Roles[2], OwnerProductId = icingOwner });
            _kept.Add(new SubAssembly
            {
                Id = 42, Name = _changedElsewhere.Contains(1) ? "Ube Halaya Filling" : "Ube Filling", RoleId = ubeMoved ? 33 : 32, Role = Roles[ubeMoved ? 2 : 1],
            });
            _kept.Add(_changedElsewhere.Contains(3)
                ? new SubAssembly { Id = 43, Name = "Cheese Filling", RoleId = 32, Role = Roles[1], OwnerProductId = ProductId, SectionName = "Cheese" }
                : new SubAssembly { Id = 43, Name = "Cheese Filling", RoleId = 32, Role = Roles[1] });

            _keptLines.Clear();
            var icing = _changedElsewhere.Contains(0) ? 350m : 300m;
            foreach (var (subAssemblyId, ingredientId, grams) in new[] { (51, 21, 1000m), (51, 22, 120m), (41, 23, icing), (41, 22, 200m), (42, 22, 150m), (43, 24, 250m) })
            {
                _keptLines.Add(new SubAssemblyLine { SubAssemblyId = subAssemblyId, IngredientId = ingredientId, Ingredient = Ingredients.First(i => i.Id == ingredientId), Grams = grams });
            }
        }
    }
}
