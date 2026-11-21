<#
.SYNOPSIS
    The checks that say work is done (CLAUDE.md), run on this PC: the solution builds with no warnings, the model has no
    change a migration is missing, and every test passes. Run it before a merge to main.

.DESCRIPTION
    What GitHub's build workflow ran until 2026-11-21 (docs/adr/0005-change-control-and-dependencies.md, its amendment),
    in the same order, each stopping the run when it fails:

      1. Build: the whole solution from scratch (--no-incremental, so every project's warnings are reported, not only
         those of the projects that changed), warnings as errors, NuGet's vulnerability audit only reported.
      2. Migrations: dotnet ef migrations has-pending-model-changes, against the build just made.
      3. Tests: dotnet test --solution, every project, on this PC's PostgreSQL 18 and the test databases.

    It checks the working tree as it is, so it says how many files aren't committed: what passes is the commit only once
    they are. It refuses to start while a test project is running already (Watch-Tests.ps1, Test-Changed.ps1, another
    run), which would reset the same test databases, or while this repo's app is open, whose build it would fail to
    overwrite. It ends with each step's time and exits 1 when a step failed.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/Invoke-LocalCI.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# The repo's folder spelled as the caller's terminal spells it: the build records each project's folder with its drive
# letter's case, so the other case would make this run recompile every project, and the next run from the terminal again.
$here = [Environment]::CurrentDirectory
$repoAsCalled = if ($here.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { $here.Substring(0, $repo.Length) } else { $repo }
$solution = Join-Path $repoAsCalled 'TinapayanRMS.slnx'

# What would get in the way: a test project running (its test databases), or the app from this repo (its build's DLLs).
$testProjects = Get-ChildItem (Join-Path $repo 'tests') -Filter '*.Tests.csproj' -Recurse -Depth 1 | ForEach-Object BaseName
$blocking = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ProcessName -in $testProjects -or
    ($_.ProcessName -eq 'TinapayanRMS.Maui' -and $_.Path -and $_.Path.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase))
})
if ($blocking) {
    foreach ($process in $blocking) {
        Write-Output "$($process.ProcessName) is running (process $($process.Id)). Stop it first: a test project's run would reset the same test databases, and the app holds its build's DLLs."
    }
    exit 1
}

$uncommitted = @(& git -C $repo status --porcelain)
if ($uncommitted.Count -gt 0) {
    $files = if ($uncommitted.Count -eq 1) { '1 file' } else { "$($uncommitted.Count) files" }
    Write-Output "Checking the working tree, with $files not committed: commit them for the commit to be what passed."
}
else {
    Write-Output "Checking $(& git -C $repo log -1 --format='%h %s')."
}

# A password in the environment wins over pgpass.conf, for Npgsql and pg_dump alike, and the backup test of a pg_dump
# with no password at all expects none: the tools read pgpass.conf, as the app does.
Remove-Item Env:PGPASSWORD, Env:PGUSER -ErrorAction Ignore
$env:DOTNET_NOLOGO = 'true'

$steps = @(
    @{ Name = 'Build'; Run = { dotnet build $solution --no-incremental -warnaserror '-warnnotaserror:NU1900,NU1901,NU1902,NU1903,NU1904' -nologo } },
    @{ Name = 'Migrations'; Run = { dotnet ef migrations has-pending-model-changes --project (Join-Path $repoAsCalled 'src/TinapayanRMS.Infrastructure') --no-build } },
    @{ Name = 'Tests'; Run = { dotnet test --solution $solution --no-build } }
)

# Each step's heading and output go straight to the console: only its result is collected, for the table at the end.
$results = foreach ($step in $steps) {
    Write-Host ''
    Write-Host "== $($step.Name)"
    $clock = [Diagnostics.Stopwatch]::StartNew()
    & $step.Run | Out-Host
    $failed = $LASTEXITCODE -ne 0
    [pscustomobject]@{ Step = $step.Name; Result = if ($failed) { 'failed' } else { 'passed' }; Time = $clock.Elapsed.ToString('m\:ss') }
    if ($failed) {
        break
    }
}

Write-Output ''
$results | Format-Table -AutoSize | Out-String -Width 120 | Write-Output
if ($results.Count -lt $steps.Count -or ($results | Where-Object Result -eq 'failed')) {
    $failedStep = ($results | Where-Object Result -eq 'failed' | Select-Object -First 1).Step
    if ($failedStep -eq 'Migrations') {
        Write-Output 'The model has changes no migration has: dotnet ef migrations add <Name>.'
    }

    Write-Output "Local CI failed at $failedStep."
    exit 1
}

Write-Output 'Local CI passed: built with no warnings, no migration missing, every test passed.'
