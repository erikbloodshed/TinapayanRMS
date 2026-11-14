<#
.SYNOPSIS
    Installs the Recipe API on this PC for the Windows user running it, or updates the copy already installed.

.DESCRIPTION
    Publishes TinapayanRMS.RecipeApi (Release), copies it to a folder of the user's own, gives it a key if it has none, puts
    a "TinapayanRMS Recipe API" shortcut in the Startup folder, which starts it hidden at sign-in, and starts it now. It's
    what lets TinapayanBMS, on this PC or another one on the LAN, read the recipes. Whatever an earlier version left in that
    folder goes, apart from its recipeapi.json and its logs.

    The API runs as the signed-in Windows user, not as a Windows service: PostgreSQL is reached with the password in that
    user's %APPDATA%\postgresql\pgpass.conf. It reads the database in appsettings.json (the app's own) through a read-only
    connection, as the role recipeapi.json names under Database:ReadOnlyUser (see -ReadOnlyUser), and listens where
    recipeapi.json says: port 5290 on every address.

    Every app that calls it has to send its key, RecipeApi:Key in the installed recipeapi.json. A first install makes one
    and shows it, to go into TinapayanBMS's Settings. Installing again keeps the key that's there, one put there by hand too.

    Run it in an administrator PowerShell to also let other PCs on the LAN in (a Windows Firewall rule for its port, on
    private networks, from the local subnet only). Without one it says the command to run.

.PARAMETER InstallFolder
    Where the API goes. Defaults to %LocalAppData%\Programs\TinapayanRMS.RecipeApi.

.PARAMETER NoStartup
    Don't start the API at sign-in.

.PARAMETER ReadOnlyUser
    The PostgreSQL role the API reads the database as, such as tinapayanrms_reader, which New-DatabaseReader.ps1 makes:
    one that can read and never write. Kept in the installed recipeapi.json (Database:ReadOnlyUser), so installing again
    without it keeps the one set. With none ever set, the API reads as the app's own role, in read-only transactions.
