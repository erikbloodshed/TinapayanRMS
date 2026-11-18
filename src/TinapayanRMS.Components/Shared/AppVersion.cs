using System.Reflection;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// Which build of the app this copy is, so what's installed can be found again in the code: the version and the commit
/// `dotnet publish` writes into the app. An everyday build carries no commit.
/// </summary>
public static class AppVersion
{
    private const int CommitLength = 7;

    /// <summary>This copy's, as Settings' Database tab shows it and the log records it at each start.</summary>
    public static string Current { get; } = Describe(
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>"1.0 (commit 6c89b34)" for the "1.0+6c89b34…" a published build carries, "1.0" with no commit.</summary>
    public static string Describe(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "Unknown";
        }

        var parts = informationalVersion.Trim().Split('+', 2);
        return parts is [var version, { Length: > 0 } commit]
            ? $"{version} (commit {commit[..Math.Min(CommitLength, commit.Length)]})"
            : parts[0];
    }
}
