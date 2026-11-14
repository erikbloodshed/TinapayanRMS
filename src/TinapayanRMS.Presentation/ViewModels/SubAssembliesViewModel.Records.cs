using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using DynamicData;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing.Versions;

namespace TinapayanRMS.Presentation.ViewModels;

// Opening, picking, loading, making, saving and deleting sub-assemblies, and the form's dependents.
public sealed partial class SubAssembliesViewModel
{
    /// <summary>Picking a sub-assembly loads it into the form.</summary>
    private void WirePicks()
    {
        // Picking a sub-assembly loads it into the form. Only a pick does (_picks): a refresh re-points the selection at its
        // refreshed copy quietly (RepointSelection), so it leaves the form and its unsaved edits alone.
        LoadLatest(_picks, (row, _) => LoadIntoFormAsync(row));
    }

    /// <summary>The roles, materials and sub-assemblies read again and merged, the form and its selection kept.</summary>
    [MemberNotNull(nameof(LoadCommand))]
    private void WireLoadCommand(ISubAssemblyRoleRepository roleRepository, IRawMaterialRepository rawMaterialRepository)
    {
        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection and unsaved lines while picking up
        // what changed elsewhere: a repriced ingredient, a renamed role, a product that links one.
        LoadCommand = ReactiveCommand.CreateFromTask(() => LoadSafelyAsync(async () =>
        {
            // All at once, on the thread pool, as Products reads its lists: one after another on the UI thread, each read came
            // back to it before the next began, and waited its turn behind whatever the screen was drawing.
            var rolesRead = Task.Run(() => roleRepository.GetAllAsync(CancellationToken.None));
            var rawMaterialsRead = Task.Run(() => rawMaterialRepository.GetAllAsync(CancellationToken.None));
            var subAssembliesRead = Task.Run(() => _subAssemblyRepository.GetStandaloneAsync(CancellationToken.None));
            var linkedByRead = Task.Run(() => _subAssemblyRepository.GetStandaloneLinkedByAsync(CancellationToken.None));
            var madeFromRead = Task.Run(() => _subAssemblyRepository.GetMadeIngredientsBySubAssemblyAsync(CancellationToken.None));
            await Task.WhenAll(rolesRead, rawMaterialsRead, subAssembliesRead, linkedByRead, madeFromRead);

            var roles = await rolesRead;
            var rawMaterials = await rawMaterialsRead;
            var subAssemblies = await subAssembliesRead;
            var linkedBy = await linkedByRead;
            var madeFrom = await madeFromRead;

            // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
            var selectedId = SelectedRow?.Id;
            var roleId = EditingRole?.Id;

            // A brand-new form has no role until the roles arrive, and the merge below gives it the first one.
            // That isn't the user's change, so what Save measures against has to follow it.
            var formHasNoRole = _editingId == 0 && roleId is null;

            ApplyRefresh(() =>
            {
                Roles.MergeFrom(roles, r => r.Id);
                Ingredients.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Ingredient).OrderBy(m => m.Name)], m => m.Id);
                PackagingMaterials.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Packaging).OrderBy(m => m.Name)], m => m.Id);
                _rowCache.MergeFrom(subAssemblies.Select(s => new StandaloneSubAssemblyRow(s, linkedBy.GetValueOrDefault(s.Id) ?? [], madeFrom.GetValueOrDefault(s.Id))));

                // A form opened before the roles were loaded (a new one, shown at start) takes the first role.
                EditingRole = Roles.FirstOrDefault(r => r.Id == roleId) ?? (_editingId == 0 && roleId is null ? Roles.FirstOrDefault() : null);

                IngredientLineRow.Resolve(Lines, Ingredients.ToDictionary(m => m.Id));
                PackagingLineRow.Resolve(PackagingLines, PackagingMaterials.ToDictionary(m => m.Id));
                RecomputeTotals();
                SetCost = PackagingLines.Sum(l => l.CostPerUnit);

