<#
.SYNOPSIS
    Installs TinapayanRMS on this PC for the Windows user running it, or updates the copy already installed.

.DESCRIPTION
    Publishes the app (src\TinapayanRMS.Maui, Release: framework-dependent ReadyToRun, unpackaged, with the Windows App
    SDK in its folder) to a temporary folder, then copies it to a folder of the user's own and puts a "TinapayanRMS"
    shortcut in the Start menu and on the Desktop. A publish that fails leaves the installed copy as it was.

    An installed copy that's open is closed first, as its own Close does. One that's asking about changes not saved stays
    open, and the script stops there: answer it, close the app, and run this again. Nothing is ever lost by updating.

    Whatever an earlier version left in the folder goes, apart from the web view's own folder (TinapayanRMS.Maui.exe.WebView2,
    its cache, kept so the first start after an update isn't slower) and a logs folder. The app keeps its view state and
    its log in %LocalAppData%\TinapayanRMS\Maui, which this never touches, nor the database: apply any new migrations with
    `dotnet ef database update --project src/TinapayanRMS.Infrastructure`.

    Building needs what building from source does: the .NET SDK in global.json, the maui-windows workload, and the
    SYNCFUSION_LICENSE_KEY user environment variable, which the build compiles into the app. The app reaches PostgreSQL
    as the signed-in Windows user, with the password in that user's %APPDATA%\postgresql\pgpass.conf.

    At the end it looks for the WPF app this replaced (Remove-WpfApp.ps1) and says what it found; it doesn't remove it.

.PARAMETER InstallFolder
    Where the app goes. Defaults to %LocalAppData%\Programs\TinapayanRMS.

.PARAMETER NoDesktopShortcut
    Don't put a shortcut on the Desktop.

.PARAMETER NoStartMenuShortcut
    Don't put a shortcut in the Start menu. With -NoDesktopShortcut too, a trial install into a folder of its own leaves
    nothing behind outside that folder.
#>
[CmdletBinding()]
param(
    [string]$InstallFolder = (Join-Path $env:LOCALAPPDATA 'Programs\TinapayanRMS'),
    [switch]$NoDesktopShortcut,
    [switch]$NoStartMenuShortcut
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\TinapayanRMS.Maui\TinapayanRMS.Maui.csproj'
$exe = Join-Path $InstallFolder 'TinapayanRMS.Maui.exe'
$webViewFolder = 'TinapayanRMS.Maui.exe.WebView2'

# A copy running from the install folder holds its files: closed as its Close button does, so a copy asking about
# changes not saved stays open rather than losing them. The WPF app is closed too, in case it was put in this folder.
function Close-InstalledCopy([string]$Name) {
    $path = Join-Path $InstallFolder "$Name.exe"
    $running = @(Get-Process -Name $Name -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $path })
    if (-not $running) {
        return
    }

    Write-Host "Closing the installed $Name..."
    $running | ForEach-Object { [void]$_.CloseMainWindow() }
    $running | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
    if (@($running | Where-Object { -not $_.HasExited })) {
        throw "The installed $Name is still open, most likely asking whether to save changes. Answer it, close the app, and run this again."
    }
}

# The license key is compiled into the app, and the build fails without it. A shell opened before the variable was
# set doesn't have it, so it's read from the user's environment too. Never printed.
if (-not $env:SYNCFUSION_LICENSE_KEY) {
    $env:SYNCFUSION_LICENSE_KEY = [Environment]::GetEnvironmentVariable('SYNCFUSION_LICENSE_KEY', 'User')
}
if (-not $env:SYNCFUSION_LICENSE_KEY) {
    throw 'The SYNCFUSION_LICENSE_KEY user environment variable is not set, and the build compiles it into the app. Set it, open a new PowerShell, and run this again.'
}

if (-not ((dotnet workload list 2>$null) -match '^\s*maui-windows\b')) {
    throw 'The maui-windows workload is not installed. In an administrator terminal: dotnet workload install maui-windows --skip-manifest-update'
}

$pgpass = Join-Path $env:APPDATA 'postgresql\pgpass.conf'
if (-not (Test-Path -LiteralPath $pgpass)) {
    Write-Warning "$pgpass is missing, so the app won't be able to sign in to PostgreSQL as this Windows user. See the developer manual's setup."
}

# Published to a folder of its own first, so a build that fails leaves the installed copy alone. Satori GC stays off
# whatever the shell says: an installed copy is the everyday build.
$published = Join-Path ([IO.Path]::GetTempPath()) "TinapayanRMS.$([Guid]::NewGuid().ToString('N'))"
Write-Host 'Publishing (a few minutes)...'
dotnet publish $project --configuration Release --output $published -p:UseSatoriGC=false --nologo
if ($LASTEXITCODE -ne 0) {
    $failedWith = $LASTEXITCODE
    Remove-Item -LiteralPath $published -Recurse -Force -ErrorAction SilentlyContinue
    throw "dotnet publish failed with exit code $failedWith."
}

try {
    Close-InstalledCopy 'TinapayanRMS.Maui'
    Close-InstalledCopy 'TinapayanRMS.UI'
}
catch {
    Remove-Item -LiteralPath $published -Recurse -Force -ErrorAction SilentlyContinue
    throw
}

# The folder is made to hold what was published and no more: copying over it would leave what an earlier version
# needed and this one doesn't.
Write-Host "Copying to $InstallFolder ..."
& robocopy $published $InstallFolder /MIR /XD $webViewFolder logs /NFL /NDL /NJH /NJS /NP | Out-Null
$copied = $LASTEXITCODE
Remove-Item -LiteralPath $published -Recurse -Force -ErrorAction SilentlyContinue
if ($copied -ge 8) {
    throw "Copying to $InstallFolder failed (robocopy's exit code $copied). If something has a file there open, close it and run this again."
}

function New-Shortcut([string]$Path) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $InstallFolder
    $shortcut.Description = 'Recipes and costing for Tinapayan Festival Bakeshoppe.'
    $shortcut.IconLocation = "$exe,0"
    $shortcut.Save()
}

if (-not $NoStartMenuShortcut) {
    $startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'TinapayanRMS.lnk'
    New-Shortcut $startMenu
    Write-Host "Start menu shortcut: $startMenu"
}

if (-not $NoDesktopShortcut) {
    $desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'TinapayanRMS.lnk'
    New-Shortcut $desktop
    Write-Host "Desktop shortcut: $desktop"
}

Write-Host ''
Write-Host "Installed in $InstallFolder. Open TinapayanRMS from the Start menu or the Desktop, or run `"$exe`"."
Write-Host 'If the code brought new migrations, apply them before opening it: dotnet ef database update --project src/TinapayanRMS.Infrastructure'
Write-Host ''
& (Join-Path $PSScriptRoot 'Remove-WpfApp.ps1')
