<#
.SYNOPSIS
    Clears TinapayanRMS's password, for when it's been forgotten: the app then asks for a new one as it opens.

.DESCRIPTION
    Run it on the PC that holds the TinapayanRMS database, as a Windows user whose %APPDATA%\postgresql\pgpass.conf has
    the app's role's password (the user the app runs as). It asks before it clears anything. The password's hash and salt
    are set to nothing in the access_settings table; the auto-lock's minutes stay as they were. The next time the app
    opens, or its window is locked, it asks for a new password, twice, and that sets it.

    It's the way back in, so it's as safe as that Windows account: whoever can sign in to Windows as that user can run it.

.PARAMETER Database
    The database to clear it in. Defaults to tinapayanrms, the app's (src\TinapayanRMS.Maui\appsettings.json).

.PARAMETER User
    The PostgreSQL role to connect as. Defaults to tinapayanrms, the app's.

.PARAMETER Psql
    PostgreSQL's psql. Defaults to PostgreSQL 18's.

.PARAMETER Force
    Clears it without asking first.
#>
[CmdletBinding()]
param(
    [string]$Database = 'tinapayanrms',
    [string]$User = 'tinapayanrms',
    [string]$Server = 'localhost',
    [int]$Port = 5432,
    [string]$Psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

if ($Database -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
    throw "A database name here is letters, digits and underscores, and $Database isn't."
}
if (-not (Test-Path -LiteralPath $Psql)) {
    throw "psql wasn't found at $Psql. Pass -Psql with where it is."
}

if (-not $Force) {
    $answer = Read-Host "Clear the TinapayanRMS password in the $Database database? The app will ask for a new one. (y/N)"
    if ($answer -notmatch '^(y|yes)$') {
        Write-Host 'Nothing was changed.'
        return
    }
}

$sql = 'UPDATE access_settings SET password_hash = NULL, password_salt = NULL, password_changed_at = NULL WHERE password_hash IS NOT NULL'
$ErrorActionPreference = 'Continue'
$output = & $Psql --host $Server --port $Port --username $User --dbname $Database --no-password --no-psqlrc `
    --set ON_ERROR_STOP=1 --command $sql 2>&1 | Out-String
$exitCode = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($exitCode -ne 0) {
    Write-Host $output.Trim()
    throw "psql couldn't clear the password (exit code $exitCode). $User's password has to be in pgpass.conf, and the database up to date (dotnet ef database update)."
}

if ($output.Trim() -eq 'UPDATE 0') {
    Write-Host 'There was no password to clear: the app already asks for a new one.'
}
else {
    Write-Host 'The password was cleared: the app asks for a new one as it opens, or when its window is next locked.'
}
