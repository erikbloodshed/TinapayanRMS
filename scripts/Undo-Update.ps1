<#
.SYNOPSIS
    Goes back from an update that went wrong: puts the data back as it was just before the update, keeps the updated data
    aside, and installs the older version.

.DESCRIPTION
    The first time a new version of TinapayanRMS opens, it backs the data up (a "Before update" backup) and then brings it
    up to its version. An older version won't open data a newer one has updated, and the MSI won't install an older version
    over a newer one. So to go back:

      1. Close TinapayanRMS on every PC that uses this data.
      2. Run this, on the PC that holds the database, as the Windows user whose %APPDATA%\postgresql\pgpass.conf has the
         app's role's password (the user the app runs as). With -Msi, run it in an administrator PowerShell.

    It asks first, saying when the backup it puts back was taken. Then:

      - The data as it is now (updated) is renamed aside, to <database>_before_undo_<date>_<time>, and kept. Nothing in it
        is deleted: what was saved since the update is there, to copy back by hand if it's needed.
      - The newest Before update backup of the database in the backups folder (or -Backup) is restored as the app's
        database, all or nothing, as the app's own restore does. If it fails, the updated data gets its name back.
      - With -Msi, the installed TinapayanRMS (the MSI's, found by its upgrade code) is uninstalled, and the older MSI
        installed. Without it, it says how: uninstall TinapayanRMS from Settings > Apps, then open the older MSI; or, for a
        copy installed with Install-TinapayanRMS.ps1, run that script from a checkout of the older commit.

    The older version then opens the restored data as its own. Installing the newer version again later updates it again,
    after another Before update backup. Drop the database set aside once it's no longer needed (the last line says how).

.PARAMETER Msi
    The older version's MSI, to install in place of the one installed. Needs an administrator PowerShell.

.PARAMETER Backup
    The backup to put back. Defaults to the newest Before update backup of the database in -BackupsFolder.

.PARAMETER Database
    The app's database. Defaults to tinapayanrms (src\TinapayanRMS.Maui\appsettings.json).

.PARAMETER User
    The PostgreSQL role to work as, the database's owner. Defaults to tinapayanrms, the app's.

.PARAMETER BackupsFolder
    Where the app keeps its backups. Defaults to %ProgramData%\TinapayanRMS\Backups.

.PARAMETER PostgresBin
    PostgreSQL's bin folder, with psql and pg_restore. Defaults to PostgreSQL 18's.

.PARAMETER Force
    Goes ahead without asking first.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\Undo-Update.ps1 -Msi D:\TinapayanRMS-1.0.512-x64.msi
#>
[CmdletBinding()]
param(
    [string]$Msi,
    [string]$Backup,
    [string]$Database = 'tinapayanrms',
    [string]$User = 'tinapayanrms',
    [string]$Server = 'localhost',
    [int]$Port = 5432,
    [string]$BackupsFolder = (Join-Path $env:ProgramData 'TinapayanRMS\Backups'),
    [string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# The MSI's upgrade code (installer\Package.wxs): the app's for good, whatever its version.
$upgradeCode = '{4E4A8926-1F88-40FF-A780-B8EC774B5CE3}'

if ($Database -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*$' -or $User -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
    throw 'A database or role name here is letters, digits and underscores.'
}
$psql = Join-Path $PostgresBin 'psql.exe'
$pgRestore = Join-Path $PostgresBin 'pg_restore.exe'
foreach ($tool in $psql, $pgRestore) {
    if (-not (Test-Path -LiteralPath $tool)) { throw "$tool isn't there. Pass -PostgresBin with PostgreSQL 18's bin folder." }
}

if ($Msi) {
    $Msi = (Resolve-Path -LiteralPath $Msi).Path
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Installing an MSI needs an administrator: open PowerShell as an administrator and run this again.'
    }
}

$open = @(Get-Process TinapayanRMS.Maui -ErrorAction SilentlyContinue)
if ($open) {
    throw "TinapayanRMS is open on this PC (process $($open[0].Id)). Close it, and on every other PC that uses this data, then run this again."
}

# A command line tool run with what it writes kept: Windows PowerShell turns a native tool's error output into errors.
function Invoke-Tool([string]$File, [string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& $File @Arguments 2>&1 | ForEach-Object { "$_" })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    [pscustomobject]@{ ExitCode = $exitCode; Output = $output }
}

# A statement on the server's maintenance database (postgres), as the app's role: where a database is renamed, made and
# dropped. --no-password, so a missing pgpass.conf line fails at once rather than waiting at a prompt.
function Invoke-Sql([string]$Sql) {
    $run = Invoke-Tool $psql @('-h', $Server, '-p', "$Port", '-U', $User, '-d', 'postgres', '--no-password', '-v', 'ON_ERROR_STOP=1', '-At', '-c', $Sql)
    if ($run.ExitCode -ne 0) { throw "psql failed: $($run.Output -join ' ')" }
    $run.Output
}

function Test-Database([string]$Name) { (Invoke-Sql "SELECT count(*) FROM pg_database WHERE datname = '$Name'") -eq '1' }

# Everyone out of the database, kept out: the app's role's own connections are ended, and another role's (the Recipe API's
# reader, whose idle connections close by themselves after 10 seconds) waited for, for up to 30 seconds.
function Close-Database([string]$Name) {
    Invoke-Sql "ALTER DATABASE `"$Name`" WITH ALLOW_CONNECTIONS false" | Out-Null
    $giveUpAt = (Get-Date).AddSeconds(30)
    while ($true) {
        # As the app's restore asks it (DatabaseBackups.DisconnectEveryoneAsync): how many are in it, ending the role's own.
        $left = (Invoke-Sql ("SELECT count(*), count(pg_terminate_backend(pid)) FILTER (WHERE usename = current_user) FROM pg_stat_activity " +
            "WHERE datname = '$Name' AND pid <> pg_backend_pid() AND backend_type = 'client backend'")) -split '\|' | Select-Object -First 1
        if ($left -eq '0') { return }
        if ((Get-Date) -gt $giveUpAt) {
            Invoke-Sql "ALTER DATABASE `"$Name`" WITH ALLOW_CONNECTIONS true" | Out-Null
            throw "Something else kept using $Name, so nothing was changed. Close it (the Recipe API, pgAdmin, TinapayanRMS on another PC), then run this again."
        }
        Start-Sleep -Milliseconds 500
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# The backup to put back.

if ($Backup) {
    $Backup = (Resolve-Path -LiteralPath $Backup).Path
}
else {
    # BackupRules' names: <database>_<yyyyMMdd>_<HHmmss>_before-update.backup, which sort by when they were taken.
    $found = Get-ChildItem -LiteralPath $BackupsFolder -Filter "${Database}_*_before-update.backup" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match "^$([regex]::Escape($Database))_\d{8}_\d{6}_before-update\.backup$" } |
        Sort-Object Name | Select-Object -Last 1
    if (-not $found) {
        throw "There's no Before update backup of $Database in $BackupsFolder, so there's nothing to go back to. Pass -Backup with the backup to put back."
    }
    $Backup = $found.FullName
}
$takenAt = if ([IO.Path]::GetFileName($Backup) -match '_(\d{8}_\d{6})_') {
    [datetime]::ParseExact($Matches[1], 'yyyyMMdd_HHmmss', [Globalization.CultureInfo]::InvariantCulture)
} else { (Get-Item -LiteralPath $Backup).LastWriteTime }

# A file pg_restore can't read is refused before anything changes, as the app refuses it.
$listing = Invoke-Tool $pgRestore @('--list', $Backup)
if ($listing.ExitCode -ne 0 -or -not ($listing.Output -match 'dbname:')) {
    throw "$Backup isn't a backup of a whole database, so nothing was changed."
}

if (-not (Test-Database $Database)) {
    throw "There's no database $Database on this server, so there's nothing to go back from. To put a backup on a new PC, use Setup-Database.ps1 -Backup."
}
$aside = '{0}_before_undo_{1}' -f $Database, (Get-Date -Format 'yyyyMMdd_HHmmss')

Write-Host "This puts back TinapayanRMS's data as it was on $($takenAt.ToString('MMMM d, yyyy \a\t h:mm tt')), from:"
Write-Host "  $Backup"
Write-Host "Anything saved since then isn't in it. The data as it is now is kept aside, as the database $aside."
if ($Msi) { Write-Host "Then TinapayanRMS is uninstalled, and $Msi installed." }
if (-not $Force) {
    $answer = Read-Host 'Go ahead? (y/n)'
    if ($answer -notmatch '^(y|yes)$') { Write-Host 'Nothing was changed.'; exit 1 }
}

# ---------------------------------------------------------------------------------------------------------------------
# The data: the updated database aside, the backup restored in its place.

Write-Host "Setting the updated data aside as $aside..."
Close-Database $Database
try {
    Invoke-Sql "ALTER DATABASE `"$Database`" RENAME TO `"$aside`"" | Out-Null
}
catch {
    Invoke-Sql "ALTER DATABASE `"$Database`" WITH ALLOW_CONNECTIONS true" | Out-Null
    throw
}
Invoke-Sql "ALTER DATABASE `"$aside`" WITH ALLOW_CONNECTIONS true" | Out-Null

Write-Host 'Restoring the backup...'
try {
    Invoke-Sql "CREATE DATABASE `"$Database`" TEMPLATE template0 ENCODING 'UTF8'" | Out-Null
    # As the app restores one: all or nothing, its objects the restoring role's own.
    $restore = Invoke-Tool $pgRestore @('-h', $Server, '-p', "$Port", '-U', $User, '--no-password', '--no-owner', '--no-privileges',
        '--exit-on-error', '--single-transaction', '--dbname', $Database, $Backup)
    if ($restore.ExitCode -ne 0) { throw "pg_restore failed: $($restore.Output -join ' ')" }
}
catch {
    Write-Host 'The restore failed: the updated data gets its name back.'
    if (Test-Database $Database) {
        Close-Database $Database
        Invoke-Sql "DROP DATABASE `"$Database`"" | Out-Null
    }
    Close-Database $aside
    Invoke-Sql "ALTER DATABASE `"$aside`" RENAME TO `"$Database`"" | Out-Null
    Invoke-Sql "ALTER DATABASE `"$Database`" WITH ALLOW_CONNECTIONS true" | Out-Null
    throw
}
Write-Host "Restored. TinapayanRMS's data is as it was on $($takenAt.ToString('MMMM d, yyyy \a\t h:mm tt'))."

# ---------------------------------------------------------------------------------------------------------------------
# The app.

if ($Msi) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    foreach ($product in @($installer.RelatedProducts($upgradeCode))) {
        Write-Host "Uninstalling TinapayanRMS ($product)..."
        $remove = Start-Process msiexec.exe -ArgumentList '/x', $product, '/passive', '/norestart' -Wait -PassThru
        if ($remove.ExitCode -notin 0, 3010) { throw "Uninstalling TinapayanRMS failed (msiexec exit code $($remove.ExitCode)). The data is restored: uninstall it from Settings > Apps, then open $Msi." }
    }
    Write-Host "Installing $Msi..."
    $install = Start-Process msiexec.exe -ArgumentList '/i', "`"$Msi`"", '/passive', '/norestart' -Wait -PassThru
    if ($install.ExitCode -notin 0, 3010) { throw "Installing $Msi failed (msiexec exit code $($install.ExitCode)). The data is restored: open $Msi to install it." }
    Write-Host 'Installed.'
}
else {
    Write-Host ''
    Write-Host 'Now install the older version, before anyone opens TinapayanRMS: the newer one would update the data again.'
    Write-Host '  An MSI install: uninstall TinapayanRMS from Settings > Apps, then open the older MSI.'
    Write-Host '  An install by Install-TinapayanRMS.ps1: run that script from a checkout of the older commit.'
}

Write-Host ''
Write-Host "The updated data is kept as the database $aside. Once it's no longer needed, drop it:"
Write-Host "  & `"$psql`" -h $Server -U $User -d postgres -c 'DROP DATABASE `"$aside`"'"
