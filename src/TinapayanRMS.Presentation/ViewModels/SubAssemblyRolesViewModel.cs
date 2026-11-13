using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The Settings dialog's Roles tab: the sections a product's recipe is split into (Dough, Filling, …),
/// in their order, and which of them are product-owned (a section made in a product stays that product's own).
/// Picking a role loads it for editing; New, or finishing a Save or Delete, clears the form, so the next Save
/// always adds a role rather than renaming the last one touched. A new role goes last; Move Up and Move Down change
/// the picked one's place.
/// </summary>
public sealed class SubAssemblyRolesViewModel : ViewModelBase, IDisposable
{
    private readonly ISubAssemblyRoleRepository _roleRepository;
    private readonly IValidator<SubAssemblyRole> _roleValidator;
    private readonly SourceCache<SubAssemblyRole, int> _roleCache = new(r => r.Id);
    private readonly ReadOnlyObservableCollection<SubAssemblyRole> _roles;

    /// <summary>Every role, in recipe order.</summary>
    public ReadOnlyObservableCollection<SubAssemblyRole> Roles => _roles;

    private SubAssemblyRole? _selectedRole;
    public SubAssemblyRole? SelectedRole
    {
        get => _selectedRole;
        set => this.RaiseAndSetIfChanged(ref _selectedRole, value);
    }

    private int _editingId;

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    private bool _editingIsProductOwned;
    /// <summary>
    /// A section in this role made in a product (new, copied, or Make a Copy) stays that product's own: edited in its
    /// recipe, not on the Sub-Assemblies screen, and copied rather than linked by other products.
    /// </summary>
    public bool EditingIsProductOwned
    {
        get => _editingIsProductOwned;
        set => this.RaiseAndSetIfChanged(ref _editingIsProductOwned, value);
    }

    private bool _canEditProductOwned = true;
    /// <summary>False for the Packaging role, which holds packaging sets from the Sub-Assemblies screen.</summary>
    public bool CanEditProductOwned
    {
        get => _canEditProductOwned;
        private set => this.RaiseAndSetIfChanged(ref _canEditProductOwned, value);
    }

    private readonly ObservableAsPropertyHelper<string> _formTitle;
    /// <summary>"New Role", or "Edit Role" while one is picked, so it's clear what Save will do.</summary>
    public string FormTitle => _formTitle.Value;

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

    /// <summary>Swaps the picked role with the one above it; off for the first.</summary>
    public ReactiveCommand<RxVoid, RxVoid> MoveUpCommand { get; }

    /// <summary>Swaps the picked role with the one below it; off for the last.</summary>
    public ReactiveCommand<RxVoid, RxVoid> MoveDownCommand { get; }

    public SubAssemblyRolesViewModel(ISubAssemblyRoleRepository roleRepository, IValidator<SubAssemblyRole> roleValidator, ILogger logger)
        : base(logger)
    {
        _roleRepository = roleRepository;
        _roleValidator = roleValidator;

        _roleCache.Connect()
            .SortAndBind(out _roles, SortExpressionComparer<SubAssemblyRole>.Ascending(r => r.SortOrder).ThenByAscending(r => r.Name))
            .Subscribe();

        _formTitle = this.WhenAnyValue(x => x.SelectedRole)
            .Select(role => role is null ? "New Role" : "Edit Role")
            .ToProperty(this, x => x.FormTitle, "New Role");

        // Skips while LoadCommand merges, which only re-points the selection at its refreshed copy.
        this.WhenAnyValue(x => x.SelectedRole)
            .Where(role => role is not null && !IsRefreshing)
            .Subscribe(role =>
            {
                _editingId = role!.Id;
                EditingName = role.Name;
                EditingIsProductOwned = role.IsProductOwned;
                CanEditProductOwned = !role.IsPackaging;
            });

        // Runs each time Settings opens and after this tab's own saves. It merges rather than
        // replaces, so the selection and any unsaved edits survive.
        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            var roles = await _roleRepository.GetAllAsync(CancellationToken.None);
            var selectedId = SelectedRole?.Id;

            ApplyRefresh(() =>
            {
                _roleCache.MergeFrom(roles);
                SelectedRole = Roles.FirstOrDefault(r => r.Id == selectedId);
            });
        }));

        NewCommand = ReactiveCommand.Create(ClearForm);

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var stored = Roles.FirstOrDefault(r => r.Id == _editingId);
            var role = new SubAssemblyRole
            {
                Id = _editingId,
                Name = EditingName.Trim(),
                SortOrder = stored?.SortOrder ?? 0,
                IsPackaging = stored?.IsPackaging ?? false,
                IsProductOwned = EditingIsProductOwned,
            };

            var validation = _roleValidator.Validate(role);
            if (!validation.IsValid)
            {
                ErrorMessage = validation.ToString(" ");
                return;
            }

            if (_editingId == 0)
            {
                await _roleRepository.AddAsync(role, CancellationToken.None);
                await FinishSaveAsync();
                return;
            }

            // Turning it on or off moves sub-assemblies between products and the Sub-Assemblies screen, which turning it
            // round again doesn't quite undo (merged add-ins stay merged), so it's confirmed first.
            if (stored is not null && stored.IsProductOwned != role.IsProductOwned)
            {
                var count = await _roleRepository.CountOwnershipChangesAsync(role.Id, role.IsProductOwned, CancellationToken.None);
                await RunWithConfirmationAsync(OwnershipChangeMessage(role, count), async () =>
                {
                    await _roleRepository.UpdateAsync(role, CancellationToken.None);
                    await FinishSaveAsync();
                });
                return;
            }

            await _roleRepository.UpdateAsync(role, CancellationToken.None);
            await FinishSaveAsync();
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete the '{SelectedRole!.Name}' role? This can't be undone.", async () =>
            {
                await _roleRepository.DeleteAsync(SelectedRole!.Id, CancellationToken.None);
                ClearForm();
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedRole).Select(role => role is not null));

        // Re-checked when the pick changes and when the list does, since a move or a new role changes what's first and last.
        var placeChanged = this.WhenAnyValue(x => x.SelectedRole).Select(_ => RxVoid.Default)
            .Merge(ObservableCollectionEx.ObserveCollectionChanges((INotifyCollectionChanged)_roles).Select(_ => RxVoid.Default));
        MoveUpCommand = ReactiveCommand.CreateFromTask(
            () => MoveAsync(up: true),
            placeChanged.Select(_ => SelectedRole is { } role && Roles.IndexOf(role) > 0));
        MoveDownCommand = ReactiveCommand.CreateFromTask(
            () => MoveAsync(up: false),
            placeChanged.Select(_ => SelectedRole is { } role && Roles.IndexOf(role) is var index && index >= 0 && index < Roles.Count - 1));
    }

    /// <summary>
    /// Moves the picked role, then reloads. The form is left as it is, unsaved edits and all: a move only changes the
    /// role's place, which the form doesn't show.
    /// </summary>
    private Task MoveAsync(bool up) => RunSafelyAsync(async () =>
    {
        await _roleRepository.MoveAsync(SelectedRole!.Id, up, CancellationToken.None);
        await LoadCommand.Execute();
    });

    private async Task FinishSaveAsync()
    {
        ClearForm();
        await LoadCommand.Execute();
    }

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

    /// <summary>An empty form for a new role, which goes after the others.</summary>
    private void ClearForm()
    {
        SelectedRole = null;
        _editingId = 0;
        EditingName = string.Empty;
        EditingIsProductOwned = false;
        CanEditProductOwned = true;
    }

    public void Dispose() => _roleCache.Dispose();
}
