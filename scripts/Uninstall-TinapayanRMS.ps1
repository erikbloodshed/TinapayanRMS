<#
.SYNOPSIS
    Removes the installed TinapayanRMS and its shortcuts, for the Windows user running it.

.DESCRIPTION
    Closes the installed copy, as its own Close does (one asking whether to save changes stays open, and the script stops:
    answer it, close the app, and run this again), then deletes its folder and its Start menu and Desktop shortcuts.

    Left as they are: the database, its backups, and the app's view state and log in %LocalAppData%\TinapayanRMS\Maui.

.PARAMETER InstallFolder
    Where the app was installed. Defaults to %LocalAppData%\Programs\TinapayanRMS.
#>
[CmdletBinding()]
param(
    [string]$InstallFolder = (Join-Path $env:LOCALAPPDATA 'Programs\TinapayanRMS')
)

$ErrorActionPreference = 'Stop'
$exe = Join-Path $InstallFolder 'TinapayanRMS.Maui.exe'

$running = @(Get-Process -Name 'TinapayanRMS.Maui' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
if ($running) {
    Write-Host 'Closing the installed copy...'
    $running | ForEach-Object { [void]$_.CloseMainWindow() }
    $running | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
    if (@($running | Where-Object { -not $_.HasExited })) {
        throw 'TinapayanRMS is still open, most likely asking whether to save changes. Answer it, close the app, and run this again.'
    }
}

# Only the shortcuts that open this copy: one made by hand for another stays.
$shell = New-Object -ComObject WScript.Shell
foreach ($shortcut in @(
        (Join-Path ([Environment]::GetFolderPath('Programs')) 'TinapayanRMS.lnk'),
        (Join-Path ([Environment]::GetFolderPath('Desktop')) 'TinapayanRMS.lnk'))) {
    if ((Test-Path -LiteralPath $shortcut) -and $shell.CreateShortcut($shortcut).TargetPath -eq $exe) {
        Remove-Item -LiteralPath $shortcut -Force
        Write-Host "Removed $shortcut"
    }
}

if (Test-Path -LiteralPath $exe) {
    Remove-Item -LiteralPath $InstallFolder -Recurse -Force
    Write-Host "Removed $InstallFolder"
}
else {
    Write-Host "Nothing is installed in $InstallFolder."
}
