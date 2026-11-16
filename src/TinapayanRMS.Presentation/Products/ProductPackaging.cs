using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using DynamicData.Binding;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Products;

/// <summary>How the Packaging tab's picker takes the packaging picked: as a link, or as a copy of its lines.</summary>
public enum PackagingSourceMode
{
    Link,
    Copy,
}

/// <summary>
/// What the Packaging tab (<see cref="ProductPackaging"/>) asks of the Products screen, besides its runners, messages and
/// undo steps: the screen's cost basis, which its rows are priced at, the other products, whose packaging Copy offers, and
/// the product's costs worked out again. The screen implements it explicitly, as Raw Materials does IBrandPriceRowHost.
/// </summary>
public interface IProductPackagingHost : IScreenPartHost, IUndoRecorder
{
    /// <summary>Which of the materials' costs the screen shows, buffered or not: what a row is priced at.</summary>
    CostBasis CostBasis { get; }

    /// <summary>Every product but the one showing, whose packaging Copy offers.</summary>
    IEnumerable<Product> OtherProducts { get; }

    /// <summary>The product's costs worked out again, after a change to the packaging made in one go.</summary>
    void RecomputeCosts();
}

/// <summary>
/// The Products screen's Packaging tab, always the Recipe Structure's last (PackagingSection, through
/// ProductsState.Packaging): the per-piece packaging lines, typed straight into its table, or what they link (a set
/// on the Sub-Assemblies screen, or another product's packaging), the picker its Link and Copy open, its bulk packaging
/// (a box several units share, with how many it holds), a Move to Sub-Assemblies waiting for Save, and what was last
/// loaded, which Save compares against. Its commands are its own, each change one undo step of the screen's form
/// (<see cref="IProductPackagingHost"/>); the screen keeps the undo history, the costs these lines add to, and Save.
/// </summary>
public sealed class ProductPackaging : ObservableState, IDisposable
{
    private readonly IProductPackagingHost _host;
    private readonly IReadOnlyList<RawMaterial> _packagingMaterials;
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly IPackagingLineRepository _packagingLineRepository;

    /// <summary>The standalone packaging sets (sub-assemblies in the Packaging role), by name, as last loaded, for Link and Copy.</summary>
    private IReadOnlyList<SubAssembly> _packagingSets = [];

    /// <summary>The key of the link last loaded or saved, or null for none: Save writes the link only when it differs.</summary>
    private int? _savedLinkKey;

    /// <summary>The lines last loaded, so Save only sends them when they changed.</summary>
    private List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> _savedLines = [];

    /// <summary>The bulk lines last loaded, so Save only sends the bulk packaging when it changed.</summary>
    private List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> _savedBulkLines = [];

    /// <summary>How many products a bulk pack held as last loaded.</summary>
    private int? _savedBulkQuantity;

    /// <summary>Each packaging picked to link or copy, nothing picked first: what loads its lines (PickedSource).</summary>
    private readonly BehaviorSubject<LinkableSubAssemblyOption?> _pickedSources = new(null);

