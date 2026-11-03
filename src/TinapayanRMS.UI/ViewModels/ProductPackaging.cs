using System.Collections.ObjectModel;
using DynamicData;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// The Products screen's Packaging tab, in its Recipe Structure (PackagingSectionView, through ProductsViewModel.Packaging,
/// since 2026-10-28): the per-piece packaging lines, typed straight into its table, or what they link (another product's
/// packaging, or a set on the Sub-Assemblies screen), a Move to Sub-Assemblies waiting for Save, and what was last loaded,
/// which Save compares against. The screen keeps whether the product has packaging at all (HasPackaging, the tab), the
/// undo history these are part of, and the commands that change them as undo steps.
/// </summary>
/// <param name="findPackagingMaterial">A packaging material as the screen last loaded it, for a row that prices itself.</param>
/// <param name="costBasis">The screen's cost basis, buffered or not, which a new row is priced at.</param>
public sealed class ProductPackaging(Func<int, RawMaterial?> findPackagingMaterial, Func<CostBasis> costBasis) : ReactiveObject
{
    /// <summary>The key of the link last loaded or saved, or null for none: Save writes the link only when it differs.</summary>
    private int? _savedLinkKey;

    /// <summary>The lines last loaded, so Save only sends them when they changed.</summary>
    private List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> _savedLines = [];

    /// <summary>The packaging lines, typed straight into the Packaging tab's table.</summary>
    public ObservableCollection<PackagingLineRow> LineEdits { get; } = [];

    /// <summary>The line the packaging table's add row edits: it offers its material's units and prices itself before it's committed.</summary>
    public Func<PackagingLineRow> NewLineEdit => () => new PackagingLineRow(findPackagingMaterial) { CostBasis = costBasis() };

    private PackagingLineRow? _selectedLineEdit;
    public PackagingLineRow? SelectedLineEdit
    {
        get => _selectedLineEdit;
        set => this.RaiseAndSetIfChanged(ref _selectedLineEdit, value);
    }

    /// <summary>
    /// What the per-piece packaging links as the form stands, saved or not (the Add Sub-Assembly form's Link sets it,
    /// the Packaging tab's Make a Copy and Delete Sub-Assembly clear it), or null while the product owns its lines.
    /// </summary>
    public PackagingLinkTarget? LinkTarget { get; private set; }

