<#
.SYNOPSIS
    Sets up the Recipe API's Windows service on this PC, the one that holds the database: its key, the role it reads the
    database as, and that role's password. Run it in an administrator PowerShell.

.DESCRIPTION
    The Recipe API comes with the app's MSI, as a Windows service its Recipe API feature installs, on the PC that holds the
    database only: msiexec /i <msi> ADDLOCAL=ALL, once (ADR 0021). Each later MSI updates it with the app, keeping the
    feature. This gives the service what it knows of this PC, once:

    - %ProgramData%\TinapayanRMS\RecipeApi, which only the administrators, SYSTEM and the service (NT SERVICE\
      TinapayanRMSRecipeApi) can read; every user can open it to reach its logs folder, which they can read.
    - recipeapi.json there, with the key every app that calls the API sends (RecipeApi:Key) and the role it reads as
      (Database:ReadOnlyUser), set on what the file already holds, so any other setting in it stays. A first setup makes
      the key and shows it, to go into TinapayanBMS's Settings, unless the copy an earlier Install-RecipeApi.ps1 put in
      this user's profile has one: that copy's settings are kept, its key included, so TinapayanBMS needs no change.
      Running it again keeps the key and the role that are there.
    - pgpass.conf there, with the role's line from this Windows user's own %APPDATA%\postgresql\pgpass.conf, as the
      service has no profile of its own. New-DatabaseReader.ps1 put the reader's there; when it gives the role a new
      password, run this again to copy it.

    It then stops that earlier per-user copy, if this user has one, so the port is free, restarts the service, and checks
    that the service itself is what listens on the port and that it answers with the key. Only then does it take the
    per-user copy away (its Startup shortcut, its firewall rule and its folder). If the service doesn't answer, it's
    stopped and the per-user copy started again, so the PC keeps the Recipe API it had. The MSI opens the port to the local
    subnet, on private networks only.

.PARAMETER ReadOnlyUser
    The PostgreSQL role the API reads the database as, such as tinapayanrms_reader, which New-DatabaseReader.ps1 makes:
    one that can read and never write. Kept in recipeapi.json, so running this again without it keeps the one set. With
    none ever set, the API reads as the app's own role, in read-only transactions.

.PARAMETER PasswordFile
    The pgpass.conf to copy the role's password from. Defaults to this Windows user's, which is where New-DatabaseReader.ps1
    and Setup-Database.ps1 put it.
#>
[CmdletBinding()]
param(
    [string]$ReadOnlyUser,
    [string]$PasswordFile = (Join-Path $env:APPDATA 'postgresql\pgpass.conf')
)

$ErrorActionPreference = 'Stop'
$serviceName = 'TinapayanRMSRecipeApi'
$serviceAccount = "NT SERVICE\$serviceName"
$dataFolder = Join-Path $env:ProgramData 'TinapayanRMS\RecipeApi'
$settingsFile = Join-Path $dataFolder 'recipeapi.json'
$servicePasswordFile = Join-Path $dataFolder 'pgpass.conf'
$logs = Join-Path $dataFolder 'logs'

# The copy Install-RecipeApi.ps1 put in this user's profile before the API came with the MSI.
$legacyFolder = Join-Path $env:LOCALAPPDATA 'Programs\TinapayanRMS.RecipeApi'
$legacyExe = Join-Path $legacyFolder 'TinapayanRMS.RecipeApi.exe'
$legacySettings = Join-Path $legacyFolder 'recipeapi.json'
$legacyStartup = Join-Path ([Environment]::GetFolderPath('Startup')) 'TinapayanRMS Recipe API.lnk'

function Read-Json([string]$Path, [string]$What) {
    try {
        Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        throw "Couldn't read $Path, $What`: $($_.Exception.Message)"
    }
}

function Set-Setting($Settings, [string]$Section, [string]$Name, [string]$Value) {
    if (-not $Settings.$Section) {
        $Settings | Add-Member -NotePropertyName $Section -NotePropertyValue ([pscustomobject]@{}) -Force
    }
    $Settings.$Section | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
}

