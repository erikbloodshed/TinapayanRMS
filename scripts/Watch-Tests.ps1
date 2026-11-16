<#
.SYNOPSIS
    Runs one test project's tests again each time you change its code, within seconds and without a rebuild where it can:
    Microsoft.Testing.Platform's hot reload, under dotnet watch.

.DESCRIPTION
    Starts the test project under dotnet watch with hot reload on (TESTINGPLATFORM_HOTRELOAD_ENABLED). A change to a
    method's body, in the tests or the code they test, is applied to the tests running, and they run again. A change
    hot reload can't apply (a new method, type or parameter) makes dotnet watch rebuild and start them again
    (DOTNET_WATCH_RESTART_ON_RUDE_EDIT), which takes longer. Stop it with Ctrl+C.

    Give it a class or a test to keep each run short: with neither, every test in the project runs after each change,
    about 3 minutes for Presentation.Tests.

    Hot reload runs the tests again in the same test session, which TUnit 1.81.16 doesn't expect: without
    tests/HotReloadReruns.cs, every run after the first also reported a failure of TUnit's own ("ChannelClosedException:
    The channel has been closed."). If that comes back after a TUnit upgrade, see that file.

    While it runs it holds the project's build and uses its test databases, so stop it before building or testing that
    project another way: a second run would reset the databases under this one's tests. Test-Changed.ps1 refuses to run
    it meanwhile.

.PARAMETER Project
    The test project: Presentation, Components, Infrastructure, Pricing, Mcp, RecipeApi or ApplicationCore (with or
    without .Tests).

.PARAMETER Class
    Only classes whose name matches, with * for any characters: "*ProductsStateRefreshTests".

.PARAMETER Method
    Only tests whose name matches: "*keeps_the_selection*". With -Class too, only those tests of those classes. Both go
    into one --treenode-filter.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/Watch-Tests.ps1 Presentation -Class "*ProductsStateRefreshTests"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Project,
    [string[]]$Class,
    [string[]]$Method
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# Spelled as the caller's terminal spells the repo's folder, drive letter's case and all, or the watcher's build would
# recompile every project, and the next build from the terminal would again (see Test-Changed.ps1).
$here = [Environment]::CurrentDirectory
$repoAsCalled = if ($here.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { $here.Substring(0, $repo.Length) } else { $repo }

$name = 'TinapayanRMS.' + ($Project -replace '^TinapayanRMS\.', '' -replace '\.Tests$', '') + '.Tests'
$path = Join-Path $repoAsCalled "tests\$name\$name.csproj"
if (-not (Test-Path -LiteralPath $path)) {
    $known = (Get-ChildItem (Join-Path $repo 'tests') -Directory -Filter '*.Tests' | ForEach-Object { $_.Name -replace '^TinapayanRMS\.', '' -replace '\.Tests$', '' }) -join ', '
    Write-Output "There's no test project $name. The test projects: $known."
    exit 1
}
$running = Get-Process -Name $name -ErrorAction SilentlyContinue
if ($running) {
    Write-Output "$name is running already (process $($running[0].Id)). Stop it first: both would reset the same test databases."
    exit 1
}

# TUnit's tree-node filter is /assembly/namespace/class/test, and (a)|(b) matches either.
function Join-Patterns([string[]]$Patterns) {
    if (-not $Patterns) { '*' } elseif ($Patterns.Count -eq 1) { $Patterns[0] } else { ($Patterns | ForEach-Object { "($_)" }) -join '|' }
}

$arguments = @()
if ($Class -or $Method) {
    $arguments += '--treenode-filter', "/*/*/$(Join-Patterns $Class)/$(Join-Patterns $Method)"
} else {
    Write-Output "No -Class or -Method, so every test in $name runs after each change."
}

$env:TESTINGPLATFORM_HOTRELOAD_ENABLED = '1'
$env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = '1'
& dotnet watch --project $path run -- @arguments
exit $LASTEXITCODE
