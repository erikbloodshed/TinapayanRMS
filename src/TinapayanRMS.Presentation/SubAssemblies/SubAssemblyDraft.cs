using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.UseCases.SubAssemblies;

namespace TinapayanRMS.Presentation.SubAssemblies;

/// <summary>
/// The sub-assembly a Sub-Assemblies form edits (docs/adr/0002-layers-use-cases-and-drafts.md): its name, its role, and its
/// lines, a recipe's (<see cref="Batch"/>) or a packaging set's (<see cref="PackagingSet"/>) as the role says, with the rules
/// that follow them as they're typed. It's filled from what was saved (<see cref="Show"/>), captured and put back for an
/// undo step (<see cref="Capture"/>, <see cref="Restore"/>), pointed at a load's fresh copies of what it refers to
/// (<see cref="Rebase"/>), and read as a Save writes it (<see cref="ToSaveRequest"/>). It reads no database and knows no
/// screen: the form around it keeps which sub-assembly it is, the undo history, Save and the tabs.
/// </summary>
public sealed class SubAssemblyDraft : ObservableState
{
    private readonly IReadOnlyList<SubAssemblyRole> _roles;

    /// <summary>The draft's derived updates, run as their inputs change, or once at the end of a restore.</summary>
    private readonly DerivedUpdates _updates = new();

    /// <param name="recorder">The undo history Scale records its step in.</param>
    /// <param name="showError">Where Scale says why it can't scale.</param>
    /// <param name="roles">The roles as the screen last loaded them, which <see cref="Role"/> is one of.</param>
    /// <param name="ingredients">The ingredients as the screen last loaded them, which a recipe's line prices itself from.</param>
    /// <param name="packagingMaterials">The packaging materials as the screen last loaded them, for a packaging set's lines.</param>
    public SubAssemblyDraft(
        IUndoRecorder recorder,
        Action<string?> showError,
        IReadOnlyList<SubAssemblyRole> roles,
        IReadOnlyList<RawMaterial> ingredients,
        IReadOnlyList<RawMaterial> packagingMaterials)
    {
        _roles = roles;
        PackagingSet = new SubAssemblyPackagingSet(packagingMaterials, _updates);
        Batch = new SubAssemblyBatch(recorder, showError, ingredients, _updates, () => !IsPackaging);

        // Every change to anything the draft holds, its lines' cells included, moves its version: what's worked out from
        // the whole draft (whether there's anything to save) hears of it here, in one place.
        Batch.PropertyChanged += (_, _) => Touch();
        PackagingSet.PropertyChanged += (_, _) => Touch();
        RowChanges.OnAnyChange(Batch.Lines, Touch);
        RowChanges.OnAnyChange(PackagingSet.Lines, Touch);
    }

    /// <summary>A recipe's lines in grams, their formula base and totals, and Scale.</summary>
    public SubAssemblyBatch Batch { get; }

    /// <summary>A packaging set's lines and what the set costs.</summary>
    public SubAssemblyPackagingSet PackagingSet { get; }

    /// <summary>A count that any change to the draft moves, its lines and their cells included.</summary>
    public long Version { get; private set; }

