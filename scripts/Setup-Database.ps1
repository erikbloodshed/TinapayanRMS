<#
.SYNOPSIS
    Sets PostgreSQL up for TinapayanRMS on a new PC: the app's role, this Windows user's password file, and, from a backup,
    the shop's data.

.DESCRIPTION
    Run it on the PC that will hold TinapayanRMS's database, once PostgreSQL 18 is installed (EDB's installer), as the
    Windows user who'll use the app. Then install the app (the MSI) and open it. It's safe to run again: what's there
    already is left as it is.

      1. As PostgreSQL's postgres superuser (from pgpass.conf, or psql asks for the password chosen when PostgreSQL was
         installed), it creates the app's login role, tinapayanrms, with a new random password and the right to create
         databases (CREATEDB: a restore and the weekly check of a backup make databases of their own), unless it's there.
      2. It puts the role's password in this Windows user's %APPDATA%\postgresql\pgpass.conf, which the app, psql,
         pg_dump and pg_restore read, so no setting holds it. A role made by an earlier run, or for another Windows user,
         keeps its password: this asks for it once, when this user's file has none, and checks it.
      3. With -Backup, it restores that backup as the app's database: a backup copied from the old PC, by Also Copy To or
         Copy To.... Only onto a PC where the app's database isn't there yet; on one where it is, restore from the app's
         Settings > Database instead. A backup from an earlier version of the app is brought up to date the first time
         the app opens, after a backup of its own. Without -Backup, the app makes the database, empty, the first time it
         opens, and asks for a password to set.

    Every other Windows user of the PC who uses the app runs it too, for their own password file. For the Recipe API,
    install the MSI with it (msiexec /i <msi> ADDLOCAL=ALL), then run New-DatabaseReader.ps1 and Setup-RecipeApi.ps1.

.PARAMETER Backup
    A TinapayanRMS backup to restore as the app's database.

.PARAMETER Database
    The app's database. Defaults to tinapayanrms (src\TinapayanRMS.Maui\appsettings.json).

.PARAMETER Role
    The app's role. Defaults to tinapayanrms.

.PARAMETER PgPassFile
    The password file the role's password goes in. Defaults to this Windows user's, %APPDATA%\postgresql\pgpass.conf.

.PARAMETER PostgresBin
    PostgreSQL's bin folder, with psql and pg_restore. Defaults to PostgreSQL 18's.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\Setup-Database.ps1 -Backup E:\Backups\tinapayanrms_20261124_000512_auto.backup
#>
[CmdletBinding()]
param(
    [string]$Backup,
    [string]$Database = 'tinapayanrms',
    [string]$Role = 'tinapayanrms',
    [string]$Server = 'localhost',
    [int]$Port = 5432,
    [string]$PgPassFile = (Join-Path $env:APPDATA 'postgresql\pgpass.conf'),
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin'
)

$ErrorActionPreference = 'Stop'

if ($Database -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*$' -or $Role -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
    throw 'A database or role name here is letters, digits and underscores.'
}
$psql = Join-Path $PostgresBin 'psql.exe'
$pgRestore = Join-Path $PostgresBin 'pg_restore.exe'
foreach ($tool in $psql, $pgRestore) {
    if (-not (Test-Path -LiteralPath $tool)) { throw "$tool isn't there. Install PostgreSQL 18, or pass -PostgresBin with its bin folder." }
}
if ($Backup) { $Backup = (Resolve-Path -LiteralPath $Backup).Path }
$PgPassFile = [IO.Path]::GetFullPath($PgPassFile)

# A command line tool run with what it writes kept: Windows PowerShell turns a native tool's error output into errors.
# As the app's role, the tools read -PgPassFile (PGPASSFILE); as postgres, the user's own password file, or psql asks.
function Invoke-Tool([string]$File, [string[]]$Arguments, [switch]$AsRole) {
    $previous = $ErrorActionPreference
    $previousFile = $env:PGPASSFILE
    $ErrorActionPreference = 'Continue'
    try {
        if ($AsRole) { $env:PGPASSFILE = $PgPassFile }
        $output = @(& $File @Arguments 2>&1 | ForEach-Object { "$_" })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
        $env:PGPASSFILE = $previousFile
    }
    [pscustomobject]@{ ExitCode = $exitCode; Output = $output }
}

function Invoke-AsPostgres([string]$Sql) {
    # Not through Invoke-Tool: psql may need to ask for postgres's password, on this console.
    $result = & $psql -h $Server -p $Port -U postgres -d postgres -v ON_ERROR_STOP=1 -At -c $Sql
    if ($LASTEXITCODE -ne 0) { throw "psql as postgres failed (exit code $LASTEXITCODE) running: $Sql" }
    $result
}

