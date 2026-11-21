using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TinapayanRMS.Benchmarks.Scenarios;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.UseCases.Access;

namespace TinapayanRMS.Benchmarks.Startup;

/// <summary>
/// The database the cold starts (ColdStart) and the comparison's starts of the window run on, which outlives any one of
/// them: made from the fixture by each side's own build, so its migrations are that side's, and dropped once the
/// comparison is done. Its name is one of the benchmarks' (BenchServer.Ensure).
/// </summary>
public static class StartDatabase
{
    /// <summary>The password a prepared database is given, which the comparison types into the window's sign-in panel.</summary>
    public const string Password = "bench-start";

    /// <summary>
    /// <paramref name="database"/> made afresh from the fixture, at this build's migrations, so a start finds it up to date as
    /// a normal start does; its password set to <see cref="Password"/> and its auto-lock off, for the window's sign-in panel.
    /// Writes its connection string (no password in it: pgpass.conf has that) as one line.
    /// </summary>
    public static async Task<int> PrepareAsync(string database)
    {
        await DropAsync(database);
        var restored = await BenchDatabases.RestoreAsync(database, CancellationToken.None);

        // Whatever password the copy came with goes, as scripts/Reset-Password.ps1 clears one; then the known one is set, as
        // the sign-in panel sets a first password.
        await using (var connection = new NpgsqlConnection(restored.ConnectionString))
        {
            await connection.OpenAsync();
            await using var clear = new NpgsqlCommand(
                "UPDATE access_settings SET password_hash = NULL, password_salt = NULL, password_changed_at = NULL WHERE password_hash IS NOT NULL",
                connection);
            await clear.ExecuteNonQueryAsync();
        }

        await using (var services = AppServices.Build(restored.ConnectionString))
        {
            var access = services.GetRequiredService<IAccessSettingsEditor>();
            await access.CreatePasswordAsync(Password, CancellationToken.None);
            await access.SetAutoLockMinutesAsync(0, CancellationToken.None);
        }

        NpgsqlConnection.ClearAllPools();
        await Console.Out.WriteLineAsync(restored.ConnectionString);
        return 0;
    }

    /// <summary>
    /// Drops <paramref name="database"/> if it's there, and deletes the backups the window's starts took of it (the day's
    /// automatic backup, in the backups folder every Windows user shares).
    /// </summary>
    public static async Task<int> DropAsync(string database)
    {
        await BenchDatabases.DropAsync(database);
        if (Directory.Exists(DatabaseBackups.DefaultFolder))
        {
            foreach (var backup in Directory.EnumerateFiles(DatabaseBackups.DefaultFolder, $"{database}_*"))
            {
                File.Delete(backup);
            }
        }

        return 0;
    }

    /// <summary>Drops every benchmark database a killed run left, with its backups.</summary>
    public static async Task<int> CleanAsync()
    {
        foreach (var database in await BenchDatabases.LeftoversAsync(CancellationToken.None))
        {
            await Console.Out.WriteLineAsync($"Dropping {database}");
            await DropAsync(database);
        }

        return 0;
    }
}