function Write-Utf8([string]$Path, [string]$Text) {
    # Without a byte order mark, which is what PowerShell 5.1's own UTF-8 would put at the start of the file.
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding $false))
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

function Invoke-Icacls([string[]]$Arguments) {
    & icacls.exe @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "icacls $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

# The process listening on the port, or none.
function Get-PortOwner([int]$Port) {
    Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -First 1
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw 'Setting up the Recipe API needs an administrator: open PowerShell as an administrator and run this again.'
}

$service = Get-CimInstance Win32_Service -Filter "Name = '$serviceName'"
if (-not $service) {
    throw "The Recipe API isn't installed on this PC. Install the app's MSI with it, in this administrator PowerShell: msiexec /i <the msi> ADDLOCAL=ALL. Then run this again."
}
$installFolder = Split-Path -Parent ($service.PathName.Trim('"'))

# Read before anything changes: a file that can't be read might hold a key the other apps already use, so it isn't
# replaced. The service's own settings come first, then the per-user copy's, whose every setting carries over.
$settings = $null
foreach ($file in $settingsFile, $legacySettings) {
    if (-not $settings -and (Test-Path -LiteralPath $file)) {
        $settings = Read-Json $file "which holds the Recipe API's key. Put it right or delete it, then run this again"
    }
}
if (-not $settings) {
    $settings = [pscustomobject]@{}
}
$key = $settings.RecipeApi.Key
$readsAs = $settings.Database.ReadOnlyUser
if ($ReadOnlyUser) {
    $readsAs = $ReadOnlyUser.Trim()
}

# The role it signs in as: the reader, or else the app's own, as appsettings.json beside the service names it.
$signsInAs = $readsAs
if (-not $signsInAs) {
    $connection = (Read-Json (Join-Path $installFolder 'appsettings.json') 'the database the Recipe API reads. Install the MSI again to put it back').ConnectionStrings.Default
    if ($connection -match '(?i)(?:^|;)\s*(?:Username|User ID|UserId|User)\s*=\s*([^;]+)') {
        $signsInAs = $Matches[1].Trim()
    }
    else {
        throw "Couldn't find the role the app signs in as in $installFolder\appsettings.json."
    }
}

# Its password, from this user's file: pgpass.conf's lines are host:port:database:role:password.
$passwordLines = @()
if (Test-Path -LiteralPath $PasswordFile) {
    $passwordLines = @(Get-Content -LiteralPath $PasswordFile | Where-Object { ($_ -split ':')[3] -ceq $signsInAs })
}
if (-not $passwordLines) {
    if ($readsAs) {
        throw "$PasswordFile has no password for $signsInAs. Run New-DatabaseReader.ps1 first, as this Windows user, or pass -PasswordFile."
    }
    throw "$PasswordFile has no password for $signsInAs. Run Setup-Database.ps1 first, as this Windows user, or pass -PasswordFile."
}

# The port it listens on: 5290, or the one its settings give Kestrel.
$port = 5290
if ("$($settings.Kestrel.Endpoints.Http.Url)" -match ':(\d+)/?$') {
    $port = [int]$Matches[1]
}

# The folder only the administrators, SYSTEM and the service can read, which every user can open (this folder only: list
# and traverse) to reach the logs, which the service writes and every user can read. What it held before is reset to
# follow it. Made before the files, so they're never readable by more.
New-Item -ItemType Directory -Force -Path $logs | Out-Null
Invoke-Icacls @($dataFolder, '/inheritance:r', '/grant:r', '*S-1-5-32-544:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F', "${serviceAccount}:(OI)(CI)RX", '*S-1-5-32-545:(RX)')
Invoke-Icacls @("$dataFolder\*", '/reset', '/T', '/C', '/Q')
Invoke-Icacls @($logs, '/grant', "${serviceAccount}:(OI)(CI)M", '*S-1-5-32-545:(OI)(CI)RX')

$isNewKey = -not $key
if ($isNewKey) {
    $key = New-ApiKey
}
Set-Setting $settings 'RecipeApi' 'Key' $key
if ($readsAs) {
    Set-Setting $settings 'Database' 'ReadOnlyUser' $readsAs
}
Write-Utf8 $settingsFile ($settings | ConvertTo-Json -Depth 10)
Write-Utf8 $servicePasswordFile (($passwordLines -join "`n") + "`n")

# The per-user copy is stopped, so the service can have the port, and taken away only once the service answers.
$legacyRunning = @(Get-Process -Name 'TinapayanRMS.RecipeApi' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $legacyExe })
if ($legacyRunning) {
    Write-Host 'Stopping the Recipe API this user ran at sign-in...'
    $legacyRunning | Stop-Process -Force
    $legacyRunning | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
}

