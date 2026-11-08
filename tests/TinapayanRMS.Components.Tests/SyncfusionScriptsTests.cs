using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using TinapayanRMS.Components.Shared;

namespace TinapayanRMS.Components.Tests;

/// <summary>
/// The page loads the scripts of only the Syncfusion components on the list in the Maui project's wwwroot/syncfusion.js,
/// through the loader in Syncfusion's base script. A component the screens use that isn't on the list has no script, and
/// fails on its screen in the window, where nothing else would see it first.
/// </summary>
public partial class SyncfusionScriptsTests
{
    [Test]
    public void The_page_loads_the_scripts_of_every_Syncfusion_component_the_screens_use()
    {
        var loader = LoaderComponents();
        // The loader's own names (SfGrid is "sfgrid"): a component it has no scripts for, as SfButton, needs none.
        var needingScripts = SyncfusionComponentsTheScreensUse()
            .Select(component => loader.Keys.FirstOrDefault(name => string.Equals(name, component, StringComparison.OrdinalIgnoreCase)))
            .OfType<string>();

        PageList().Should().BeEquivalentTo(needingScripts,
            "syncfusion.js names each Syncfusion component the screens use that has scripts, and no other");
    }

    [Test]
    public void Syncfusions_loader_knows_every_component_the_page_names()
    {
        // Checked against the package at SyncfusionVersion, so an upgrade that changes the loader fails here first.
        var loader = LoaderComponents();
        var page = PageList();

        page.Should().NotBeEmpty().And.BeSubsetOf(loader.Keys, "the loader fetches only the components in its map");
        page.Should().AllSatisfy(name => loader[name].Should().NotBeEmpty($"the loader fetches {name}'s scripts"));
    }

    /// <summary>
    /// The Syncfusion components the Components assembly names (its type references): everything a screen or a shared
    /// component draws, as SfGrid`1.
    /// </summary>
    private static List<string> SyncfusionComponentsTheScreensUse()
    {
        using var stream = File.OpenRead(typeof(ScreenBase<>).Assembly.Location);
        using var assembly = new PEReader(stream);
        var metadata = assembly.GetMetadataReader();

        return metadata.TypeReferences
            .Select(handle => metadata.GetTypeReference(handle))
            .Where(type => metadata.GetString(type.Namespace).StartsWith("Syncfusion.Blazor", StringComparison.Ordinal))
            .Select(type => metadata.GetString(type.Name).Split('`')[0])
            .Where(name => name.StartsWith("Sf", StringComparison.Ordinal))
            .Distinct()
            .ToList();
    }

    /// <summary>The components syncfusion.js has the loader load: its <c>const components = [...]</c>.</summary>
    private static List<string> PageList()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "syncfusion.js"));
        var list = ListLiteral().Match(script);
        list.Success.Should().BeTrue("syncfusion.js keeps its list in a const components array");

        return QuotedName().Matches(list.Groups["items"].Value).Select(match => match.Groups["name"].Value).ToList();
    }

    /// <summary>
    /// The loader's map in Syncfusion's base script, each component with the scripts it fetches for it: the object right
    /// after <c>sfBlazor.loadDependencies=</c>'s function, as <c>{sftextbox:["sf-textbox"],…}</c>.
    /// </summary>
    private static Dictionary<string, string[]> LoaderComponents()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "syncfusion-blazor-base.min.js"));
        var loader = script.IndexOf("sfBlazor.loadDependencies=", StringComparison.Ordinal);
        loader.Should().BePositive("Syncfusion's base script defines the loader its per-component scripts call");
        var end = script.IndexOf("]}", loader, StringComparison.Ordinal);
        end.Should().BePositive("the loader's map follows its function");

        return MapEntry().Matches(script[loader..(end + 2)])
            .ToDictionary(match => match.Groups["component"].Value, match => match.Groups["scripts"].Value.Split(','));
    }

    [GeneratedRegex(@"const components = \[(?<items>[^\]]*)\]")]
    private static partial Regex ListLiteral();

    [GeneratedRegex("\"(?<name>[a-z0-9]+)\"")]
    private static partial Regex QuotedName();

    [GeneratedRegex(@"(?<component>sf[a-z0-9]+):\[(?<scripts>[^\]]*)\]")]
    private static partial Regex MapEntry();
}
