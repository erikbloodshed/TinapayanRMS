using System.Buffers;
using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace TinapayanRMS.Components.Tests;

/// <summary>
/// Every file the guides name is there: a path in backticks, or a link, in PLAN.md, the CLAUDE.md files, the README, the
/// manuals and the design notes. The build plan is left out, since its log tells of files that have gone.
/// </summary>
public partial class DocPathsTests
{
    private static readonly string[] PathRoots = ["src/", "tests/", "scripts/", "installer/", ".github/", "docs/"];

    /// <summary>A path standing for many, or for one a reader fills in: `src/*/*.csproj`, `docs/&lt;name&gt;.md`.</summary>
    private static readonly SearchValues<char> Placeholders = SearchValues.Create("*<>…{}$%");

    [Test]
    public void Every_path_the_guides_name_is_there()
    {
        var root = RepositoryRoot();
        var missing = Guides(root).SelectMany(guide => MissingFrom(guide, root)).ToList();

        missing.Should().BeEmpty("a guide names only what's in the repository");
    }

    private static IEnumerable<string> Guides(string root) =>
    [
        Path.Combine(root, "PLAN.md"),
        Path.Combine(root, "CLAUDE.md"),
        Path.Combine(root, "README.md"),
        Path.Combine(root, "tests", "CLAUDE.md"),
        .. Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md"),
        .. Directory.EnumerateFiles(Path.Combine(root, "docs", "plan"), "*.md").Where(file => Path.GetFileName(file) != "build-plan.md"),
    ];

    private static IEnumerable<string> MissingFrom(string guide, string root)
    {
        var text = File.ReadAllText(guide);
        var named = CodeSpan().Matches(text)
            .Select(match => match.Groups[1].Value.Trim().Replace('\\', '/'))
            .Where(path => PathRoots.Any(start => path.StartsWith(start, StringComparison.Ordinal)) && !path.Contains(' ', StringComparison.Ordinal) && !path.AsSpan().ContainsAny(Placeholders) && !IsBuildOutput(path))
            .Select(path => Path.Combine(root, path));
        var linked = Link().Matches(text)
            .Select(match => match.Groups[1].Value)
            .Where(target => !target.Contains(':', StringComparison.Ordinal))
            .Select(target => Path.Combine(Path.GetDirectoryName(guide)!, target));

        return named.Concat(linked)
            .Where(path => !Path.Exists(path))
            .Select(path => $"{Path.GetRelativePath(root, guide)}: {Path.GetRelativePath(root, path)}")
            .Distinct();
    }

    /// <summary>What a build writes, such as `src/TinapayanRMS.Mcp/bin/Release/net11.0`: a fresh checkout doesn't have it yet.</summary>
    private static bool IsBuildOutput(string path) =>
        path.Contains("/bin/", StringComparison.Ordinal) || path.Contains("/obj/", StringComparison.Ordinal);

    /// <summary>The folder holding the solution, above where the tests run from.</summary>
    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "TinapayanRMS.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The tests aren't running under the repository's folder.");
    }

    [GeneratedRegex(@"`([^`\r\n]+)`")]
    private static partial Regex CodeSpan();

    /// <summary>A Markdown link's target, without an anchor in the page it opens: an anchor alone has no path to check.</summary>
    [GeneratedRegex(@"\]\(([^)#\s]+)(?:#[^)\s]*)?\)")]
    private static partial Regex Link();
}