    /// <param name="host">The Products screen: its cost basis, its runners and messages, and its undo steps.</param>
    /// <param name="packagingMaterials">The packaging materials as the screen last loaded them, which a row prices itself from.</param>
    public ProductPackaging(
        IProductPackagingHost host,
        IReadOnlyList<RawMaterial> packagingMaterials,
        ISubAssemblyRepository subAssemblyRepository,
        IPackagingLineRepository packagingLineRepository)
    {
        _host = host;
        _packagingMaterials = packagingMaterials;
        _subAssemblyRepository = subAssemblyRepository;
        _packagingLineRepository = packagingLineRepository;

        // Every row put in a table, however it got there (a product read, an undo, a link or copy), is priced at the
        // screen's costs, buffered or not; the add rows' factories see to a row still being typed.
        LineEdits.ToObservableChangeSet().OnItemAdded(r => r.CostBasis = host.CostBasis).Subscribe();
        BulkEdits.ToObservableChangeSet().OnItemAdded(r => r.CostBasis = host.CostBasis).Subscribe();

        // The linked packaging's lines, as they show, become the product's own; Save drops the link and writes them.
        MakeCopyCommand = ScreenCommand.Of(() => host.AsUndoStep(() => ShowLink(null)), () => IsPackagingLinked);

        // The link and the lines it shows go, leaving none for each piece; Save drops the link. The bulk packaging stays.
        RemoveLinkCommand = ScreenCommand.Of(
            () => host.AsUndoStep(() =>
            {
                SelectedLineEdit = null;
                LineEdits.Clear();
                ShowLink(null);
                host.RecomputeCosts();
            }),
            () => IsPackagingLinked);

        // Link and Copy open the picker; picking in it does the rest (ApplySourceAsync). Opening or closing it changes
        // nothing Save writes, so neither is an undo step.
        StartLinkingCommand = ScreenCommand.Of(StartLinking);
        StartCopyingCommand = ScreenCommand.Of(StartCopying);
        CancelPickingCommand = ScreenCommand.Of(EndPickingSource);
        host.LoadLatest(
            _pickedSources.Where(source => source is not null && IsPickingSource).Select(source => source!),
            ApplySourceAsync);

        // The product's own packaging lines go to the Sub-Assemblies screen as a packaging set on Save, which it links.
        // They're typed straight into the tab rather than through a form, so they're checked now instead: a line half
        // filled in, or none at all, is refused with the message, as a recipe section's form a rule refuses is. They're
        // still typed into until Save.
        MoveToSubAssembliesCommand = ScreenCommand.Of(
            () => host.AsUndoStep(() =>
            {
                host.ShowError(null);
                int lineCount;
                try
                {
                    lineCount = CurrentLines().Count;
                }
                catch (BusinessRuleException ex)
                {
                    host.ShowError(ex.Message);
                    return;
                }

                if (lineCount == 0)
                {
                    host.ShowError("Add the packaging materials one piece takes first: a packaging set needs at least one.");
                    return;
                }

                IsPackagingMoving = true;
            }),
            () => CanMovePackaging);

        RemoveLineCommand = ScreenCommand.Of(
            () => host.AsUndoStep(() => LineEdits.Remove(SelectedLineEdit!)),
            () => SelectedLineEdit is not null && !IsPackagingLinked);

        RemoveBulkLineCommand = ScreenCommand.Of(
            () => host.AsUndoStep(() => BulkEdits.Remove(SelectedBulkEdit!)),
            () => SelectedBulkEdit is not null);
    }

    /// <summary>Make a Copy on linked packaging: the lines it shows become the product's own, and the link goes with Save.</summary>
    public ScreenCommand MakeCopyCommand { get; }

    /// <summary>Remove Link on linked packaging: the link and the lines it shows go, leaving no packaging per piece.</summary>
    public ScreenCommand RemoveLinkCommand { get; }

    /// <summary>Link: a picker of the packaging sets, the one picked linked in place of the packaging per piece.</summary>
    public ScreenCommand StartLinkingCommand { get; }

    /// <summary>Copy: a picker of the packaging sets and the other products, the one picked's lines copied in as this product's own.</summary>
    public ScreenCommand StartCopyingCommand { get; }

    /// <summary>Closes Link's or Copy's picker with nothing picked.</summary>
    public ScreenCommand CancelPickingCommand { get; }

    /// <summary>
    /// Move to Sub-Assemblies, while the lines are the product's own: Save puts them on the Sub-Assemblies screen as a
    /// packaging set named after the product, and links it (PackagingLinkDraft.NewSet). Make a Copy the other way round.
    /// </summary>
    public ScreenCommand MoveToSubAssembliesCommand { get; }

    /// <summary>Removes the line picked in the packaging per piece's table, as one undo step.</summary>
    public ScreenCommand RemoveLineCommand { get; }

    /// <summary>Removes the line picked in the Bulk Packaging table, as one undo step.</summary>
    public ScreenCommand RemoveBulkLineCommand { get; }

    /// <summary>The packaging lines, typed straight into the Packaging tab's table.</summary>
    public ObservableCollection<PackagingLineRow> LineEdits { get; } = [];

    /// <summary>The line the packaging table's add row edits: it offers its material's units and prices itself before it's committed.</summary>
    public Func<PackagingLineRow> NewLineEdit => () => new PackagingLineRow(FindMaterial) { CostBasis = _host.CostBasis };

