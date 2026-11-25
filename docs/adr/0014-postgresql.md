# 0014. PostgreSQL in place of SQL Server Express

**Status:** Accepted, 2026-10-21, at the owner's request (commit d942c7d1). Recorded after the fact on 2026-11-25, from
the build plan's log, PLAN.md ("Development Environment") and the commit, as the open item from the ADR review of
2026-11-23 asked; the log gave no reason, and the owner gave it that day: "Postgresql has been selected because it is
free and does not have limitations unlike Sql Server Express".

## Context

**PostgreSQL is free and has no limits on the database's size or the server's use.** SQL Server Express is free too,
but limited: Microsoft caps each database at 10 GB and the server at the lesser of one socket or four cores, and caps
the memory it uses for its buffer pool. The next edition up is paid.

Until 2026-10-21 the app's database was SQL Server Express on the developer's PC, through EF Core's SQL Server provider,
with Dapper's reads in T-SQL. Backups (2026-10-03) ran SQL Server's own `BACKUP … COPY_ONLY` and `RESTORE … REPLACE`,
which the SQL service's account executes, not the user: it can't write to a user's profile, so the backups' folder went
to `%ProgramData%\TinapayanRMS\Backups` and the app gave the service's account Modify on it.

The sibling repo `../TinapayanRMS_Web`, a copy of this one since 2026-10-10, already ran on PostgreSQL, with backups by
`pg_dump` and `pg_restore` (`PostgresTools`) and a restore swapped in by renaming a scratch database. TinapayanBMS's
database is on the same PostgreSQL server.

## Decision

- **PostgreSQL 18** (EDB's installer, service `postgresql-x64-18`, `localhost:5432`) is the one database, for the app,
  `dotnet ef`, the servers and the tests. The app connects as the `tinapayanrms` role (`CREATEDB`, not a superuser),
  its password in each user's `pgpass.conf`, so no connection string carries one.
- **EF Core on Npgsql, set up in one place** (`TinapayanRMSDbContext.Configure`): every table and column snake_case
  (`EFCore.NamingConventions`), `SetPostgresVersion(18, 0)`, a nondeterministic ICU collation (`case_insensitive`,
  `und-u-ks-level2`) on every text column so the unique names stay case-insensitive, and every `DateTime` a
  `timestamp without time zone`, since the app stamps local time.
- **One fresh `InitialCreate`** in place of the SQL Server migrations, and the Dapper SQL rewritten for PostgreSQL
  (`unnest` over `int[]` id lists, `WITH RECURSIVE`); `Failure` reads PostgreSQL's SQLSTATE codes.
- **Backups are `pg_dump`'s custom format, run as the user**; a restore goes into a `<db>_restoring` scratch database,
  is brought up to date there, and is swapped in by renaming, so a refused or failed restore never touches the data.
- The dev data was copied table by table and checked identical.

## Consequences

- The backups run as the user, so the folder needs only `BUILTIN\Users`' Modify, and a restore can't leave the
  database half-replaced; the restore through a scratch database also became how the golden master and the benchmarks
  restore their fixture (ADR 0008).
- The tests run on the same server as the app, each project on databases of its own reset by Respawn's Postgres
  adapter, and GitHub CI installs EDB's PostgreSQL 18 on its runner to match (ADR 0013).
- `psql`, `pg_dump` and `pg_restore` aren't on the PATH; the app and the scripts find them in
  `C:\Program Files\PostgreSQL\18\bin`, and the scripts, the run skill, the manuals and the context's
  `SetPostgresVersion` name the major version.
- A backup made by one major version's `pg_dump` doesn't restore on an older server, so the shop's PC and the
  developer's move between majors together. A move to PostgreSQL 19 (planned once 19.1 is out) stays within this
  decision, but changes the version wherever it's named, and is measured before and after (ADR 0008).
- Backups taken before the move are SQL Server's and can't be restored. SQL Server Express and LocalDB are still
  installed on the developer's PC with the data from before it, unused.

## Rejected

- **Staying on SQL Server Express.** Its limits (the owner's reason), and, as the log shows, backups run by the
  service's account, so a folder the service can write to and grants for it, and a restore that replaced the database
  in place.
- **A paid edition of SQL Server.** It lifts the limits, at a cost PostgreSQL doesn't have.
- **Porting the SQL Server migrations.** Their SQL was SQL Server's; one generated `InitialCreate` started the history
  afresh, with the data copied across rather than migrated.
