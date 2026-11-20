using System.Collections.ObjectModel;
using DynamicData;
using DynamicData.Binding;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Products;

/// <summary>What a product's packaging draft (<see cref="ProductPackagingDraft"/>) asks of its form: the cost basis its rows are priced at.</summary>
public interface IProductPackagingDraftHost
{
    /// <summary>Which of the materials' costs the screen shows, buffered or not: what a row is priced at.</summary>
    CostBasis CostBasis { get; }
}

/// <summary>
/// A product's packaging as its form edits it (ADR 0002): the lines for each piece, typed in or what they link (a packaging
/// set on the Sub-Assemblies screen, or another product's packaging), a Move to Sub-Assemblies waiting for Save, its bulk
/// packaging (a box several units share, with how many it holds), and the packaging last loaded, which Save compares against.
/// It holds the rules that follow an edit (what a link or a move does to the lines and to Save), its undo share
/// (<see cref="Capture"/>, <see cref="Restore"/>), a load's re-pointing at fresh materials (<see cref="ResolveRows"/>, which
/// changes nothing typed, ADR 0007), and what Save writes of it (<see cref="ChangedLink"/>, <see cref="ChangedLines"/>,
/// <see cref="ChangedBulk"/>). It asks nothing and reads nothing: the Packaging tab (<see cref="ProductPackaging"/>) has the
/// commands, the picker, the rows picked and the undo steps, and the form loads and saves it.
/// </summary>
public sealed class ProductPackagingDraft : ObservableState, IDisposable
{
    private readonly IProductPackagingDraftHost _host;
    private readonly IReadOnlyList<RawMaterial> _packagingMaterials;
    private readonly List<IDisposable> _subscriptions = [];

    /// <summary>The key of the link last loaded or saved, or null for none: Save writes the link only when it differs.</summary>
    private int? _savedLinkKey;

    /// <summary>The lines last loaded, so Save only sends them when they changed.</summary>
    private List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> _savedLines = [];

    /// <summary>The bulk lines last loaded, so Save only sends the bulk packaging when it changed.</summary>
    private List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> _savedBulkLines = [];

    /// <summary>How many products a bulk pack held as last loaded.</summary>
    private int? _savedBulkQuantity;

    /// <param name="host">The form: the cost basis the rows are priced at.</param>
    /// <param name="packagingMaterials">The packaging materials as the screen last loaded them, which a row prices itself from.</param>
    public ProductPackagingDraft(IProductPackagingDraftHost host, IReadOnlyList<RawMaterial> packagingMaterials)
    {
        _host = host;
        _packagingMaterials = packagingMaterials;

        // Every row put in a table, however it got there (a product read, an undo, a link or copy), is priced at the
        // screen's costs, buffered or not; the add rows' factories see to a row still being typed.
        _subscriptions.Add(LineEdits.ToObservableChangeSet().OnItemAdded(r => r.CostBasis = host.CostBasis).Subscribe());
        _subscriptions.Add(BulkEdits.ToObservableChangeSet().OnItemAdded(r => r.CostBasis = host.CostBasis).Subscribe());

        // A material picked on a row points it at the screen's copy at once, its units and cost with it: a row the table
        // made has its own lookup, but one shown from a save or the picker doesn't, and would go on pricing (and offering
        // the units of) the material it had until the next load.
        _subscriptions.Add(LineEdits.ToObservableChangeSet().AutoRefresh(r => r.PackagingMaterialId).Subscribe(_ => ResolveRows()));
        _subscriptions.Add(BulkEdits.ToObservableChangeSet().AutoRefresh(r => r.PackagingMaterialId).Subscribe(_ => ResolveRows()));

        // Whatever it holds changing, its fields, its tables or a row typed into: what the form's dirty check follows.
        PropertyChanged += (_, _) => Changed?.Invoke();
        _subscriptions.Add(RowChanges.OnAnyChange(LineEdits, () => Changed?.Invoke()));
        _subscriptions.Add(RowChanges.OnAnyChange(BulkEdits, () => Changed?.Invoke()));
    }

    /// <summary>Anything the draft holds changed: a field, a table, or a row typed into. What the form's dirty check follows.</summary>
    public event Action? Changed;

    /// <summary>The packaging lines for each piece, typed straight into the Packaging tab's table.</summary>
    public ObservableCollection<PackagingLineRow> LineEdits { get; } = [];

