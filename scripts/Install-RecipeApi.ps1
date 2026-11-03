<#
.SYNOPSIS
    Installs the Recipe API on this PC for the Windows user running it, or updates the copy already installed.

.DESCRIPTION
    Publishes TinapayanRMS.RecipeApi (Release) to a folder of the user's own, puts a "TinapayanRMS Recipe API" shortcut in
    the Startup folder, which starts it hidden at sign-in, and starts it now. It's what lets TinapayanBMS, on this PC
    or another one on the LAN, read the recipes.

    The API runs as the signed-in Windows user, not as a Windows service: PostgreSQL is reached with the password in that
    user's %APPDATA%\postgresql\pgpass.conf. It reads the database in appsettings.json (the app's own) through a read-only
    connection, and listens where recipeapi.json says: port 5290 on every address. Set RecipeApi:Key there to make the
    other apps send a key.

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
$port = 5290

# The files of a running copy are locked: stop it first. Only the copy in the install folder, not one run from source.
$running = Get-Process -Name 'TinapayanRMS.RecipeApi' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($running) {
    Write-Host 'Stopping the installed copy...'
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
}

Write-Host "Publishing to $InstallFolder ..."
dotnet publish $project --configuration Release --output $InstallFolder --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
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
Write-Host ''
Write-Host "Installed and started. Check it with: Invoke-RestMethod http://localhost:$port/api/v1/status"
