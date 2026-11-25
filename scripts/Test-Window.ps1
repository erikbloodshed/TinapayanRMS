<#
.SYNOPSIS
Checks the app's window before an install: the smoke run, a soak run for anything left alive, the close question, and
the log, on a scratch copy of the shop's data.

.DESCRIPTION
The window's checks that CI can't run (ADR 0013: GitHub's runner has no desktop session), run by hand before an install:

1. The golden master's anonymized copy of the shop's data restored into a scratch database, tinapayanrms_bench_window,
   by the benchmarks' own build (`startup prepare`: Copy In, then Restore, which brings it to this build's migrations, so
   a start races no update), never the dev database.
2. The Debug app started on it signed in (TINAPAYANRMS_SIGNED_IN), its window kept past the desktop's right edge and its
   web view's debugging port open, with none of the throttling a web view nobody sees would get. With -Exe, that copy
   instead (a trial install of the commit to install, say), signed in with the scratch database's password.
3. The run-tinapayanrms driver's smoke run: every screen and Settings page, then a record of each record screen; a step
   fails on the page's error bar, a screen's error panel, a red toast or a page error.
4. The soak run (-Rounds, 4 by default; 0 leaves it out): the smoke run's walk again and again, quietly. After two
   rounds to warm up, the app's live heap is counted by dotnet-dump on Settings → Keyboard, a page with no grid, then
   again halfway through the rounds and after the last. A type of the app's own (a screen state, a page, a component, a
   row) or a Syncfusion grid with more live instances in both halves fails: something that outlives a screen keeps
   hold of one more each visit. A count that rises once and holds isn't a leak: a screen keeps a second copy of what it
   shows after its second load (TableRows keeps the rows it had when new ones show the same). The record picks
   alternate between two records a screen, so each half's rounds are made even, and every count has the same ones open.
5. The close question, both ways: a raw material renamed, then Close answered Cancel (the window and the edit stay)
   and Close answered No (the window goes); then the scratch database is read to see No saved nothing.
6. The app's log: an error or a fatal line it gained during the run fails; its warnings are listed.