Write-Host 'Starting the Recipe API...'
Restart-Service -Name $serviceName

# Asked as TinapayanBMS asks, with the key read where it is rather than printed, and only once the service's own process
# is the one on the port: another copy listening there could answer too. Only the status code is shown.
$answer = $null
$holder = $null
for ($attempt = 0; $attempt -lt 20 -and $answer -ne 200; $attempt++) {
    Start-Sleep -Seconds 1
    $servicePid = (Get-CimInstance Win32_Service -Filter "Name = '$serviceName'").ProcessId
    $holder = Get-PortOwner $port
    if (-not $holder -or $holder -ne $servicePid) {
        continue
    }
    try {
        $answer = (Invoke-WebRequest "http://localhost:$port/api/v1/status" -Headers @{ 'X-Api-Key' = $key } -UseBasicParsing -TimeoutSec 5).StatusCode
    }
    catch {
        if ($_.Exception.Response) {
            $answer = [int]$_.Exception.Response.StatusCode
        }
    }
}

if ($answer -ne 200) {
    $why = if ($holder -and $holder -ne $servicePid) {
        "Another program has port ${port}: $((Get-Process -Id $holder -ErrorAction SilentlyContinue).Path) (process $holder). If it's a Recipe API installed for another Windows user, remove it there with Uninstall-RecipeApi.ps1 from an older checkout, or stop it, then run this again."
    }
    elseif ($answer) {
        "It answered $answer. Its log in $logs says why."
    }
    else {
        "It didn't answer on port $port. Its log in $logs, or Windows' Event Viewer, says why."
    }

    if ($legacyRunning) {
        Write-Host 'Stopping the service and starting the Recipe API this user had again, so the PC keeps one that works...'
        Stop-Service -Name $serviceName -ErrorAction SilentlyContinue
        Start-Process -FilePath $legacyExe -WorkingDirectory $legacyFolder -WindowStyle Hidden
    }
    throw "The Recipe API's service isn't working yet. $why The settings are written: run this again once it's put right."
}

# The service answers: the per-user copy goes, the shortcut that started it, its firewall rule (only the one for its
# .exe: the MSI's has the same name) and its folder, whose settings are now the service's.
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

Write-Host ''
Write-Host "The Recipe API is running as a Windows service, reading the database as $signsInAs."
if (-not $readsAs) {
    Write-Warning 'It reads the database as the app''s own role, in read-only transactions. To make it a role that can only read, run New-DatabaseReader.ps1, then this again with -ReadOnlyUser tinapayanrms_reader.'
}
if ($isNewKey) {
    Write-Host 'The Recipe API has a new key. Paste it under Key in the Settings of each TinapayanBMS that reads from this PC:'
    Write-Host "  $key"
}
else {
    Write-Host 'The Recipe API kept its key, so TinapayanBMS needs no change. To copy the key, in an administrator PowerShell:'
    Write-Host "  (Get-Content '$settingsFile' -Raw | ConvertFrom-Json).RecipeApi.Key | Set-Clipboard"
}
