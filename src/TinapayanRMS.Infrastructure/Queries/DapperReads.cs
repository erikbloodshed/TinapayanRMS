using System.Data.Common;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace TinapayanRMS.Infrastructure.Queries;

/// <summary>
/// Reads go through Dapper and writes through EF Core (PLAN.md, "Reads through Dapper, writes through EF
/// Core"). A read borrows a DbContext's connection, so it uses the same connection string as EF, and Dapper
/// opens and closes the connection around the query when EF hasn't already opened it. On a context holding a
/// transaction, a read joins that transaction (<see cref="Command"/>), so a save's untracked reads can come
/// through here too.
/// </summary>
internal static class DapperReads
{
    /// <summary>
    /// The ids in <c>@ids</c> (a <see cref="IdList"/>) as a one-column table, for <c>WHERE x IN (…)</c>. One
    /// parameter however many ids, so it never runs into SQL Server's limit of 2,100 parameters.
    /// </summary>
    public const string IdsTable = "SELECT CAST(value AS int) FROM STRING_SPLIT(@ids, ',')";

    public static string IdList(IEnumerable<int> ids) => string.Join(',', ids);

    public static DbConnection Connection(this TinapayanRMSDbContext dbContext) => dbContext.Database.GetDbConnection();

    /// <summary>A command on <paramref name="dbContext"/>'s connection, in its transaction if it has one.</summary>
    public static CommandDefinition Command(
        this TinapayanRMSDbContext dbContext, string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, dbContext.Database.CurrentTransaction?.GetDbTransaction(), cancellationToken: cancellationToken);

    public static async Task<IReadOnlyList<T>> QueryAsync<T>(
        this TinapayanRMSDbContext dbContext, string sql, object? parameters, CancellationToken cancellationToken) =>
        (await dbContext.Connection().QueryAsync<T>(dbContext.Command(sql, parameters, cancellationToken))).AsList();

    public static Task<T?> QuerySingleOrDefaultAsync<T>(
        this TinapayanRMSDbContext dbContext, string sql, object? parameters, CancellationToken cancellationToken) =>
        dbContext.Connection().QuerySingleOrDefaultAsync<T?>(dbContext.Command(sql, parameters, cancellationToken));

    public static async Task<IReadOnlyList<T>> QueryAsync<T>(
        this IDbContextFactory<TinapayanRMSDbContext> dbContextFactory, string sql, object? parameters, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.QueryAsync<T>(sql, parameters, cancellationToken);
    }

    public static async Task<T?> QuerySingleOrDefaultAsync<T>(
        this IDbContextFactory<TinapayanRMSDbContext> dbContextFactory, string sql, object? parameters, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.QuerySingleOrDefaultAsync<T>(sql, parameters, cancellationToken);
    }

    /// <summary>For a read that needs more than one plain query, such as a multi-mapped join.</summary>
    public static async Task<TResult> ReadAsync<TResult>(
        this IDbContextFactory<TinapayanRMSDbContext> dbContextFactory, Func<TinapayanRMSDbContext, Task<TResult>> read, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await read(dbContext);
    }
}
