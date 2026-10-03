using System.IO;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// What a screen tells the user when something failed, and how it's logged (2026-10-03). A rule's refusal is shown in its
/// own words. What the database said is put plainly, by its error number: its own text is technical, and the same
/// exception type covers very different things (a duplicate name, a row still in use, a server that isn't running), which
/// one fixed message each for DbUpdateException and SqlException used to read as "That name is already in use." and
/// "This item is currently in use elsewhere and can't be deleted." whatever had happened. Anything else is a bug: it's
/// still shown, so nothing fails silently, and it's logged as an error with its stack.
/// </summary>
/// <param name="Text">What the screen's error notice says.</param>
/// <param name="IsRejection">
/// True when the app or the database refused what the user did, which is theirs to put right, and is logged as a warning;
/// false when something failed (the database couldn't be reached, or a bug), which is logged as an error.
/// </param>
public readonly record struct Failure(string Text, bool IsRejection)
{
    public const string DuplicateNameText = "That name is already in use.";

    public const string InUseText = "This item is currently in use elsewhere and can't be deleted.";

    public const string UnreachableText = "Couldn't reach the database. Check that SQL Server Express is running, then try again.";

    public const string TimedOutText = "The database didn't answer in time. Try again.";

    public const string OutOfDateText = "The database is out of date for this version of the app: its latest update hasn't been applied.";

    public const string TooLargeText = "A number is too large to save.";

    public const string FileInUseText = "Couldn't write the file. If it's open in another program, such as Excel, close it there and try again.";

    public const string NoAccessText = "Windows didn't allow the app to use that file or folder. Pick another one.";

    public static Failure Of(Exception exception) => exception switch
    {
        BusinessRuleException rule => new(rule.Message, IsRejection: true),
        DbUpdateConcurrencyException => new(
            "It was changed or deleted on another screen in the meantime. Open it again, then make the change.", IsRejection: true),
        DbUpdateException { InnerException: SqlException sql } => Of(sql),

        // SqlClient refuses a number with more digits than its column holds before sending it: "Parameter value
        // '1000000000000000.0000' is out of range."
        DbUpdateException { InnerException: ArgumentException argument } when argument.Message.Contains("out of range", StringComparison.OrdinalIgnoreCase) =>
            new(TooLargeText, IsRejection: true),
        SqlException sql => Of(sql),
        TimeoutException => new(TimedOutText, IsRejection: false),

        // A file the app writes or reads, such as an exported workbook: the user can close it elsewhere or pick another place.
        IOException => new(FileInUseText, IsRejection: true),
        UnauthorizedAccessException => new(NoAccessText, IsRejection: true),
        _ => new($"Something went wrong, and the details went to the log: {FirstLine(exception.Message)}", IsRejection: false),
    };

    /// <summary>
    /// SQL Server's error numbers: 2601 and 2627 a unique index, 547 a foreign key or check constraint (raised by
    /// ExecuteDeleteAsync directly, or by SaveChanges inside a DbUpdateException), 515 a missing value, 8115 a number too
    /// large for its column, 8152 and 2628 text too long, 1205 a deadlock, -2 a timeout, 207 and 208 a column or table the
    /// database doesn't have yet.
    /// </summary>
    private static Failure Of(SqlException sql) => sql.Number switch
    {
        2601 or 2627 => new(DuplicateText(sql.Message), IsRejection: true),
        547 when sql.Message.Contains("DELETE statement", StringComparison.OrdinalIgnoreCase) => new(InUseText, IsRejection: true),
        547 when sql.Message.Contains("CHECK constraint", StringComparison.OrdinalIgnoreCase) =>
            new("One of the values isn't allowed, so the change wasn't saved.", IsRejection: true),
        547 => new("Something this refers to no longer exists. It may have been deleted on another screen.", IsRejection: true),
        515 => new("A required value is missing, so the change wasn't saved.", IsRejection: true),
        8115 => new(TooLargeText, IsRejection: true),
        8152 or 2628 => new("Some text is too long to save.", IsRejection: true),
        1205 => new("The database was busy with another change. Try again.", IsRejection: false),
        -2 => new(TimedOutText, IsRejection: false),
        207 or 208 => new(OutOfDateText, IsRejection: false),
        _ when IsConnectionFailure(sql) => new(UnreachableText, IsRejection: false),
        _ => new($"The database reported a problem: {FirstLine(sql.Message)}", IsRejection: false),
    };

    /// <summary>
    /// A server not found or not running (-1, 2, 53, and the network's own numbers), a database it won't open (4060, 4064), a
    /// login it refuses (18456), or a connection broken mid-way, which SQL Server raises at severity 20 or above.
    /// </summary>
    private static bool IsConnectionFailure(SqlException sql) =>
        sql.Class >= 20
        || sql.Number is -1 or 2 or 53 or 64 or 121 or 233 or 1231 or 4060 or 4064 or 18456 or 10053 or 10054 or 10060 or 10061 or 11001;

    /// <summary>Which unique index refused it, by its name: IX_Products_Name, IX_UnitsOfMeasure_Abbreviation, or another.</summary>
    private static string DuplicateText(string message) =>
        message.Contains("_Name'", StringComparison.OrdinalIgnoreCase) ? DuplicateNameText
        : message.Contains("_Abbreviation'", StringComparison.OrdinalIgnoreCase) ? "That abbreviation is already in use."
        : "That's already there, and it can only be there once.";

    private static string FirstLine(string message)
    {
        var end = message.IndexOfAny(['\r', '\n']);
        return (end < 0 ? message : message[..end]).Trim();
    }
}