    private PackagingLineRow? _selectedLineEdit;
    public PackagingLineRow? SelectedLineEdit
    {
        get => _selectedLineEdit;
        set => RaiseAndSetIfChanged(ref _selectedLineEdit, value);
    }

    /// <summary>
    /// The bulk packaging's lines, typed into the Packaging tab's Bulk Packaging table: what one bulk pack takes (a box
    /// several units share, its tape), each quantity per pack (PackagingLine.IsBulk). The product's own, whatever its
    /// packaging links.
    /// </summary>
    public ObservableCollection<PackagingLineRow> BulkEdits { get; } = [];

    /// <summary>The line the Bulk Packaging table's add row edits, as <see cref="NewLineEdit"/> is for the lines.</summary>
    public Func<PackagingLineRow> NewBulkEdit => () => new PackagingLineRow(FindMaterial) { CostBasis = _host.CostBasis };

    private PackagingLineRow? _selectedBulkEdit;
    public PackagingLineRow? SelectedBulkEdit
    {
        get => _selectedBulkEdit;
        set => RaiseAndSetIfChanged(ref _selectedBulkEdit, value);
    }

    private int? _bulkQuantity;
    /// <summary>How many products one bulk pack holds (Product.BulkPackagingQuantity), typed beside its table; null until it is.</summary>
    public int? BulkQuantity
    {
        get => _bulkQuantity;
        set
        {
            if (RaiseAndSetIfChanged(ref _bulkQuantity, value))
            {
                BulkQuantityChanged?.Invoke();
            }

            RaiseCostsChanged();
        }
    }

    /// <summary>How many products a bulk pack holds changed: the product's costs follow (the screen's PackagingLinesEdited).</summary>
    public event Action? BulkQuantityChanged;

    /// <summary>
    /// What the packaging per piece costs one sellable unit, its lines alone: the figure beside its heading. The bulk
    /// packaging's share is in its own caption (BulkCostText); the product's packaging cost holds both.
    /// </summary>
    public decimal LinesCost => LineEdits.Sum(l => l.CostPerUnit);

    /// <summary>What one bulk pack's lines cost together, at the screen's cost basis.</summary>
    public decimal BulkPackCost => BulkEdits.Sum(l => l.CostPerUnit);

    /// <summary>What the bulk packaging costs one sellable unit: the pack's cost shared out among the products it holds.</summary>
    public decimal BulkCostPerUnit => ProductRules.CalculateBulkPackagingCost(BulkPackCost, BulkQuantity);

    /// <summary>
    /// The Bulk Packaging table's caption: "Pack cost 42.58 ÷ 6 products = 7.10 per unit", or, while it has no lines, what
    /// to add (a quantity alone saves nothing).
    /// </summary>
    public string BulkCostText =>
        BulkEdits.Count == 0 ? "No bulk packaging. Add what one bulk pack takes, such as a box several products share."
        : BulkQuantity is >= 1 ? $"Pack cost {BulkPackCost:N2} ÷ {BulkQuantity} {(BulkQuantity == 1 ? "product" : "products")} = {BulkCostPerUnit:N2} per unit"
        : $"Pack cost {BulkPackCost:N2}. Enter how many products one pack holds.";

    /// <summary>The tab's cost figures again, after a line or the quantity changed: the packaging per piece's and the bulk packaging's.</summary>
    public void RaiseCostsChanged()
    {
        RaisePropertyChanged(nameof(LinesCost));
        RaisePropertyChanged(nameof(BulkPackCost));
        RaisePropertyChanged(nameof(BulkCostPerUnit));
        RaisePropertyChanged(nameof(BulkCostText));
    }

    /// <summary>Every row the Packaging tab shows, its lines and then its bulk lines: what the screen prices and resolves.</summary>
    public IEnumerable<PackagingLineRow> AllLineEdits => LineEdits.Concat(BulkEdits);

    /// <summary>Every row pointed at its material as the screen last loaded it, at its refreshed cost and units, in place.</summary>
    public void ResolveRows() => PackagingLineRow.Resolve(AllLineEdits, _packagingMaterials.ToDictionary(m => m.Id));

