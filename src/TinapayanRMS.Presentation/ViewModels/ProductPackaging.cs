using System.Collections.ObjectModel;
using DynamicData;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>How the Packaging tab's picker takes the packaging picked: as a link, or as a copy of its lines.</summary>
public enum PackagingSourceMode
{
    Link,
    Copy,
}

/// <summary>
/// The Products screen's Packaging tab, always the Recipe Structure's last (PackagingSection, through
/// ProductsViewModel.Packaging): the per-piece packaging lines, typed straight into its table, or what they link (a set
/// on the Sub-Assemblies screen, or another product's packaging), the picker its Link and Copy open, its bulk packaging
/// (a box several units share, with how many it holds), a Move to Sub-Assemblies waiting for Save, and what was last
/// loaded, which Save compares against. The screen keeps the undo history these are part of, and the commands that
/// change them as undo steps.
/// </summary>
/// <param name="findPackagingMaterial">A packaging material as the screen last loaded it, for a row that prices itself.</param>
/// <param name="costBasis">The screen's cost basis, buffered or not, which a new row is priced at.</param>
public sealed class ProductPackaging(Func<int, RawMaterial?> findPackagingMaterial, Func<CostBasis> costBasis) : ReactiveObject
{
    /// <summary>The key of the link last loaded or saved, or null for none: Save writes the link only when it differs.</summary>
    private int? _savedLinkKey;

    /// <summary>The lines last loaded, so Save only sends them when they changed.</summary>
    private List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> _savedLines = [];

    /// <summary>The bulk lines last loaded, so Save only sends the bulk packaging when it changed.</summary>
    private List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> _savedBulkLines = [];

    /// <summary>How many products a bulk pack held as last loaded.</summary>
    private int? _savedBulkQuantity;

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
    /// The bulk packaging's lines, typed into the Packaging tab's Bulk Packaging table: what one bulk pack takes (a box
    /// several units share, its tape), each quantity per pack (PackagingLine.IsBulk). The product's own, whatever its
    /// packaging links.
    /// </summary>
    public ObservableCollection<PackagingLineRow> BulkEdits { get; } = [];

    /// <summary>The line the Bulk Packaging table's add row edits, as <see cref="NewLineEdit"/> is for the lines.</summary>
    public Func<PackagingLineRow> NewBulkEdit => () => new PackagingLineRow(findPackagingMaterial) { CostBasis = costBasis() };

    private PackagingLineRow? _selectedBulkEdit;
    public PackagingLineRow? SelectedBulkEdit
    {
        get => _selectedBulkEdit;
        set => this.RaiseAndSetIfChanged(ref _selectedBulkEdit, value);
    }

    private int? _bulkQuantity;
    /// <summary>How many products one bulk pack holds (Product.BulkPackagingQuantity), typed beside its table; null until it is.</summary>
    public int? BulkQuantity
    {
        get => _bulkQuantity;
        set
        {
            this.RaiseAndSetIfChanged(ref _bulkQuantity, value);
            RaiseBulkCostChanged();
        }
    }

    /// <summary>What one bulk pack's lines cost together, at the screen's cost basis.</summary>
    public decimal BulkPackCost => BulkEdits.Sum(l => l.CostPerUnit);

    /// <summary>What the bulk packaging costs one sellable unit: the pack's cost shared out among the products it holds.</summary>
    public decimal BulkCostPerUnit => ProductRules.CalculateBulkPackagingCost(BulkPackCost, BulkQuantity);

    /// <summary>The Bulk Packaging table's caption: "Pack cost 42.58 ÷ 6 products = 7.10 per unit".</summary>
    public string BulkCostText => BulkQuantity is >= 1
        ? $"Pack cost {BulkPackCost:N2} ÷ {BulkQuantity} {(BulkQuantity == 1 ? "product" : "products")} = {BulkCostPerUnit:N2} per unit"
        : $"Pack cost {BulkPackCost:N2}. Enter how many products one pack holds.";

    /// <summary>The bulk figures again, after a line or the quantity changed.</summary>
    public void RaiseBulkCostChanged()
    {
        this.RaisePropertyChanged(nameof(BulkPackCost));
        this.RaisePropertyChanged(nameof(BulkCostPerUnit));
        this.RaisePropertyChanged(nameof(BulkCostText));
    }

    /// <summary>Every row the Packaging tab shows, its lines and then its bulk lines: what the screen prices and resolves.</summary>
    public IEnumerable<PackagingLineRow> AllLineEdits => LineEdits.Concat(BulkEdits);

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

