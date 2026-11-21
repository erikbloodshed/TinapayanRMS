using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using TinapayanRMS.Benchmarks.Scenarios;

namespace TinapayanRMS.Benchmarks.Tests.Budgets;

/// <summary>What a scenario was approved to take: the SQL commands it sends, and the kilobytes it allocates.</summary>
public sealed record Budget(string Scenario, int Commands, long Kilobytes)
{
    public override string ToString() => $"{Scenario} | sql = {Commands} | allocated = {Kilobytes} KB";

    public static Budget Parse(string line)
    {
        var parts = line.Split('|', StringSplitOptions.TrimEntries);
        if (parts is not [var scenario, var sql, var allocated]
            || !sql.StartsWith("sql = ", StringComparison.Ordinal)
            || !allocated.StartsWith("allocated = ", StringComparison.Ordinal)
            || !allocated.EndsWith(" KB", StringComparison.Ordinal))
        {
            throw new FormatException($"A budget reads \"<scenario> | sql = <commands> | allocated = <kilobytes> KB\", and \"{line}\" doesn't.");
        }

        return new Budget(
            scenario,
            int.Parse(sql["sql = ".Length..], CultureInfo.InvariantCulture),
            long.Parse(allocated["allocated = ".Length..^" KB".Length], CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// The budgets approved, one line a scenario (Budgets/budgets.approved.txt), as the build copies them into the tests'
/// folder; approving writes them in the source folder, in the catalog's order, so a change shows in its diff.
/// </summary>
public static class ApprovedBudgets
{
    public const string FileName = "budgets.approved.txt";

    /// <summary>Set to 1 on a developer's PC to write what this run measures over the budgets approved.</summary>
    public const string ApproveVariable = "TINAPAYANRMS_APPROVE_BUDGETS";

    public static bool Approving => Environment.GetEnvironmentVariable(ApproveVariable) == "1";

    /// <summary>The budgets approved, by scenario.</summary>
    public static IReadOnlyDictionary<string, Budget> Read()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Budgets", FileName);
        return File.Exists(path)
            ? File.ReadAllLines(path).Where(line => line.Length > 0).Select(Budget.Parse).ToDictionary(b => b.Scenario)
            : new Dictionary<string, Budget>();
    }

    /// <summary>
    /// <paramref name="budget"/> approved in the source folder's file, in place of its scenario's last one; and a scenario
    /// no longer in the catalog dropped from it.
    /// </summary>
    public static void Approve(Budget budget) => Write(budgets => budgets[budget.Scenario] = budget);

    /// <summary>The source folder's file without the budgets of scenarios no longer in the catalog.</summary>
    public static void DropOrphans() => Write(_ => { });

    private static void Write(Action<Dictionary<string, Budget>> change)
    {
        var path = SourceFile();
        Dictionary<string, Budget> budgets = File.Exists(path)
            ? File.ReadAllLines(path).Where(line => line.Length > 0).Select(Budget.Parse).ToDictionary(b => b.Scenario)
            : [];
        change(budgets);
        var lines = ScenarioCatalog.Names.Where(budgets.ContainsKey).Select(name => budgets[name].ToString());
        File.WriteAllText(path, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
    }

    /// <summary>The approved file in the source folder, as the build copies it from there into the tests' own.</summary>
    private static string SourceFile([CallerFilePath] string thisFile = "") =>
        Path.Combine(Path.GetDirectoryName(thisFile)!, FileName);
}
