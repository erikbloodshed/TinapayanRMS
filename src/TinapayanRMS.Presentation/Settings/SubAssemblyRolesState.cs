using DynamicData.Binding;
using TinapayanRMS.Domain.Entities;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.UseCases.SubAssemblies;

namespace TinapayanRMS.Presentation.Settings;

/// <summary>
/// Settings' Roles tab: the sections a product's recipe is split into (Dough, Filling, …),
/// in their order, which of them are product-owned (a section made in a product stays that product's own), and which can
/// go into another section (a sponge on the dough's lines). A new role goes last; Move Up and Move Down change the picked
/// one's place.
/// </summary>
public sealed class SubAssemblyRolesState : LookupListState<SubAssemblyRole>
{
    private readonly ISubAssemblyRoleEditor _roleEditor;

    private bool _editingIsProductOwned;
    /// <summary>
    /// A section in this role made in a product (new, copied, or Make a Copy) stays that product's own: edited in its
    /// recipe, not on the Sub-Assemblies screen, and copied rather than linked by other products.
    /// </summary>
    public bool EditingIsProductOwned
    {
        get => _editingIsProductOwned;
        set => RaiseAndSetIfChanged(ref _editingIsProductOwned, value);
    }

    private bool _editingCanGoIntoOtherSections;
    /// <summary>
    /// A section in this role can be a line of another of its product's sections, as a sponge goes into the dough: the
    /// section form's picker offers it there. Unticking it is refused while a product has one on another's lines.
    /// </summary>
    public bool EditingCanGoIntoOtherSections
    {
        get => _editingCanGoIntoOtherSections;
        set => RaiseAndSetIfChanged(ref _editingCanGoIntoOtherSections, value);
    }

    private bool _canEditProductOwned = true;
    /// <summary>
    /// False for the Packaging role, which holds packaging sets from the Sub-Assemblies screen: neither Product-owned nor
    /// Into other sections is for it.
    /// </summary>
    public bool CanEditProductOwned
    {
        get => _canEditProductOwned;
        private set => RaiseAndSetIfChanged(ref _canEditProductOwned, value);
    }

    /// <summary>Swaps the picked role with the one above it; off for the first.</summary>
    public ScreenCommand MoveUpCommand { get; }

    /// <summary>Swaps the picked role with the one below it; off for the last.</summary>
    public ScreenCommand MoveDownCommand { get; }

    public SubAssemblyRolesState(ISubAssemblyRoleEditor roleEditor, ILogger logger)
        : base("Role", r => r.Id, SortExpressionComparer<SubAssemblyRole>.Ascending(r => r.SortOrder).ThenByAscending(r => r.Name), roleEditor, logger)
    {
        _roleEditor = roleEditor;

        // Asked of the pick's place in the list as it is, since a move or a new role changes what's first and last.
        MoveUpCommand = new ScreenCommand(
            () => MoveAsync(up: true),
            () => SelectedItem is { } role && Items.IndexOf(role) > 0);
        MoveDownCommand = new ScreenCommand(
            () => MoveAsync(up: false),
            () => SelectedItem is { } role && Items.IndexOf(role) is var index && index >= 0 && index < Items.Count - 1);
    }

    protected override string DeleteQuestion(SubAssemblyRole item) => $"Delete the '{item.Name}' role? This can't be undone.";

    protected override void LoadIntoForm(SubAssemblyRole item)
    {
        EditingName = item.Name;
        EditingIsProductOwned = item.IsProductOwned;
        EditingCanGoIntoOtherSections = item.CanGoIntoOtherSections;
        CanEditProductOwned = !item.IsPackaging;
    }

    /// <summary>The form's role, keeping the stored one's place and whether it's the Packaging role.</summary>
    protected override SubAssemblyRole FormItem()
    {
        var stored = Items.FirstOrDefault(r => r.Id == EditingId);
        return new SubAssemblyRole
        {
            Id = EditingId,
            Name = EditingName.Trim(),
            SortOrder = stored?.SortOrder ?? 0,
            IsPackaging = stored?.IsPackaging ?? false,
            IsProductOwned = EditingIsProductOwned,
            CanGoIntoOtherSections = EditingCanGoIntoOtherSections,
        };
    }

    /// <summary>An empty form for a new role, which goes after the others.</summary>
    protected override void ClearFields()
    {
        EditingIsProductOwned = false;
        EditingCanGoIntoOtherSections = false;
        CanEditProductOwned = true;
    }

    /// <summary>
    /// Turning product-owned on or off moves sub-assemblies between products and the Sub-Assemblies screen, which turning
    /// it round again doesn't quite undo (merged add-ins stay merged), so it's confirmed first.
    /// </summary>
    protected override async Task SaveAsync(SubAssemblyRole item)
    {
        if (Items.FirstOrDefault(r => r.Id == item.Id) is { } stored && stored.IsProductOwned != item.IsProductOwned)
        {
            var count = await _roleEditor.CountOwnershipChangesAsync(item.Id, item.IsProductOwned, CancellationToken.None);
            await RunWithConfirmationAsync(OwnershipChangeMessage(item, count), async () =>
            {
                await _roleEditor.UpdateAsync(item, CancellationToken.None);
                await FinishSaveAsync();
            });
            return;
        }

        await base.SaveAsync(item);
    }

    /// <summary>
    /// Moves the picked role, then reloads. The form is left as it is, unsaved edits and all: a move only changes the
    /// role's place, which the form doesn't show.
    /// </summary>
    private Task MoveAsync(bool up) => RunSafelyAsync(async () =>
    {
        await _roleEditor.MoveAsync(SelectedItem!.Id, up, CancellationToken.None);
        await LoadCommand.ExecuteAsync();
    });

    /// <summary>What Save asks before turning a role product-owned, or back, saying how many sub-assemblies it moves.</summary>
    public static string OwnershipChangeMessage(SubAssemblyRole role, int count)
    {
        var them = count == 1 ? "1 sub-assembly" : $"{count} sub-assemblies";
        if (role.IsProductOwned)
        {
            return count == 0
                ? $"Make {role.Name} product-owned? New {role.Name} sections made in a product will stay that product's own. No sub-assembly on the Sub-Assemblies screen is used by just one product, so none moves."
                : $"Make {role.Name} product-owned? {them} on the Sub-Assemblies screen used by just one product will become that product's own: edited in its recipe, gone from the Sub-Assemblies screen, and copied rather than linked by other products. Any add-ins on them are merged into their lines. Ones used by several products stay where they are.";
        }

        return count == 0
            ? $"Stop making {role.Name} product-owned? New {role.Name} sections will be kept on the Sub-Assemblies screen and linked. No product owns one now, so none moves."
            : $"Stop making {role.Name} product-owned? The {them} products own in it will move to the Sub-Assemblies screen, still used by the same products, which will link them. Add-ins merged into their lines stay merged.";
    }
}