Pass or fail is read from the page, never from shots: a web view off the only monitor may not paint. The user's
view-state.json, which every copy of the app shares, is copied first and put back after, its backup-copy-folder left out
meanwhile (the day's automatic backup of the scratch database would be copied there); the scratch database is dropped at
the end with its backups. It refuses to start while any copy of the app is running. Exits 1 when anything failed.

Needs Node.js (playwright-core is installed on first use, as the driver installs it), dotnet-dump for the soak run
(dotnet tool install -g dotnet-dump), and the local PostgreSQL server.

.PARAMETER Rounds
The soak run's counted rounds, after the smoke run and two to warm up, made a multiple of 4. 0 leaves the soak run
out. Default 4.

.PARAMETER Exe
A copy of the app to check in place of the Debug build: a Release copy asks for the password, which is typed.

.PARAMETER NoBuild
Uses the builds already there (the Debug app's and the benchmarks').

.PARAMETER KeepDumps
Keeps the soak run's heap dumps, which are deleted after counting unless something grew.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts/Test-Window.ps1

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts/Test-Window.ps1 -Rounds 0

.EXAMPLE
powershell -ExecutionPolicy Bypass -File scripts/Test-Window.ps1 -Exe "$env:TEMP\trial\TinapayanRMS.Maui.exe"
#>
param(
    [int]$Rounds = 4,
    [string]$Exe = '',
    [switch]$NoBuild,
    [switch]$KeepDumps
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# The repo's folder spelled as the caller's terminal spells it: the build records each project's folder with its drive
# letter's case, so the other case would make this run recompile every project (as Compare-Benchmarks.ps1 does).
$here = [Environment]::CurrentDirectory
$repoAsCalled = if ($here.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { $here.Substring(0, $repo.Length) } else { $repo }

$database = 'tinapayanrms_bench_window'
$work = Join-Path ([IO.Path]::GetTempPath()) 'trms-window'
$driver = Join-Path $repo '.claude\skills\run-tinapayanrms\driver.js'
$signIn = Join-Path $PSScriptRoot 'benchmarks\window-start.js'
$tools = Join-Path $env:LOCALAPPDATA 'TinapayanRMS\dev\playwright'
$dataFolder = Join-Path $env:LOCALAPPDATA 'TinapayanRMS\Maui'
$logFolder = Join-Path $dataFolder 'logs'
$viewState = Join-Path $dataFolder 'view-state.json'
$benchExe = Join-Path $repo 'tests\TinapayanRMS.Benchmarks\bin\Debug\net11.0\TinapayanRMS.Benchmarks.exe'
$app = if ($Exe) { (Resolve-Path -LiteralPath $Exe).Path }
       else { Join-Path $repo 'src\TinapayanRMS.Maui\bin\Debug\net11.0-windows10.0.19041.0\win-x64\TinapayanRMS.Maui.exe' }
# Each half of the rounds even: the record picks alternate between two records a screen, so every count is taken with
# the same ones open.
if ($Rounds -lt 0) { $Rounds = 0 }
if ($Rounds % 4) { $Rounds += 4 - $Rounds % 4 }

$failures = New-Object System.Collections.Generic.List[string]

# A command line tool run with what it writes kept and, unless -Quiet, shown as it comes: Windows PowerShell turns a
# native tool's error output into errors, which would stop this script at the first warning it writes.
function Invoke-Tool([string]$File, [string[]]$Arguments, [switch]$Quiet) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& $File @Arguments 2>&1 | ForEach-Object {
            $line = "$_"
            if (-not $Quiet) { Write-Host $line }
            $line
        })
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    [pscustomobject]@{ ExitCode = $exitCode; Output = $output }
}

function Invoke-Driver([string[]]$Arguments, [switch]$Quiet) { Invoke-Tool 'node' (@($driver, "$port") + $Arguments) -Quiet:$Quiet }

function Get-Copies { @(Get-Process TinapayanRMS.Maui -ErrorAction SilentlyContinue) }

function Test-Port([int]$Number) {
    $client = New-Object Net.Sockets.TcpClient
    try { $client.ConnectAsync('127.0.0.1', $Number).Wait(500) -and $client.Connected } catch { $false } finally { $client.Dispose() }
}

# ---------------------------------------------------------------------------------------------------------------------
# What it needs, and what would get in the way.

$running = Get-Copies
if ($running) {
    foreach ($copy in $running) { Write-Output "TinapayanRMS.Maui is running (process $($copy.Id), $($copy.Path)): close it first, as every copy shares the view state and the log." }
    exit 1
}
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'The window''s checks need Node.js (node) on the PATH.' }
if ($Rounds -gt 0 -and -not (Get-Command dotnet-dump -ErrorAction SilentlyContinue)) {
    throw 'The soak run counts the heap with dotnet-dump: dotnet tool install -g dotnet-dump, or -Rounds 0 to leave it out.'
}
$port = 9222..9240 | Where-Object { -not (Test-Port $_) } | Select-Object -First 1
if (-not $port) { throw 'Ports 9222 to 9240 are all taken: the window''s web view needs one for its debugging port.' }

if (-not (Test-Path (Join-Path $tools 'node_modules\playwright-core'))) {
    # As the run-tinapayanrms driver installs it.
    New-Item -ItemType Directory -Force $tools | Out-Null
    npm install --prefix $tools playwright-core@1.48.2 --no-audit --no-fund --silent | Out-Null
}

New-Item -ItemType Directory -Force $work | Out-Null

if (-not $NoBuild) {
    Push-Location $repoAsCalled
    try {
        $projects = @('tests\TinapayanRMS.Benchmarks')
        if (-not $Exe) { $projects = @('src\TinapayanRMS.Maui') + $projects }
        foreach ($project in $projects) {
            Write-Output "Building $project..."
            $build = Invoke-Tool 'dotnet' @('build', $project, '-nologo', '-v', 'q') -Quiet
            if ($build.ExitCode -ne 0) {
                $build.Output | Select-Object -Last 30 | Write-Output
                throw "$project didn't build."
            }
        }
    }
    finally { Pop-Location }
}
foreach ($file in $app, $benchExe) { if (-not (Test-Path -LiteralPath $file)) { throw "Not built: $file" } }

# ---------------------------------------------------------------------------------------------------------------------
# The run.

$environment = @{}
foreach ($name in 'ConnectionStrings__Default', 'WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS', 'WEBVIEW2_USER_DATA_FOLDER', 'TINAPAYANRMS_SIGNED_IN', 'TINAPAYANRMS_SIGN_IN', 'NODE_PATH', 'SHOTS') {
    $environment[$name] = [Environment]::GetEnvironmentVariable($name)
}
$viewStateCopy = Join-Path $work 'view-state.json'
$hadViewState = Test-Path -LiteralPath $viewState
$copiedViewState = $false
$prepared = $false
$process = $null
$mover = $null
$dumps = @()
$logEnds = @{}
$started = Get-Date

