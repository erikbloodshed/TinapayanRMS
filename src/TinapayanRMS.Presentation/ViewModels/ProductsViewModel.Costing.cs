using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Pricing.Versions;

namespace TinapayanRMS.Presentation.ViewModels;

// The costs: what reprices the product, the costing defaults, the cost basis, and the printouts.
public sealed partial class ProductsViewModel
{
    /// <summary>
    /// What reprices the product (its packaging, a composite's products, the costing percentages, its price, quantity
    /// and category), and the captions naming its figures.
    /// </summary>
    private void WireCosts()
    {
        // A packaging line picked, typed or deleted in its table: it's priced, and the product's costs follow.
        Packaging.LineEdits.ToObservableChangeSet()
            .AutoRefresh(l => l.PackagingMaterialId)
            .AutoRefresh(l => l.CostPerUnit)
            .Subscribe(_ => _updates.Run(PackagingLinesEdited));

        // The same for a bulk packaging line, and the products a bulk pack holds.
        Packaging.WhenAnyValue(p => p.BulkQuantity).Skip(1).Subscribe(_ => _updates.Run(PackagingLinesEdited));
        Packaging.BulkEdits.ToObservableChangeSet()
            .AutoRefresh(l => l.PackagingMaterialId)
            .AutoRefresh(l => l.CostPerUnit)
            .Subscribe(_ => _updates.Run(PackagingLinesEdited));

        // A composite's line picked, typed or deleted in its Products table: it's priced once its product's recipe cost
        // has been read (a new pick's is read then), and the composite's costs follow.
        //
        // One refresh operator for all four properties, not an AutoRefresh each: each operator takes its own lock, and the
        // subscriber sets the rows' costs (Resolve), so with a chain a cost set on one thread could hold one operator's lock
        // while waiting for the next, and a cost set on another thread the other way round. One operator
        // can't deadlock, whatever thread a cost is set on, and is the simpler way to say it.
        ComponentsTable.ComponentEdits.ToObservableChangeSet()
            .AutoRefreshOnObservable(r => r.WhenAnyPropertyChanged(
                nameof(ProductComponentRow.ComponentProductId),
                nameof(ProductComponentRow.Quantity),
                nameof(ProductComponentRow.RecipeCost),
                nameof(ProductComponentRow.PackagingCost)))
            .Subscribe(_ => _updates.Run(ComponentsEdited));

        Costing.WhenAnyValue(
                c => c.EditingLaborPercent,
                c => c.EditingOverheadPercent,
                c => c.EditingAdministrativePercent,
                c => c.EditingMarginPercent)
            .Subscribe(_ => _updates.Run(RecomputeCosts));

        // The category too: it can set the Wastage % the product is priced with.
        this.WhenAnyValue(x => x.EditingCurrentPrice, x => x.EditingQuantityRequired, x => x.EditingCategory)
            .Subscribe(_ => _updates.Run(RecomputeCosts));

        // A composite's products' packaging joining its own, or leaving it.
        this.WhenAnyValue(x => x.EditingIncludesComponentPackaging)
            .Subscribe(_ => _updates.Run(RecomputeCosts));

        // The caption on every figure names what it's for, so it follows the quantity, and a composite's is always one unit.
        this.WhenAnyValue(x => x.EditingQuantityRequired, x => x.IsEditingComposite)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(CostUnitCaption));
                this.RaisePropertyChanged(nameof(SuggestedSellingPriceLabel));
                this.RaisePropertyChanged(nameof(MaterialCostTooltip));
                this.RaisePropertyChanged(nameof(MetricBaseTooltip));
            });
    }

    /// <summary>Print, and the cost basis's toggle.</summary>
    [MemberNotNull(nameof(PrintCommand), nameof(ToggleCostBasisCommand))]
    private void WireCostingCommands()
    {
        // Print shows the product as the screen does, unsaved edits and all. A form left
        // open is part of that, as it's part of what Save writes, so it's applied first; a rule refusing it shows its
        // message, and the Print tab doesn't open. It stays enabled while a picked product's details are on their way,
        // and does nothing if clicked then: disabled for that moment, it flickered on every pick. (Save is off then
        // anyway, with nothing unsaved, so it doesn't flicker.)
        PrintCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(() =>
            {
                if (!AreDetailsReady)
                {
                    return;
                }

                // On the History tab it prints the version picked there (PreparePrintout), so the form isn't applied: its
                // pages aren't the form's.
                if (SelectedDetailsTab == HistoryTab)
                {
                    if (VersionHistory.SelectedRow is null)
                    {
                        ErrorMessage = "Pick a version in the History tab's table to print it.";
                        return;
                    }
                }
                else
                {
                    ApplyOpenForm();
                }

                _printRequests.OnNext(new PrintScope.ThisProduct());
            }));

        ToggleCostBasisCommand = ReactiveCommand.Create(ToggleCostBasis);
    }

    /// <summary>
    /// Applies Settings' costing defaults (labor, overhead, administrative and markup) to <paramref name="products"/>, once
    /// it's confirmed. The product the form shows, when it's one of them, has its unsaved changes saved or thrown away
    /// first, and shows its new costing after.
    /// </summary>
    private async Task ApplyCostingDefaultsAsync(IReadOnlyList<Product> products)
    {
        if (products.Count == 0 || (products.Any(p => p.Id == _detailProductId) && !await ResolveUnsavedChangesAsync(revert: true)))
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            var defaults = await Task.Run(() => _costingDefaultsRepository.GetAsync(CancellationToken.None));
            var question = $"Set the costing of {ProductNames(products)} to the costing defaults: labor {defaults.LaborPercent:0.##}%, overhead "
                + $"{defaults.OverheadPercent:0.##}%, administrative {defaults.AdministrativePercent:0.##}% and markup {defaults.MarginPercent:0.##}%?"
                + "\n\nA version is recorded of each one this changes.";
            if (!await ConfirmDelete.Handle(question))
            {
                return;
            }

            var costing = new CostingParameters
            {
                LaborPercent = defaults.LaborPercent,
                OverheadPercent = defaults.OverheadPercent,
                AdministrativePercent = defaults.AdministrativePercent,
                MarginPercent = defaults.MarginPercent,
            };
            var changed = await Task.Run(() => _versionedSaves.ApplyCostingAsync(
                [.. products.Select(p => p.Id)], costing, DateTime.Now, CancellationToken.None));
            await LoadCommand.Execute();

            // The product the form shows reads its new costing, as after its own Save.
            if (_detailProductId is { } openId && products.Any(p => p.Id == openId)
                && await Task.Run(() => _productRepository.GetByIdAsync(openId, CancellationToken.None)) is { } open)
            {
                var detail = await Task.Run(() => _savedProducts.ReadDetailAsync(openId, CancellationToken.None));
                History.Clear();
                ShowSavedProduct(open, detail);
                if (SelectedDetailsTab == HistoryTab)
                {
                    RequestVersions(force: true);
                }
                else
                {
                    VersionHistory.Clear();
                }
            }

            SuccessMessage = changed.Count == 0
                ? $"{ProductNames(products)} already {(products.Count == 1 ? "has" : "have")} the costing defaults."
                : $"The costing defaults were applied to {ProductNames(changed)}, with a version of each.";
        });

        static string ProductNames<T>(IReadOnlyList<T> items) where T : notnull
        {
            List<string> names = [.. items.Select(i => i is Product p ? p.Name : i.ToString()!)];
            return names.Count switch
            {
                1 => names[0],
                <= 5 => $"{string.Join(", ", names[..^1])} and {names[^1]}",
                _ => $"{string.Join(", ", names.Take(4))} and {names.Count - 4} more",
            };
        }
    }

    /// <summary>
    /// Takes up the defaults as LoadCommand read them. A new product still showing the ones it started with (the screen's
    /// first load, or defaults changed in Settings since) takes the new ones; one whose costing was typed into keeps it.
    /// A new product started by New goes in the new stage whatever was typed, since its stage was never picked.
    /// </summary>
    private void RefreshCostingDefaults(CostingDefaults defaults)
    {
        var untouchedNewProduct = _editingId == 0 && Costing.ShowsDefaults;
        var shopFiguresChanged = Costing.SetDefaults(defaults);
        _defaultNewProductStage = defaults.NewProductStage;
        if (shopFiguresChanged)
        {
            // Every product is priced with the shop's wastage, VAT and price rounding, the one open included: its Costing
            // tab follows at once.
            RecomputeCosts();
        }
        if (_editingId == 0 && _newProductInDefaultStage)
        {
            NewProductStage = _defaultNewProductStage;
        }
        if (untouchedNewProduct)
        {
            Costing.ApplyDefaults();

            // The defaults landing on a form that was still showing the previous ones isn't the user's change -
            // on the screen's first load they land on a blank form costed at nothing - so what Save measures
            // against follows them. Only the costing moves: a product whose costing was typed into isn't here.
            History.Rebase((settled, now) => settled with { Costing = now.Costing });
        }
    }

    /// <summary><paramref name="lines"/> with each ingredient swapped for its refreshed copy when that differs; unchanged lines are kept as they are.</summary>
    private static IReadOnlyList<SectionLine> Repriced(IReadOnlyList<SectionLine> lines, Dictionary<int, RawMaterial> ingredientsById) =>
        [.. lines.Select(l => ingredientsById.TryGetValue(l.Ingredient.Id, out var fresh) && !CollectionMerge.HasSameValues(fresh, l.Ingredient)
            ? new SectionLine(fresh, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)
            : l)];

    /// <summary>
    /// Unbuffered or Buffered, pressed: every figure moves to the other of the materials' costs. The lines tables' rows are priced
    /// again, the composite's products from what's already been read of them, and the sections from the costs they
    /// already hold, so nothing is read again, and nothing about the product changes.
    /// </summary>
    private void ToggleCostBasis()
    {
        CostBasis = CostBasis == CostBasis.Buffered ? CostBasis.Unbuffered : CostBasis.Buffered;
        this.RaisePropertyChanged(nameof(MaterialCostTooltip));
        foreach (var row in UsageForm.FormLines.Concat(UsageForm.FormAddIns))
        {
            row.CostBasis = CostBasis;
        }

        Packaging.Reprice();
        ComponentsTable.Reprice();

        RecomputeCosts();
        UsageForm.Recompute();
        VersionHistory.Reprice(CostBasis);
    }

    /// <summary>The cost summary, from the recipe and packaging as they are now and the costing fields.</summary>
    private void RecomputeCosts()
    {
        // A composite's recipe cost is what its products bring, each line its quantity at the product's recipe cost (a
        // line still being filled in brings nothing yet), then its own sections, each per whole composite.
        List<ProductComponentRow> lines = IsEditingComposite
            ? [.. ComponentsTable.ComponentEdits.Where(r => r.Product is not null && r.Quantity > 0)]
            : [];
        List<(string Name, decimal Cost)> components = [.. lines.Select(r => (r.LineName, r.RecipeCostTotal))];

        // The Products table's figure adds up its Cost column: the products' packaging is in it while the box is ticked.
        ComponentsTable.ComponentsCost = lines.Sum(r => r.Cost);

        // With the box ticked, each product's own packaging for one sellable unit, times its quantity, joins the
        // composite's packaging, not its recipe cost (a product whose costs haven't been read yet brings none until they are).
        decimal? componentPackaging = IsEditingComposite && EditingIncludesComponentPackaging
            ? lines.Sum(r => r.PackagingCostTotal)
            : null;
        Costing.Recompute(new ProductCostInputs
        {
            Parts = [.. components, .. UsageEdits.Select(u => (u.SectionName, u.CostPerPieceAt(CostBasis)))],
            QuantityRequired = EditingQuantityRequired,
            PackagingCosts = [.. Packaging.LineEdits.Select(l => l.CostPerUnit), .. Packaging.BulkEdits.Count > 0 ? [Packaging.BulkCostPerUnit] : Array.Empty<decimal>()],
            ComponentPackaging = componentPackaging,
            CurrentPrice = EditingCurrentPrice,
            IsComposite = IsEditingComposite,
            HasOwnSections = IsEditingComposite && UsageEdits.Count > 0,
        },
        EditingCategory?.WastagePercent);

        List<string> belowCost =
        [
            .. UsageEdits.SelectMany(u => u.Lines.Concat(u.AddIns)).Select(l => l.Ingredient)
                .Concat(Packaging.AllLineEdits.Select(l => l.PackagingMaterial).OfType<RawMaterial>())
                .Where(RawMaterialRules.IsBufferedBelowCost)
                .Select(m => m.Name)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase),
        ];
        BelowCostText = belowCost.Count switch
        {
            0 => null,
            1 => $"Prices have risen past the buffered cost of {belowCost[0]}, so this product is priced under what it costs. "
                + "Re-apply its target buffer on Raw Materials, or in Reports' Cost Buffers.",
            _ => $"Prices have risen past the buffered costs of {string.Join(", ", belowCost[..^1])} and {belowCost[^1]}, so this "
                + "product is priced under what they cost. Re-apply their target buffers on Raw Materials, or in Reports' Cost Buffers.",
        };
    }

    /// <summary>
    /// The product the screen has open, as the Print tab shows it: asked for each time that tab is shown, so it follows
    /// the edits made here since, and the product opened here since. A sub-assembly form
    /// left open is applied first, as Print applies it. Empty while the details are still arriving, and a
    /// <see cref="PrintProblem"/> when a rule refuses the form, with its message (shown on this screen too, for when it's
    /// next shown): the tab then keeps what it had.
    /// </summary>
    public PreparedPrintout PreparePrintout()
    {
        if (!AreDetailsReady)
        {
            return default;
        }

        // With the History tab showing, the pages are the version picked there.
        if (SelectedDetailsTab == HistoryTab)
        {
            return VersionHistory.SelectedRow is { } row && SelectedProduct is { } product
                ? VersionHistory.PrintoutOf(row, product)
                : default(PreparedPrintout);
        }

        try
        {
            ApplyOpenForm();
        }
        catch (BusinessRuleException ex)
        {
            ErrorMessage = ex.Message;
            return new PrintProblem(ex.Message);
        }

        return CurrentPrintout();
    }

    /// <summary>
    /// What Print puts on paper: the product as the screen shows it now, its details, every section in recipe order, its
    /// packaging and its costing, with a note when that isn't what's saved.
    /// </summary>
    private ProductPrintout CurrentPrintout() => new(
        string.IsNullOrWhiteSpace(EditingName) ? (IsEditingComposite ? "New composite product" : "New product") : EditingName.Trim(),
        EditingCategory?.Name ?? "Uncategorized",
        ProductRules.StageTitle(SelectedProduct?.Stage ?? NewProductStage),
        CostUnitCaption,
        LastUpdatedText,
        _detailProductId is null
            ? ProductPrintout.NotSavedNote
            : History.IsDirty ? ProductPrintout.UnsavedChangesNote : null,
        DateTime.Now,
        [.. UsageEdits.Select(u => PrintedSection.From(u.ToPriced(), CostBasis))],
        Packaging.LineEdits.Count > 0 || Packaging.BulkEdits.Count > 0
            ? PrintedPackaging.From(
                Packaging.LineEdits.Select(l => l.ToPriced()),
                Packaging.IsPackagingLinked ? Packaging.PackagingLinkedToText : null,
                Packaging.BulkEdits.Count == 0 ? null : new PricedBulkPackaging([.. Packaging.BulkEdits.Select(l => l.ToPriced())], Packaging.BulkQuantity))
            : null,
        Costing.Costs,
        IsEditingComposite ? PrintedComponent.From(ComponentsTable.ComponentEdits) : null)
    {
        CostBasis = CostBasis,
    };
}