    private PackagingSourceMode? _sourceMode;
    /// <summary>
    /// Link or Copy was pressed on the Packaging tab: a picker of what to link or copy (SourceOptions) stands in for the
    /// two buttons until one is picked (PickedSource) or the pick is cancelled. Null while nothing is being picked.
    /// </summary>
    public PackagingSourceMode? SourceMode
    {
        get => _sourceMode;
        private set
        {
            this.RaiseAndSetIfChanged(ref _sourceMode, value);
            this.RaisePropertyChanged(nameof(IsPickingSource));
            this.RaisePropertyChanged(nameof(SourcePlaceholder));
        }
    }

    public bool IsPickingSource => SourceMode is not null;

    /// <summary>The picker's prompt, by what it's for.</summary>
    public string SourcePlaceholder => SourceMode == PackagingSourceMode.Copy ? "Copy the packaging of…" : "Link a packaging set…";

    /// <summary>
    /// What the picker offers: to link, the Sub-Assemblies screen's packaging sets; to copy, those and every other
    /// product's packaging.
    /// </summary>
    public ObservableCollection<LinkableSubAssemblyOption> SourceOptions { get; } = [];

    private LinkableSubAssemblyOption? _pickedSource;
    /// <summary>The packaging picked to link or copy: its lines load, and then fill the table (the screen's ApplyPackagingSourceAsync).</summary>
    public LinkableSubAssemblyOption? PickedSource
    {
        get => _pickedSource;
        set => this.RaiseAndSetIfChanged(ref _pickedSource, value);
    }

    /// <summary>Opens the picker for <paramref name="mode"/>, offering <paramref name="options"/>, nothing picked yet.</summary>
    public void StartPickingSource(PackagingSourceMode mode, IEnumerable<LinkableSubAssemblyOption> options)
    {
        PickedSource = null;
        SourceOptions.Clear();
        SourceOptions.AddRange(options);
        SourceMode = mode;
    }

    /// <summary>Closes the picker, picked or not.</summary>
    public void EndPickingSource()
    {
        SourceMode = null;
        PickedSource = null;
        SourceOptions.Clear();
    }

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

    /// <summary>Puts <paramref name="rows"/> in the Packaging tab's Bulk Packaging table, in place of whatever it held.</summary>
    public void ShowBulkLines(IEnumerable<PackagingLineRow> rows)
    {
        SelectedBulkEdit = null;
        BulkEdits.Clear();
        BulkEdits.AddRange(rows);
        RaiseBulkCostChanged();
    }

    /// <summary>Shows <paramref name="lines"/> as saved, and makes them what Save compares against.</summary>
    public void LoadLines(IReadOnlyList<PackagingLine> lines)
    {
        ShowLines(lines.Select(PackagingLineRow.FromSaved));
        _savedLines = CurrentLines();
    }

    /// <summary>
    /// Shows the bulk packaging as saved, its lines for one pack and how many products it holds, and makes it what Save
    /// compares against.
    /// </summary>
    public void LoadBulk(IReadOnlyList<PackagingLine> lines, int? quantity)
    {
        ShowBulkLines(lines.Select(PackagingLineRow.FromSaved));
        BulkQuantity = lines.Count > 0 ? quantity : null;
        _savedBulkLines = CurrentBulkLines();
        _savedBulkQuantity = BulkQuantity;
    }

    /// <summary>No packaging, lines or link, no bulk packaging, and none saved: a new product's, or the details cleared for another's.</summary>
    public void Clear()
    {
        EndPickingSource();
        LineEdits.Clear();
        LoadLink(null);
        _savedLines = [];
        LoadBulk([], null);
    }

    /// <summary>The packaging lines as they're typed, those filled in; a line half filled in is refused (BusinessRuleException).</summary>
    public List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> CurrentLines() =>
        [.. PackagingLineRow.CompleteLines(LineEdits).Select(l => (l.PackagingMaterial.Id, l.UnitId, PackagingLineAuthoredField.QtyPerUnit, l.QtyPerUnit))];

    /// <summary>The bulk lines as they're typed, each quantity per pack, those filled in; one half filled in is refused (BusinessRuleException).</summary>
    public List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> CurrentBulkLines() =>
        [.. PackagingLineRow.CompleteLines(BulkEdits, "pack").Select(l => (l.PackagingMaterial.Id, l.UnitId, PackagingLineAuthoredField.QtyPerUnit, l.QtyPerUnit))];

    /// <summary>
    /// The bulk packaging, its lines and how many products a pack holds, when either differs from what was last loaded,
    /// else null. Lines need the quantity (refused at Save, ProductRules.EnsureValidBulkPackaging); no lines saves none.
    /// </summary>
    public BulkPackagingDraft? ChangedBulk()
    {
        var lines = CurrentBulkLines();
        var quantity = lines.Count > 0 ? BulkQuantity : null;
        return lines.SequenceEqual(_savedBulkLines) && quantity == _savedBulkQuantity ? null : new BulkPackagingDraft(lines, quantity);
    }

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