try {
    Write-Output "Restoring the golden master's copy of the shop's data into $database..."
    $prepare = Invoke-Tool $benchExe @('startup', 'prepare', '--database', $database) -Quiet
    $prepared = $true
    # Its connection string, by the database it names: a start on no connection string at all would open the dev database.
    $connection = $prepare.Output | Where-Object { $_ -match "(^|;)\s*Database=$database\s*(;|$)" } | Select-Object -Last 1
    if ($prepare.ExitCode -ne 0 -or -not $connection) {
        $prepare.Output | Select-Object -Last 30 | Write-Output
        throw "$database couldn't be prepared."
    }

    # The view state every copy shares: put back as it was at the end, and meanwhile without the backups' second folder.
    if ($hadViewState) {
        Copy-Item -LiteralPath $viewState $viewStateCopy -Force
        $copiedViewState = $true
        $state = Get-Content -LiteralPath $viewState -Raw | ConvertFrom-Json
        if ($state.PSObject.Properties['backup-copy-folder']) {
            $state.PSObject.Properties.Remove('backup-copy-folder')
            [IO.File]::WriteAllText($viewState, ($state | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding $false))
        }
    }

    # Where each log file ended, so only what this run adds is read.
    if (Test-Path -LiteralPath $logFolder) {
        foreach ($log in Get-ChildItem -LiteralPath $logFolder -Filter 'tinapayanrms-*.log') { $logEnds[$log.Name] = $log.Length }
    }

    $env:ConnectionStrings__Default = $connection
    $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$port --disable-backgrounding-occluded-windows --disable-renderer-backgrounding --disable-background-timer-throttling"
    $env:TINAPAYANRMS_SIGNED_IN = '1'
    $env:NODE_PATH = Join-Path $tools 'node_modules'
    $env:SHOTS = $work
    Remove-Item Env:WEBVIEW2_USER_DATA_FOLDER, Env:TINAPAYANRMS_SIGN_IN -ErrorAction Ignore

    # The window moved past the desktop's right edge as it shows, by a helper thread (Compare-Benchmarks.ps1 -Window's).
    . (Join-Path $PSScriptRoot 'benchmarks\WindowStarts.ps1')
    $mover = Start-WindowMover

    Write-Output "Starting $app (debugging port $port)..."
    $process = Start-Process -FilePath $app -PassThru
    $mover.Shared.ProcessId = $process.Id

    $deadline = (Get-Date).AddSeconds(90)
    while (-not (Test-Port $port)) {
        if ($process.HasExited) { throw "The app ended as it started (exit code $($process.ExitCode)): see $logFolder." }
        if ((Get-Date) -gt $deadline) { throw "The window didn't open its debugging port within 90 seconds: see $logFolder." }
        Start-Sleep -Milliseconds 500
    }
    # A Release copy asks for the password whatever TINAPAYANRMS_SIGNED_IN says: typed as the benchmarks' window starts type it.
    if ($Exe) {
        $ready = Invoke-Tool 'node' @($signIn, "$port", $script:WindowPassword, '90000') -Quiet
    }
    else {
        do {
            $ready = Invoke-Driver @('ready') -Quiet
            if ($ready.ExitCode -ne 0) { Start-Sleep -Seconds 1 }
        } while ($ready.ExitCode -ne 0 -and (Get-Date) -lt $deadline)
    }
    if ($ready.ExitCode -ne 0) {
        $ready.Output | Write-Output
        throw 'The window never showed its first screen.'
    }

    Write-Output ''
    Write-Output 'The smoke run:'
    $smoke = Invoke-Driver @('smoke')
    if ($smoke.ExitCode -ne 0) { $failures.Add('the smoke run') }

    if ($Rounds -gt 0) {
        # Counted on a page with no grid, so a grid still alive is one something kept.
        function Get-LiveCounts([string]$Name) {
            [void](Invoke-Driver @('go', 'settings/keyboard') -Quiet)
            Start-Sleep -Seconds 3
            $file = Join-Path $work "$Name.dmp"
            Remove-Item -LiteralPath $file -ErrorAction Ignore
            $collect = Invoke-Tool 'dotnet-dump' @('collect', '-p', "$($process.Id)", '--type', 'Heap', '-o', $file) -Quiet
            if ($collect.ExitCode -ne 0) { $collect.Output | Write-Output; throw "dotnet-dump couldn't take the $Name dump." }
            $script:dumps += $file
            $analyze = Invoke-Tool 'dotnet-dump' @('analyze', $file, '-c', 'dumpheap -stat -live', '-c', 'exit') -Quiet
            if ($analyze.ExitCode -ne 0) { $analyze.Output | Select-Object -Last 20 | Write-Output; throw "dotnet-dump couldn't count the $Name dump." }
            $live = @{}
            foreach ($line in $analyze.Output) {
                if ($line -match '^\s*[0-9a-fA-F]{8,}\s+([\d,]+)\s+([\d,]+)\s+(\S.*?)\s*$') {
                    $type = $Matches[3]
                    # The app's own types and Syncfusion's grids; not the compiler's closures and caches, which come and go
                    # with the calls that make them.
                    if (($type -like 'TinapayanRMS.*' -or $type -like 'Syncfusion.Blazor.Grids.SfGrid*') -and
                        $type -notmatch '\+<>c|DisplayClass|Choice\[\]|Comparer') {
                        $live[$type] = [long]($Matches[1] -replace ',', '')
                    }
                }
            }
            $live
        }

        function Invoke-Rounds([int]$From, [int]$To, [int]$Of, [string]$What) {
            for ($round = $From; $round -le $To; $round++) {
                $walk = Invoke-Driver @('smoke', '--quiet')
                Write-Output ("{0} {1} of {2}: {3}" -f $What, $round, $Of, $(if ($walk.ExitCode -eq 0) { 'ok' } else { 'FAILED' }))
                if ($walk.ExitCode -ne 0) { $failures.Add("the soak run's $What $round") }
            }
        }

        Write-Output ''
        Write-Output "The soak run: 2 rounds to warm up, then $Rounds, the live heap counted before, halfway and after."
        # A screen keeps a second copy of what it shows once it has loaded twice (TableRows keeps the rows it had when the
        # new ones show the same), so the first count comes after every screen's third visit.
        Invoke-Rounds 1 2 2 'Warm-up round'
        $counts = @((Get-LiveCounts 'before'))
        Invoke-Rounds 1 ($Rounds / 2) $Rounds 'Round'
        $counts += (Get-LiveCounts 'halfway')
        Invoke-Rounds ($Rounds / 2 + 1) $Rounds $Rounds 'Round'
        $counts += (Get-LiveCounts 'after')

        # Grown in both halves: one more a visit, or a few, which is something kept. A count that went up once and then
        # held is a cache filled, or a holder that settled.
        $of = { param($table, $type) if ($table.ContainsKey($type)) { $table[$type] } else { 0 } }
        $grown = @($counts[2].Keys | ForEach-Object {
            $a = & $of $counts[0] $_; $b = & $of $counts[1] $_; $c = $counts[2][$_]
            if ($b -gt $a -and $c -gt $b) { [pscustomobject]@{ Type = $_; Before = $a; Halfway = $b; After = $c } }
        } | Sort-Object @{ Expression = { $_.After - $_.Before }; Descending = $true }, Type)
        if ($grown) {
            $failures.Add("the soak run's live heap ($($grown.Count) of the app's types kept growing)")
            Write-Output "FAIL live instances that grew in both halves of $Rounds rounds (gcroot one in the dumps kept in $work to see what holds it):"
            ($grown | Select-Object -First 40 | Format-Table -AutoSize | Out-String -Width 250).Trim("`r", "`n") | Write-Output
        }
        else {
            $once = @($counts[2].Keys | Where-Object { $counts[2][$_] -gt (& $of $counts[0] $_) }).Count
            Write-Output "ok   no live instance of the app's types or a grid kept growing over $Rounds rounds ($($counts[2].Count) types counted; $once grew in one half only)."
        }
    }

    Write-Output ''
    Write-Output 'The close question:'
    $close = Invoke-Driver @('closecheck')
    if ($close.ExitCode -ne 0) { $failures.Add('the close question') }
    elseif (-not $process.WaitForExit(30000)) { $failures.Add('the close question'); Write-Output 'FAIL the window was still open 30 seconds after Close was answered No.' }
    else {
        Write-Output 'ok   the window closed'
        # No saved nothing: the rename isn't in the database.
        $psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
        $saved = Invoke-Tool $psql @('-h', 'localhost', '-U', 'tinapayanrms', '-d', $database, '-At', '-c', "SELECT count(*) FROM raw_materials WHERE name LIKE '% (close check)'") -Quiet
        if ($saved.ExitCode -ne 0) { $failures.Add('the close question'); Write-Output "FAIL the scratch database couldn't be read: $($saved.Output -join ' ')" }
        elseif ("$($saved.Output | Select-Object -Last 1)".Trim() -ne '0') { $failures.Add('the close question'); Write-Output 'FAIL the rename was saved, though Close was answered No.' }
        else { Write-Output 'ok   No saved nothing' }
    }
}
finally {
    if ($mover) { Stop-WindowMover $mover }
    if ($process -and -not $process.HasExited) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(15000)) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue; [void]$process.WaitForExit(10000) }
    }
    foreach ($name in $environment.Keys) { [Environment]::SetEnvironmentVariable($name, $environment[$name]) }
    Start-Sleep -Milliseconds 500
    if ($copiedViewState) { Copy-Item -LiteralPath $viewStateCopy $viewState -Force; Remove-Item -LiteralPath $viewStateCopy }
    elseif (-not $hadViewState -and (Test-Path -LiteralPath $viewState)) { Remove-Item -LiteralPath $viewState -Force }
    if ($prepared) {
        $drop = Invoke-Tool $benchExe @('startup', 'drop', '--database', $database) -Quiet
        if ($drop.ExitCode -ne 0) { Write-Output "Couldn't drop $database (and its backups): $($drop.Output -join ' ')" }
    }
    $grewKept = $failures | Where-Object { $_ -like "*live heap*" }
    if ($dumps -and ($KeepDumps -or $grewKept)) { Write-Output "The heap dumps are kept: $($dumps -join ', ')" }
    else { foreach ($dump in $dumps) { Remove-Item -LiteralPath $dump -ErrorAction Ignore } }
}

