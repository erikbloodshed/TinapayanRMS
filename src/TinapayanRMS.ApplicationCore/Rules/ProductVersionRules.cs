using System.Globalization;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// A product's version history (ProductVersion): what makes a new version, and what changed from one version to another.
/// The two are one rule: a Save records a version exactly when <see cref="Changes"/> finds something to say, so a version
/// never shows up with nothing listed as changed.
/// </summary>
public static class ProductVersionRules
{
    /// <summary>The longest note a version takes.</summary>
    public const int MaxNoteLength = 200;

    /// <summary>The note on the version a product's first Save records of how it stood before versions were kept.</summary>
    public const string BaselineNote = "Before version history began";

    /// <summary>A note as it's stored: trimmed, and null when blank. Refused when longer than <see cref="MaxNoteLength"/>.</summary>
    public static string? NormalizeNote(string? note)
    {
        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > MaxNoteLength
            ? throw new BusinessRuleException($"A version's note can be at most {MaxNoteLength} characters.")
            : trimmed;
    }

    /// <summary>
    /// Whether two versions hold the same recipe and price: nothing <see cref="Changes"/> would list. Materials' costs
    /// aren't compared, nor are the product's name, a sub-assembly's id (a section of the product's own rewritten as a new
    /// one is the same section), or the order of lines.
    /// </summary>
    public static bool IsSameRecipe(ProductVersionContent a, ProductVersionContent b) => Changes(a, b).Count == 0;

    /// <summary>
    /// What changed from <paramref name="older"/> to <paramref name="newer"/>, one line each, in the order the Products screen
    /// shows them: the product's own figures, then its recipe section by section, its products, its packaging, and its
    /// costing. E.g. "Dough: Bread Flour 500 g → 450 g", "Packaging: Paper Bag added, 1 pc", "Markup %: 30 → 35".
    /// </summary>
    public static IReadOnlyList<string> Changes(ProductVersionContent older, ProductVersionContent newer)
    {
        List<string> changes = [];
        var names = new MaterialNames(older, newer);

        if (older.QuantityRequired != newer.QuantityRequired)
        {
            changes.Add($"Quantity Required: {older.QuantityRequired} → {newer.QuantityRequired}");
        }

        switch (older.CurrentPrice, newer.CurrentPrice)
        {
            case (null, { } set):
                changes.Add($"Current Price: set to {Money(set)}");
                break;
            case ({ } was, null):
                changes.Add($"Current Price: cleared (was {Money(was)})");
                break;
            case ({ } was, { } now) when was != now:
                changes.Add($"Current Price: {Money(was)} → {Money(now)}");
                break;
        }

        if (older.IncludesComponentPackaging != newer.IncludesComponentPackaging)
        {
            changes.Add(newer.IncludesComponentPackaging
                ? "Now includes its products' packaging"
                : "No longer includes its products' packaging");
        }

        AddSectionChanges(changes, older, newer, names);
        AddComponentChanges(changes, older.Components, newer.Components);
        AddPackagingChanges(changes, older, newer, names);
        AddCostingChanges(changes, older.Costing, newer.Costing);
        return changes;
    }

    /// <summary>What a section is called in a change: its role, and its label when it has one, "Filling: Ube".</summary>
    public static string SectionTitle(VersionSection section, string productName) =>
        SubAssemblyRules.SectionLabel(section.SubAssemblyName, productName, section.Role.Name) is { } label
            ? $"{section.Role.Name}: {label}"
            : section.Role.Name;

