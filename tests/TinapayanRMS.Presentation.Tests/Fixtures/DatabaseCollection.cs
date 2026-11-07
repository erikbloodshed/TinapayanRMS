using System.Diagnostics.CodeAnalysis;

namespace TinapayanRMS.Presentation.Tests.Fixtures;

/// <summary>
/// The four collections a database test class goes in, each with a database of its own (DatabaseFixtureBase), so they
/// run side by side while the tests in one run one at a time. A class names its collection twice, as the key of both:
/// <code>
/// [NotInParallel(DatabaseCollection.B)]
/// [ClassDataSource&lt;DatabaseFixture&gt;(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
/// </code>
/// NotInParallel keeps the collection's tests from running at once, and the keyed ClassDataSource hands them the one
/// fixture, and so the one database, TUnit makes for that key. With one collection, this project's database tests took
/// 450 s one after another, which was the whole suite's time. They're balanced by the time their classes take: put a new class in whichever has
/// least in it.
/// </summary>
[SuppressMessage("Naming", "CA1711", Justification = "Named for the collections it names.")]
public static class DatabaseCollection
{
    public const string A = "Database A";
    public const string B = "Database B";
    public const string C = "Database C";
    public const string D = "Database D";
}
