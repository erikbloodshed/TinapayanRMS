using Microsoft.EntityFrameworkCore;
using Npgsql;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// What a screen tells the user when something failed, and how it's logged. A rule's refusal is shown in its own words.
/// What the database said is put plainly, by its error code: its own text is technical, and the same exception type covers
/// very different things (a duplicate name, a row still in use, a server that isn't running), so one fixed message per
/// exception type would say the wrong thing. Anything else is a bug: it's
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

    public const string UnreachableText = "Couldn't reach the database. Check that PostgreSQL is running, then try again.";

    public const string TimedOutText = "The database didn't answer in time. Try again.";

    public const string OutOfDateText = "The database is out of date for this version of the app: its latest update hasn't been applied.";

    public const string TooLargeText = "A number is too large to save.";

    public const string FileInUseText = "Couldn't write the file. If it's open in another program, such as Excel, close it there and try again.";

    public const string NoAccessText = "Windows didn't allow the app to use that file or folder. Pick another one.";

    public const string FileMissingText = "That file or folder isn't there any more. It may have been moved or deleted.";

    /// <summary>What Windows reports for a file another program has open (ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION).</summary>
    private const int SharingViolation = unchecked((int)0x80070020);

    private const int LockViolation = unchecked((int)0x80070021);

    public static Failure Of(Exception exception) => exception switch
    {
        BusinessRuleException rule => new(rule.Message, IsRejection: true),

        // Since Npgsql 11 a deadlock or serialization failure in SaveChanges comes as a concurrency exception, even
        // inside our own transaction (only one raised by CommitAsync doesn't), with the server's error inside it.
        DbUpdateConcurrencyException { InnerException: PostgresException postgres } => Of(postgres),
        DbUpdateConcurrencyException => new(
            "It was changed or deleted on another screen in the meantime. Open it again, then make the change.", IsRejection: true),
        DbUpdateException { InnerException: NpgsqlException npgsql } => Of(npgsql),
        NpgsqlException npgsql => Of(npgsql),
        TimeoutException => new(TimedOutText, IsRejection: false),

        // A file the app writes or reads, such as an exported workbook: the user can close it elsewhere or pick another place.
        // Only one another program has open is said to be that: a missing file, or a full disk, has its own words.
        FileNotFoundException or DirectoryNotFoundException => new(FileMissingText, IsRejection: true),
        IOException { HResult: SharingViolation or LockViolation } => new(FileInUseText, IsRejection: true),
        IOException io => new($"Couldn't read or write the file: {FirstLine(io.Message)}", IsRejection: true),
        UnauthorizedAccessException => new(NoAccessText, IsRejection: true),

        // pg_dump or pg_restore said why it stopped, in words whoever looks into it needs.
        PostgresToolException tool => new($"PostgreSQL's {tool.Tool} reported a problem: {FirstLine(tool.Error)}", IsRejection: false),
        _ => new($"Something went wrong, and the details went to the log: {FirstLine(exception.Message)}", IsRejection: false),
    };

    /// <summary>
    /// What the server refused, by its SQLSTATE code; a command that ran out of time (Npgsql wraps the TimeoutException);
    /// and anything else Npgsql throws without the server's word for it, which is a connection that couldn't be made or
    /// was lost.
    /// </summary>
    private static Failure Of(NpgsqlException npgsql) => npgsql switch
    {
        PostgresException postgres => Of(postgres),
        { InnerException: TimeoutException } => new(TimedOutText, IsRejection: false),
        _ => new(UnreachableText, IsRejection: false),
    };

    /// <summary>
    /// PostgreSQL's error codes: 23505 a unique index, 23001 a Restrict foreign key refusing a delete and 23503 any other
    /// foreign key (raised by ExecuteDeleteAsync directly, or by SaveChanges inside a DbUpdateException), 23514 a check constraint, 23502 a missing value, 22003 a number too large
    /// for its column, 22001 text too long, 40P01 and 40001 a deadlock or a clash with another change, 57014 a command
    /// cancelled for taking too long, 42703 and 42P01 a column or table the database doesn't have yet, and classes 08, 28
    /// and 57P0 a connection refused or lost, 3D000 a database that isn't there.
    /// </summary>
    private static Failure Of(PostgresException postgres) => postgres.SqlState switch
    {
        PostgresErrorCodes.UniqueViolation => new(DuplicateText(postgres.ConstraintName), IsRejection: true),
        PostgresErrorCodes.RestrictViolation => new(InUseText, IsRejection: true),
        PostgresErrorCodes.ForeignKeyViolation when postgres.MessageText.StartsWith("update or delete", StringComparison.OrdinalIgnoreCase) =>
            new(InUseText, IsRejection: true),
        PostgresErrorCodes.ForeignKeyViolation =>
            new("Something this refers to no longer exists. It may have been deleted on another screen.", IsRejection: true),
        PostgresErrorCodes.CheckViolation => new("One of the values isn't allowed, so the change wasn't saved.", IsRejection: true),
        PostgresErrorCodes.NotNullViolation => new("A required value is missing, so the change wasn't saved.", IsRejection: true),
        PostgresErrorCodes.NumericValueOutOfRange => new(TooLargeText, IsRejection: true),
        PostgresErrorCodes.StringDataRightTruncation => new("Some text is too long to save.", IsRejection: true),
        PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure =>
            new("The database was busy with another change. Try again.", IsRejection: false),
        PostgresErrorCodes.QueryCanceled => new(TimedOutText, IsRejection: false),
        PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.UndefinedTable => new(OutOfDateText, IsRejection: false),
        PostgresErrorCodes.InvalidCatalogName => new(UnreachableText, IsRejection: false),
        { } code when code.StartsWith("08", StringComparison.Ordinal) || code.StartsWith("28", StringComparison.Ordinal)
            || code.StartsWith("57P0", StringComparison.Ordinal) => new(UnreachableText, IsRejection: false),
        _ => new($"The database reported a problem: {FirstLine(postgres.MessageText)}", IsRejection: false),
    };

    /// <summary>Which unique index refused it, by its name: ix_products_name, ix_units_of_measure_abbreviation, or another.</summary>
    private static string DuplicateText(string? index) =>
        index?.EndsWith("_name", StringComparison.OrdinalIgnoreCase) == true ? DuplicateNameText
        : index?.EndsWith("_abbreviation", StringComparison.OrdinalIgnoreCase) == true ? "That abbreviation is already in use."
        : "That's already there, and it can only be there once.";

    /// <summary>The first line of a message: what an exception's says, without what it carries after that.</summary>
    public static string FirstLine(string message)
    {
        var end = message.IndexOfAny(['\r', '\n']);
        return (end < 0 ? message : message[..end]).Trim();
    }
}