function Invoke-AsRole([string]$Sql) {
    $run = Invoke-Tool $psql @('-h', $Server, '-p', "$Port", '-U', $Role, '-d', 'postgres', '--no-password', '-v', 'ON_ERROR_STOP=1', '-At', '-c', $Sql) -AsRole
    if ($run.ExitCode -ne 0) { throw "psql as $Role failed: $($run.Output -join ' ')" }
    $run.Output
}

# The role's line in the password file, in place of any other for the same server, port and role; every other line kept.
function Set-PasswordLine([string]$Password) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $PgPassFile) | Out-Null
    $existing = if (Test-Path -LiteralPath $PgPassFile) { @(Get-Content -LiteralPath $PgPassFile) } else { @() }
    $kept = @($existing | Where-Object { $_ -notmatch "^$([regex]::Escape($Server)):$($Port):\*:$([regex]::Escape($Role)):" })
    Set-Content -LiteralPath $PgPassFile -Value ($kept + "${Server}:${Port}:*:${Role}:$Password") -Encoding ascii
}

function Test-PasswordLine {
    (Test-Path -LiteralPath $PgPassFile) -and
        [bool](Get-Content -LiteralPath $PgPassFile | Where-Object { $_ -match "^($([regex]::Escape($Server))|\*):($Port|\*):\*:$([regex]::Escape($Role)):" })
}

# ---------------------------------------------------------------------------------------------------------------------
# The role, and this user's password for it.

if ((Invoke-AsPostgres "SELECT count(*) FROM pg_roles WHERE rolname = '$Role'") -eq '1') {
    Write-Host "The role $Role is there already: its password stays as it is."
    if (-not (Test-PasswordLine)) {
        $secure = Read-Host "Type $Role's password (it's in pgpass.conf of the Windows user who first ran this)" -AsSecureString
        $typed = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
        if ($typed -match '[:\\]') { throw 'A password with : or \ in it needs escaping in pgpass.conf: add its line by hand.' }
        Set-PasswordLine $typed
        Write-Host "Put it in $PgPassFile."
    }
}
else {
    # Letters and digits only, so it needs no quoting in SQL or in pgpass.conf. Create().GetBytes, which Windows
    # PowerShell's .NET Framework has.
    $chars = [char[]]'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789'
    $bytes = [byte[]]::new(32)
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    $random.GetBytes($bytes)
    $random.Dispose()
    $password = -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })

    Invoke-AsPostgres "CREATE ROLE $Role LOGIN CREATEDB PASSWORD '$password'" | Out-Null
    Set-PasswordLine $password
    Write-Host "Created the role $Role, and put its password in $PgPassFile."
}

# The password file's line works: the app will get in as this user.
try {
    Invoke-AsRole 'SELECT 1' | Out-Null
}
catch {
    throw "$Role couldn't sign in to PostgreSQL with the password in $PgPassFile. Put the right one in its line, then run this again. ($($_.Exception.Message))"
}
Write-Host "$Role signs in to PostgreSQL with the password in $PgPassFile."

# ---------------------------------------------------------------------------------------------------------------------
# The data, from a backup.

if ($Backup) {
    if ((Invoke-AsRole "SELECT count(*) FROM pg_database WHERE datname = '$Database'") -eq '1') {
        throw "The database $Database is there already, so the backup wasn't restored. To replace its data, open TinapayanRMS and use Restore from File... in Settings > Database, which takes a safety backup first."
    }

    # A file pg_restore can't read is refused before anything is made, as the app refuses it.
    $listing = Invoke-Tool $pgRestore @('--list', $Backup)
    if ($listing.ExitCode -ne 0 -or -not ($listing.Output -match 'dbname:')) {
        throw "$Backup isn't a backup of a whole database, so nothing was restored."
    }

    Write-Host "Restoring $Backup as $Database..."
    Invoke-AsRole "CREATE DATABASE `"$Database`" TEMPLATE template0 ENCODING 'UTF8'" | Out-Null
    # As the app restores one: all or nothing, its objects the role's own, whoever owned them where it was taken.
    $restore = Invoke-Tool $pgRestore @('-h', $Server, '-p', "$Port", '-U', $Role, '--no-password', '--no-owner', '--no-privileges',
        '--exit-on-error', '--single-transaction', '--dbname', $Database, $Backup) -AsRole
    if ($restore.ExitCode -ne 0) {
        Invoke-AsRole "DROP DATABASE IF EXISTS `"$Database`"" | Out-Null
        throw "The backup couldn't be restored, so no database was left: $($restore.Output -join ' ')"
    }
    Write-Host "Restored. The first time TinapayanRMS opens, it brings the data up to its version if the backup is from an earlier one."
}

Write-Host ''
Write-Host 'PostgreSQL is ready for TinapayanRMS. Install the app (open its MSI) and open it.'
if (-not $Backup) {
    Write-Host 'It makes its database, empty, the first time it opens, and asks for a password to set.'
}
