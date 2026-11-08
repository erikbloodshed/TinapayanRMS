<#
.SYNOPSIS
    Finds the WPF TinapayanRMS this app replaced, and with -Remove takes it off this PC for the Windows user running it.

.DESCRIPTION
    The WPF app (the tag wpf-final) had no installer: a copy was published and put in place by hand, so it's found by what
    points at it. Its exe, TinapayanRMS.UI.exe, is looked for in the shortcuts on the Desktop, in the Start menu and in the
    Startup folder (this user's and all users'), in the taskbar's pinned items, in a running copy, and in any -Folder given.

    Without -Remove it only says what it found and what -Remove would do. With -Remove it closes a running copy as its
    own Close does (one asking whether to save changes stays open, and the script stops: answer it, close the app, and
    run this again), deletes the shortcuts, and deletes each folder the WPF app was in, apart from its logs folder.

    Left alone:
    - A copy inside a source checkout (a folder with .git above it), such as a build of wpf-final: that's a developer's.
    - A taskbar pin, which Windows doesn't let a script take away cleanly: right-click it and choose Unpin from taskbar.
    - %LocalAppData%\TinapayanRMS\view-state.json, from which the new app takes how the lists were left the first time it
      runs; the backups; and the database, which the new app uses as it is.

    Shortcuts for all users need an administrator PowerShell to delete; without one it says which are left.

.PARAMETER Folder
    A folder holding the WPF app (TinapayanRMS.UI.exe) that no shortcut points at.

.PARAMETER Remove
    Remove what was found, rather than only listing it.
#>
[CmdletBinding()]
param(
    [string[]]$Folder = @(),
    [switch]$Remove
)

$ErrorActionPreference = 'Stop'
$exeName = 'TinapayanRMS.UI.exe'

# powershell -File passes "a,b" as one string rather than a list.
$Folder = @($Folder | ForEach-Object { $_ -split ',' } | Where-Object { $_ })

function Test-InSourceCheckout([string]$Path) {
    $dir = Get-Item -LiteralPath $Path
    while ($dir) {
        if (Test-Path -LiteralPath (Join-Path $dir.FullName '.git')) {
            return $true
        }
        $dir = $dir.Parent
    }
    return $false
}

# The shortcuts, wherever Windows keeps them for this user and for all users.
$shell = New-Object -ComObject WScript.Shell
$taskbar = Join-Path $env:APPDATA 'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar'
$places = @(
    [Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('CommonDesktopDirectory'),
    [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('CommonPrograms'),
    [Environment]::GetFolderPath('Startup'), [Environment]::GetFolderPath('CommonStartup'),
    $taskbar) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique

$shortcuts = @()
foreach ($place in $places) {
    foreach ($link in Get-ChildItem -LiteralPath $place -Filter '*.lnk' -Recurse -ErrorAction SilentlyContinue) {
        $target = $shell.CreateShortcut($link.FullName).TargetPath
        if ($target -and (Split-Path -Leaf $target) -eq $exeName) {
            $shortcuts += [pscustomobject]@{ Path = $link.FullName; Target = $target; Pinned = $place -eq $taskbar }
        }
    }
}

$running = @(Get-Process -Name 'TinapayanRMS.UI' -ErrorAction SilentlyContinue)

# The folders: where the shortcuts and a running copy point, and any named. One counts only while the WPF app is still
# in it, so a folder the new app was installed over isn't taken for it.
$candidates = @($shortcuts | ForEach-Object { Split-Path -Parent $_.Target }) +
    @($running | Where-Object { $_.Path } | ForEach-Object { Split-Path -Parent $_.Path }) +
    $Folder
$folders = @()
$checkouts = @()
foreach ($candidate in ($candidates | Where-Object { $_ } | Select-Object -Unique)) {
    if (-not (Test-Path -LiteralPath (Join-Path $candidate $exeName))) {
        continue
    }
    $full = (Resolve-Path -LiteralPath $candidate).ProviderPath
    if (Test-InSourceCheckout $full) {
        $checkouts += $full
    }
    elseif ($folders -notcontains $full) {
        $folders += $full
    }
}
foreach ($named in $Folder) {
    if (-not (Test-Path -LiteralPath (Join-Path $named $exeName))) {
        Write-Warning "$named has no $exeName in it, so it isn't the WPF app's."
    }
}

if (-not $shortcuts -and -not $folders -and -not $running -and -not $checkouts) {
    Write-Host 'No copy of the WPF TinapayanRMS was found for this Windows user.'
    return
}

Write-Host 'The WPF TinapayanRMS this app replaced:'
foreach ($found in $folders) { Write-Host "  Folder:   $found" }
foreach ($found in $checkouts) { Write-Host "  Build in a source checkout (left alone): $found" }
foreach ($found in $shortcuts) {
    $note = if ($found.Pinned) { ' (pinned to the taskbar: unpin it by hand)' } elseif (-not (Test-Path -LiteralPath $found.Target)) { ' (its app is gone)' } else { '' }
    Write-Host "  Shortcut: $($found.Path)$note"
}
foreach ($found in $running) { Write-Host "  Running:  $($found.Path)" }

if (-not $Remove) {
    Write-Host ''
    Write-Host 'To take it away (its folders, apart from their logs, and its shortcuts), run:'
    $named = if ($Folder) { " -Folder `"$($Folder -join ',')`"" } else { '' }
    Write-Host "  powershell -ExecutionPolicy Bypass -File `"$PSCommandPath`"$named -Remove"
    return
}

# Only a copy from a folder being removed is closed: one run from a source checkout is a developer's.
$closing = @($running | Where-Object { $_.Path -and $folders -contains (Split-Path -Parent $_.Path) })
if ($closing) {
    Write-Host 'Closing the WPF app...'
    $closing | ForEach-Object { [void]$_.CloseMainWindow() }
    $closing | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
    if (@($closing | Where-Object { -not $_.HasExited })) {
        throw 'The WPF app is still open, most likely asking whether to save changes. Answer it, close it, and run this again.'
    }
}

$left = @()
foreach ($found in ($shortcuts | Where-Object { -not $_.Pinned })) {
    try {
        Remove-Item -LiteralPath $found.Path -Force
        Write-Host "Removed $($found.Path)"
    }
    catch {
        $left += $found.Path
    }
}

foreach ($found in $folders) {
    Get-ChildItem -LiteralPath $found -Force | Where-Object { $_.Name -ne 'logs' } | Remove-Item -Recurse -Force
    if (Test-Path -LiteralPath (Join-Path $found 'logs')) {
        Write-Host "Removed the WPF app from $found (its logs folder is kept)"
    }
    else {
        Remove-Item -LiteralPath $found -Force
        Write-Host "Removed $found"
    }
}

foreach ($path in $left) {
    Write-Warning "Couldn't delete $path, a shortcut for all users: run this again in an administrator PowerShell, or delete it by hand."
}
if (@($shortcuts | Where-Object { $_.Pinned })) {
    Write-Host 'Unpin the WPF app from the taskbar by hand: right-click it and choose Unpin from taskbar.'
}