    private bool _isPackagingLinked;
    public bool IsPackagingLinked
    {
        get => _isPackagingLinked;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isPackagingLinked, value);
            this.RaisePropertyChanged(nameof(IsPackagingEditable));
            this.RaisePropertyChanged(nameof(CanMovePackaging));
        }
    }

    /// <summary>The packaging is the product's own, so its table can be typed into; linked packaging is kept where it's from.</summary>
    public bool IsPackagingEditable => !IsPackagingLinked;

    private bool _isPackagingMoving;
    /// <summary>
    /// Move to Sub-Assemblies was pressed on the Packaging tab: Save puts the product's own lines on the Sub-Assemblies
    /// screen as a packaging set, and links it. They're still typed into here until then, and the tab's "Moves to
    /// Sub-Assemblies on Save" badge says so. Whatever changes what the packaging links ends it (ShowLink).
    /// </summary>
    public bool IsPackagingMoving
    {
        get => _isPackagingMoving;
        set
        {
            this.RaiseAndSetIfChanged(ref _isPackagingMoving, value);
            this.RaisePropertyChanged(nameof(CanMovePackaging));
        }
    }

    /// <summary>The packaging is the product's own lines, not on their way already: the Packaging tab offers Move to Sub-Assemblies.</summary>
    public bool CanMovePackaging => IsPackagingEditable && !IsPackagingMoving;

    private string _packagingLinkedToText = string.Empty;
    /// <summary>"Linked to Cream Loaf" or "Linked to Gift Box Set (Sub-Assemblies)", or empty while the product has its own packaging.</summary>
    public string PackagingLinkedToText
    {
        get => _packagingLinkedToText;
        private set => this.RaiseAndSetIfChanged(ref _packagingLinkedToText, value);
    }

    /// <summary>The packaging set linked packaging comes from, whose badge opens it on Sub-Assemblies; null for another product's, or none.</summary>
    public int? PackagingLinkedSetId => (LinkTarget as PackagingLinkTarget.Set)?.SubAssemblyId;

    public bool IsPackagingLinkedToSet => PackagingLinkedSetId is not null;

    public bool IsPackagingLinkedToProduct => IsPackagingLinked && !IsPackagingLinkedToSet;

    /// <summary>
    /// What the per-piece packaging links as the form stands, saved or not, and the Packaging tab's banner. A load, Delete
    /// Sub-Assembly, Make a Copy and an add all come through here, so a Move to Sub-Assemblies waiting for Save ends here
    /// too; an undo puts it back afterwards (the screen's RestoreSnapshot).
    /// </summary>
    public void ShowLink(PackagingLinkTarget? target)
    {
        LinkTarget = target;
        IsPackagingMoving = false;
        IsPackagingLinked = target is not null;
        PackagingLinkedToText = target?.LinkedToText ?? string.Empty;
        this.RaisePropertyChanged(nameof(PackagingLinkedSetId));
        this.RaisePropertyChanged(nameof(IsPackagingLinkedToSet));
        this.RaisePropertyChanged(nameof(IsPackagingLinkedToProduct));
    }

    /// <summary>The saved packaging link, or none: what the Packaging tab shows, and what Save compares against.</summary>
    public void LoadLink(PackagingLink? link)
    {
        var target = link is null ? null : PackagingLinkTarget.From(link);
        _savedLinkKey = target?.Key;
        ShowLink(target);
    }

    /// <summary>Whether the form still shows <paramref name="savedLink"/>, the link as saved, so a refresh may read its lines again.</summary>
    public bool ShowsSavedLink(PackagingLinkTarget savedLink) => savedLink.Key == _savedLinkKey && LinkTarget?.Key == savedLink.Key;

    /// <summary>Puts <paramref name="rows"/> in the Packaging tab's table, in place of whatever it held.</summary>
    public void ShowLines(IEnumerable<PackagingLineRow> rows)
    {
        SelectedLineEdit = null;
        LineEdits.Clear();
        LineEdits.AddRange(rows);
    }

    /// <summary>Shows <paramref name="lines"/> as saved, and makes them what Save compares against.</summary>
    public void LoadLines(IReadOnlyList<PackagingLine> lines)
    {
        ShowLines(lines.Select(PackagingLineRow.FromSaved));
        _savedLines = CurrentLines();
    }

    /// <summary>No packaging, lines or link, and none saved: a new product's, or the details cleared for another's.</summary>
    public void Clear()
    {
        LineEdits.Clear();
        LoadLink(null);
        _savedLines = [];
    }

    /// <summary>The packaging lines as they're typed, those filled in; a line half filled in is refused (BusinessRuleException).</summary>
    public List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> CurrentLines() =>
        [.. PackagingLineRow.CompleteLines(LineEdits).Select(l => (l.PackagingMaterial.Id, l.UnitId, PackagingLineAuthoredField.QtyPerUnit, l.QtyPerUnit))];

    /// <summary>
    /// The packaging lines when they differ from those last loaded, else null. Linked packaging is never saved here. Lines
    /// moving to the Sub-Assemblies screen always go, since they're what the new packaging set is made of.
    /// </summary>
    public List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)>? ChangedLines()
    {
        if (IsPackagingLinked)
        {
            return null;
        }

        // Packaging that was linked when loaded, and is the product's own now (Make a Copy, or a new or copied one in its
        // place), is written even when its lines match what the link showed: the product had none of its own.
        var current = CurrentLines();
        return current.SequenceEqual(_savedLines) && _savedLinkKey is null && !IsPackagingMoving ? null : current;
    }

    /// <summary>
    /// What the packaging links when it differs from the link last loaded, else null; no link is PackagingLinkDraft.None, and
    /// a set made of its own lines (Move to Sub-Assemblies) is PackagingLinkDraft.NewSet.
    /// </summary>
    public PackagingLinkDraft? ChangedLink() =>
        IsPackagingMoving ? new PackagingLinkDraft.NewSet()
        : LinkTarget?.Key == _savedLinkKey ? null
        : LinkTarget?.ToDraft() ?? new PackagingLinkDraft.None();
}
