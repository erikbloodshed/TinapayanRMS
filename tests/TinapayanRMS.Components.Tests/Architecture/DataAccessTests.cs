using System.Data;
using System.Data.Common;
using System.Reflection;
using AwesomeAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace TinapayanRMS.Components.Tests.Architecture;

/// <summary>
/// The screens reach the database through use cases (docs/adr/0002-layers-use-cases-and-drafts.md), never by holding a
/// repository, the unit of work, the DbContext or a service built on them. The screens' types that still held one on
/// 2026-11-20 are listed with how many they held: a count may only go down, and is lowered here when it does, so the list
/// only shrinks as each screen moves to use cases. A type not listed may hold none. Nor may any method of theirs, but
/// the composition root's (<see cref="ServiceRegistration"/>), reach one without holding it: resolved from the services,
/// made, cast to or called.
/// </summary>
public class DataAccessTests
{
    /// <summary>34 types holding 121 between them on 2026-11-20; 30 holding 106 once Sub-Assemblies moved (phase 1); 24 holding 83 once Raw Materials did (phase 2); 16 holding 32 once Products read and wrote through its use cases (phase 3); 9 holding 24 once Settings' lists, Costing Defaults and Printing did (phase 4's first step); 4 holding 19 once the password and the backups did (its second); 2 holding 10 once Quick Find and Reports did (its third); 1 holding 4 once the Price Simulator did (its fourth); none once the Print dialog did (its fifth).</summary>
    private static readonly Dictionary<string, int> StillReaching = new(StringComparer.Ordinal)
    {
    };

    /// <summary>
    /// Ports Infrastructure implements that aren't the database: the window's view state is a file of the user's. Before
    /// <see cref="DataAccess"/>, which reads it as it's made.
    /// </summary>
    private static readonly HashSet<Type> NotTheDatabase = [typeof(ApplicationCore.Interfaces.IViewStateStore)];

    [Test]
    public void The_screens_reach_the_database_only_where_they_still_did_on_2026_11_20()
    {
        var reaching = Codebase.WrittenTypes(Codebase.Presentation).Concat(Codebase.WrittenTypes(Codebase.Components))
            .Select(type => (Name: type.FullName!, Holds: DataAccessHeldBy(type).Order(StringComparer.Ordinal).ToList()))
            .Where(type => type.Holds.Count > 0)
            .ToDictionary(type => type.Name, type => type.Holds, StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var (name, holds) in reaching.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var listed = StillReaching.GetValueOrDefault(name);
            if (holds.Count > listed)
            {
                problems.Add($"{name} holds {holds.Count} data-access types where {listed} are allowed ({string.Join(", ", holds)}): reach the database through a use case (ADR 0002)");
            }
            else if (holds.Count < listed)
            {
                problems.Add($"{name} holds {holds.Count} data-access types now: lower StillReaching to {holds.Count}");
            }
        }

        problems.AddRange(StillReaching.Keys
            .Where(name => !reaching.ContainsKey(name))
            .Select(name => $"{name} holds no data-access type now: take it out of StillReaching"));

        string.Join(Environment.NewLine, problems).Should().BeEmpty();
    }

    [Test]
    public void No_screen_method_reaches_the_database_outside_the_composition_root()
    {
        var reaching = Codebase.Presentation.GetTypes().Concat(Codebase.Components.GetTypes())
            .Where(type => Outermost(type) != typeof(ServiceRegistration))
            .SelectMany(IlReader.Bodies)
            .SelectMany(method => DataAccessUsedBy(method).Select(used => $"{method.DeclaringType!.FullName}.{method.Name} uses {used}"))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        string.Join(Environment.NewLine, reaching).Should().BeEmpty("a screen reaches the database through a use case's interface (ADR 0002)");
    }

    [Test]
    [Arguments(nameof(Planted.Resolved))]
    [Arguments(nameof(Planted.ResolvedByType))]
    [Arguments(nameof(Planted.ResolvedAfterAnAwait))]
    [Arguments(nameof(Planted.ContextMade))]
    public void Data_access_reached_without_being_held_is_found(string method) =>
        DataAccessUsedBy(typeof(Planted).GetMethod(method)!).Should().NotBeEmpty();

    [Test]
    public void A_use_case_resolved_by_its_interface_is_let_through() =>
        DataAccessUsedBy(typeof(Planted).GetMethod(nameof(Planted.UseCaseResolved))!).Should().BeEmpty();

    [Test]
    public void A_context_or_a_connection_held_is_found() =>
        DataAccessHeldBy(typeof(Planted.Holder)).Should().BeEquivalentTo(
            nameof(DbContext), nameof(DbConnection));

    private static class Planted
    {
        public static object Resolved(IServiceProvider services) =>
            services.GetRequiredService<ApplicationCore.Interfaces.IProductRepository>();

        public static object? ResolvedByType(IServiceProvider services) =>
            services.GetService(typeof(ApplicationCore.Interfaces.IProductRepository));

        // Runtime async keeps an async method's body in the method, with no state machine to look in.
        public static async Task<object> ResolvedAfterAnAwait(IServiceProvider services)
        {
            await Task.Yield();
            return services.GetRequiredService<ApplicationCore.Interfaces.IProductRepository>();
        }

