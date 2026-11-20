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
/// A product's recipe draft on its own, with no database and no screen (docs/adr/0006): random sequences of what the recipe
/// part does to a saved product's sections (a section of its own added, a sub-assembly linked, a linked one copied or
/// unlinked, one of its own moved to Sub-Assemblies, its lines or a linked one's piece size changed, a section taken out),
/// each an undo step, with loads between them that bring fresh copies of the ingredients and the sub-assemblies, their
/// costs moved or not. Whatever the sequence, the sections stay in recipe order and told apart, each knows whether others
/// share its role, the dirty check says whether a Save would write the recipe differently from where it was loaded, a load
/// changes nothing Save writes, a recipe that saves as loaded is as saved, and undoing every step gives back where it
/// started, redoing them where it ended.
/// </summary>
public class ProductRecipeDraftTests
{
    [Test]
    public void Any_sequence_undoes_back_to_where_it_started_and_keeps_the_sections_in_order() =>
        Steps.Array[0, 40].Sample(steps =>
        {
            var world = new World();
            var start = world.Capture();

            foreach (var step in steps)
            {
                world.Apply(step);
                world.ShouldHoldItsRules($"after {step}");
                world.History.IsDirty.Should().Be(!SavesSameAs(start, world.Capture()), $"after {step}, the dirty check says whether Save would write it");
            }

            // Exactly, with no load between the steps; with one, as Save would write it: a step keeps the ingredients it was
            // taken with, so an undo across a load that moved their costs shows those costs until the next load (the build
            // plan's open items).
            Func<IReadOnlyList<ProductUsageEdit>, IReadOnlyList<ProductUsageEdit>, bool> same = steps.Any(s => s is Load) ? SavesSameAs : ShowsSameAs;
            var end = world.Capture();
            while (world.TryUndo())
            {
            }

            same(world.Capture(), start).Should().BeTrue("undoing every step gives back where it started");
            world.History.IsDirty.Should().BeFalse();
            world.Draft.IsAsSaved(world.Draft.CurrentDraft()).Should().BeTrue("undone back to how it was loaded, Save needn't write it");
            world.ShouldHoldItsRules("undone");

            while (world.TryRedo())
            {
            }

            same(world.Capture(), end).Should().BeTrue("redoing every step gives back where it ended");
            world.ShouldHoldItsRules("redone");
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
    public void A_recipe_that_saves_as_loaded_is_as_saved() =>
        Steps.Array[0, 40].Sample(steps =>
        {
            var world = new World();
            var loaded = Describe(world.Draft.CurrentDraft());
            foreach (var step in steps)
            {
                world.Apply(step);
                var now = world.Draft.CurrentDraft();
                world.Draft.IsAsSaved(now).Should().Be(Describe(now) == loaded, $"after {step}");
            }
        }, iter: 300);

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

    private static readonly string?[] Names = [null, "Ube", "Cheese"];

    private static readonly Gen<Step> Steps = Gen.OneOf<Step>(
        Gen.Select(Gen.Int[0, 2], Gen.Int[0, 2], Gen.Int[0, 3], Gen.Int[1, 900], (role, name, ingredient, grams) => (Step)new AddOwn(role, name, ingredient, grams)),
        Gen.Int[0, 2].Select(subAssembly => (Step)new Link(subAssembly)),
        Gen.Select(Gen.Int[0, 9], Gen.Bool, (row, isOwn) => (Step)new CopyLinked(row, isOwn)),
        Gen.Int[0, 9].Select(row => (Step)new MoveToSubAssemblies(row)),
        Gen.Select(Gen.Int[0, 9], Gen.Int[0, 3], Gen.Int[1, 900], (row, ingredient, grams) => (Step)new EditLines(row, ingredient, grams)),
        Gen.Select(Gen.Int[0, 9], Gen.Bool, Gen.Int[1, 120], (row, yield, value) => (Step)new PieceSize(row, yield, value)),
        Gen.Int[0, 9].Select(row => (Step)new Remove(row)),
        Gen.Bool.Select(costsChanged => (Step)new Load(costsChanged)));

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
        }

        public ObservableCollection<RawMaterial> Ingredients { get; } = [];

        public ObservableCollection<SubAssemblyRole> Roles { get; } = [];

        public ProductRecipeDraft Draft { get; }

        public UndoHistory<Snapshot> History { get; }

        public bool IsEditingComposite => false;

        public int? DetailProductId => ProductId;

        public string EditingName => "Ube Loaf";

        public CostBasis CostBasis => CostBasis.Buffered;

        public IReadOnlyList<ProductUsageEdit> Capture() => [.. Draft.UsageEdits];

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

        public void Apply(Step step)
        {
            if (step is Load load)
            {
                // Not the part's: written as a load writes, recording nothing.
                Load(load.CostsChanged);
                _restoring = true;
                Draft.RefreshRows(_kept, _keptLines);
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
                // Refused, as the part would refuse it: nothing changed.
            }

            History.End();
        }

        public bool TryUndo() => Move(History.TryUndo(out var snapshot), snapshot);

        public bool TryRedo() => Move(History.TryRedo(out var snapshot), snapshot);

        private bool Move(bool moved, Snapshot snapshot)
        {
            if (moved)
            {
                _restoring = true;
                Draft.ReplaceUsageItems(snapshot.Usages);
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
                case Link(var index) when _kept.Where(s => s.OwnerProductId is null).ElementAt(index) is var subAssembly && sections.All(u => u.SubAssemblyId != subAssembly.Id):
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
                    Draft.ReplaceInRecipeOrder(usage, usage.IsLinked ? usage.WithLinkedForm(lines, usage.AuthoredField, usage.AuthoredValue, usage.AddIns) : usage.With(usage.Role, lines));
                    break;
                case PieceSize(var row, var yield, var value) when At(row) is { IsLinked: true } usage:
                    Draft.ReplaceInRecipeOrder(usage, usage.WithLinkedForm(usage.Lines, yield ? UsageAuthoredField.Yield : UsageAuthoredField.WeightPerPiece, value, usage.AddIns));
                    break;
                case Remove(var row) when At(row) is { } usage:
                    sections.Remove(usage);
                    break;
            }
        }

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

            _kept.Clear();
            _kept.Add(new SubAssembly { Id = 51, Name = "Ube Loaf Dough", RoleId = 31, Role = Roles[0], OwnerProductId = ProductId });
            _kept.Add(new SubAssembly { Id = 41, Name = "Butter Icing", RoleId = 33, Role = Roles[2] });
            _kept.Add(new SubAssembly { Id = 42, Name = "Ube Filling", RoleId = 32, Role = Roles[1] });
            _kept.Add(new SubAssembly { Id = 43, Name = "Cheese Filling", RoleId = 32, Role = Roles[1] });

            _keptLines.Clear();
            foreach (var (subAssemblyId, ingredientId, grams) in new[] { (51, 21, 1000m), (51, 22, 120m), (41, 23, 300m), (41, 22, 200m), (42, 22, 150m), (43, 24, 250m) })
            {
                _keptLines.Add(new SubAssemblyLine { SubAssemblyId = subAssemblyId, IngredientId = ingredientId, Ingredient = Ingredients.First(i => i.Id == ingredientId), Grams = grams });
            }
        }
    }
}
