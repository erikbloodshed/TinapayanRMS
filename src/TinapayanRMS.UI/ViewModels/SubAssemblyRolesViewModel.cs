using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// The Settings dialog's Roles tab: the sections a product's recipe is split into (Dough, Filling, …),
/// in their order. Picking a role loads it for editing; New, or finishing a Save or Delete, clears the
/// form, so the next Save always adds a role rather than renaming the last one touched.
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

    private decimal _editingSortOrder;
    public decimal EditingSortOrder
    {
        get => _editingSortOrder;
        set => this.RaiseAndSetIfChanged(ref _editingSortOrder, value);
    }

    private readonly ObservableAsPropertyHelper<string> _formTitle;
    /// <summary>"New Role", or "Edit Role" while one is picked, so it's clear what Save will do.</summary>
    public string FormTitle => _formTitle.Value;

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

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
                EditingSortOrder = role.SortOrder;
            });

        // Runs each time Settings opens and after this tab's own saves. It merges rather than
        // replaces, so the selection and any unsaved edits survive.
        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var roles = await _roleRepository.GetAllAsync(CancellationToken.None);
            var selectedId = SelectedRole?.Id;

            ApplyRefresh(() =>
            {
                _roleCache.MergeFrom(roles);
                SelectedRole = Roles.FirstOrDefault(r => r.Id == selectedId);

                // A form nobody has touched yet offers the next place in the order, as New does.
                if (_editingId == 0 && string.IsNullOrWhiteSpace(EditingName) && EditingSortOrder == 0)
                {
                    EditingSortOrder = NextSortOrder();
                }
            });
        });

        NewCommand = ReactiveCommand.Create(ClearForm);

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var role = new SubAssemblyRole { Id = _editingId, Name = EditingName.Trim(), SortOrder = (int)EditingSortOrder };

            var validation = _roleValidator.Validate(role);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            if (_editingId == 0)
            {
                await _roleRepository.AddAsync(role, CancellationToken.None);
            }
            else
            {
                await _roleRepository.UpdateAsync(role, CancellationToken.None);
            }

            ClearForm();
            await LoadCommand.Execute();
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete the '{SelectedRole!.Name}' role? This can't be undone.", async () =>
            {
                await _roleRepository.DeleteAsync(SelectedRole!.Id, CancellationToken.None);
                ClearForm();
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedRole).Select(role => role is not null));
    }

    /// <summary>An empty form for a new role, which goes after the others unless its order is changed.</summary>
    private void ClearForm()
    {
        SelectedRole = null;
        _editingId = 0;
        EditingName = string.Empty;
        EditingSortOrder = NextSortOrder();
    }

    private int NextSortOrder() => Roles.Count == 0 ? 1 : Roles.Max(r => r.SortOrder) + 1;

    public void Dispose() => _roleCache.Dispose();
}