    /// <summary>Every row priced at the screen's cost basis again: Unbuffered or Buffered was pressed.</summary>
    public void Reprice()
    {
        foreach (var row in AllLineEdits)
        {
            row.CostBasis = _host.CostBasis;
        }
    }

    /// <summary>A packaging material as the screen last loaded it, for a row that prices itself.</summary>
    private RawMaterial? FindMaterial(int id) => _packagingMaterials.FirstOrDefault(m => m.Id == id);

    /// <summary>The packaging sets as LoadCommand read them: what Link offers, and Copy before the other products.</summary>
    public void TakePackagingSets(IReadOnlyList<SubAssembly> packagingSets) => _packagingSets = packagingSets;

    /// <summary>
    /// What the per-piece packaging links as the form stands, saved or not (the Packaging tab's Link sets it, and its Make a
    /// Copy and Remove Link clear it), or null while the product owns its lines.
    /// </summary>
    public PackagingLinkTarget? LinkTarget { get; private set; }

    private bool _isPackagingLinked;
    public bool IsPackagingLinked
    {
        get => _isPackagingLinked;
        private set
        {
            RaiseAndSetIfChanged(ref _isPackagingLinked, value);
            RaisePropertyChanged(nameof(IsPackagingEditable));
            RaisePropertyChanged(nameof(CanMovePackaging));
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
            RaiseAndSetIfChanged(ref _isPackagingMoving, value);
            RaisePropertyChanged(nameof(CanMovePackaging));
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
            RaiseAndSetIfChanged(ref _sourceMode, value);
            RaisePropertyChanged(nameof(IsPickingSource));
            RaisePropertyChanged(nameof(SourcePlaceholder));
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
    /// <summary>The packaging picked to link or copy: its lines load, and then fill the table (ApplySourceAsync).</summary>
    public LinkableSubAssemblyOption? PickedSource
    {
        get => _pickedSource;
        set
        {
            if (RaiseAndSetIfChanged(ref _pickedSource, value))
            {
                _pickedSources.OnNext(value);
            }
        }
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

    /// <summary>
    /// Link: the picker offers the Sub-Assemblies screen's packaging sets, less the one linked now. With none, it says how
    /// to make one. Another product's packaging is only copied, never linked.
    /// </summary>
    private void StartLinking()
    {
        var linkedKey = LinkTarget?.Key;
        List<LinkableSubAssemblyOption> options = [.. _packagingSets.Select(s => new LinkableSubAssemblyOption(s)).Where(o => o.Key != linkedKey)];
        if (options.Count == 0)
        {
            _host.ShowError(_packagingSets.Count == 0
                ? "There's no packaging set to link yet. Make one on the Sub-Assemblies screen, or type the lines here and press Move to Sub-Assemblies."
                : "This product links the only packaging set there is.");
            return;
        }

        _host.ShowError(null);
        StartPickingSource(PackagingSourceMode.Link, options);
    }

    /// <summary>Copy: the picker offers the packaging sets, then every other product's packaging.</summary>
    private void StartCopying()
    {
        _host.ShowError(null);
        StartPickingSource(
            PackagingSourceMode.Copy,
            [
                .. _packagingSets.Select(s => new LinkableSubAssemblyOption(s)),
                .. _host.OtherProducts.Select(p => new LinkableSubAssemblyOption(p)),
            ]);
    }

    /// <summary>
    /// The packaging picked, once its lines are in: a set's lines, or another product's packaging as it shows (through any
    /// link of its own). Linked, they show read-only and follow the set; copied, they're this product's own to type into.
    /// Either takes the place of the packaging per piece there was, as one undo step, and Save writes it; the bulk packaging
    /// stays. Packaging with no lines is refused, and the picker stays open for another.
    /// </summary>
    private async Task ApplySourceAsync(LinkableSubAssemblyOption source, CancellationToken cancellationToken)
    {
        var target = source.PackagingTarget!;
        IReadOnlyList<PackagingLineRow> rows = target switch
        {
            PackagingLinkTarget.Set(var setId, _) => [.. (await _subAssemblyRepository.GetPackagingLinesAsync(setId, cancellationToken))
                .Select(l => new PackagingLineRow(FindMaterial(l.PackagingMaterialId) ?? l.PackagingMaterial!, l.QtyPerSet, l.UnitId))],
            PackagingLinkTarget.OtherProduct(var productId, _) =>
                [.. (await _packagingLineRepository.GetEffectiveForProductAsync(productId, cancellationToken)).Select(PackagingLineRow.FromSaved)],
        };
        cancellationToken.ThrowIfCancellationRequested();

        // The picker moved on while the lines loaded (cancelled, another pick, or another product): they're stale.
        if (!ReferenceEquals(PickedSource, source))
        {
            return;
        }

        var linking = SourceMode == PackagingSourceMode.Link;
        if (rows.Count == 0)
        {
            _host.ShowError($"{target.Name} has no packaging for each piece to {(linking ? "link" : "copy")}. Pick another.");
            return;
        }

        _host.ShowError(null);
        _host.AsUndoStep(() =>
        {
            EndPickingSource();
            ShowLines(rows);
            ShowLink(linking ? target : null);
            _host.RecomputeCosts();
        });
    }

    private string _packagingLinkedToText = string.Empty;
    /// <summary>"Linked to Cream Loaf" or "Linked to Gift Box Set (Sub-Assemblies)", or empty while the product has its own packaging.</summary>
    public string PackagingLinkedToText
    {
        get => _packagingLinkedToText;
        private set => RaiseAndSetIfChanged(ref _packagingLinkedToText, value);
    }

    /// <summary>The packaging set linked packaging comes from, whose badge opens it on Sub-Assemblies; null for another product's, or none.</summary>
    public int? PackagingLinkedSetId => (LinkTarget as PackagingLinkTarget.Set)?.SubAssemblyId;

    public bool IsPackagingLinkedToSet => PackagingLinkedSetId is not null;

    public bool IsPackagingLinkedToProduct => IsPackagingLinked && !IsPackagingLinkedToSet;

    /// <summary>
    /// What the per-piece packaging links as the form stands, saved or not, and the Packaging tab's banner. A load, Link,
    /// Copy, Make a Copy and Remove Link all come through here, so a Move to Sub-Assemblies waiting for Save ends here too;
    /// an undo puts it back afterwards (<see cref="Restore"/>).
    /// </summary>
    public void ShowLink(PackagingLinkTarget? target)
    {
        LinkTarget = target;
        IsPackagingMoving = false;
        IsPackagingLinked = target is not null;
        PackagingLinkedToText = target?.LinkedToText ?? string.Empty;
        RaisePropertyChanged(nameof(PackagingLinkedSetId));
        RaisePropertyChanged(nameof(IsPackagingLinkedToSet));
        RaisePropertyChanged(nameof(IsPackagingLinkedToProduct));
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
        RaiseCostsChanged();
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

    /// <summary>The tab as one undo step keeps it: what it links, the move to Sub-Assemblies, its lines and its bulk packaging.</summary>
    public PackagingSnapshot Capture() => new()
    {
        Link = LinkTarget,
        IsMoving = IsPackagingMoving,
        Lines = [.. LineEdits.Select(PackagingLineSnapshot.Of)],
        BulkLines = [.. BulkEdits.Select(PackagingLineSnapshot.Of)],
        BulkQuantity = BulkQuantity,
    };

    /// <summary>
    /// Puts the tab back as <paramref name="snapshot"/> had it, for an undo (the screen's RestoreSnapshot, which has taken
    /// the rows picked away first). The link as saved is deliberately left alone: it's what Save compares against. Showing
    /// the link ends a Move to Sub-Assemblies, so the move goes back after it.
    /// </summary>
    public void Restore(PackagingSnapshot snapshot)
    {
        ShowLink(snapshot.Link);
        IsPackagingMoving = snapshot.IsMoving;
        LineEdits.Clear();
        foreach (var line in snapshot.Lines)
        {
            LineEdits.Add(PackagingLineRow.Restore(line, FindMaterial));
        }

        BulkEdits.Clear();
        foreach (var line in snapshot.BulkLines)
        {
            BulkEdits.Add(PackagingLineRow.Restore(line, FindMaterial));
        }

        BulkQuantity = snapshot.BulkQuantity;
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

    public void Dispose() => _pickedSources.Dispose();
}