# ---------------------------------------------------------------------------------------------------------------------
# The log: what the run added to it.

Write-Output ''
Write-Output 'The log:'
$errors = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
if (Test-Path -LiteralPath $logFolder) {
    foreach ($log in Get-ChildItem -LiteralPath $logFolder -Filter 'tinapayanrms-*.log' | Where-Object { $_.LastWriteTime -ge $started.AddSeconds(-5) }) {
        $from = if ($logEnds.ContainsKey($log.Name)) { $logEnds[$log.Name] } else { 0 }
        if ($log.Length -le $from) { continue }
        # Shared, as a copy still closing may hold it.
        $stream = New-Object IO.FileStream($log.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        try {
            [void]$stream.Seek($from, [IO.SeekOrigin]::Begin)
            $reader = New-Object IO.StreamReader($stream)
            $lines = $reader.ReadToEnd() -split "`r?`n"
        }
        finally { $stream.Dispose() }
        # An entry is its line and the exception lines under it, up to the next entry's timestamp.
        $current = $null
        foreach ($line in $lines + @('0000-00-00 00:00:00')) {
            if ($line -match '^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}') {
                if ($current) {
                    if ($current.Level -in 'ERR', 'FTL') { $errors.Add($current.Text) } elseif ($current.Level -eq 'WRN') { $warnings.Add($current.Text) }
                }
                $level = if ($line -match '\[(VRB|DBG|INF|WRN|ERR|FTL)\]') { $Matches[1] } else { '' }
                $current = [pscustomobject]@{ Level = $level; Text = $line; Lines = 1 }
            }
            elseif ($current -and $line.Trim() -and $current.Lines -lt 6) {
                $current.Text += "`n       $($line.Trim())"
                $current.Lines++
            }
        }
    }
}
if ($errors.Count) {
    $failures.Add("the log ($($errors.Count) errors)")
    Write-Output "FAIL $($errors.Count) errors logged during the run:"
    $errors | ForEach-Object { Write-Output "     $_" }
}
else {
    Write-Output 'ok   no error logged during the run'
}
if ($warnings.Count) {
    Write-Output "     $($warnings.Count) warnings logged (not failures):"
    $warnings | Group-Object { ($_ -split "`n")[0] -replace '^\S+ \S+ \S+ ', '' } | ForEach-Object {
        Write-Output ("     {0}x {1}" -f $_.Count, (($_.Group[0] -split "`n") -join "`n     "))
    }
}

Write-Output ''
if ($failures.Count) {
    Write-Output "FAILED: $($failures -join '; ')."
    exit 1
}
Write-Output "Passed: the smoke run$(if ($Rounds) { ", $Rounds soak rounds" }), the close question and the log, in $([math]::Round(((Get-Date) - $started).TotalMinutes, 1)) minutes."
exit 0