    /// <summary>
    /// The bulk packaging's lines, typed into the Packaging tab's Bulk Packaging table: what one bulk pack takes (a box
    /// several units share, its tape), each quantity per pack (PackagingLine.IsBulk). The product's own, whatever its
    /// packaging links.
    /// </summary>
    public ObservableCollection<PackagingLineRow> BulkEdits { get; } = [];

    /// <summary>Every row, its lines and then its bulk lines: what the form prices and resolves.</summary>
    public IEnumerable<PackagingLineRow> AllLineEdits => LineEdits.Concat(BulkEdits);

    /// <summary>A new row for a table's add row: it offers its material's units and prices itself before it's committed.</summary>
    public PackagingLineRow NewRow() => new(FindMaterial) { CostBasis = _host.CostBasis };

    /// <summary>A packaging material as the screen last loaded it, for a row that prices itself.</summary>
    public RawMaterial? FindMaterial(int id) => _packagingMaterials.FirstOrDefault(m => m.Id == id);

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

    /// <summary>How many products a bulk pack holds changed: the product's costs follow (the form's PackagingLinesEdited).</summary>
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

    /// <summary>The cost figures again, after a line or the quantity changed: the packaging per piece's and the bulk packaging's.</summary>
    public void RaiseCostsChanged()
    {
        RaisePropertyChanged(nameof(LinesCost));
        RaisePropertyChanged(nameof(BulkPackCost));
        RaisePropertyChanged(nameof(BulkCostPerUnit));
        RaisePropertyChanged(nameof(BulkCostText));
    }

    /// <summary>
    /// Every row pointed at its material as the screen last loaded it, at its refreshed cost and units, in place: what a load
    /// does to the draft (ADR 0007), which changes nothing typed.
    /// </summary>
    public void ResolveRows() => PackagingLineRow.Resolve(AllLineEdits, _packagingMaterials.ToDictionary(m => m.Id));

    /// <summary>Every row priced at the form's cost basis again: Unbuffered or Buffered was pressed.</summary>
    public void Reprice()
    {
        foreach (var row in AllLineEdits)
        {
            row.CostBasis = _host.CostBasis;
        }
    }

    /// <summary>
    /// What the per-piece packaging links as the form stands, saved or not (Link sets it, and Make a Copy and Remove Link
    /// clear it), or null while the product owns its lines.
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
    /// Move to Sub-Assemblies was pressed: Save puts the product's own lines on the Sub-Assemblies screen as a packaging set,
    /// and links it. They're still typed into until then, and the tab's "Moves to Sub-Assemblies on Save" badge says so.
    /// Whatever changes what the packaging links ends it (<see cref="ShowLink"/>).
    /// </summary>
    public bool IsPackagingMoving
    {
        get => _isPackagingMoving;
        private set
        {
            RaiseAndSetIfChanged(ref _isPackagingMoving, value);
            RaisePropertyChanged(nameof(CanMovePackaging));
        }
    }

    /// <summary>The packaging is the product's own lines, not on their way already: Move to Sub-Assemblies is offered.</summary>
    public bool CanMovePackaging => IsPackagingEditable && !IsPackagingMoving;

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

    /// <summary>Make a Copy: the linked packaging's lines, as they show, become the product's own, and the link goes with Save.</summary>
    public void MakeCopy() => ShowLink(null);

    /// <summary>Remove Link: the link and the lines it shows go, leaving none for each piece; the bulk packaging stays.</summary>
    public void RemoveLink()
    {
        LineEdits.Clear();
        ShowLink(null);
    }

    /// <summary>
    /// Move to Sub-Assemblies: Save puts the product's own lines on the Sub-Assemblies screen as a packaging set named after
    /// the product, and links it (PackagingLinkDraft.NewSet). They're typed straight into the tab rather than through a
    /// form, so they're checked now: a line half filled in, or none at all, is refused (BusinessRuleException), and nothing
    /// changes.
    /// </summary>
    public void StartMoving()
    {
        if (CurrentLines().Count == 0)
        {
            throw new BusinessRuleException("Add the packaging materials one piece takes first: a packaging set needs at least one.");
        }

        IsPackagingMoving = true;
    }

