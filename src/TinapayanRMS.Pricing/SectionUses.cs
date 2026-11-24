using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Pricing;

/// <summary>
/// A product's sections with their lines that are others of its sections priced (UsageSectionLine): a sponge on the dough's
/// lines. Each such line costs its grams at the section it uses' cost per gram, that section's own such lines included, so a
/// chain prices through (a sponge in a dough in a bigger dough); and each section's piece size shares out what the others
/// leave of its batch, so the product counts each gram once. The one place this is worked out: a saved product's readers
/// (<see cref="SavedProductReader"/>) and the Products screen's recipe both come here.
/// </summary>
public static class SectionUses
{
    /// <summary>
    /// Saved <paramref name="sections"/>, each of whose figures is its own batch's (<see cref="PricedSection.FromSaved"/>),
    /// priced by the sub-assembly each of its <see cref="PricedSection.SectionLines"/> uses. A line using a section the
    /// product doesn't have (which the save never lets happen) prices as nothing.
    /// </summary>
    public static IReadOnlyList<PricedSection> Apply(IReadOnlyList<PricedSection> sections)
    {
        if (sections.All(s => s.SectionLines.Count == 0))
        {
            return sections;
        }

        var indexOf = sections.Select((s, i) => (s.SubAssemblyId, i)).Where(p => p.SubAssemblyId > 0).ToDictionary(p => p.SubAssemblyId, p => p.i);
        return Apply(sections, [.. sections.Select(s => (IReadOnlyList<int>)[.. s.SectionLines.Select(l => indexOf.GetValueOrDefault(l.SubAssemblyId, -1))])]);
    }

    /// <summary>
    /// <paramref name="sections"/>, each of whose figures is its own batch's, priced by <paramref name="usedIndexes"/>: for
    /// each, the index among them of the section each of its <see cref="PricedSection.SectionLines"/> uses, or -1 for none of
    /// them. In the same order. A loop (which neither the save nor the form lets happen) is cut where it closes, its line
    /// priced as nothing, so nothing is counted twice.
    /// </summary>
    public static IReadOnlyList<PricedSection> Apply(IReadOnlyList<PricedSection> sections, IReadOnlyList<IReadOnlyList<int>> usedIndexes)
    {
        // No section is on another's lines, as in most recipes: each section's own figures are its figures in the recipe.
        if (usedIndexes.All(u => u.Count == 0))
        {
            return sections;
        }

        bool Uses(int i, int k) => usedIndexes[i][k] is var used && used >= 0 && used < sections.Count && used != i;
        var usedElsewhere = new decimal[sections.Count];
        for (var i = 0; i < sections.Count; i++)
        {
            for (var k = 0; k < usedIndexes[i].Count; k++)
            {
                if (Uses(i, k))
                {
                    usedElsewhere[usedIndexes[i][k]] += sections[i].SectionLines[k].Grams;
                }
            }
        }

        var figures = new SectionFigures?[sections.Count];
        var working = new bool[sections.Count];

        SectionFigures FiguresOf(int index)
        {
            if (figures[index] is { } done)
            {
                return done;
            }

            working[index] = true;
            var section = sections[index];
            var batch = SectionsBatch.None;
            for (var k = 0; k < usedIndexes[index].Count; k++)
            {
                if (Uses(index, k) && !working[usedIndexes[index][k]])
                {
                    var line = section.SectionLines[k] with { UsedBatch = BatchOf(FiguresOf(usedIndexes[index][k])) };
                    batch += new SectionsBatch(line.Grams, line.CostAt(CostBasis.Buffered), line.CostAt(CostBasis.Unbuffered));
                }
            }

            var result = SectionFigures.Of(section.Lines, section.AddIns, section.AuthoredField, section.AuthoredValue, batch, usedElsewhere[index]);
            working[index] = false;
            figures[index] = result;
            return result;
        }

        // Every section's figures first, so each line's section's are its final ones.
        for (var i = 0; i < sections.Count; i++)
        {
            FiguresOf(i);
        }

        return
        [
            .. sections.Select((section, i) => section with
            {
                Figures = figures[i]!,
                SectionLines =
                [
                    .. section.SectionLines.Select((line, k) => Uses(i, k)
                        ? line with
                        {
                            SectionName = sections[usedIndexes[i][k]].SectionName,
                            UsedBatch = BatchOf(figures[usedIndexes[i][k]]!),
                        }
                        : line),
                ],
            }),
        ];
    }

    private static SectionsBatch BatchOf(SectionFigures figures) => new(figures.TotalWeight, figures.BufferedTotalCost, figures.UnbufferedTotalCost);
}
