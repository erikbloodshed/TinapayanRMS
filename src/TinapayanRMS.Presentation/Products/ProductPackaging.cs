using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.UseCases.Products;

namespace TinapayanRMS.Presentation.Products;

/// <summary>How the Packaging tab's picker takes the packaging picked: as a link, or as a copy of its lines.</summary>
public enum PackagingSourceMode
{
    Link,
    Copy,
}

/// <summary>
/// What the Packaging tab (<see cref="ProductPackaging"/>) asks of the Products screen, besides its runners, messages and
/// undo steps: the other products, whose packaging Copy offers, and the product's costs worked out again. The cost basis
/// its rows are priced at is what its draft asks (<see cref="IProductPackagingDraftHost"/>). The screen implements it
/// explicitly, as Raw Materials does IBrandPriceRowHost.
/// </summary>
public interface IProductPackagingHost : IScreenPartHost, IUndoRecorder
{
    /// <summary>Every product but the one showing, whose packaging Copy offers.</summary>
    IEnumerable<Product> OtherProducts { get; }

    /// <summary>The packaging changed in one go: the product's costs are worked out again as they're next read.</summary>
    void InvalidateCosts();
}

/// <summary>
/// The Products screen's Packaging tab, always the Recipe Structure's last (PackagingSection, through ProductForm.Packaging):
/// its commands, each one undo step of the screen's form (<see cref="IProductPackagingHost"/>), the picker its Link and Copy
/// open, and the rows picked in its tables. The packaging itself, what it links, its lines and bulk packaging, the rules
/// that follow an edit and what Save writes, is its draft's (<see cref="Draft"/>), whose fields the tab passes on under the
/// names the page binds; the screen keeps the undo history, the costs these lines add to, and Save.
/// </summary>
public sealed class ProductPackaging : ObservableState, IDisposable
{
    private readonly IProductPackagingHost _host;
    private readonly IProductQueries _queries;

    /// <summary>The standalone packaging sets (sub-assemblies in the Packaging role), by name, as last loaded, for Link and Copy.</summary>
    private IReadOnlyList<SubAssembly> _packagingSets = [];

    /// <summary>Each packaging picked to link or copy, nothing picked first: what loads its lines (PickedSource).</summary>
    private readonly BehaviorSubject<LinkableSubAssemblyOption?> _pickedSources = new(null);

