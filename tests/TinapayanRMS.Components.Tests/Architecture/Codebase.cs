using System.Reflection;
using System.Runtime.CompilerServices;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Components.Tests.Architecture;

/// <summary>
/// What the architecture tests read: the repository's folder, the app's own assemblies by layer, and the types written by
/// hand in them (docs/adr/0002-layers-use-cases-and-drafts.md).
/// </summary>
internal static class Codebase
{
    public static readonly Assembly Domain = typeof(Product).Assembly;
    public static readonly Assembly ApplicationCore = typeof(ApplicationCore.Rules.CostingRules).Assembly;
    public static readonly Assembly Infrastructure = typeof(TinapayanRMSDbContext).Assembly;
    public static readonly Assembly Pricing = typeof(Pricing.SavedProductReader).Assembly;
    public static readonly Assembly Presentation = typeof(ScreenState).Assembly;
    public static readonly Assembly Components = typeof(ServiceRegistration).Assembly;

    /// <summary>The folder holding TinapayanRMS.slnx.</summary>
    public static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "TinapayanRMS.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The tests aren't running under the repository's folder.");
    }

    /// <summary>The types of <paramref name="assembly"/> someone wrote: not a closure, an iterator or a Razor builder's.</summary>
    public static IEnumerable<Type> WrittenTypes(Assembly assembly) =>
        assembly.GetTypes().Where(type => !IsGenerated(type));

    private static bool IsGenerated(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        || type.Name.Contains('<', StringComparison.Ordinal)
        || (type.DeclaringType is { } outer && IsGenerated(outer));

    /// <summary>A type and every type it's built from: <c>IDbContextFactory&lt;TinapayanRMSDbContext&gt;</c> is both.</summary>
    public static IEnumerable<Type> Unwrapped(Type type)
    {
        yield return type;
        if (type.HasElementType)
        {
            foreach (var inner in Unwrapped(type.GetElementType()!))
            {
                yield return inner;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var inner in type.GetGenericArguments().SelectMany(Unwrapped))
            {
                yield return inner;
            }
        }
    }
}
