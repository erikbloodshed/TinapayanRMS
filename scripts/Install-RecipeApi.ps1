<#
.SYNOPSIS
    Installs the Recipe API on this PC for the Windows user running it, or updates the copy already installed.

.DESCRIPTION
    Publishes TinapayanRMS.RecipeApi (Release) to a folder of the user's own, gives it a key if it has none, puts a
    "TinapayanRMS Recipe API" shortcut in the Startup folder, which starts it hidden at sign-in, and starts it now. It's
    what lets TinapayanBMS, on this PC or another one on the LAN, read the recipes.

    The API runs as the signed-in Windows user, not as a Windows service: PostgreSQL is reached with the password in that
    user's %APPDATA%\postgresql\pgpass.conf. It reads the database in appsettings.json (the app's own) through a read-only
    connection, and listens where recipeapi.json says: port 5290 on every address.

    Every app that calls it has to send its key, RecipeApi:Key in the installed recipeapi.json. A first install makes one
    and shows it, to go into TinapayanBMS's Settings. Installing again keeps the key that's there, one put there by hand too.

    Run it in an administrator PowerShell to also let other PCs on the LAN in (a Windows Firewall rule for its port, on
    private networks, from the local subnet only). Without one it says the command to run.

.PARAMETER InstallFolder
    Where the API goes. Defaults to %LocalAppData%\Programs\TinapayanRMS.RecipeApi.

.PARAMETER NoStartup
    Don't start the API at sign-in.
#>
[CmdletBinding()]
param(
    [string]$InstallFolder = (Join-Path $env:LOCALAPPDATA 'Programs\TinapayanRMS.RecipeApi'),
    [switch]$NoStartup
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

# The files of a running copy are locked: stop it first. Only the copy in the install folder, not one run from source.
$running = Get-Process -Name 'TinapayanRMS.RecipeApi' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($running) {
    Write-Host 'Stopping the installed copy...'
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
}

Write-Host "Publishing to $InstallFolder ..."
dotnet publish $project --configuration Release --output $InstallFolder --nologo
$published = $LASTEXITCODE

# Publishing can put the repo's recipeapi.json, which has no key, over the installed one. The key goes back even if
# publishing then failed, or the next install would make a new one and every app would have to be given it again.
if ($key) {
    Set-ApiKey $key
}
if ($published -ne 0) {
    throw "dotnet publish failed with exit code $published."
}

$isNewKey = -not $key
if ($isNewKey) {
    $key = New-ApiKey
    Set-ApiKey $key
}

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
