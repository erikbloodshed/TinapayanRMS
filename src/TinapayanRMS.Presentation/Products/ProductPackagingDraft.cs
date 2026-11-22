using System.Collections.ObjectModel;
using DynamicData;
using System.Reactive.Linq;
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
/// packaging (a box several units share, typed in or a packaging set it links, with how many it holds), and the packaging
/// last loaded, which Save compares against.
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

    /// <summary>The key of the packaging set the bulk packaging linked as last loaded, or null for none.</summary>
    private int? _savedBulkLinkKey;

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

        // A material picked on a row points that row at the screen's copy at once, its units and cost with it: a row the
        // table made has its own lookup, but one shown from a save or the picker doesn't, and would go on pricing (and
        // offering the units of) the material it had. Only a pick: a row added or taken out, a load's or an undo's, is the
        // form's to resolve (PackagingLinesEdited), as it always was.
        _subscriptions.Add(LineEdits.ToObservableChangeSet().MergeMany(MaterialPicks).Subscribe(ResolveRow));
        _subscriptions.Add(BulkEdits.ToObservableChangeSet().MergeMany(MaterialPicks).Subscribe(ResolveRow));

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
    /// several units share, its tape), each quantity per pack (PackagingLine.IsBulk), whatever the packaging per piece links;
    /// read-only while the bulk packaging links a set (<see cref="BulkLinkTarget"/>), whose lines they are.
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
        BulkEdits.Count == 0 ? "No bulk packaging. Add what one bulk pack takes, such as a box several products share, or Link a packaging set."
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

    /// <summary>A row's material picked, after it joined its table: not the one it had as it joined.</summary>
    private static IObservable<PackagingLineRow> MaterialPicks(PackagingLineRow row) =>
        row.WhenPropertyChanged(r => r.PackagingMaterialId, notifyOnInitialValue: false).Select(change => change.Sender);

    /// <summary><paramref name="row"/> pointed at the material picked on it, as the screen last loaded it.</summary>
    private void ResolveRow(PackagingLineRow row) =>
        PackagingLineRow.Resolve(
            [row],
            row.PackagingMaterialId is { } id && FindMaterial(id) is { } material ? new Dictionary<int, RawMaterial> { [id] = material } : []);

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

    /// <summary>
    /// The packaging set the bulk packaging links as the form stands, saved or not (Link sets it, and Make a Copy and Remove
    /// Link clear it), or null while its lines are the product's own. How many products a pack holds is the product's either way.
    /// </summary>
    public PackagingLinkTarget? BulkLinkTarget { get; private set; }

    /// <summary>The bulk packaging links a packaging set, so its table shows the set's lines, read-only.</summary>
    public bool IsBulkLinked => BulkLinkTarget is not null;

    /// <summary>The bulk packaging's lines are the product's own, so its table can be typed into.</summary>
    public bool IsBulkEditable => !IsBulkLinked;

    /// <summary>"Linked to Box of 6 (Sub-Assemblies)", or empty while the bulk packaging is the product's own.</summary>
    public string BulkLinkedToText => BulkLinkTarget?.LinkedToText ?? string.Empty;

    /// <summary>The packaging set the bulk packaging links, whose badge opens it on Sub-Assemblies; null for none.</summary>
    public int? BulkLinkedSetId => (BulkLinkTarget as PackagingLinkTarget.Set)?.SubAssemblyId;

    /// <summary>What the bulk packaging links as the form stands, saved or not: a load, Link, Make a Copy and Remove Link come through here.</summary>
    public void ShowBulkLink(PackagingLinkTarget? target)
    {
        BulkLinkTarget = target;
        RaisePropertyChanged(nameof(BulkLinkTarget));
        RaisePropertyChanged(nameof(IsBulkLinked));
        RaisePropertyChanged(nameof(IsBulkEditable));
        RaisePropertyChanged(nameof(BulkLinkedToText));
        RaisePropertyChanged(nameof(BulkLinkedSetId));
    }

    /// <summary>Make a Copy on linked bulk packaging: the set's lines, as they show, become the product's own; the link goes with Save.</summary>
    public void MakeBulkCopy() => ShowBulkLink(null);

    /// <summary>Remove Link on linked bulk packaging: the link and the lines it shows go, leaving no bulk packaging.</summary>
    public void RemoveBulkLink()
    {
        ShowBulkLines([]);
        ShowBulkLink(null);
    }

    /// <summary>The packaging set picked on the bulk packaging's picker, its lines read: <paramref name="rows"/> in place of its lines, linked to <paramref name="link"/>.</summary>
    public void TakeBulk(IEnumerable<PackagingLineRow> rows, PackagingLinkTarget link)
    {
        ShowBulkLines(rows);
        ShowBulkLink(link);
    }

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

    /// <summary>
    /// Of the saved links, <paramref name="link"/> of the packaging per piece and <paramref name="bulkLink"/> of the bulk
    /// packaging, those the form still shows as saved, so a refresh may read their lines again; null for each it doesn't.
    /// </summary>
    public (PackagingLinkTarget? Link, PackagingLinkTarget? BulkLink) SavedLinksShowing(PackagingLink? link, PackagingLink? bulkLink)
    {
        var savedLink = link is null ? null : PackagingLinkTarget.From(link);
        var savedBulkLink = bulkLink is null ? null : PackagingLinkTarget.From(bulkLink);
        return (
            savedLink is not null && savedLink.Key == _savedLinkKey && LinkTarget?.Key == savedLink.Key ? savedLink : null,
            savedBulkLink is not null && savedBulkLink.Key == _savedBulkLinkKey && BulkLinkTarget?.Key == savedBulkLink.Key ? savedBulkLink : null);
    }

    /// <summary>
    /// What linked packaging shows as the view's load read it again, for each link the form showed as saved
    /// (<see cref="SavedLinksShowing"/>): the link under its name now, and its lines in place of the rows when they differ
    /// from those last loaded. True when anything showed differently, so the undo steps take it in too (<see cref="AsLinkedNow"/>).
    /// </summary>
    public bool RefreshLinked(PackagingLinkTarget? savedLink, IReadOnlyList<PackagingLine> lines, PackagingLinkTarget? savedBulkLink, IReadOnlyList<PackagingLine> bulkLines)
    {
        // A renamed product or set reads right in the banner too.
        var changed = false;
        if (savedLink is not null)
        {
            changed |= !Equals(LinkTarget, savedLink);
            ShowLink(savedLink);
            changed |= RefreshLinkedLines(lines);
        }

        if (savedBulkLink is not null)
        {
            changed |= !Equals(BulkLinkTarget, savedBulkLink);
            ShowBulkLink(savedBulkLink);
            if (!AsTyped(bulkLines).SequenceEqual(_savedBulkLines))
            {
                ShowBulkLines(bulkLines.Select(PackagingLineRow.FromSaved));
                _savedBulkLines = CurrentBulkLines();
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// An undo step's packaging with what its links show now (<paramref name="now"/>, as <see cref="RefreshLinked"/> left the
    /// draft): each part linking what the form links now takes the lines it shows now, so undoing something typed before a
    /// set was saved on the Sub-Assemblies screen never puts back the set's lines as they were.
    /// </summary>
    public static PackagingSnapshot AsLinkedNow(PackagingSnapshot step, PackagingSnapshot now)
    {
        var piece = now.Link is { } link && step.Link?.Key == link.Key && !step.IsMoving;
        var bulk = now.BulkLink is { } bulkLink && step.BulkLink?.Key == bulkLink.Key;
        return step with
        {
            Link = piece ? now.Link : step.Link,
            Lines = piece ? now.Lines : step.Lines,
            BulkLink = bulk ? now.BulkLink : step.BulkLink,
            BulkLines = bulk ? now.BulkLines : step.BulkLines,
        };
    }

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
    /// (<see cref="SavedLinksShowing"/>): shown in place of the rows only when they differ from the lines last loaded. The view's
    /// load merges into every open tab, and a table rebuilt row by row each time, its changes running through every
    /// subscription to it, was a fifth of each tab's merge; the rows are pointed at their refreshed materials either way
    /// (<see cref="ResolveRows"/>). True when they were shown again.
    /// </summary>
    public bool RefreshLinkedLines(IReadOnlyList<PackagingLine> lines)
    {
        if (AsTyped(lines).SequenceEqual(_savedLines))
        {
            return false;
        }

        LoadLines(lines);
        return true;
    }

    /// <summary>Saved lines as the rows they show would be read back (<see cref="CurrentLines"/>): each quantity per unit, in its unit.</summary>
    private static IEnumerable<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> AsTyped(IEnumerable<PackagingLine> lines) =>
        lines.Select(line =>
        {
            var authoredValue = line.AuthoredField == PackagingLineAuthoredField.QtyPerUnit ? line.QtyPerUnit : line.UnitsPerPackagingUnit;
            var qtyPerUnit = ProductRules.ReconcileReciprocal(line.AuthoredField, authoredValue).QtyPerUnit;
            return (line.PackagingMaterialId, line.UnitId, PackagingLineAuthoredField.QtyPerUnit, qtyPerUnit);
        });

    /// <summary>
    /// Shows the bulk packaging as saved, its lines for one pack (the set's, when it links <paramref name="link"/>) and how
    /// many products it holds, and makes it what Save compares against.
    /// </summary>
    public void LoadBulk(IReadOnlyList<PackagingLine> lines, int? quantity, PackagingLink? link = null)
    {
        var target = link is null ? null : PackagingLinkTarget.From(link);
        _savedBulkLinkKey = target?.Key;
        ShowBulkLink(target);
        ShowBulkLines(lines.Select(PackagingLineRow.FromSaved));
        BulkQuantity = lines.Count > 0 || target is not null ? quantity : null;
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
        BulkLink = BulkLinkTarget,
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

        ShowBulkLink(snapshot.BulkLink);
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
    /// The bulk packaging, its lines or the set it links and how many products a pack holds, when any differs from what was
    /// last loaded, else null. A linked one sends no lines, since they're the set's. Lines or a set need the quantity
    /// (refused at Save, ProductRules.EnsureValidBulkPackaging); neither saves none. Bulk packaging that was linked when
    /// loaded and is the product's own now (Make a Copy) is written even when its lines match what the link showed.
    /// </summary>
    public BulkPackagingDraft? ChangedBulk()
    {
        var setId = BulkLinkedSetId;
        var lines = setId is null ? CurrentBulkLines() : [];
        var quantity = lines.Count > 0 || setId is not null ? BulkQuantity : null;
        var unchanged = BulkLinkTarget?.Key == _savedBulkLinkKey
            && (setId is not null || lines.SequenceEqual(_savedBulkLines))
            && quantity == _savedBulkQuantity;
        return unchanged ? null : new BulkPackagingDraft(lines, quantity, setId);
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
