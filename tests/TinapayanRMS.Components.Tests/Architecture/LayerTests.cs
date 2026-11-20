using System.Reflection;
using System.Xml.Linq;
using AwesomeAssertions;

namespace TinapayanRMS.Components.Tests.Architecture;

/// <summary>
/// The layers (docs/adr/0002-layers-use-cases-and-drafts.md): each project references only the layers it may, and the
/// layers below the screens use no UI framework. A reference the layers don't allow that was there on 2026-11-20 is
/// listed, with what takes it away; one that's gone must go from the list too, so the list only shrinks.
/// </summary>
public class LayerTests
{
    /// <summary>
    /// The projects each project under src may reference (TinapayanRMS. left off). A project not here fails: a new project
    /// is a new place in the layers, decided in an ADR first. Components is the composition root (ServiceRegistration), the
    /// one place above Infrastructure that sees it, with Maui, the app, above that.
    /// </summary>
    private static readonly Dictionary<string, string[]> MayReference = new()
    {
        ["Domain"] = [],
        ["ApplicationCore"] = ["Domain"],
        ["Pricing"] = ["ApplicationCore"],
        ["Infrastructure"] = ["Domain", "ApplicationCore"],
        ["Presentation"] = ["Domain", "ApplicationCore", "Pricing"],
        ["Components"] = ["Presentation", "Infrastructure"],
        ["Maui"] = ["Components"],
        ["Mcp"] = ["Infrastructure", "Pricing"],
        ["RecipeApi"] = ["Infrastructure", "Pricing"],
    };

    /// <summary>References the layers don't allow that are still there, each with what takes it away.</summary>
    private static readonly Dictionary<string, string> StillThere = new()
    {
        ["Presentation -> Infrastructure"] = "Failure's words and ProductStageConverter, which move behind use cases (ADR 0002, phase 4)",
    };

    [Test]
    public void Each_project_references_only_the_layers_it_may()
    {
        var src = Path.Combine(Codebase.RepositoryRoot(), "src");
        var references = Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToDictionary(ShortName, file => XDocument.Load(file).Descendants("ProjectReference")
                .Select(reference => ShortName(reference.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)))
                .ToList());

        var problems = new List<string>();
        foreach (var (project, referenced) in references.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!MayReference.TryGetValue(project, out var allowed))
            {
                problems.Add($"{project} isn't in the layers: decide its place in an ADR, then add it to MayReference");
                continue;
            }

            problems.AddRange(referenced
                .Where(target => !allowed.Contains(target) && !StillThere.ContainsKey($"{project} -> {target}"))
                .Select(target => $"{project} -> {target} isn't allowed (docs/adr/0002)"));
        }

        problems.AddRange(StillThere.Keys
            .Where(edge => edge.Split(" -> ") is [var from, var to] && !(references.GetValueOrDefault(from)?.Contains(to) ?? false))
            .Select(edge => $"{edge} is gone: take it out of StillThere"));

        string.Join(Environment.NewLine, problems).Should().BeEmpty();
    }

    /// <summary>
    /// What each layer below the screens must not use, by assembly name: no UI framework below Components, and none of the
    /// screens' Rx or DynamicData below Presentation, so the Recipe API and the MCP server stay plain net11.0.
    /// </summary>
    private static readonly (Assembly Layer, string[] Forbidden)[] Forbidden =
    [
        (Codebase.Domain, [.. UiFrameworks, .. ScreenLibraries, "Microsoft.EntityFrameworkCore"]),
        (Codebase.ApplicationCore, [.. UiFrameworks, .. ScreenLibraries]),
        (Codebase.Pricing, [.. UiFrameworks, .. ScreenLibraries]),
        (Codebase.Infrastructure, [.. UiFrameworks, .. ScreenLibraries]),
        (Codebase.Presentation, UiFrameworks),
    ];

    private static string[] UiFrameworks => ["Microsoft.AspNetCore.Components", "Microsoft.JSInterop", "Microsoft.Maui", "Syncfusion", "Microsoft.WindowsAppSDK", "Microsoft.WinUI"];

    private static string[] ScreenLibraries => ["System.Reactive", "DynamicData"];

    [Test]
    public void The_layers_below_the_screens_use_no_UI_framework()
    {
        var problems = Forbidden
            .SelectMany(rule => rule.Layer.GetReferencedAssemblies()
                .Where(used => rule.Forbidden.Any(prefix => used.Name!.StartsWith(prefix, StringComparison.Ordinal)))
                .Select(used => $"{rule.Layer.GetName().Name} uses {used.Name}"))
            .ToList();

        string.Join(Environment.NewLine, problems).Should().BeEmpty();
    }

    private static string ShortName(string projectFile) =>
        Path.GetFileNameWithoutExtension(projectFile).Replace("TinapayanRMS.", "", StringComparison.Ordinal);
}
