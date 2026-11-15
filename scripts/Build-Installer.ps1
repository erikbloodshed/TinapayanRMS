<#
.SYNOPSIS
    Builds TinapayanRMS's MSI: a per-machine install into Program Files, with Start menu and Desktop shortcuts for every
    Windows user on the PC.

.DESCRIPTION
    Publishes the app (src\TinapayanRMS.Maui, Release: framework-dependent ReadyToRun, unpackaged, with the Windows App SDK
    in its folder, as Install-TinapayanRMS.ps1 does) to artifacts\installer\app, then builds installer\
    TinapayanRMS.Installer.wixproj over it (WiX 5) into artifacts\installer\TinapayanRMS-<version>-x64.msi.

    Building needs what building from source does: the .NET SDK in global.json, the maui-windows workload, and the
    SYNCFUSION_LICENSE_KEY user environment variable, which the build compiles into the app, so the MSI carries the key:
    keep it within the shop.

    The PC it's installed on needs the .NET 11 Runtime (x64), which the MSI checks for, and WebView2, which Windows 10 and
    11 have. Each Windows user who opens the app reaches PostgreSQL with the password in their own
    %APPDATA%\postgresql\pgpass.conf. The MSI never touches the database: apply any new migrations with
    `dotnet ef database update --project src/TinapayanRMS.Infrastructure`.

    Install it by opening it (it asks for an administrator), or `msiexec /i <msi>`; remove it from Settings > Apps.

.PARAMETER Version
    The MSI's version, three numbers. Defaults to the app's ApplicationDisplayVersion (1.0) and the commit count, so each
    commit's MSI replaces the last one's. An MSI replaces an installed copy only when its version is the same or higher.
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d{1,3}\.\d{1,3}\.\d{1,5}$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\TinapayanRMS.Maui\TinapayanRMS.Maui.csproj'
$installerProject = Join-Path $repo 'installer\TinapayanRMS.Installer.wixproj'
$artifacts = Join-Path $repo 'artifacts\installer'
$published = Join-Path $artifacts 'app'

if (-not $Version) {
    $displayVersion = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.ApplicationDisplayVersion |
        Where-Object { $_ } | Select-Object -First 1
    $commits = (git -C $repo rev-list --count HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $displayVersion) {
        throw 'Could not work out the version: pass -Version.'
    }
    $Version = "$displayVersion.$commits"
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

if (git -C $repo status --porcelain) {
    Write-Warning 'The working tree has changes not committed: the MSI carries them, under the last commit''s version.'
}

# A fresh folder, so the MSI holds what this publish made and nothing an earlier one left. Satori GC stays off whatever
# the shell says: an installed copy is the everyday build.
if (Test-Path -LiteralPath $published) {
    Remove-Item -LiteralPath $published -Recurse -Force
}
Write-Host "Publishing TinapayanRMS $Version (a few minutes)..."
dotnet publish $project --configuration Release --output $published -p:UseSatoriGC=false --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Write-Host 'Building the MSI...'
dotnet build $installerProject --configuration Release --nologo `
    "-p:AppPublishDir=$published\" "-p:ProductVersion=$Version"
if ($LASTEXITCODE -ne 0) {
    throw "Building the MSI failed with exit code $LASTEXITCODE."
}

$built = Join-Path $repo 'installer\bin\Release\TinapayanRMS.msi'
$msi = Join-Path $artifacts "TinapayanRMS-$Version-x64.msi"
Copy-Item -LiteralPath $built -Destination $msi -Force
Write-Host "Built $msi ($([Math]::Round((Get-Item -LiteralPath $msi).Length / 1MB)) MB)."
