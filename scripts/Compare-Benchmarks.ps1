<#
.SYNOPSIS
    Compares how fast two builds of the app are, a base commit's and the working tree's, to show what a refactor made
    slower: the screens' reads, the costing, the cost cascade, the screen states, and the start.

.DESCRIPTION
    Both builds are timed on the same data, the golden master's anonymized copy of the shop's, in processes that alternate
    between the two (base, head, then head, base), so whatever the PC is doing meanwhile falls on both alike: timings
    taken minutes apart on this laptop aren't comparable (docs/adr/0008).

      Benchmarks  tests/TinapayanRMS.Benchmarks' scenarios, warm, timed by BenchmarkDotNet: -Rounds processes a side.
      Startup     the app's start without its window, cold, one sample a process (startup sample): -StartupRuns pairs.
                  Each stage's time, and what the start took in SQL commands, methods compiled, assemblies and memory.
      Window      with -Window: the published app's own start, as the shop starts it: -WindowRuns pairs. When its window
                  showed, when its page began, when the sign-in panel could be typed into, how long Raw Materials' list
                  took to show once signed in, and the page's JS heap. A window opens on the mouse's monitor at each
                  start for a moment before it's moved off the desktop, and can take the keyboard: run it while you
                  aren't typing. It needs no copy of the app open (they share the Windows user's view state), Node.js,
                  and port 9223 free.

    Each round, or start of each side, is a pair of neighbours in time. A time is worse when the median of head / base over
    the pairs (the table's Change) is more than -Threshold per cent over 1, and chance alone would show head that much
    slower 1.5% of the time or less: every head round beyond every base round (with 4 a side, 1 in 70), or head slower in
    9 pairs of 10. A table compares some twenty times, so a looser bar would trip on an unchanged tree too often. Better the
    other way round. With too few pairs ever to tell (-Quick's 2 rounds) it says "slower?", which fails nothing. SQL
    commands and assemblies are compared as counts (one more is worse), memory within 5%, methods compiled within 2%. It
    ends with a table, also written as summary.md in the results folder it names, and exits 1 when anything got worse.
    -Results reads an earlier run's folder again, with this run's -Threshold, and runs nothing.

    The base is checked out in a git worktree at %TEMP%\trms-bench\base, kept between runs so its build stays incremental
    (-Clean removes it). Both sides build with -c Bench -p:Optimize=true into %TEMP%\trms-bench, which leaves your Debug
    build and the MCP server's Release one alone. -Window also publishes each side's app there, as
    Install-TinapayanRMS.ps1 publishes it, which builds Release in each tree, the working tree's bin and obj included, as
    that script does: the MCP server's next start rebuilds what it changed. Each side measures on databases of its own, restored from the fixture by its
    own build (tinapayanrms_bench_*), vacuumed and analyzed before anything is timed, dropped again at the end, never the
    dev database. It refuses to start while a test project is running, and with -Window while any copy of the app is.

.PARAMETER Base
    The build compared against: a commit (main, HEAD~2, a SHA), or a folder holding a copy of the repo. Default main.

.PARAMETER Head
    The build compared: a commit, or a folder. Default the working tree, as it is now.

.PARAMETER Filter
    The benchmark scenarios to time, * for anything, a comma between patterns ('Screens.*,Pricing.ReadAll'). Default all.

.PARAMETER Rounds
    Benchmark processes a side. Default 4, 2 with -Quick.

.PARAMETER StartupRuns
    Cold starts a side. Default 10, 4 with -Quick.

.PARAMETER WindowRuns
    Starts of the window a side, with -Window. Default 10.

.PARAMETER Threshold
    How many per cent slower a time must be to count as worse. Default 10.

.PARAMETER Window
    Times the published app's start in its window too.

.PARAMETER Only
    One part alone: Benchmarks, Startup or Window.

.PARAMETER Quick
    BenchmarkDotNet's short run and fewer rounds and starts: a first look in a few minutes, too few pairs to trust a
    small change.

.PARAMETER Results
    An earlier run's results folder: its table worked out again from what it measured, nothing run.

.PARAMETER Clean
    Removes what earlier runs kept (the base's worktree, the builds, the results, the copies' web view profiles) and drops
    any benchmark database a killed run left, then stops.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/Compare-Benchmarks.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/Compare-Benchmarks.ps1 -Base HEAD -Filter 'Screens.*' -Only Benchmarks

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/Compare-Benchmarks.ps1 -Base main -Window
#>
[CmdletBinding()]
param(
    [string]$Base = 'main',
    [string]$Head = '',
    [string]$Filter = '*',
    [int]$Rounds = 0,
    [int]$StartupRuns = 0,
    [int]$WindowRuns = 10,
    [double]$Threshold = 10,
    [switch]$Window,
    [ValidateSet('', 'Benchmarks', 'Startup', 'Window')][string]$Only = '',
    [switch]$Quick,
    [string]$Results = '',
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# The repo's folder spelled as the caller's terminal spells it: the build records each project's folder with its drive
# letter's case, so the other case would make this run recompile every project, and the next run from the terminal again.
$here = [Environment]::CurrentDirectory
$repoAsCalled = if ($here.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { $here.Substring(0, $repo.Length) } else { $repo }

$work = Join-Path ([IO.Path]::GetTempPath()) 'trms-bench'
$benchProject = 'tests\TinapayanRMS.Benchmarks\TinapayanRMS.Benchmarks.csproj'

if ($Rounds -le 0) { $Rounds = if ($Quick) { 2 } else { 4 } }
if ($StartupRuns -le 0) { $StartupRuns = if ($Quick) { 4 } else { 10 } }
$parts = if ($Only) { @($Only) } elseif ($Window) { @('Benchmarks', 'Startup', 'Window') } else { @('Benchmarks', 'Startup') }

# A command line tool run with what it writes kept: Windows PowerShell turns a native tool's error output into errors,
# which would stop this script at the first warning it writes.
function Invoke-Tool([string]$File, [string[]]$Arguments, [string]$Log) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $File @Arguments 2>&1 | ForEach-Object { "$_" }
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($Log) { $output | Out-File -FilePath $Log -Append -Encoding utf8 }
    [pscustomobject]@{ ExitCode = $exitCode; Output = @($output) }
}

# A folder git or Remove-Item can't take (paths too long for them): emptied by robocopy first.
function Remove-Folder([string]$Folder) {
    if (-not (Test-Path -LiteralPath $Folder)) { return }
    $empty = Join-Path ([IO.Path]::GetTempPath()) 'trms-bench-empty'
    New-Item -ItemType Directory -Force $empty | Out-Null
    robocopy $empty $Folder /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    Remove-Item -LiteralPath $Folder -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $empty -Force -ErrorAction SilentlyContinue
}

# The web view folder MauiProgram gives a copy of the app in $Folder (CopyKey: a hash of its folder).
function Get-WebViewFolder([string]$Folder) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $bytes = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($Folder.TrimEnd('\') + '\').ToUpperInvariant()))
    $key = (($bytes | ForEach-Object { $_.ToString('X2') }) -join '').Substring(0, 16)
    Join-Path $env:LOCALAPPDATA "TinapayanRMS\Maui\WebView2\$key"
}

# What a run measured, kept in its results folder as JSON, which the table is worked out from (and -Results again).
function Save-Json($Value, [string]$Path) {
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 6), (New-Object Text.UTF8Encoding $false))
}

function Read-Json([string]$Path) { @(Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json) }

# ---------------------------------------------------------------------------------------------------------------------
# The verdicts.

function Get-Median([double[]]$Values) {
    $sorted = @($Values | Where-Object { -not [double]::IsNaN($_) } | Sort-Object)
    if ($sorted.Count -eq 0) { return [double]::NaN }
    $middle = [math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
}

function Get-Choose([int]$N, [int]$K) {
    $ways = 1.0
    for ($i = 1; $i -le $K; $i++) { $ways = $ways * ($N - $K + $i) / $i }
    $ways
}

# How often chance alone would make head this much slower than base (with -Faster, faster), were there no difference
# between them: the smaller of two odds. Every head value beyond every base value: 1 in (2n choose n). Head slower in k
# pairs of n or more: a coin's odds of k heads or more in n tosses.
function Get-Chance([double[]]$BaseValues, [double[]]$HeadValues, [switch]$Faster) {
    $n = [math]::Min($BaseValues.Count, $HeadValues.Count)
    if ($n -eq 0) { return 1.0 }
    $sign = if ($Faster) { -1 } else { 1 }
    $headLeast = ($HeadValues | ForEach-Object { $sign * $_ } | Measure-Object -Minimum).Minimum
    $baseMost = ($BaseValues | ForEach-Object { $sign * $_ } | Measure-Object -Maximum).Maximum
    $apart = if ($headLeast -gt $baseMost) { 1.0 / (Get-Choose (2 * $n) $n) } else { 1.0 }

    $k = 0
    for ($i = 0; $i -lt $n; $i++) { if ($sign * $HeadValues[$i] -gt $sign * $BaseValues[$i]) { $k++ } }
    $tail = 0.0
    for ($j = $k; $j -le $n; $j++) { $tail += (Get-Choose $n $j) / [math]::Pow(2, $n) }
    [math]::Min($apart, $tail)
}

# The least chance any result of $Count pairs could show: when even that is over the odds a verdict needs, no result of
# them would do, and a difference is only a "slower?".
function Get-LeastChance([int]$Count) { [math]::Min(1.0 / (Get-Choose (2 * $Count) $Count), 1.0 / [math]::Pow(2, $Count)) }

# A verdict needs odds of 1.5% or less: 4 rounds a side pass only with every head round beyond every base round (1 in
# 70), 10 starts with 9 pairs of 10 (1.1%).
$script:Odds = 0.015

$rows = New-Object System.Collections.Generic.List[object]

function Format-Value([double]$Value, [string]$Unit) {
    if ([double]::IsNaN($Value)) { return '-' }
    switch ($Unit) {
        'ms' { '{0:N1} ms' -f $Value }
        'KB' { '{0:N0} KB' -f $Value }
        'MB' { '{0:N2} MB' -f $Value }
        default { '{0:N0}' -f $Value }
    }
}

function Add-Row($Part, $Measure, [double]$BaseValue, [double]$HeadValue, $Unit, [double]$Ratio, $Verdict, [bool]$Worse) {
    $rows.Add([pscustomobject]@{
        Part    = $Part
        Measure = $Measure
        Base    = Format-Value $BaseValue $Unit
        Head    = Format-Value $HeadValue $Unit
        Change  = if ([double]::IsNaN($Ratio)) { '' } else { '{0:+0.0;-0.0;0.0}%' -f (($Ratio - 1) * 100) }
        Verdict = if ($Worse) { "$Verdict (worse)" } else { $Verdict }
        Worse   = $Worse
    })
}

# A time, pair by pair: worse when the median of head / base over the pairs is over 1 + Threshold%, and chance would show
# head that much slower 1.5% of the time or less (Get-Chance); better the other way round. With too few pairs ever to
# tell, "slower?" or "faster?", which fails nothing.
function Add-Time([string]$Part, [string]$Measure, [double[]]$BaseValues, [double[]]$HeadValues, [string]$Unit) {
    $base = New-Object System.Collections.Generic.List[double]
    $head = New-Object System.Collections.Generic.List[double]
    for ($i = 0; $i -lt [math]::Min($BaseValues.Count, $HeadValues.Count); $i++) {
        if ($BaseValues[$i] -gt 0 -and -not [double]::IsNaN($BaseValues[$i]) -and -not [double]::IsNaN($HeadValues[$i])) {
            $base.Add($BaseValues[$i])
            $head.Add($HeadValues[$i])
        }
    }
    $ratio = if ($base.Count -gt 0) { Get-Median @(0..($base.Count - 1) | ForEach-Object { $head[$_] / $base[$_] }) } else { [double]::NaN }
    $undecidable = (Get-LeastChance $base.Count) -gt $script:Odds
    $verdict = if ($base.Count -eq 0) { 'not measured' }
               elseif ($ratio -gt 1 + $Threshold / 100) {
                   if ($undecidable) { 'slower?' } elseif ((Get-Chance $base.ToArray() $head.ToArray()) -le $script:Odds) { 'slower' } else { 'same' }
               }
               elseif ($ratio -lt 1 - $Threshold / 100) {
                   if ($undecidable) { 'faster?' } elseif ((Get-Chance $base.ToArray() $head.ToArray() -Faster) -le $script:Odds) { 'faster' } else { 'same' }
               }
               else { 'same' }
    Add-Row $Part $Measure (Get-Median $BaseValues) (Get-Median $HeadValues) $Unit $ratio $verdict ($verdict -eq 'slower')
}

# An amount (memory, methods compiled): worse when head's median is more than $Allowed% over base's.
function Add-Amount([string]$Part, [string]$Measure, [double[]]$BaseValues, [double[]]$HeadValues, [string]$Unit, [double]$Allowed) {
    $baseMedian = Get-Median $BaseValues
    $headMedian = Get-Median $HeadValues
    $ratio = if ($baseMedian -gt 0) { $headMedian / $baseMedian } else { [double]::NaN }
    $verdict = if ([double]::IsNaN($ratio)) { 'same' }
               elseif ($ratio -gt 1 + $Allowed / 100) { 'more' }
               elseif ($ratio -lt 1 - $Allowed / 100) { 'less' }
               else { 'same' }
    Add-Row $Part $Measure $baseMedian $headMedian $Unit $ratio $verdict ($verdict -eq 'more')
}

# A count (SQL commands, assemblies): worse when head's median is any higher.
function Add-Count([string]$Part, [string]$Measure, [double[]]$BaseValues, [double[]]$HeadValues) {
    $baseMedian = Get-Median $BaseValues
    $headMedian = Get-Median $HeadValues
    $verdict = if ($headMedian -gt $baseMedian) { 'more' } elseif ($headMedian -lt $baseMedian) { 'fewer' } else { 'same' }
    $ratio = if ($baseMedian -gt 0) { $headMedian / $baseMedian } else { [double]::NaN }
    Add-Row $Part $Measure $baseMedian $headMedian '' $ratio $verdict ($verdict -eq 'more')
}

# ---------------------------------------------------------------------------------------------------------------------
# Clean.

if ($Clean) {
    # A benchmark or budget run going on elsewhere has its databases open: they aren't leftovers.
    $running = @(Get-Process TinapayanRMS.Benchmarks, TinapayanRMS.Benchmarks.Tests -ErrorAction SilentlyContinue)
    if ($running) {
        Write-Output "$($running[0].ProcessName) is running (process $($running[0].Id)): its databases aren't leftovers. Clean once it's done."
        exit 1
    }
    $exe = @('head', 'base') | ForEach-Object { Join-Path $work "bench-$_\TinapayanRMS.Benchmarks.exe" } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($exe) {
        $dropped = Invoke-Tool $exe @('clean')
        $dropped.Output | Write-Output
    }
    else {
        Write-Output 'No build of the benchmarks to drop leftover databases with (tinapayanrms_bench_*): none was made.'
    }
    foreach ($side in 'base', 'head') { Remove-Folder (Get-WebViewFolder (Join-Path $work "app-$side")) }
    foreach ($tree in 'base', 'head-tree') { Remove-Folder (Join-Path $work $tree) }
    & git -C $repo worktree prune
    Remove-Folder $work
    Write-Output "Removed $work, its worktrees and the copies' web view folders."
    exit 0
}

# ---------------------------------------------------------------------------------------------------------------------
# The runs: what each side measures, kept in the results folder.

if ($Results) {
    $results = (Resolve-Path -LiteralPath $Results).Path
}
else {
    # What would get in the way: a test project running (it loads the PC the timings are taken on); and for the window,
    # any copy of the app (they share the Windows user's view state, and one run from this repo holds the DLLs a publish of
    # the working tree rebuilds). The benchmarks build into bin\Bench only, which no copy of the app runs from.
    $testProjects = Get-ChildItem (Join-Path $repo 'tests') -Filter '*.Tests.csproj' -Recurse -Depth 1 | ForEach-Object BaseName
    $blocking = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -in $testProjects -or ($_.ProcessName -eq 'TinapayanRMS.Maui' -and $parts -contains 'Window')
    })
    if ($blocking) {
        foreach ($process in $blocking) {
            $why = if ($process.ProcessName -eq 'TinapayanRMS.Maui') { 'the window''s starts share its view state' } else { 'a test run loads the PC the timings are taken on' }
            Write-Output "$($process.ProcessName) is running (process $($process.Id)): close it first, as $why."
        }
        exit 1
    }

    if ($parts -contains 'Window') {
        if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'The window''s starts need Node.js (node) on the PATH.' }
        $client = New-Object Net.Sockets.TcpClient
        try { $taken = $client.ConnectAsync('127.0.0.1', 9223).Wait(500) -and $client.Connected } catch { $taken = $false } finally { $client.Dispose() }
        if ($taken) { throw 'Port 9223 is taken: the window''s starts open their web view''s debugging port there. Close what holds it.' }
    }

    New-Item -ItemType Directory -Force $work | Out-Null
    $results = Join-Path $work ("results\" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Force $results | Out-Null

    # Each side: its tree (the working tree, a folder, or a worktree checked out at the commit), and what to call it.
    function Resolve-Side([string]$Key, [string]$Spec) {
        if ($Spec -eq '' -and $Key -eq 'head') {
            $uncommitted = @(& git -C $repo status --porcelain).Count
            $name = if ($uncommitted -gt 0) { "the working tree ($uncommitted files not committed)" } else { "the working tree ($(& git -C $repo log -1 --format='%h %s'))" }
            return [pscustomobject]@{ Key = $Key; Tree = $repoAsCalled; Name = $name }
        }
        if (Test-Path -LiteralPath (Join-Path $Spec 'TinapayanRMS.slnx')) {
            return [pscustomobject]@{ Key = $Key; Tree = (Resolve-Path -LiteralPath $Spec).Path; Name = "the copy in $Spec" }
        }

        $sha = & git -C $repo rev-parse --verify --quiet "$Spec^{commit}"
        if (-not $sha) { throw "$Spec is neither a commit nor a folder holding the repo." }
        $tree = Join-Path $work $(if ($Key -eq 'base') { 'base' } else { 'head-tree' })
        & git -C $repo worktree prune
        if (Test-Path -LiteralPath (Join-Path $tree '.git')) {
            & git -C $tree checkout --detach --force --quiet $sha
            & git -C $tree clean -fdq
        }
        else {
            Remove-Folder $tree
            & git -C $repo worktree add --detach --quiet $tree $sha
        }
        if ($LASTEXITCODE -ne 0) { throw "Couldn't check $Spec out in $tree." }
        [pscustomobject]@{ Key = $Key; Tree = $tree; Name = "$Spec ($(& git -C $repo log -1 --format='%h %s' $sha))" }
    }

    $sides = [ordered]@{ base = (Resolve-Side 'base' $Base); head = (Resolve-Side 'head' $Head) }
    foreach ($side in $sides.Values) {
        if (-not (Test-Path -LiteralPath (Join-Path $side.Tree $benchProject))) {
            throw "$($side.Name) has no $benchProject to time it with: compare commits from the one that added it on."
        }
    }
    Save-Json ([pscustomobject]@{ base = $sides.base.Name; head = $sides.head.Name }) (Join-Path $results 'sides.json')

    Write-Output "Base: $($sides.base.Name)"
    Write-Output "Head: $($sides.head.Name)"
    Write-Output "Results: $results"

    $exes = @{}
    foreach ($side in $sides.Values) {
        Write-Output "Building the benchmarks of the $($side.Key) side..."
        $out = Join-Path $work "bench-$($side.Key)"
        $build = Invoke-Tool 'dotnet' @('build', (Join-Path $side.Tree $benchProject), '-c', 'Bench', '-p:Optimize=true', '-o', $out, '-nologo', '-v', 'q') (Join-Path $results "build-$($side.Key).log")
        if ($build.ExitCode -ne 0) {
            $build.Output | Select-Object -Last 20 | Write-Output
            throw "The $($side.Key) side's benchmarks didn't build: see $results\build-$($side.Key).log."
        }
        $exes[$side.Key] = Join-Path $out 'TinapayanRMS.Benchmarks.exe'
    }

    # The order of the two sides in a pair: base first in odd ones, head first in even ones.
    function Get-Order([int]$Pair) { if ($Pair % 2) { @('base', 'head') } else { @('head', 'base') } }

    $databases = @{ base = 'tinapayanrms_bench_startup_base'; head = 'tinapayanrms_bench_startup_head' }
    $prepared = @{}
    try {
        if ($parts -contains 'Benchmarks') {
            for ($round = 1; $round -le $Rounds; $round++) {
                foreach ($key in (Get-Order $round)) {
                    Write-Output "Benchmarks: round $round of $Rounds, $key..."
                    $arguments = @('bench', '--scenario', $Filter, '--artifacts', (Join-Path $results "bench-$round-$key"))
                    if ($Quick) { $arguments += '--quick' }
                    $run = Invoke-Tool $exes[$key] $arguments (Join-Path $results "bench-$round-$key.log")
                    if ($run.ExitCode -ne 0) { throw "The $key side's benchmarks failed in round ${round}: see $results\bench-$round-$key.log." }
                }
            }
        }

        if ($parts -contains 'Startup' -or $parts -contains 'Window') {
            foreach ($key in 'base', 'head') {
                Write-Output "Preparing the $key side's start database..."
                $prepare = Invoke-Tool $exes[$key] @('startup', 'prepare', '--database', $databases[$key]) (Join-Path $results "prepare-$key.log")
                if ($prepare.ExitCode -ne 0) { throw "The $key side couldn't prepare its database: see $results\prepare-$key.log." }
                # Its connection string, by the database it names: a start on no connection string at all would open the
                # app's own database, the developer's, from the appsettings.json built into it.
                $prepared[$key] = $prepare.Output | Where-Object { $_ -match "(^|;)\s*Database=$($databases[$key])\s*(;|$)" } | Select-Object -Last 1
                if (-not $prepared[$key]) { throw "The $key side's prepare named no $($databases[$key]): see $results\prepare-$key.log." }
            }
        }

        if ($parts -contains 'Startup') {
            $samples = @{ base = @(); head = @() }
            # A first start of each, not counted: Windows reads a new build's files from the disk the first time.
            for ($pair = 0; $pair -le $StartupRuns; $pair++) {
                foreach ($key in (Get-Order $pair)) {
                    if ($pair -gt 0) { Write-Output "Cold starts: $pair of $StartupRuns, $key..." }
                    $sample = Invoke-Tool $exes[$key] @('startup', 'sample', '--database', $databases[$key]) (Join-Path $results "startup-$key.log")
                    $json = $sample.Output | Where-Object { $_.StartsWith('{') } | Select-Object -Last 1
                    if ($sample.ExitCode -ne 0 -or -not $json) { throw "A cold start of the $key side failed: see $results\startup-$key.log." }
                    if ($pair -gt 0) { $samples[$key] += ($json | ConvertFrom-Json) }
                }
            }
            foreach ($key in 'base', 'head') { Save-Json $samples[$key] (Join-Path $results "startup-$key.json") }
        }

        if ($parts -contains 'Window') {
            . (Join-Path $PSScriptRoot 'benchmarks\WindowStarts.ps1')
            $watcher = Join-Path $PSScriptRoot 'benchmarks\window-start.js'

            $tools = Join-Path $env:LOCALAPPDATA 'TinapayanRMS\dev\playwright'
            if (-not (Test-Path (Join-Path $tools 'node_modules\playwright-core'))) {
                # As the run-tinapayanrms driver installs it.
                New-Item -ItemType Directory -Force $tools | Out-Null
                npm install --prefix $tools playwright-core@1.48.2 --no-audit --no-fund --silent | Out-Null
            }

            $apps = @{}
            foreach ($side in $sides.Values) {
                Write-Output "Publishing the $($side.Key) side's app (a few minutes)..."
                $out = Join-Path $work "app-$($side.Key)"
                $publish = Invoke-Tool 'dotnet' @('publish', (Join-Path $side.Tree 'src\TinapayanRMS.Maui'), '--configuration', 'Release', '--output', $out, '-p:UseSatoriGC=false', '--nologo', '-v', 'q') (Join-Path $results "publish-$($side.Key).log")
                if ($publish.ExitCode -ne 0) { throw "The $($side.Key) side's app didn't publish: see $results\publish-$($side.Key).log." }
                $apps[$side.Key] = Join-Path $out 'TinapayanRMS.Maui.exe'
            }

            # The Windows user's view state, which both copies read and write: put back as it was at the end, and meanwhile
            # without the backups' second folder, where each copy's automatic backup of its database would be copied.
            $viewState = Join-Path $env:LOCALAPPDATA 'TinapayanRMS\Maui\view-state.json'
            $viewStateCopy = Join-Path $results 'view-state.json'
            $hadViewState = Test-Path -LiteralPath $viewState
            if ($hadViewState) {
                Copy-Item -LiteralPath $viewState $viewStateCopy
                $state = Get-Content -LiteralPath $viewState -Raw | ConvertFrom-Json
                if ($state.PSObject.Properties['backup-copy-folder']) {
                    $state.PSObject.Properties.Remove('backup-copy-folder')
                    [IO.File]::WriteAllText($viewState, ($state | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding $false))
                }
            }

            $environment = @{}
            foreach ($name in 'ConnectionStrings__Default', 'WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS', 'WEBVIEW2_USER_DATA_FOLDER', 'TINAPAYANRMS_SIGNED_IN', 'TINAPAYANRMS_SIGN_IN', 'NODE_PATH') {
                $environment[$name] = [Environment]::GetEnvironmentVariable($name)
            }
            # The debugging port, and none of the throttling a web view off the screen would otherwise get.
            $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$script:WindowPort --disable-backgrounding-occluded-windows --disable-renderer-backgrounding --disable-background-timer-throttling"
            Remove-Item Env:WEBVIEW2_USER_DATA_FOLDER, Env:TINAPAYANRMS_SIGNED_IN, Env:TINAPAYANRMS_SIGN_IN -ErrorAction Ignore
            $env:NODE_PATH = Join-Path $tools 'node_modules'

            $mover = Start-WindowMover
            try {
                $starts = @{ base = @(); head = @() }
                # A first start of each, not counted: a new build's files read from the disk, the web view's folder made, and
                # the database's automatic backup of the day taken.
                for ($pair = 0; $pair -le $WindowRuns; $pair++) {
                    foreach ($key in (Get-Order $pair)) {
                        if ($pair -gt 0) { Write-Output "Window starts: $pair of $WindowRuns, $key..." } else { Write-Output "Window: a first start of the $key side, not counted..." }
                        $start = Measure-WindowStart $apps[$key] $prepared[$key] $mover $watcher (Join-Path $results "window-$key.log")
                        if ($pair -gt 0) { $starts[$key] += $start }
                    }
                }
                foreach ($key in 'base', 'head') { Save-Json $starts[$key] (Join-Path $results "window-$key.json") }
            }
            finally {
                Stop-WindowMover $mover
                foreach ($name in $environment.Keys) { [Environment]::SetEnvironmentVariable($name, $environment[$name]) }
                foreach ($exe in $apps.Values) {
                    Get-Process TinapayanRMS.Maui -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Stop-Process -Force -ErrorAction SilentlyContinue
                }
                Start-Sleep -Milliseconds 500
                if ($hadViewState) { Copy-Item -LiteralPath $viewStateCopy $viewState -Force }
                elseif (Test-Path -LiteralPath $viewState) { Remove-Item -LiteralPath $viewState -Force }
            }
        }
    }
    finally {
        foreach ($key in $prepared.Keys) {
            $drop = Invoke-Tool $exes[$key] @('startup', 'drop', '--database', $databases[$key]) (Join-Path $results "prepare-$key.log")
            if ($drop.ExitCode -ne 0) { Write-Output "Couldn't drop $($databases[$key]): run this script with -Clean." }
        }
    }
}

# ---------------------------------------------------------------------------------------------------------------------
# The table, from what the results folder holds.

$names = if (Test-Path -LiteralPath (Join-Path $results 'sides.json')) { Read-Json (Join-Path $results 'sides.json') } else { @([pscustomobject]@{ base = 'base'; head = 'head' }) }

# Benchmarks: scenario -> side -> round -> (median ms, KB a run), from each round's BenchmarkDotNet report.
$timings = @{}
$benchRounds = @()
foreach ($folder in Get-ChildItem -LiteralPath $results -Directory) {
    if ($folder.Name -notmatch '^bench-(\d+)-(base|head)$') { continue }
    $round = [int]$Matches[1]
    $key = $Matches[2]
    $benchRounds += $round
    foreach ($report in Get-ChildItem (Join-Path $folder.FullName 'results') -Filter '*-report-full.json' -ErrorAction SilentlyContinue) {
        foreach ($benchmark in (Get-Content -LiteralPath $report.FullName -Raw | ConvertFrom-Json).Benchmarks) {
            $name = if ($benchmark.FullName -match 'Scenario: "([^"]+)"') { $Matches[1] }
                    elseif ($benchmark.Parameters -match 'Scenario=([^&]+)') { $Matches[1] }
                    else { $benchmark.Method }
            if (-not $timings.ContainsKey($name)) { $timings[$name] = @{ base = @{}; head = @{} } }
            $timings[$name][$key][$round] = @(([double]$benchmark.Statistics.Median / 1e6), ([double]$benchmark.Memory.BytesAllocatedPerOperation / 1KB))
        }
    }
}
$benchRounds = @($benchRounds | Sort-Object -Unique)
foreach ($name in ($timings.Keys | Sort-Object)) {
    $both = @($benchRounds | Where-Object { $timings[$name].base.ContainsKey($_) -and $timings[$name].head.ContainsKey($_) })
    Add-Time 'Benchmarks' $name @($both | ForEach-Object { $timings[$name].base[$_][0] }) @($both | ForEach-Object { $timings[$name].head[$_][0] }) 'ms'
    Add-Amount 'Benchmarks' "$name memory" @($both | ForEach-Object { $timings[$name].base[$_][1] }) @($both | ForEach-Object { $timings[$name].head[$_][1] }) 'KB' 5
}

$startupBase = Join-Path $results 'startup-base.json'
$startups = 0
if (Test-Path -LiteralPath $startupBase) {
    $samples = @{ base = (Read-Json $startupBase); head = (Read-Json (Join-Path $results 'startup-head.json')) }
    $startups = $samples.base.Count
    foreach ($stage in 'runtimeMs', 'servicesMs', 'readinessMs', 'signInMs', 'firstScreenMs', 'totalMs') {
        Add-Time 'Startup' ($stage -replace 'Ms$', '') @($samples.base | ForEach-Object { [double]$_.$stage }) @($samples.head | ForEach-Object { [double]$_.$stage }) 'ms'
    }
    Add-Count 'Startup' 'SQL commands' @($samples.base | ForEach-Object { [double]$_.sqlCommands }) @($samples.head | ForEach-Object { [double]$_.sqlCommands })
    Add-Count 'Startup' 'assemblies' @($samples.base | ForEach-Object { [double]$_.assemblies }) @($samples.head | ForEach-Object { [double]$_.assemblies })
    Add-Amount 'Startup' 'methods compiled' @($samples.base | ForEach-Object { [double]$_.jitMethods }) @($samples.head | ForEach-Object { [double]$_.jitMethods }) '' 2
    Add-Amount 'Startup' 'memory' @($samples.base | ForEach-Object { [double]$_.allocatedBytes / 1KB }) @($samples.head | ForEach-Object { [double]$_.allocatedBytes / 1KB }) 'KB' 5
}

$windowBase = Join-Path $results 'window-base.json'
$windowStarts = 0
if (Test-Path -LiteralPath $windowBase) {
    $starts = @{ base = (Read-Json $windowBase); head = (Read-Json (Join-Path $results 'window-head.json')) }
    $windowStarts = $starts.base.Count
    foreach ($measure in @(@('windowMs', 'window shown'), @('pageMs', 'page begun'), @('signInReadyMs', 'sign-in ready'), @('listAfterSignInMs', 'signed in to list'))) {
        Add-Time 'Window' $measure[1] @($starts.base | ForEach-Object { [double]$_.($measure[0]) }) @($starts.head | ForEach-Object { [double]$_.($measure[0]) }) 'ms'
    }
    Add-Amount 'Window' 'JS heap' @($starts.base | ForEach-Object { [double]$_.heapMB }) @($starts.head | ForEach-Object { [double]$_.heapMB }) 'MB' 5
}

$table = $rows | Select-Object Part, Measure, Base, Head, Change, Verdict
$table | Format-Table -AutoSize | Out-String -Width 160 | Write-Output
Write-Output "Base and Head are each side's median; a time's Change is the median of head / base over the pairs, a count's or an amount's the medians'."

$summary = New-Object System.Text.StringBuilder
[void]$summary.AppendLine("# Benchmarks compared")
[void]$summary.AppendLine()
[void]$summary.AppendLine("- Base: $($names[0].base)")
[void]$summary.AppendLine("- Head: $($names[0].head)")
[void]$summary.AppendLine("- $($benchRounds.Count) benchmark rounds, $startups cold starts, $windowStarts window starts a side; a time is worse over $Threshold% when chance would show that 1.5% of the time or less")
[void]$summary.AppendLine("- Base and Head are each side's median; a time's Change is the median of head / base over the pairs")
[void]$summary.AppendLine()
[void]$summary.AppendLine('| Part | Measure | Base | Head | Change | Verdict |')
[void]$summary.AppendLine('|---|---|---:|---:|---:|---|')
foreach ($row in $table) { [void]$summary.AppendLine("| $($row.Part) | $($row.Measure) | $($row.Base) | $($row.Head) | $($row.Change) | $($row.Verdict) |") }
[IO.File]::WriteAllText((Join-Path $results 'summary.md'), $summary.ToString(), (New-Object Text.UTF8Encoding $false))

$worse = @($rows | Where-Object Worse)
Write-Output "The results and each run's log are in $results."
if ($worse.Count -gt 0) {
    Write-Output "Worse in $($worse.Count): $(($worse | ForEach-Object { "$($_.Part) $($_.Measure)" }) -join ', ')."
    exit 1
}
Write-Output 'Nothing got worse.'