    /// <summary>
    /// The packaging picked on the tab's picker, its lines read: <paramref name="rows"/> in place of the packaging per piece
    /// there was, linked to <paramref name="link"/>, or the product's own when it's null (a copy). The bulk packaging stays.
    /// </summary>
    public void Take(IEnumerable<PackagingLineRow> rows, PackagingLinkTarget? link)
    {
        ShowLines(rows);
        ShowLink(link);
    }

    /// <summary>
    /// What the per-piece packaging links as the form stands, saved or not, and the banner. A load, Link, Copy, Make a Copy
    /// and Remove Link all come through here, so a Move to Sub-Assemblies waiting for Save ends here too; an undo puts it
    /// back afterwards (<see cref="Restore"/>).
    /// </summary>
    public void ShowLink(PackagingLinkTarget? target)
    {
        LinkTarget = target;
        IsPackagingMoving = false;
        IsPackagingLinked = target is not null;
        PackagingLinkedToText = target?.LinkedToText ?? string.Empty;
        RaisePropertyChanged(nameof(LinkTarget));
        RaisePropertyChanged(nameof(PackagingLinkedSetId));
        RaisePropertyChanged(nameof(IsPackagingLinkedToSet));
        RaisePropertyChanged(nameof(IsPackagingLinkedToProduct));
    }

    /// <summary>The saved packaging link, or none: what the tab shows, and what Save compares against.</summary>
    public void LoadLink(PackagingLink? link)
    {
        var target = link is null ? null : PackagingLinkTarget.From(link);
        _savedLinkKey = target?.Key;
        ShowLink(target);
    }

    /// <summary>Whether the form still shows <paramref name="savedLink"/>, the link as saved, so a refresh may read its lines again.</summary>
    public bool ShowsSavedLink(PackagingLinkTarget savedLink) => savedLink.Key == _savedLinkKey && LinkTarget?.Key == savedLink.Key;

    /// <summary>Puts <paramref name="rows"/> in the lines' table, in place of whatever it held.</summary>
    public void ShowLines(IEnumerable<PackagingLineRow> rows)
    {
        LineEdits.Clear();
        LineEdits.AddRange(rows);
    }

    /// <summary>Puts <paramref name="rows"/> in the Bulk Packaging table, in place of whatever it held.</summary>
    public void ShowBulkLines(IEnumerable<PackagingLineRow> rows)
    {
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
    /// The linked packaging's lines as the view's load read them again, while the form shows the link as saved
    /// (<see cref="ShowsSavedLink"/>): shown in place of the rows only when they differ from the lines last loaded. The view's
    /// load merges into every open tab, and a table rebuilt row by row each time, its changes running through every
    /// subscription to it, was a fifth of each tab's merge; the rows are pointed at their refreshed materials either way
    /// (<see cref="ResolveRows"/>). True when they were shown again.
    /// </summary>
    public bool RefreshLinkedLines(IReadOnlyList<PackagingLine> lines)
    {
        var fresh = lines.Select(line =>
        {
            var authoredValue = line.AuthoredField == PackagingLineAuthoredField.QtyPerUnit ? line.QtyPerUnit : line.UnitsPerPackagingUnit;
            var qtyPerUnit = ProductRules.ReconcileReciprocal(line.AuthoredField, authoredValue).QtyPerUnit;
            return (line.PackagingMaterialId, line.UnitId, PackagingLineAuthoredField.QtyPerUnit, qtyPerUnit);
        });
        if (fresh.SequenceEqual(_savedLines))
        {
            return false;
        }

        LoadLines(lines);
        return true;
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
        LineEdits.Clear();
        LoadLink(null);
        _savedLines = [];
        LoadBulk([], null);
    }

    /// <summary>The draft as one undo step keeps it: what it links, the move to Sub-Assemblies, its lines and its bulk packaging.</summary>
    public PackagingSnapshot Capture() => new()
    {
        Link = LinkTarget,
        IsMoving = IsPackagingMoving,
        Lines = [.. LineEdits.Select(PackagingLineSnapshot.Of)],
        BulkLines = [.. BulkEdits.Select(PackagingLineSnapshot.Of)],
        BulkQuantity = BulkQuantity,
    };

    /// <summary>
    /// Puts the draft back as <paramref name="snapshot"/> had it, for an undo (the form's RestoreSnapshot, which has taken the
    /// rows picked away first). The link as saved is deliberately left alone: it's what Save compares against. Showing the
    /// link ends a Move to Sub-Assemblies, so the move goes back after it.
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

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }
}
