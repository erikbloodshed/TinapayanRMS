namespace TinapayanRMS.Pricing;

/// <summary>
/// A product's sections with the ones that go whole into another folded in (ProductSubAssemblyUsage.GoesIntoSubAssemblyId):
/// a sponge into the dough. The dough's batch weighs and costs its own lines, its add-ins and the sponge's whole batch, which
/// its piece size shares out; the sponge has no pieces of its own, so the product counts it once, through the dough. A chain
/// sums through (a sponge into a dough into a bigger dough). The one place this is worked out: a saved product's readers
/// (<see cref="SavedProductReader"/>) and the Products screen's recipe both come here.
/// </summary>
public static class SectionFolds
{
    /// <summary>
    /// Saved <paramref name="sections"/>, each of whose figures is its own batch's (<see cref="PricedSection.FromSaved"/>),
    /// folded by the sub-assembly each goes into (<see cref="PricedSection.GoesIntoSubAssemblyId"/>). A target the product
    /// doesn't use (which the save never lets happen) folds nothing.
    /// </summary>
    public static IReadOnlyList<PricedSection> Apply(IReadOnlyList<PricedSection> sections)
    {
        if (sections.All(s => s.GoesIntoSubAssemblyId is null))
        {
            return sections;
        }

        var indexOf = sections.Select((s, i) => (s.SubAssemblyId, i)).Where(p => p.SubAssemblyId > 0).ToDictionary(p => p.SubAssemblyId, p => p.i);
        return Apply(sections, [.. sections.Select(s => s.GoesIntoSubAssemblyId is { } id && indexOf.TryGetValue(id, out var i) ? i : (int?)null)]);
    }

    /// <summary>
    /// <paramref name="sections"/>, each of whose figures is its own batch's, folded by <paramref name="goesInto"/>: for each,
    /// the index among them of the one it goes into whole, or null. In the same order. A loop (which neither the save nor the
    /// form lets happen) is cut where it closes, so nothing is counted twice.
    /// </summary>
    public static IReadOnlyList<PricedSection> Apply(IReadOnlyList<PricedSection> sections, IReadOnlyList<int?> goesInto)
    {
        // Nothing goes into anything, as in most recipes: each section's own figures are its figures in the recipe.
        if (goesInto.All(g => g is null))
        {
            return sections;
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
            var foldedIn = FoldedBatch.None;
            for (var j = 0; j < sections.Count; j++)
            {
                if (goesInto[j] == index && j != index && !working[j])
                {
                    foldedIn += FiguresOf(j).AsFolded;
                }
            }

            var section = sections[index];
            var result = SectionFigures.Of(
                section.Lines, section.AddIns, section.AuthoredField, section.AuthoredValue, foldedIn, goesWhole: goesInto[index] is not null);
            working[index] = false;
            figures[index] = result;
            return result;
        }

        return
        [
            .. sections.Select((section, i) => section with
            {
                Figures = FiguresOf(i),
                GoesIntoName = goesInto[i] is { } target ? sections[target].SectionName : null,
                FoldedIn = [.. Enumerable.Range(0, sections.Count)
                    .Where(j => goesInto[j] == i && j != i)
                    .Select(j => new FoldedSection(sections[j].SectionName, FiguresOf(j).AsFolded))],
            }),
        ];
    }
}
