using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Pricing.Versions;

/// <summary>
/// Turns a saved product into a version's snapshot (ProductVersionContent) and back. The way back gives the
/// <see cref="ProductDetail"/> SavedProductReader.Build prices, so a version's figures are worked out by the same code as
/// the screen's: with its materials at their costs then, or, given the materials as they are now, at today's.
/// </summary>
public static class ProductVersionSnapshot
{
    /// <summary><paramref name="product"/> as <paramref name="detail"/> has it saved, with its materials at their costs now.</summary>
    public static ProductVersionContent From(Product product, ProductDetail detail)
    {
        Dictionary<int, RawMaterial> materials = [];
        void Use(RawMaterial? material)
        {
            if (material is not null)
            {
                materials.TryAdd(material.Id, material);
            }
        }

        var linesBySubAssembly = detail.SubAssemblyLines.ToLookup(l => l.SubAssemblyId);
        var addInsByUsage = detail.AddIns.ToLookup(a => a.UsageId);
        List<VersionSection> sections = [];
        foreach (var usage in detail.Usages)
        {
            var subAssembly = usage.SubAssembly!;
            var role = subAssembly.Role!;
            var lines = linesBySubAssembly[usage.SubAssemblyId].ToList();
            var addIns = addInsByUsage[usage.Id].ToList();
            lines.ForEach(l => Use(l.Ingredient));
            addIns.ForEach(a => Use(a.Ingredient));
            sections.Add(new VersionSection(
                subAssembly.Id,
                subAssembly.Name,
                subAssembly.OwnerProductId == product.Id,
                new VersionRole(role.Id, role.Name, role.SortOrder, role.IsPackaging, role.IsProductOwned),
                usage.AuthoredField,
                usage.AuthoredValue,
                [.. lines.Select(l => new VersionLine(l.IngredientId, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
                [.. addIns.Select(a => new VersionAddIn(a.IngredientId, a.Grams))],
                SubAssemblyRules.NormalizeName(SubAssemblyRules.LabelIn(subAssembly, product.Id, product.Name, role.Name))));
        }

        // As the Packaging tab shows them (PackagingLineRow.FromSaved): a line saved as pieces per unit is its quantity per
        // unit, in its unit while the material still has that unit, else in the base unit. Its bulk packaging after, each
        // line's quantity per bulk pack.
        List<VersionPackagingLine> packaging = [];
        foreach (var line in detail.Lines.Concat(detail.BulkLines))
        {
            Use(line.PackagingMaterial);
            var authoredValue = line.AuthoredField == PackagingLineAuthoredField.QtyPerUnit ? line.QtyPerUnit : line.UnitsPerPackagingUnit;
            var qtyPerUnit = ProductRules.ReconcileReciprocal(line.AuthoredField, authoredValue).QtyPerUnit;
            int? unitId = line.UnitId is { } id && PricedPackagingLine.UnitChoices(line.PackagingMaterial, CostBasis.Buffered).Any(c => c.Key == id)
                ? id
                : null;
            packaging.Add(new VersionPackagingLine(line.PackagingMaterialId, unitId, qtyPerUnit, line.IsBulk));
        }

        var costing = detail.Costing;
        return new ProductVersionContent
        {
            ProductName = product.Name,
            IsComposite = product.IsComposite,
            QuantityRequired = product.QuantityRequired,
            CurrentPrice = product.CurrentPrice,
            IncludesComponentPackaging = product.IncludesComponentPackaging,
            BulkPackagingQuantity = detail.BulkLines.Count > 0 ? product.BulkPackagingQuantity : null,
            Costing = costing is null
                ? null
                : new VersionCosting(costing.LaborPercent, costing.OverheadPercent, costing.AdministrativePercent, costing.MarginPercent),
            WastagePercent = detail.WastagePercent,
            VatPercent = detail.VatPercent,
            Sections = sections,
            PackagingLink = detail.PackagingLink is { } link
                ? new VersionPackagingLink(
                    link.LinkedProductId,
                    link.LinkedSubAssemblyId,
                    PackagingLinkTarget.From(link).Name)
                : null,
            PackagingLines = packaging,
            Components =
            [
                .. detail.Components.Select(c => new VersionComponent(
                    c.Component.ComponentProductId,
                    c.Component.ComponentProduct?.Name ?? string.Empty,
                    c.Component.Quantity,
                    c.Costs.RecipeBuffered,
                    c.Costs.RecipeUnbuffered,
                    c.Costs.PackagingBuffered,
                    c.Costs.PackagingUnbuffered)),
            ],
            Materials = [.. materials.Values.OrderBy(m => m.Id).Select(ToVersionMaterial)],
        };
    }

    /// <summary>
    /// <paramref name="content"/> as a product and its detail, for SavedProductReader.Build: <paramref name="productId"/>'s,
    /// with its materials at their costs then, its products at what they brought then and the wastage it had then. Pass
    /// <paramref name="componentCostsNow"/> for each product's cost as it is now instead, where it's known, and
    /// <paramref name="wastagePercentNow"/> and <paramref name="vatPercentNow"/> for the wastage and VAT as they are now.
    /// </summary>
    public static (Product Product, ProductDetail Detail) ToDetail(
        int productId,
        ProductVersionContent content,
        IReadOnlyDictionary<int, ComponentCosts>? componentCostsNow = null,
        decimal? wastagePercentNow = null,
        decimal? vatPercentNow = null)
    {
        var materials = content.Materials.ToDictionary(m => m.Id, ToRawMaterial);
        RawMaterial MaterialOf(int id) => materials.GetValueOrDefault(id) ?? new RawMaterial { Id = id, Name = $"Material {id}" };

        var product = new Product
        {
            Id = productId,
            Name = content.ProductName,
            IsComposite = content.IsComposite,
            QuantityRequired = content.QuantityRequired,
            CurrentPrice = content.CurrentPrice,
            IncludesComponentPackaging = content.IncludesComponentPackaging,
            BulkPackagingQuantity = content.BulkPackagingQuantity,
        };

        List<ProductSubAssemblyUsage> usages = [];
        List<SubAssemblyLine> lines = [];
        List<UsageAddInLine> addIns = [];
        for (var i = 0; i < content.Sections.Count; i++)
        {
            var section = content.Sections[i];

            // A usage id of its own, which only its add-ins need to find it by.
            var usageId = i + 1;
            var role = new SubAssemblyRole
            {
                Id = section.Role.Id,
                Name = section.Role.Name,
                SortOrder = section.Role.SortOrder,
                IsPackaging = section.Role.IsPackaging,
                IsProductOwned = section.Role.IsProductOwned,
            };
            usages.Add(new ProductSubAssemblyUsage
            {
                Id = usageId,
                ProductId = productId,
                SubAssemblyId = section.SubAssemblyId,
                SubAssembly = new SubAssembly
                {
                    Id = section.SubAssemblyId,
                    Name = section.SubAssemblyName,
                    RoleId = role.Id,
                    Role = role,
                    OwnerProductId = section.IsOwn ? productId : null,
                    SectionName = section.IsOwn ? section.LabelIn(content.ProductName, content.Schema) : null,
                },
                AuthoredField = section.AuthoredField,
                AuthoredValue = section.AuthoredValue,
            });
            lines.AddRange(section.Lines.Select((l, sortOrder) => new SubAssemblyLine
            {
                SubAssemblyId = section.SubAssemblyId,
                IngredientId = l.IngredientId,
                Ingredient = MaterialOf(l.IngredientId),
                Grams = l.Grams,
                IsFormulaBase = l.IsFormulaBase,
                AuthoredBakersPercent = l.AuthoredBakersPercent,
                SortOrder = sortOrder,
            }));
            addIns.AddRange(section.AddIns.Select((a, sortOrder) => new UsageAddInLine
            {
                UsageId = usageId,
                IngredientId = a.IngredientId,
                Ingredient = MaterialOf(a.IngredientId),
                Grams = a.Grams,
                SortOrder = sortOrder,
            }));
        }

        List<PackagingLine> PackagingOf(bool isBulk) =>
        [
            .. content.PackagingLines.Where(l => l.IsBulk == isBulk).Select((l, sortOrder) => new PackagingLine
            {
                ProductId = productId,
                PackagingMaterialId = l.MaterialId,
                PackagingMaterial = MaterialOf(l.MaterialId),
                UnitId = l.UnitId,
                QtyPerUnit = l.QtyPerUnit,
                AuthoredField = PackagingLineAuthoredField.QtyPerUnit,
                SortOrder = sortOrder,
                IsBulk = isBulk,
            }),
        ];
        var packaging = PackagingOf(isBulk: false);

        var costing = content.Costing is { } c
            ? new CostingParameters
            {
                ProductId = productId,
                LaborPercent = c.LaborPercent,
                OverheadPercent = c.OverheadPercent,
                AdministrativePercent = c.AdministrativePercent,
                MarginPercent = c.MarginPercent,
            }
            : null;

        var link = content.PackagingLink is { } l
            ? new PackagingLink
            {
                ProductId = productId,
                LinkedProductId = l.ProductId,
                LinkedProduct = l.ProductId is { } linkedProductId ? new Product { Id = linkedProductId, Name = l.Name } : null,
                LinkedSubAssemblyId = l.SubAssemblyId,
                LinkedSubAssembly = l.SubAssemblyId is { } setId ? new SubAssembly { Id = setId, Name = l.Name } : null,
            }
            : null;

        var detail = new ProductDetail(link, usages, packaging, costing, lines, addIns)
        {
            BulkLines = PackagingOf(isBulk: true),
            WastagePercent = wastagePercentNow ?? content.WastagePercent ?? 0m,
            VatPercent = vatPercentNow ?? content.VatThen,
            Components =
            [
                .. content.Components.Select((component, sortOrder) => new SavedComponent(
                    new ProductComponent
                    {
                        ProductId = productId,
                        ComponentProductId = component.ProductId,
                        ComponentProduct = new Product { Id = component.ProductId, Name = component.ProductName },
                        Quantity = component.Quantity,
                        SortOrder = sortOrder,
                    },
                    componentCostsNow?.GetValueOrDefault(component.ProductId)
                        ?? new ComponentCosts(
                            component.RecipeBuffered, component.RecipeUnbuffered, component.PackagingBuffered, component.PackagingUnbuffered))),
            ],
        };
        return (product, detail);
    }

    private static VersionMaterial ToVersionMaterial(RawMaterial material) =>
        new(
            material.Id,
            material.Name,
            material.Type,
            material.BaseUnit is { } baseUnit ? ToVersionUnit(baseUnit) : null,
            material.CostPerBaseUnit,
            material.BufferedCostPerBaseUnit,
            [
                .. material.Units
                    .Where(u => u.UnitOfMeasure is not null)
                    .Select(u => new VersionMaterialUnit(u.Id, ToVersionUnit(u.UnitOfMeasure!), u.BaseUnitsPerUnit, u.Description)),
            ]);

    private static VersionUnitOfMeasure ToVersionUnit(UnitOfMeasure unit) => new(unit.Id, unit.Name, unit.Abbreviation);

    private static RawMaterial ToRawMaterial(VersionMaterial material)
    {
        var baseUnit = material.BaseUnit is { } unit ? ToUnitOfMeasure(unit) : null;
        return new RawMaterial
        {
            Id = material.Id,
            Name = material.Name,
            Type = material.Type,
            BaseUnitId = baseUnit?.Id,
            BaseUnit = baseUnit,
            CostPerBaseUnit = material.CostPerBaseUnit,
            BufferedCostPerBaseUnit = material.BufferedCostPerBaseUnit,
            Units =
            [
                .. material.Units.Select(u => new RawMaterialUnit
                {
                    Id = u.Id,
                    RawMaterialId = material.Id,
                    UnitOfMeasureId = u.UnitOfMeasure.Id,
                    UnitOfMeasure = ToUnitOfMeasure(u.UnitOfMeasure),
                    BaseUnitsPerUnit = u.BaseUnitsPerUnit,
                    Description = u.Description,
                }),
            ],
        };
    }

    private static UnitOfMeasure ToUnitOfMeasure(VersionUnitOfMeasure unit) =>
        new() { Id = unit.Id, Name = unit.Name, Abbreviation = unit.Abbreviation };
}