        public static DbContext ContextMade(DbContextOptions options) => new(options);

        public static object UseCaseResolved(IServiceProvider services) =>
            services.GetRequiredService<UseCases.SubAssemblies.ISubAssemblyEditor>();

        public sealed class Holder(DbContext context, DbConnection connection)
        {
            public object[] Both => [context, connection];
        }
    }

    private static Type Outermost(Type type) => type.DeclaringType is { } outer ? Outermost(outer) : type;

    /// <summary>
    /// The data-access types <paramref name="method"/>'s IL names. A service's static method is a calculation unless it's
    /// Infrastructure's or EF Core's own (SavedProductReader.Build prices what was already read, with no query).
    /// </summary>
    private static IEnumerable<string> DataAccessUsedBy(MethodBase method) =>
        IlReader.UsedBy(method).SelectMany(TypesNamedBy).Where(IsDataAccess).Select(found => found.Name).Distinct();

    private static IEnumerable<Type> TypesNamedBy(MemberInfo member)
    {
        if (member is Type type)
        {
            return Codebase.Unwrapped(type);
        }

        var named = new List<Type>();
        if (member.DeclaringType is { } declaring)
        {
            var isStatic = member is MethodInfo { IsStatic: true } or FieldInfo { IsStatic: true };
            named.AddRange(Codebase.Unwrapped(declaring).Where(found => found != declaring || !isStatic || IsDatabaseLibrary(found)));
        }

        if (member is MethodInfo { IsGenericMethod: true } generic)
        {
            named.AddRange(generic.GetGenericArguments().SelectMany(Codebase.Unwrapped));
        }

        return named;
    }

    /// <summary>The data-access types <paramref name="type"/> holds: in a field, or taken by its constructor or an [Inject] property.</summary>
    private static IEnumerable<string> DataAccessHeldBy(Type type)
    {
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var held = type.GetFields(declared).Select(field => field.FieldType)
            .Concat(type.GetConstructors(declared).SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType))
            .Concat(type.GetProperties(declared).Where(property => property.IsDefined(typeof(InjectAttribute))).Select(property => property.PropertyType));

        return held.SelectMany(Codebase.Unwrapped).Where(IsDataAccess).Select(found => found.Name).Distinct();
    }

    /// <summary>
    /// What <see cref="DataAccess"/> found, anything of a database library's but its exceptions (which a screen may read), and
    /// ADO.NET's own connection, command or data source, whoever's it is.
    /// </summary>
    private static bool IsDataAccess(Type type) =>
        DataAccess.Contains(type)
        || (IsDatabaseLibrary(type) && !type.IsAssignableTo(typeof(Exception)))
        || type.IsAssignableTo(typeof(IDbConnection))
        || type.IsAssignableTo(typeof(IDbCommand))
        || type.IsAssignableTo(typeof(DbDataSource));

    /// <summary>Infrastructure's, EF Core's, Npgsql's or Dapper's.</summary>
    private static bool IsDatabaseLibrary(Type type) =>
        type.Assembly == Codebase.Infrastructure
        || (type.Assembly.GetName().Name is { } name
            && (name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || name.StartsWith("Npgsql", StringComparison.Ordinal)
                || name == "Dapper"));

    /// <summary>
    /// What reaches the database: the DbContext and anything of Infrastructure's or EF Core's; the ports Infrastructure
    /// implements (the repositories, the unit of work, the cloner, the backups); and a service below the screens built on
    /// any of those (Pricing's SavedProductReader and VersionedSaves, and a use case's own class, SubAssemblyEditor: a screen
    /// holds its interface, which a test can stand in for).
    /// </summary>
    private static readonly HashSet<Type> DataAccess = FindDataAccess();

    private static HashSet<Type> FindDataAccess()
    {
        var implemented = Codebase.Infrastructure.GetTypes().SelectMany(type => type.GetInterfaces()).ToHashSet();
        var found = Codebase.ApplicationCore.GetTypes()
            .Where(type => type.IsInterface && implemented.Contains(type) && !NotTheDatabase.Contains(type))
            .Concat(Codebase.WrittenTypes(Codebase.Infrastructure).Where(type => type.IsPublic || type.IsNestedPublic))
            .ToHashSet();

        // Services built on those, as far as they go: a reader that takes a repository, a save that takes that reader.
        var services = Codebase.WrittenTypes(Codebase.Pricing)
            .Concat(Codebase.WrittenTypes(Codebase.ApplicationCore))
            .Concat(Codebase.WrittenTypes(Codebase.UseCases))
            .Where(type => type.IsClass && !type.IsAbstract)
            .ToList();
        bool grew;
        do
        {
            grew = false;
            foreach (var service in services.Where(service => !found.Contains(service)))
            {
                if (service.GetConstructors().SelectMany(constructor => constructor.GetParameters())
                    .SelectMany(parameter => Codebase.Unwrapped(parameter.ParameterType))
                    .Any(type => found.Contains(type) || type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true))
                {
                    grew |= found.Add(service);
                }
            }
        }
        while (grew);

        return found;
    }
}