    /// <summary>Raised on every change to the draft, as <see cref="Version"/> moves.</summary>
    public event Action? Changed;

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set => RaiseAndSetIfChanged(ref _name, value);
    }

    private SubAssemblyRole? _role;
    public SubAssemblyRole? Role
    {
        get => _role;
        set
        {
            if (RaiseAndSetIfChanged(ref _role, value))
            {
                RaisePropertyChanged(nameof(IsPackaging));
            }
        }
    }

    /// <summary>The first of the roles, which a new sub-assembly starts in; none before the roles are loaded.</summary>
    private SubAssemblyRole? FirstRole => _roles.Count > 0 ? _roles[0] : null;

    /// <summary>A packaging set: its role is the Packaging role, so it takes packaging materials per set, not ingredients in grams.</summary>
    public bool IsPackaging => Role?.IsPackaging == true;

    protected override void OnPropertyChanged(string? propertyName) => Touch();

    /// <summary>Nothing in it: a new sub-assembly in <paramref name="role"/>, or the first role.</summary>
    public void Clear(SubAssemblyRole? role)
    {
        Name = string.Empty;
        Role = role ?? FirstRole;
        Batch.Clear();
        PackagingSet.Clear();
    }

    /// <summary>A saved sub-assembly as read: its name, its role by id, and its lines.</summary>
    public void Show(string name, int roleId, SubAssemblyContent content)
    {
        Name = name;
        Role = _roles.FirstOrDefault(r => r.Id == roleId);
        Batch.Show(content.Lines);
        PackagingSet.Show(content.PackagingLines);
    }

    /// <summary>
    /// A load's fresh copies taken in place of what the draft refers to, nothing typed changed: the role by id (a new draft
    /// with none, <paramref name="newWithoutRole"/>, takes the first role), and each line's ingredient or material, which
    /// prices it as the load read it.
    /// </summary>
    public void Rebase(int? roleId, bool newWithoutRole)
    {
        Role = _roles.FirstOrDefault(r => r.Id == roleId) ?? (newWithoutRole ? FirstRole : null);
        Batch.ResolveLines();
        PackagingSet.ResolveRows();
    }

    /// <summary>The draft as an undo step keeps it, for the sub-assembly <paramref name="editingId"/>: what it refers to, by id.</summary>
    public SubAssemblyFormSnapshot Capture(int editingId) => new()
    {
        EditingId = editingId,
        Name = Name,
        RoleId = Role?.Id,
        Lines = Batch.Capture(),
        PackagingLines = PackagingSet.Capture(),
        FormulaBase = Batch.FormulaBase,
    };

    /// <summary>
    /// Puts the draft back as <paramref name="snapshot"/> had it. The lines are rebuilt rather than written into
    /// (IngredientLineRow's setters drop a typed percentage), then pointed at their ingredients and the formula base applied
    /// once, at the end, from the whole draft put back.
    /// </summary>
    public void Restore(SubAssemblyFormSnapshot snapshot)
    {
        _updates.Batch(() =>
        {
            Batch.SelectedLine = null;
            PackagingSet.SelectedLine = null;
            Name = snapshot.Name;
            Role = snapshot.RoleId is { } roleId ? _roles.FirstOrDefault(r => r.Id == roleId) : null;
            Batch.Restore(snapshot.Lines, snapshot.FormulaBase);
            PackagingSet.Restore(snapshot.PackagingLines);
        });

        // Once more, in this order, from what was put back: an undo that leaves a table as it was asks for none of these.
        Batch.ResolveLines();
        PackagingSet.ResolveRows();
        Batch.RecomputeFormulaBase();
    }

    /// <summary>
    /// What a Save of the sub-assembly <paramref name="id"/> writes: the name trimmed, the role, and the lines of the kind
    /// the role takes, those filled in. A line half filled in, or an ingredient on two lines, is refused
    /// (BusinessRuleException), naming it.
    /// </summary>
    public SubAssemblySaveRequest ToSaveRequest(int id, DateTime? savedVersion)
    {
        var name = Name.Trim();
        return IsPackaging
            ? new SubAssemblySaveRequest(id, name, Role!.Id, true, savedVersion, [], [.. PackagingSet.CurrentLines().Select(l => new PackagingLineDraft(l.PackagingMaterialId, l.UnitId, l.QtyPerUnit))])
            : new SubAssemblySaveRequest(
                id, name, Role?.Id ?? 0, false, savedVersion,
                [.. IngredientLineRow.CompleteLines(Batch.Lines, name).Select(l => new IngredientLineDraft(l.Ingredient, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
                []);
    }

    private void Touch()
    {
        Version++;
        Changed?.Invoke();
    }
}
