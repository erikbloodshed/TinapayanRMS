<#
.SYNOPSIS
    Makes the PostgreSQL role the Recipe API reads the database as: one that can read every table and write none.

.DESCRIPTION
    Run it on the PC that holds the TinapayanRMS database, as the Windows user the Recipe API runs as, then install the API
    with -ReadOnlyUser tinapayanrms_reader. It creates the role, or gives it a new password if it's there already, with a
    password of its own that's never shown, and puts that password in this user's %APPDATA%\postgresql\pgpass.conf, beside
    the app's own role's, so no connection string or file of the API's holds it. Running it again is safe: the role gets a
    new password, and pgpass.conf has it in place of the old one.

    The role is a member of PostgreSQL's pg_read_all_data, so it reads every table, one a migration adds and a restored
    backup's included (a restore rebuilds the database, which would lose grants made inside it), and has no right to write
    or change anything. It can read the server's other databases too, as postgres can, but write none of them.

    It signs in as postgres to make the role, so postgres's password has to be in pgpass.conf as well.

.PARAMETER Role
    The role's name: lower-case letters, digits and underscores. Defaults to tinapayanrms_reader.

.PARAMETER Psql
    PostgreSQL's psql. Defaults to PostgreSQL 18's.
#>
[CmdletBinding()]
param(
    [string]$Role = 'tinapayanrms_reader',
    [string]$Server = 'localhost',
    [int]$Port = 5432,
    [string]$Psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
)

$ErrorActionPreference = 'Stop'

if ($Role -cnotmatch '^[a-z_][a-z0-9_]*$') {
    throw "A role name here is lower-case letters, digits and underscores, and $Role isn't."
}
if (-not (Test-Path -LiteralPath $Psql)) {
    throw "psql wasn't found at $Psql. Pass -Psql with where it is."
}

# Hex, so pgpass.conf needs no escaping (it reserves : and \) and SQL no quoting.
$bytes = New-Object byte[] 24
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $random.GetBytes($bytes)
}
finally {
    $random.Dispose()
}
$password = -join ($bytes | ForEach-Object { $_.ToString('x2') })

# Fed through standard input, so the password is on no command line another process could read; the server keeps only
# its SCRAM verifier. What psql prints comes back with the password masked, should an error quote the statement.
$sql = @"
\set ON_ERROR_STOP on
SELECT NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '$Role') AS missing \gset
\if :missing
CREATE ROLE $Role;
\endif
ALTER ROLE $Role WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '$password';
GRANT pg_read_all_data TO $Role;
"@
$ErrorActionPreference = 'Continue'
$output = $sql | & $Psql --host $Server --port $Port --username postgres --dbname postgres --no-password --no-psqlrc --quiet --file - 2>&1 | Out-String
$exitCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$output = $output.Replace($password, '<hidden>').Trim()
if ($output) {
    Write-Host $output
}
if ($exitCode -ne 0) {
    throw "psql couldn't make the role (exit code $exitCode). postgres's password has to be in pgpass.conf."
}

# This user's pgpass.conf: the role's line in place of any it had, the rest as they were.
$pgpass = Join-Path $env:APPDATA 'postgresql\pgpass.conf'
$lines = @()
if (Test-Path -LiteralPath $pgpass) {
    $lines = @(Get-Content -LiteralPath $pgpass | Where-Object { ($_ -split ':')[3] -cne $Role })
}
else {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $pgpass) | Out-Null
}
$lines += "${Server}:${Port}:*:${Role}:$password"
[IO.File]::WriteAllLines($pgpass, [string[]]$lines, (New-Object Text.UTF8Encoding $false))

# Signs in as the role, with the password just kept, to check it is what it should be.
$ErrorActionPreference = 'Continue'
$check = (& $Psql --host $Server --port $Port --username $Role --dbname postgres --no-password --no-psqlrc --tuples-only --no-align `
    --command "SELECT pg_has_role(current_user, 'pg_read_all_data', 'MEMBER') AND NOT pg_has_role(current_user, 'pg_write_all_data', 'MEMBER')" 2>&1 |
    Out-String).Trim()
$ErrorActionPreference = 'Stop'
if ($check -ne 't') {
    throw "$Role was made but couldn't sign in as it, or isn't a reader alone: $check"
}

Write-Host "Made $Role, which reads every table and writes none. Its password is in $pgpass."
Write-Host 'Now install the Recipe API to read as it:'
Write-Host "  powershell -ExecutionPolicy Bypass -File scripts\Install-RecipeApi.ps1 -ReadOnlyUser $Role"
