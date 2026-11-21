using System.Data.Common;
using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// What a screen tells the user when something failed, and how it's logged. A rule's refusal is shown in its own words.
/// What the database said is put plainly, by its error code: its own text is technical, and the same exception type covers
/// very different things (a duplicate name, a row still in use, a server that isn't running), so one fixed message per
/// exception type would say the wrong thing. Anything else is a bug: it's still shown, so nothing fails silently, and it's
/// logged as an error with its stack.
/// </summary>
/// <remarks>
/// It reads the database's errors through what .NET itself gives every driver: <see cref="DbException"/>, its SQLSTATE
/// (<see cref="DbException.SqlState"/>), and the fields Npgsql copies into <see cref="Exception.Data"/> (ConstraintName,
/// MessageText). EF Core's update exceptions it knows by their type's name. So the layers below the screens can say what
/// failed without knowing Npgsql or EF Core (docs/adr/0002, phase 4); FailureTests (Infrastructure.Tests) checks every case
/// against the real exceptions.
/// </remarks>
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

    /// <summary>EF Core's exception for a save the database refused, with its error inside, and its concurrency one, which derives from it.</summary>
    private const string UpdateException = "Microsoft.EntityFrameworkCore.DbUpdateException";

    private const string ConcurrencyException = "Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException";

    public static Failure Of(Exception exception) => exception switch
    {
        BusinessRuleException rule => new(rule.Message, IsRejection: true),

        // Since Npgsql 11 a deadlock or serialization failure in SaveChanges comes as a concurrency exception, even
        // inside our own transaction (only one raised by CommitAsync doesn't), with the server's error inside it.
        { InnerException: DbException { SqlState: not null } server } when Is(exception, ConcurrencyException) => Of(server),
        _ when Is(exception, ConcurrencyException) => new(
            "It was changed or deleted on another screen in the meantime. Open it again, then make the change.", IsRejection: true),
        { InnerException: DbException database } when Is(exception, UpdateException) => Of(database),
        DbException database => Of(database),
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
    /// What the server refused, by its SQLSTATE code; and, with no code, what the driver threw without the server's word
    /// for it: a command that ran out of time (it wraps the TimeoutException), or a connection that couldn't be made or
    /// was lost.
    /// </summary>
    private static Failure Of(DbException database) => database.SqlState switch
    {
        null when database.InnerException is TimeoutException => new(TimedOutText, IsRejection: false),
        null => new(UnreachableText, IsRejection: false),
        var code => Of(code, database),
    };

    /// <summary>
    /// PostgreSQL's error codes: 23505 a unique index, 23001 a Restrict foreign key refusing a delete and 23503 any other
    /// foreign key (raised by ExecuteDeleteAsync directly, or by SaveChanges inside a DbUpdateException), 23514 a check
    /// constraint, 23502 a missing value, 22003 a number too large for its column, 22001 text too long, 40P01 and 40001 a
    /// deadlock or a clash with another change, 57014 a command cancelled for taking too long, 42703 and 42P01 a column or
    /// table the database doesn't have yet, and classes 08, 28 and 57P0 a connection refused or lost, 3D000 a database
    /// that isn't there.
    /// </summary>
    private static Failure Of(string code, DbException server) => code switch
    {
        "23505" => new(DuplicateText(Field(server, "ConstraintName")), IsRejection: true),
        "23001" => new(InUseText, IsRejection: true),
        "23503" when MessageText(server).StartsWith("update or delete", StringComparison.OrdinalIgnoreCase) => new(InUseText, IsRejection: true),
        "23503" => new("Something this refers to no longer exists. It may have been deleted on another screen.", IsRejection: true),
        "23514" => new("One of the values isn't allowed, so the change wasn't saved.", IsRejection: true),
        "23502" => new("A required value is missing, so the change wasn't saved.", IsRejection: true),
        "22003" => new(TooLargeText, IsRejection: true),
        "22001" => new("Some text is too long to save.", IsRejection: true),
        "40P01" or "40001" => new("The database was busy with another change. Try again.", IsRejection: false),
        "57014" => new(TimedOutText, IsRejection: false),
        "42703" or "42P01" => new(OutOfDateText, IsRejection: false),
        "3D000" => new(UnreachableText, IsRejection: false),
        _ when code.StartsWith("08", StringComparison.Ordinal) || code.StartsWith("28", StringComparison.Ordinal)
            || code.StartsWith("57P0", StringComparison.Ordinal) => new(UnreachableText, IsRejection: false),
        _ => new($"The database reported a problem: {FirstLine(MessageText(server))}", IsRejection: false),
    };

    /// <summary>Which unique index refused it, by its name: ix_products_name, ix_units_of_measure_abbreviation, or another.</summary>
    private static string DuplicateText(string? index) =>
        index?.EndsWith("_name", StringComparison.OrdinalIgnoreCase) == true ? DuplicateNameText
        : index?.EndsWith("_abbreviation", StringComparison.OrdinalIgnoreCase) == true ? "That abbreviation is already in use."
        : "That's already there, and it can only be there once.";

    /// <summary>The server's own message, without the code the driver puts before it; the exception's message without one.</summary>
    private static string MessageText(DbException server) => Field(server, "MessageText") ?? server.Message;

    /// <summary>A field of the server's error, as the driver copies it into the exception's Data; null when it has none.</summary>
    private static string? Field(DbException server, string name) => server.Data.Contains(name) ? server.Data[name] as string : null;

    /// <summary>Whether <paramref name="exception"/> is of the type named <paramref name="typeName"/>, or derives from it.</summary>
    private static bool Is(Exception exception, string typeName)
    {
        for (var type = exception.GetType(); type is not null; type = type.BaseType)
        {
            if (type.FullName == typeName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The first line of a message: what an exception's says, without what it carries after that.</summary>
    public static string FirstLine(string message)
    {
        var end = message.IndexOfAny(['\r', '\n']);
        return (end < 0 ? message : message[..end]).Trim();
    }
}