    /// <param name="host">The Products screen: its runners and messages, its undo steps, and the other products.</param>
    /// <param name="draft">The packaging the tab edits, which the form loads and saves.</param>
    /// <param name="queries">What reads the lines of the packaging picked, a set's or another product's.</param>
    public ProductPackaging(IProductPackagingHost host, ProductPackagingDraft draft, IProductQueries queries)
    {
        _host = host;
        _queries = queries;
        Draft = draft;

        // The draft's fields are the tab's to the page, under the same names; a row picked that leaves its table, however it
        // went (removed, the table shown afresh, an undo), is picked no longer.
        draft.PropertyChanged += DraftChanged;
        draft.LineEdits.CollectionChanged += LinesChanged;
        draft.BulkEdits.CollectionChanged += BulkLinesChanged;

        // The linked packaging's lines, as they show, become the product's own; Save drops the link and writes them.
        MakeCopyCommand = ScreenCommand.Of(() => host.AsUndoStep(draft.MakeCopy), () => IsPackagingLinked);

        // The link and the lines it shows go, leaving none for each piece; Save drops the link. The bulk packaging stays.
        RemoveLinkCommand = ScreenCommand.Of(
            () => host.AsUndoStep(() =>
            {
                draft.RemoveLink();
                host.InvalidateCosts();
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

        // The product's own packaging lines go to the Sub-Assemblies screen as a packaging set on Save, which it links. A
        // line half filled in, or none at all, is refused with the message, as a recipe section's form a rule refuses is.
        // They're still typed into until Save.
        MoveToSubAssembliesCommand = ScreenCommand.Of(
            () => host.AsUndoStep(() =>
            {
                host.ShowError(null);
                try
                {
                    draft.StartMoving();
                }
                catch (BusinessRuleException ex)
                {
                    host.ShowError(ex.Message);
                }
            }),
            () => CanMovePackaging);

        RemoveLineCommand = ScreenCommand.Of(
            () => host.AsUndoStep(() => draft.LineEdits.Remove(SelectedLineEdit!)),
            () => SelectedLineEdit is not null && !IsPackagingLinked);

        RemoveBulkLineCommand = ScreenCommand.Of(
            () => host.AsUndoStep(() => draft.BulkEdits.Remove(SelectedBulkEdit!)),
            () => SelectedBulkEdit is not null);
    }

    /// <summary>The packaging the tab edits: what it links, its lines and bulk packaging, and what Save writes of them.</summary>
    public ProductPackagingDraft Draft { get; }

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

    /// <summary>The packaging lines, typed straight into the tab's table (the draft's).</summary>
    public ObservableCollection<PackagingLineRow> LineEdits => Draft.LineEdits;

    /// <summary>The line the packaging table's add row edits: it offers its material's units and prices itself before it's committed.</summary>
    public Func<PackagingLineRow> NewLineEdit => Draft.NewRow;

    private PackagingLineRow? _selectedLineEdit;
    public PackagingLineRow? SelectedLineEdit
    {
        get => _selectedLineEdit;
        set => RaiseAndSetIfChanged(ref _selectedLineEdit, value);
    }

    /// <summary>The bulk packaging's lines, typed into the tab's Bulk Packaging table (the draft's).</summary>
    public ObservableCollection<PackagingLineRow> BulkEdits => Draft.BulkEdits;

    /// <summary>The line the Bulk Packaging table's add row edits, as <see cref="NewLineEdit"/> is for the lines.</summary>
    public Func<PackagingLineRow> NewBulkEdit => Draft.NewRow;

    private PackagingLineRow? _selectedBulkEdit;
    public PackagingLineRow? SelectedBulkEdit
    {
        get => _selectedBulkEdit;
        set => RaiseAndSetIfChanged(ref _selectedBulkEdit, value);
    }

    /// <summary>How many products one bulk pack holds, typed beside its table (the draft's).</summary>
    public int? BulkQuantity
    {
        get => Draft.BulkQuantity;
        set => Draft.BulkQuantity = value;
    }

    /// <summary>What the packaging per piece costs one sellable unit, its lines alone: the figure beside its heading.</summary>
    public decimal LinesCost => Draft.LinesCost;

    /// <summary>What one bulk pack's lines cost together, at the screen's cost basis.</summary>
    public decimal BulkPackCost => Draft.BulkPackCost;

    /// <summary>The Bulk Packaging table's caption (the draft's BulkCostText).</summary>
    public string BulkCostText => Draft.BulkCostText;

    public bool IsPackagingLinked => Draft.IsPackagingLinked;

    /// <summary>The packaging is the product's own, so its table can be typed into; linked packaging is kept where it's from.</summary>
    public bool IsPackagingEditable => Draft.IsPackagingEditable;

    /// <summary>Move to Sub-Assemblies was pressed: Save makes the lines a packaging set, and links it.</summary>
    public bool IsPackagingMoving => Draft.IsPackagingMoving;

    /// <summary>The packaging is the product's own lines, not on their way already: the tab offers Move to Sub-Assemblies.</summary>
    public bool CanMovePackaging => Draft.CanMovePackaging;

    /// <summary>"Linked to Cream Loaf" or "Linked to Gift Box Set (Sub-Assemblies)", or empty while the product has its own packaging.</summary>
    public string PackagingLinkedToText => Draft.PackagingLinkedToText;

    /// <summary>The packaging set linked packaging comes from, whose badge opens it on Sub-Assemblies; null for another product's, or none.</summary>
    public int? PackagingLinkedSetId => Draft.PackagingLinkedSetId;

    public bool IsPackagingLinkedToSet => Draft.IsPackagingLinkedToSet;

    public bool IsPackagingLinkedToProduct => Draft.IsPackagingLinkedToProduct;

    /// <summary>The packaging sets as LoadCommand read them: what Link offers, and Copy before the other products.</summary>
    public void TakePackagingSets(IReadOnlyList<SubAssembly> packagingSets) => _packagingSets = packagingSets;

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
    /// <summary>The packaging picked to link or copy (<see cref="PickSource"/>), or none while the picker opens or closes.</summary>
    public LinkableSubAssemblyOption? PickedSource
    {
        get => _pickedSource;
        private set => RaiseAndSetIfChanged(ref _pickedSource, value);
    }

    /// <summary>The user picks the packaging to link or copy: its lines load, and then fill the table (ApplySourceAsync).</summary>
    public void PickSource(LinkableSubAssemblyOption? source)
    {
        if (source != PickedSource)
        {
            PickedSource = source;
            _pickedSources.OnNext(source);
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

    /// <summary>No packaging and none saved, the picker closed: a new product's, or the details cleared for another's.</summary>
    public void Clear()
    {
        EndPickingSource();
        Draft.Clear();
    }

    /// <summary>
    /// Link: the picker offers the Sub-Assemblies screen's packaging sets, less the one linked now. With none, it says how
    /// to make one. Another product's packaging is only copied, never linked.
    /// </summary>
    private void StartLinking()
    {
        var linkedKey = Draft.LinkTarget?.Key;
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
            PackagingLinkTarget.Set(var setId, _) => [.. (await _queries.ReadPackagingSetLinesAsync(setId, cancellationToken))
                .Select(l => new PackagingLineRow(Draft.FindMaterial(l.PackagingMaterialId) ?? l.PackagingMaterial!, l.QtyPerSet, l.UnitId))],
            PackagingLinkTarget.OtherProduct(var productId, _) =>
                [.. (await _queries.ReadEffectivePackagingAsync(productId, cancellationToken)).Select(PackagingLineRow.FromSaved)],
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
            Draft.Take(rows, linking ? target : null);
            _host.InvalidateCosts();
        });
    }

    private void DraftChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RaisePropertyChanged(e.PropertyName);

    private void LinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (SelectedLineEdit is { } picked && !Draft.LineEdits.Contains(picked))
        {
            SelectedLineEdit = null;
        }
    }

    private void BulkLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (SelectedBulkEdit is { } picked && !Draft.BulkEdits.Contains(picked))
        {
            SelectedBulkEdit = null;
        }
    }

    public void Dispose()
    {
        Draft.PropertyChanged -= DraftChanged;
        Draft.LineEdits.CollectionChanged -= LinesChanged;
        Draft.BulkEdits.CollectionChanged -= BulkLinesChanged;
        _pickedSources.Dispose();
    }
}
