using AwesomeAssertions;

namespace TinapayanRMS.Components.Tests.Architecture;

/// <summary>
/// A file of the screens' states (Presentation) is at most <see cref="Limit"/> lines (docs/adr/0004-quality-gates.md): one
/// that grows past it is doing more than one job, and its record's state and rules belong in a draft, its reads and writes in
/// a use case (ADR 0002). Those that were longer on 2026-11-21 are listed with how long they were: a length may only go
/// down, and is lowered here when it does by a screen's worth, so the list only shrinks. It replaced phase 3's "ProductForm
/// under 700 lines", which measured the wrong thing once the record's state had left it (ADR 0002's amendment).
/// </summary>
public class FileSizeTests
{
    private const int Limit = 700;

    /// <summary>How much shorter a listed file must get before its number here is lowered, so an edit's few lines don't churn it.</summary>
    private const int Slack = 25;

    private static readonly Dictionary<string, int> StillOver = new(StringComparer.Ordinal)
    {
        ["Products/ProductForm.cs"] = 1793,
        ["Products/ProductRecipe.cs"] = 1317,
        ["Products/ProductsList.cs"] = 1232,
        ["RawMaterials/RawMaterialForm.cs"] = 1150,
        ["Products/ProductVersionHistory.cs"] = 776,
        ["SubAssemblies/SubAssembliesList.cs"] = 745,
        ["Products/ProductUsageForm.cs"] = 735,
        ["RawMaterials/RawMaterialsList.cs"] = 724,
        ["Products/ProductTree.cs"] = 721,
    };

    [Test]
    public void A_screen_state_file_is_at_most_seven_hundred_lines()
    {
        var presentation = Path.Combine(Codebase.RepositoryRoot(), "src", "TinapayanRMS.Presentation");
        var lengths = Directory.EnumerateFiles(presentation, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(Path.GetRelativePath(presentation, file)))
            .Select(file => (Path: Path.GetRelativePath(presentation, file).Replace('\\', '/'), Lines: File.ReadAllLines(file).Length))
            .Where(file => file.Lines > Limit)
            .ToDictionary(file => file.Path, file => file.Lines, StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var (path, lines) in lengths.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var allowed = StillOver.GetValueOrDefault(path, Limit);
            if (lines > allowed)
            {
                problems.Add($"{path} is {lines} lines where {allowed} are allowed: move what isn't the screen's out of it (a draft, a use case, a part)");
            }
            else if (lines <= allowed - Slack)
            {
                problems.Add($"{path} is {lines} lines now: lower StillOver to {lines}");
            }
        }

        problems.AddRange(StillOver.Keys
            .Where(path => !lengths.ContainsKey(path))
            .Select(path => $"{path} is {Limit} lines or fewer now: take it out of StillOver"));

        string.Join(Environment.NewLine, problems).Should().BeEmpty();
    }

    /// <summary>A file under the project's bin or obj folder, by its first folder alone: a source folder named "objects" is measured.</summary>
    private static bool IsBuildOutput(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0] is "bin" or "obj";
}