    private static void AddSectionChanges(List<string> changes, ProductVersionContent older, ProductVersionContent newer, MaterialNames names)
    {
        // Paired by sub-assembly first, then by role and label among the rest: a section of the product's own rewritten as a
        // new one (a restore) is still the same section, and a section linked to another sub-assembly in its place is a
        // change of link, not one removed and another added.
        List<VersionSection> unpairedOld = [.. older.Sections];
        List<(VersionSection? Old, VersionSection? New)> pairs = [];
        List<VersionSection> unpairedNew = [];
        foreach (var section in newer.Sections)
        {
            var match = unpairedOld.FirstOrDefault(o => o.SubAssemblyId == section.SubAssemblyId);
            if (match is null)
            {
                unpairedNew.Add(section);
                continue;
            }

            unpairedOld.Remove(match);
            pairs.Add((match, section));
        }

        foreach (var section in unpairedNew)
        {
            var title = SectionTitle(section, newer.ProductName);
            var match = unpairedOld.FirstOrDefault(o => o.Role.Id == section.Role.Id && SectionTitle(o, older.ProductName) == title);
            if (match is not null)
            {
                unpairedOld.Remove(match);
            }

            pairs.Add((match, section));
        }

        pairs.AddRange(unpairedOld.Select(o => ((VersionSection?)o, (VersionSection?)null)));

        foreach (var (was, now) in pairs
            .OrderBy(p => (p.New ?? p.Old)!.Role.SortOrder)
            .ThenBy(p => (p.New ?? p.Old)!.Role.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (now is null)
            {
                changes.Add($"{SectionTitle(was!, older.ProductName)} removed");
                continue;
            }

            var title = SectionTitle(now, newer.ProductName);
            if (was is null)
            {
                // A linked one's name is in its title already when it isn't named after the product ("Dough: Basic Dough").
                changes.Add(now.IsOwn || title.EndsWith($": {now.SubAssemblyName}", StringComparison.Ordinal)
                    ? $"{title} added"
                    : $"{title} added, linked to {now.SubAssemblyName}");
                continue;
            }

            if (was.IsOwn != now.IsOwn || (!now.IsOwn && was.SubAssemblyId != now.SubAssemblyId))
            {
                changes.Add(now.IsOwn
                    ? $"{title}: now the product's own (was linked to {was.SubAssemblyName})"
                    : was.IsOwn
                        ? $"{title}: now linked to {now.SubAssemblyName}"
                        : $"{title}: now linked to {now.SubAssemblyName} (was {was.SubAssemblyName})");
            }

            AddPieceSizeChanges(changes, title, was, now);
            AddLineChanges(changes, title, was.Lines, now.Lines, names);
            AddAddInChanges(changes, title, was.AddIns, now.AddIns, names);
        }
    }

    private static void AddPieceSizeChanges(List<string> changes, string title, VersionSection was, VersionSection now)
    {
        if (was.AuthoredField != now.AuthoredField)
        {
            changes.Add($"{title}: sized by {PieceSize(now.AuthoredField, now.AuthoredValue)} (was {PieceSize(was.AuthoredField, was.AuthoredValue)})");
        }
        else if (was.AuthoredValue != now.AuthoredValue)
        {
            changes.Add(now.AuthoredField == UsageAuthoredField.Yield
                ? $"{title}: yield {Amount(was.AuthoredValue)} pcs → {Amount(now.AuthoredValue)} pcs"
                : $"{title}: weight per piece {Amount(was.AuthoredValue)} g → {Amount(now.AuthoredValue)} g");
        }

        if (was.ShrinkagePercent != now.ShrinkagePercent)
        {
            changes.Add($"{title}: shrinkage {Amount(was.ShrinkagePercent)}% → {Amount(now.ShrinkagePercent)}%");
        }
    }

    private static string PieceSize(UsageAuthoredField field, decimal value) =>
        field == UsageAuthoredField.Yield ? $"yield, {Amount(value)} pcs" : $"weight per piece, {Amount(value)} g";

    private static void AddLineChanges(
        List<string> changes, string title, IReadOnlyList<VersionLine> was, IReadOnlyList<VersionLine> now, MaterialNames names)
    {
        foreach (var (old, line) in PairBy(was, now, l => l.IngredientId))
        {
            var name = names.Of((line ?? old)!.IngredientId);
            if (old is null)
            {
                changes.Add($"{title}: {name} added, {Amount(line!.Grams)} g");
            }
            else if (line is null)
            {
                changes.Add($"{title}: {name} removed");
            }
            else
            {
                if (old.Grams != line.Grams)
                {
                    changes.Add($"{title}: {name} {Amount(old.Grams)} g → {Amount(line.Grams)} g");
                }

                if (old.IsFormulaBase != line.IsFormulaBase)
                {
                    changes.Add(line.IsFormulaBase
                        ? $"{title}: {name} is now part of the formula's 100% base"
                        : $"{title}: {name} is no longer part of the formula's 100% base");
                }
            }
        }
    }

    private static void AddAddInChanges(
        List<string> changes, string title, IReadOnlyList<VersionAddIn> was, IReadOnlyList<VersionAddIn> now, MaterialNames names)
    {
        foreach (var (old, addIn) in PairBy(was, now, a => a.IngredientId))
        {
            var name = names.Of((addIn ?? old)!.IngredientId);
            if (old is null)
            {
                changes.Add($"{title}: add-in {name} added, {Amount(addIn!.Grams)} g");
            }
            else if (addIn is null)
            {
                changes.Add($"{title}: add-in {name} removed");
            }
            else if (old.Grams != addIn.Grams)
            {
                changes.Add($"{title}: add-in {name} {Amount(old.Grams)} g → {Amount(addIn.Grams)} g");
            }
        }
    }

    private static void AddComponentChanges(List<string> changes, IReadOnlyList<VersionComponent> was, IReadOnlyList<VersionComponent> now)
    {
        foreach (var (old, component) in PairBy(was, now, c => c.ProductId))
        {
            if (old is null)
            {
                changes.Add($"Products: {component!.ProductName} added, × {component.Quantity}");
            }
            else if (component is null)
            {
                changes.Add($"Products: {old.ProductName} removed");
            }
            else if (old.Quantity != component.Quantity)
            {
                changes.Add($"Products: {component.ProductName} × {old.Quantity} → × {component.Quantity}");
            }
        }
    }

    private static void AddPackagingChanges(List<string> changes, ProductVersionContent older, ProductVersionContent newer, MaterialNames names)
    {
        switch (older.PackagingLink, newer.PackagingLink)
        {
            case (null, { } linked):
                changes.Add($"Packaging: now linked to {linked.Name}");
                break;
            case ({ } was, null):
                changes.Add($"Packaging: now the product's own (was linked to {was.Name})");
                break;
            case ({ } was, { } now) when was.ProductId != now.ProductId || was.SubAssemblyId != now.SubAssemblyId:
                changes.Add($"Packaging: now linked to {now.Name} (was {was.Name})");
                break;
        }

        foreach (var (old, line) in PairBy(older.PackagingLines, newer.PackagingLines, l => l.MaterialId))
        {
            var name = names.Of((line ?? old)!.MaterialId);
            if (old is null)
            {
                changes.Add($"Packaging: {name} added, {names.Quantity(line!)}");
            }
            else if (line is null)
            {
                changes.Add($"Packaging: {name} removed");
            }
            else if (old.QtyPerUnit != line.QtyPerUnit || old.UnitId != line.UnitId)
            {
                changes.Add($"Packaging: {name} {names.Quantity(old)} → {names.Quantity(line)}");
            }
        }
    }

    private static void AddCostingChanges(List<string> changes, VersionCosting? older, VersionCosting? newer)
    {
        var was = older ?? new VersionCosting(0m, 0m, 0m, 0m, 0m);
        var now = newer ?? new VersionCosting(0m, 0m, 0m, 0m, 0m);
        AddPercent(changes, "Labor %", was.LaborPercent, now.LaborPercent);
        AddPercent(changes, "Overhead %", was.OverheadPercent, now.OverheadPercent);
        AddPercent(changes, "Admin %", was.AdministrativePercent, now.AdministrativePercent);
        AddPercent(changes, "Markup %", was.MarginPercent, now.MarginPercent);
        AddPercent(changes, "VAT %", was.VatPercent, now.VatPercent);

        static void AddPercent(List<string> changes, string label, decimal was, decimal now)
        {
            if (was != now)
            {
                changes.Add($"{label}: {Amount(was)} → {Amount(now)}");
            }
        }
    }

    /// <summary>
    /// <paramref name="was"/> and <paramref name="now"/> paired by key, in <paramref name="now"/>'s order with what went
    /// after: a key on both sides is one pair, the nth of a repeated key with the nth, and the rest stand alone.
    /// </summary>
    private static List<(T? Old, T? New)> PairBy<T>(IReadOnlyList<T> was, IReadOnlyList<T> now, Func<T, int> key)
        where T : class
    {
        List<T> unpaired = [.. was];
        List<(T? Old, T? New)> pairs = [];
        foreach (var item in now)
        {
            var match = unpaired.FirstOrDefault(o => key(o) == key(item));
            if (match is not null)
            {
                unpaired.Remove(match);
            }

            pairs.Add((match, item));
        }

        pairs.AddRange(unpaired.Select(o => ((T?)o, (T?)null)));
        return pairs;
    }

    private static string Amount(decimal value) => value.ToString("#,0.####", CultureInfo.CurrentCulture);

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>Material names and units from both versions, the newer's first: a material renamed since reads by its newer name.</summary>
    private sealed class MaterialNames(ProductVersionContent older, ProductVersionContent newer)
    {
        private readonly Dictionary<int, VersionMaterial> _materials = newer.Materials
            .Concat(older.Materials)
            .DistinctBy(m => m.Id)
            .ToDictionary(m => m.Id);

        public string Of(int materialId) => _materials.TryGetValue(materialId, out var material) ? material.Name : $"Material {materialId}";

        /// <summary>"2 pc", "1 roll": the quantity in the line's unit, or the material's base unit.</summary>
        public string Quantity(VersionPackagingLine line)
        {
            var material = _materials.GetValueOrDefault(line.MaterialId);
            var unit = line.UnitId is { } unitId
                ? material?.Units.FirstOrDefault(u => u.Id == unitId)?.UnitOfMeasure.Abbreviation
                : material?.BaseUnit?.Abbreviation;
            return unit is null ? Amount(line.QtyPerUnit) : $"{Amount(line.QtyPerUnit)} {unit}";
        }
    }
}
