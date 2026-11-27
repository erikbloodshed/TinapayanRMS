<#
.SYNOPSIS
    Removes the Recipe API from this PC: the MSI's Recipe API feature (its service and firewall rule) and its data folder,
    with its key. Run it in an administrator PowerShell.

.DESCRIPTION
    Takes the Recipe API feature out of the installed TinapayanRMS, found by the MSI's upgrade code, leaving the app as it
    is, then deletes %ProgramData%\TinapayanRMS\RecipeApi, with the key, the role's password and the logs, so a later
    setup makes a new key. It also takes away a per-user copy an earlier Install-RecipeApi.ps1 put in this Windows user's
    profile. The database is left as it is: the API only ever read it.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'TinapayanRMSRecipeApi'
$dataFolder = Join-Path $env:ProgramData 'TinapayanRMS\RecipeApi'

# The MSI's upgrade code (installer\Package.wxs): the app's for good, whatever its version.
$upgradeCode = '{4E4A8926-1F88-40FF-A780-B8EC774B5CE3}'

$legacyFolder = Join-Path $env:LOCALAPPDATA 'Programs\TinapayanRMS.RecipeApi'
$legacyExe = Join-Path $legacyFolder 'TinapayanRMS.RecipeApi.exe'
$legacyStartup = Join-Path ([Environment]::GetFolderPath('Startup')) 'TinapayanRMS Recipe API.lnk'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw 'Removing the Recipe API needs an administrator: open PowerShell as an administrator and run this again.'
}

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    foreach ($product in @($installer.RelatedProducts($upgradeCode))) {
        Write-Host "Removing the Recipe API from TinapayanRMS ($product)..."
        $remove = Start-Process msiexec.exe -ArgumentList '/i', $product, 'REMOVE=RecipeApi', '/passive', '/norestart' -Wait -PassThru
        if ($remove.ExitCode -notin 0, 3010) {
            throw "Removing the Recipe API failed (msiexec exit code $($remove.ExitCode))."
        }
    }
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        throw "The $serviceName service is still there, though no TinapayanRMS installed by the MSI has it."
    }
    Write-Host 'Removed the Recipe API service and its firewall rule.'
}
else {
    Write-Host 'The Recipe API service isn''t installed.'
}

if (Test-Path -LiteralPath $dataFolder) {
    Remove-Item -LiteralPath $dataFolder -Recurse -Force
    Write-Host "Removed $dataFolder, with the key."
}

# A per-user copy from before the API came with the MSI.
$running = Get-Process -Name 'TinapayanRMS.RecipeApi' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $legacyExe }
if ($running) {
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
}
if (Test-Path -LiteralPath $legacyStartup) {
    Remove-Item -LiteralPath $legacyStartup -Force
    Write-Host "Removed $legacyStartup"
}
Get-NetFirewallRule -DisplayName 'TinapayanRMS Recipe API' -ErrorAction SilentlyContinue |
    Where-Object { ($_ | Get-NetFirewallApplicationFilter).Program -eq $legacyExe } |
    Remove-NetFirewallRule
if (Test-Path -LiteralPath $legacyFolder) {
    Remove-Item -LiteralPath $legacyFolder -Recurse -Force
    Write-Host "Removed $legacyFolder"
}