#>
[CmdletBinding()]
param(
    [string]$InstallFolder = (Join-Path $env:LOCALAPPDATA 'Programs\TinapayanRMS.RecipeApi'),
    [switch]$NoStartup,
    [string]$ReadOnlyUser
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\TinapayanRMS.RecipeApi\TinapayanRMS.RecipeApi.csproj'
$exe = Join-Path $InstallFolder 'TinapayanRMS.RecipeApi.exe'
$settingsFile = Join-Path $InstallFolder 'recipeapi.json'
$port = 5290

function Read-Settings {
    Get-Content -LiteralPath $settingsFile -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Set-ApiKey([string]$Key) {
    $settings = Read-Settings
    if ($settings.RecipeApi.Key -ceq $Key) {
        return
    }

    if (-not $settings.RecipeApi) {
        $settings | Add-Member -NotePropertyName RecipeApi -NotePropertyValue ([pscustomobject]@{}) -Force
    }
    $settings.RecipeApi | Add-Member -NotePropertyName Key -NotePropertyValue $Key -Force

    # Without a byte order mark, which is what PowerShell 5.1's own UTF-8 would put at the start of the file.
    $path = (Resolve-Path -LiteralPath $settingsFile).ProviderPath
    [IO.File]::WriteAllText($path, ($settings | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
}

function Set-ReadOnlyUser([string]$User) {
    $settings = Read-Settings
    if (-not $settings.Database) {
        $settings | Add-Member -NotePropertyName Database -NotePropertyValue ([pscustomobject]@{}) -Force
    }
    $settings.Database | Add-Member -NotePropertyName ReadOnlyUser -NotePropertyValue $User -Force

    $path = (Resolve-Path -LiteralPath $settingsFile).ProviderPath
    [IO.File]::WriteAllText($path, ($settings | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
}

function New-ApiKey {
    $bytes = New-Object byte[] 32
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $random.GetBytes($bytes)
    }
    finally {
        $random.Dispose()
    }
    -join ($bytes | ForEach-Object { $_.ToString('x2') })
}

# A role named has to be one this Windows user can sign in as, or the API would only fail at its first request.
if ($ReadOnlyUser) {
    $pgpass = Join-Path $env:APPDATA 'postgresql\pgpass.conf'
    $known = (Test-Path -LiteralPath $pgpass) -and
        (Get-Content -LiteralPath $pgpass | Where-Object { ($_ -split ':')[3] -ceq $ReadOnlyUser })
    if (-not $known) {
        throw "$pgpass has no password for $ReadOnlyUser. Run New-DatabaseReader.ps1 first, as this Windows user."
    }
}

# Read before anything is stopped: a file that can't be read might hold a key the other apps already use, so it isn't replaced.
$key = $null
if (Test-Path -LiteralPath $settingsFile) {
    try {
        $key = (Read-Settings).RecipeApi.Key
    }
    catch {
        throw "Couldn't read $settingsFile, which holds the Recipe API's key: $($_.Exception.Message) Put it right or delete it, then run this again."
    }
}

# Published to a folder of its own first, so a build that fails leaves the copy that's running alone.
$published = Join-Path ([IO.Path]::GetTempPath()) "TinapayanRMS.RecipeApi.$([Guid]::NewGuid().ToString('N'))"
Write-Host 'Publishing...'
dotnet publish $project --configuration Release --output $published --nologo
if ($LASTEXITCODE -ne 0) {
    $failedWith = $LASTEXITCODE
    Remove-Item -LiteralPath $published -Recurse -Force -ErrorAction SilentlyContinue
    throw "dotnet publish failed with exit code $failedWith."
}

# The files of a running copy are locked: stop it first. Only the copy in the install folder, not one run from source.
$running = Get-Process -Name 'TinapayanRMS.RecipeApi' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($running) {
    Write-Host 'Stopping the installed copy...'
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
}

# The folder is made to hold what was published and no more: publishing straight into it never takes away what an earlier
# version needed and this one doesn't. What's
# installed of recipeapi.json is left as it is, with its key, and so is the logs folder.
Write-Host "Copying to $InstallFolder ..."
& robocopy $published $InstallFolder /MIR /XD logs /XF recipeapi.json /NFL /NDL /NJH /NJS /NP | Out-Null
$copied = $LASTEXITCODE
if ($copied -ge 8) {
    Remove-Item -LiteralPath $published -Recurse -Force -ErrorAction SilentlyContinue
    throw "Copying to $InstallFolder failed (robocopy's exit code $copied). If it's in use, close what has it open and run this again."
}

if (-not (Test-Path -LiteralPath $settingsFile)) {
    Copy-Item -LiteralPath (Join-Path $published 'recipeapi.json') -Destination $settingsFile
}
Remove-Item -LiteralPath $published -Recurse -Force

$isNewKey = -not $key
if ($isNewKey) {
    $key = New-ApiKey
    Set-ApiKey $key
}

if ($ReadOnlyUser) {
    Set-ReadOnlyUser $ReadOnlyUser
}
$readsAs = (Read-Settings).Database.ReadOnlyUser

# Started through PowerShell so it has no console window: the API logs to the logs folder beside it instead.
$start = "Start-Process -FilePath '$exe' -WorkingDirectory '$InstallFolder' -WindowStyle Hidden"

if (-not $NoStartup) {
    $startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'TinapayanRMS Recipe API.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($startup)
    $shortcut.TargetPath = (Get-Command powershell.exe).Source
    $shortcut.Arguments = "-NoProfile -WindowStyle Hidden -Command `"$start`""
    $shortcut.WorkingDirectory = $InstallFolder
    $shortcut.WindowStyle = 7
    $shortcut.Description = 'Starts the TinapayanRMS Recipe API at sign-in, for TinapayanBMS.'
    $shortcut.Save()
    Write-Host "Starts at sign-in: $startup"
}

$rule = 'TinapayanRMS Recipe API'
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
$firewall = "New-NetFirewallRule -DisplayName '$rule' -Direction Inbound -Protocol TCP -LocalPort $port -Profile Private -RemoteAddress LocalSubnet -Program '$exe' -Action Allow"
if ($isAdmin) {
    Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    Invoke-Expression $firewall | Out-Null
    Write-Host "Firewall: other PCs on the local network can reach port $port."
}
else {
    Write-Warning "Not an administrator, so no firewall rule was added: only this PC can reach the API. To let other PCs on the LAN in, run in an administrator PowerShell:"
    Write-Host "  $firewall"
}

Invoke-Expression $start

# Reads the key where it is rather than printing it, so a command left in a window or a script doesn't give it away.
$readKey = "(Get-Content '$settingsFile' -Raw | ConvertFrom-Json).RecipeApi.Key"
Write-Host ''
Write-Host 'Installed and started.'
if ($readsAs) {
    Write-Host "It reads the database as $readsAs."
}
else {
    Write-Warning 'It reads the database as the app''s own role, in read-only transactions. To make it a role that can only read, run New-DatabaseReader.ps1, then this again with -ReadOnlyUser tinapayanrms_reader.'
}
if ($isNewKey) {
    Write-Host 'The Recipe API has a new key. Paste it under Key in the Settings of each TinapayanBMS that reads from this PC:'
    Write-Host "  $key"
}
else {
    Write-Host 'The Recipe API kept its key, so TinapayanBMS needs no change. To copy the key, run:'
    Write-Host "  $readKey | Set-Clipboard"
}
Write-Host 'Check it with:'
Write-Host "  Invoke-RestMethod http://localhost:$port/api/v1/status -Headers @{ 'X-Api-Key' = $readKey }"
