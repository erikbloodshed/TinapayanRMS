namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// A rule refused what the user asked for, with a message written for the shop's staff, which the screen shows exactly as
/// written. Every rule check throws it: the Rules classes, the repositories' checks, and the screens' own form checks.
/// It derives from InvalidOperationException, which the checks threw until 2026-10-03, so a test expecting that still
/// passes; what it adds is that a screen can tell a refusal from a bug. EF Core, Npgsql and the framework throw plain
/// InvalidOperationExceptions too ("Sequence contains no elements", a connection pool that ran out), and those are bugs or
/// failures, not something the user can put right.
/// </summary>
public sealed class BusinessRuleException : InvalidOperationException
{
    public BusinessRuleException()
    {
    }

    public BusinessRuleException(string message)
        : base(message)
    {
    }

    public BusinessRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
