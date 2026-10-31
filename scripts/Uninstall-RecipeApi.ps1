<#
.SYNOPSIS
    Removes the installed Recipe API, its Startup shortcut and its firewall rule, for the Windows user running it.

.DESCRIPTION
    Stops the installed copy and deletes its folder and the Startup shortcut. The firewall rule is removed when run in an
    administrator PowerShell. The database is left as it is: the API only ever read it.

.PARAMETER InstallFolder
    Where the API was installed. Defaults to %LocalAppData%\Programs\TinapayanRMS.RecipeApi.
#>
[CmdletBinding()]
param(
    [string]$InstallFolder = (Join-Path $env:LOCALAPPDATA 'Programs\TinapayanRMS.RecipeApi')
)

$ErrorActionPreference = 'Stop'
$exe = Join-Path $InstallFolder 'TinapayanRMS.RecipeApi.exe'

$running = Get-Process -Name 'TinapayanRMS.RecipeApi' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe }
if ($running) {
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
}

$startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'TinapayanRMS Recipe API.lnk'
if (Test-Path $startup) {
    Remove-Item $startup -Force
    Write-Host "Removed $startup"
}

$rule = Get-NetFirewallRule -DisplayName 'TinapayanRMS Recipe API' -ErrorAction SilentlyContinue
if ($rule) {
    try {
        $rule | Remove-NetFirewallRule
        Write-Host 'Removed the firewall rule.'
    }
    catch {
        Write-Warning "The firewall rule needs an administrator to remove: Remove-NetFirewallRule -DisplayName 'TinapayanRMS Recipe API'"
    }
}

if (Test-Path $exe) {
    Remove-Item $InstallFolder -Recurse -Force
    Write-Host "Removed $InstallFolder"
}
else {
    Write-Host "Nothing is installed in $InstallFolder."
}