                RepointSelection(Rows.FirstOrDefault(r => r.Id == selectedId));
                if (_editingId > 0)
                {
                    ShowDependents(linkedBy.GetValueOrDefault(_editingId) ?? [], madeFrom.GetValueOrDefault(_editingId) ?? []);
                }
            });

            // Only the role, and only when the form had none: a role the user picked must keep counting as a
            // change, so this can't be a blanket MarkClean.
            if (formHasNoRole && EditingRole is not null)
            {
                History.Rebase((settled, now) => settled with { RoleId = now.RoleId });
            }

            // Outside ApplyRefresh, whose selection changes only re-point: this one has to load the sub-assembly's form.
            if (_subAssemblyToOpen is { } subAssemblyId)
            {
                _subAssemblyToOpen = null;
                TrySelectSubAssembly(subAssemblyId);
            }
        }));
    }

    /// <summary>New, Copy, Delete, New Product, and the Dependents tiles' links.</summary>
    [MemberNotNull(nameof(NewCommand), nameof(CopyCommand), nameof(OpenProductCommand), nameof(OpenRawMaterialCommand), nameof(NewProductCommand), nameof(DeleteCommand))]
    private void WireRecordCommands()
    {
        // A new sub-assembly replaces the one showing, so its unsaved changes are asked about first. Copy doesn't ask: the
        // copy holds them.
        NewCommand = ReactiveCommand.CreateFromTask(() => AfterUnsavedChangesAsync(() => NewSubAssemblyInList()));

        CopyCommand = ReactiveCommand.Create(CopyForm, this.WhenAnyValue(x => x.SelectedRow).Select(row => row is not null));

        OpenProductCommand = ReactiveCommand.Create<int>(_productOpenRequests.OnNext);

        OpenRawMaterialCommand = ReactiveCommand.Create<int>(_rawMaterialOpenRequests.OnNext);

        NewProductCommand = ReactiveCommand.Create(
            () =>
            {
                if (History.IsDirty)
                {
                    ErrorMessage = $"Save {HeaderTitle} first, or undo its changes: the new product links it as it's saved.";
                    return;
                }

                ErrorMessage = null;
                _newProductRequests.OnNext(SelectedRow!.Id);
            },
            this.WhenAnyValue(x => x.SelectedRow).Select(row => row is not null && row.SubAssembly.Role?.IsPackaging != true));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => DeleteSubAssemblyAsync(SelectedRow!),
            this.WhenAnyValue(x => x.SelectedRow).Select(row => row is not null));
    }

    /// <summary>Save: the name, the role and the lines together, with a version of each product it changes.</summary>
    [MemberNotNull(nameof(SaveCommand))]
    private void WireSaveCommand()
    {
        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSaveAsync(async () =>
        {
            var name = EditingName.Trim();
            var validation = _subAssemblyValidator.Validate(new SubAssembly { Name = name, RoleId = EditingRole?.Id ?? 0 });
            if (!validation.IsValid)
            {
                ErrorMessage = validation.ToString(" ");
                return;
            }

            // The lines as they're typed, read here on the UI thread, for the save below.
            var subAssembly = new SubAssembly { Id = _editingId, Name = name, RoleId = EditingRole!.Id };
            Func<CancellationToken, Task<int>> save;
            if (IsPackaging)
            {
                var setLines = PackagingLineRow.CompleteLines(PackagingLines, "set");
                if (setLines.Count == 0)
                {
                    ErrorMessage = $"Add the packaging materials that go into {name}, per set.";
                    return;
                }

                List<(int PackagingMaterialId, int? UnitId, decimal QtyPerUnit)> packagingLines =
                    [.. setLines.Select(l => (l.PackagingMaterial.Id, l.UnitId, l.QtyPerUnit))];
                save = cancellationToken => _subAssemblyRepository.SaveStandalonePackagingAsync(subAssembly, packagingLines, cancellationToken);
            }
            else
            {
                var lines = IngredientLineRow.CompleteLines(Lines, name);
                if (lines.Count == 0)
                {
                    ErrorMessage = $"Add the ingredients that go into {name}, in grams.";
                    return;
                }

                List<RecipeLine> recipeLines = [.. lines.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))];
                save = cancellationToken => _subAssemblyRepository.SaveStandaloneAsync(subAssembly, recipeLines, cancellationToken);

                // A saved one that products link: what it does to their costs is asked about first.
                if (_recipeImpact is not null && subAssembly.Id != 0)
                {
                    List<SubAssemblyLine> newLines =
                    [
                        .. lines.Select((l, i) => new SubAssemblyLine
                        {
                            SubAssemblyId = subAssembly.Id, IngredientId = l.Ingredient.Id, Ingredient = l.Ingredient, Grams = l.Grams,
                            IsFormulaBase = l.IsFormulaBase, AuthoredBakersPercent = l.AuthoredBakersPercent, SortOrder = i,
                        }),
                    ];
                    var impacts = await Task.Run(() => _recipeImpact.ReadAsync(subAssembly.Id, newLines, CancellationToken.None));
                    if (impacts.Count > 0 && !await ConfirmDelete.Handle(SharedRecipeImpact.Question(name, impacts, MadeIngredients.Count)))
                    {
                        return;
                    }
                }
            }

            // Written with a version of each product using it that this Save changes, both or neither (VersionedSaves).
            var saved = await Task.Run(() => _versionedSaves.SaveSubAssemblyAsync(subAssembly.Id, name, save, DateTime.Now, CancellationToken.None));

            // The list picks up the new totals; then the form reloads what was saved.
            await LoadCommand.Execute();
            var row = Rows.FirstOrDefault(r => r.Id == saved.Id);
            RepointSelection(row);
            await LoadIntoFormAsync(row);
            SuccessMessage = ProductVersionRecorder.RecordedMessage(saved.ProductsVersioned) is { } recorded
                ? $"{name} was saved. {recorded}"
                : $"{name} was saved.";
        }),
        History.WhenAnyValue(h => h.IsDirty));
    }

    /// <summary>
    /// New, from the header or the list. A sub-assembly still waiting for LoadCommand to open it is no longer opened: it
    /// would load over the new form, and a name typed into it since would then be saved as that sub-assembly's.
    /// </summary>
    private void StartNewSubAssembly(SubAssemblyRole? role = null)
    {
        _subAssemblyToOpen = null;
        ClearForm(role);
    }

    /// <summary>A fresh form for a new sub-assembly: nothing selected, no name or lines, in <paramref name="role"/> or the first role.</summary>
    private void ClearForm(SubAssemblyRole? role = null)
    {
        // The new sub-assembly's row, if one was named in the list, goes with the form it stood for.
        EndNewSubAssemblyRow();
        History.Clear();
        _formLoadVersion++;
        RepointSelection(null);
        FormVersion++;
        _editingId = 0;
        HeaderTitle = "New sub-assembly";
        EditingName = string.Empty;
        EditingRole = role ?? Roles.FirstOrDefault();
        SelectedLine = null;
        Lines.Clear();
        SelectedPackagingLine = null;
        PackagingLines.Clear();
        ShowDependents([], []);

        // The blank form is what Save's dirty check measures against, so nothing is offered until it's filled in.
        History.MarkClean();
    }

    /// <summary>
    /// A new, unsaved sub-assembly holding what the form shows, edits not yet saved included: the same role and lines,
    /// named "… Copy" (or "… Copy 2" and on, when that's taken), for Save to add beside the original. The original is
    /// left as it was last saved. Like New, it's a fresh form with nothing to undo, measured against a blank one, so
    /// Save is offered straight away.
    /// </summary>
    private void CopyForm()
    {
        var source = CaptureSnapshot();
        var sourceName = HeaderTitle;
        var name = SubAssemblyRules.UniqueStandaloneName($"{source.Name.Trim()} Copy", [.. Rows.Select(r => r.Name)]);

        ClearForm();
        RestoreSnapshot(source with { EditingId = 0, Name = name }, settledThen: null);
        HeaderTitle = $"Copy of {sourceName}";
        ShowDependents([], []);
        History.RefreshDirty();
    }

    /// <summary>
    /// The picked sub-assembly's name, role and lines, fetched again so they're what was saved. A load
    /// overtaken by a newer one (another pick, New) is dropped. Nothing picked clears the form.
    /// </summary>
    private async Task LoadIntoFormAsync(StandaloneSubAssemblyRow? row)
    {
        if (row is null)
        {
            ClearForm();
            return;
        }

        // A sub-assembly has one kind of line or the other, as its role says.
        var version = ++_formLoadVersion;
        var isSet = row.SubAssembly.Role?.IsPackaging == true;
        IReadOnlyList<SubAssemblyLine> saved;
        IReadOnlyList<SubAssemblyPackagingLine> savedSet;
        try
        {
            saved = isSet ? [] : await _lineRepository.GetForSubAssembliesAsync([row.Id], CancellationToken.None);
            savedSet = isSet ? await _subAssemblyRepository.GetPackagingLinesAsync(row.Id, CancellationToken.None) : [];
        }
        catch (Exception) when (version == _formLoadVersion)
        {
            // Nothing of the picked one's came: the form mustn't go on showing another sub-assembly under its row, so it's
            // left blank, with nothing picked. Picking it again tries again.
            ClearForm();
            throw;
        }

        if (version != _formLoadVersion)
        {
            return;
        }

        History.Clear();
        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        var packagingById = PackagingMaterials.ToDictionary(m => m.Id);

        // Not for the same sub-assembly loaded again, as a Save does: the boxes still hold what's its.
        if (row.Id != _editingId)
        {
            FormVersion++;
        }

        _editingId = row.Id;
        HeaderTitle = row.Name;
        EditingName = row.Name;
        EditingRole = Roles.FirstOrDefault(r => r.Id == row.SubAssembly.RoleId);
        SelectedLine = null;
        Lines.Clear();
        Lines.AddRange(saved.Select(l => new IngredientLineRow(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)));
        SelectedPackagingLine = null;
        PackagingLines.Clear();
        PackagingLines.AddRange(savedSet.Select(l => new PackagingLineRow(packagingById.GetValueOrDefault(l.PackagingMaterialId) ?? l.PackagingMaterial!, l.QtyPerSet, l.UnitId)));
        ShowDependents(row.GetDependents(), row.GetMadeIngredients());

        // The form now holds what was saved, so this is what Save's dirty check measures against. Taken at the
        // end: the lines land after the name and role, and a form measured before them would read as changed
        // the moment they arrived.
        History.MarkClean();
    }

    private void ShowDependents(IReadOnlyList<LinkingProduct> products, IReadOnlyList<MadeIngredientUse> madeIngredients)
    {
        Dependents = products;
        MadeIngredients = madeIngredients;
        DependentsText = madeIngredients.Count > 0 ? MadeFromText
            : products.Count == 0 ? IsPackaging ? SetNotUsedText : NotUsedText
            : UsedText;
    }

    /// <summary>
    /// Opens a sub-assembly as picking it in the list would, for another screen (a linked badge on Products). A search
    /// hiding it is cleared, so the list shows the row picked. A list that doesn't hold it yet (the screen not loaded
    /// since, or ever) opens it at the end of the next LoadCommand, which showing the screen runs.
    /// </summary>
    public void OpenSubAssembly(int subAssemblyId)
    {
        _subAssemblyToOpen = TrySelectSubAssembly(subAssemblyId) ? null : subAssemblyId;
    }

    /// <summary>
    /// Opens a sub-assembly at the end of the next LoadCommand, for the one in the screen's address as the screen opens:
    /// its list is fresh only then. New clicked meanwhile is the later word, and the sub-assembly isn't opened over it.
    /// </summary>
    public void OpenSubAssemblyOnLoad(int subAssemblyId)
    {
        _subAssemblyToOpen = subAssemblyId;
    }

    /// <summary>
    /// <see cref="OpenSubAssembly"/>, asking first about the unsaved changes of the sub-assembly showing when another is to
    /// replace it. False when the user stays on it.
    /// </summary>
    public async Task<bool> OpenSubAssemblyAsync(int subAssemblyId)
    {
        if (subAssemblyId != SelectedRow?.Id && !await ResolveUnsavedChangesAsync(revert: false))
        {
            return false;
        }

        OpenSubAssembly(subAssemblyId);
        return true;
    }

    /// <summary>IRecordScreen, for the Vim command line's <c>:e name</c>: opens the sub-assembly the name means best.</summary>
    public bool OpenByName(string text)
    {
        if (RecordNameMatch.Best(Rows, row => row.Name, text) is not { } row)
        {
            return false;
        }

        _ = RunSafelyAsync(() => OpenSubAssemblyAsync(row.Id));
        return true;
    }

    private bool TrySelectSubAssembly(int subAssemblyId)
    {
        if (Rows.FirstOrDefault(r => r.Id == subAssemblyId) is not { } row)
        {
            return false;
        }

        if (!VisibleRows.Contains(row))
        {
            SearchText = string.Empty;
        }

        SelectedRow = row;
        return true;
    }

    /// <summary>
    /// Deletes <paramref name="row"/>'s sub-assembly once it's confirmed: the header's Delete, for the one picked, or the
    /// list's, for any. The form starts afresh if it was showing it.
    /// </summary>
    private Task DeleteSubAssemblyAsync(StandaloneSubAssemblyRow row) =>
        RunWithConfirmationAsync($"Delete '{row.Name}'? This can't be undone.", async () =>
        {
            await _subAssemblyRepository.DeleteStandaloneAsync(row.Id, CancellationToken.None);
            if (_editingId == row.Id || SelectedRow?.Id == row.Id)
            {
                ClearForm();
            }

            await LoadCommand.Execute();
        });
}
