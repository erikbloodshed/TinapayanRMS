<#
.SYNOPSIS
    Runs only the test projects a change can reach, a quicker check while developing. Before a commit or merge, run them
    all: dotnet test --solution TinapayanRMS.slnx.

.DESCRIPTION
    Takes the files changed since -Since (HEAD by default: what isn't committed yet, new files included), and runs every
    test project that references, directly or through others, a project one of them is in. A change to the UI runs
    UI.Tests alone; one to Domain or ApplicationCore runs them all. A file another project links in (the UI's
    appsettings.json) counts for that project too.

    A build setting (Directory.Build.props, Directory.Packages.props, global.json, .editorconfig, the solution) runs them
    all, and so does any other file outside a project, to be safe. Docs, scripts\ and .claude\ run nothing: no test reads
    them.

    Several projects run side by side from one dotnet test, through a solution filter, as the whole suite does. It
    refuses while one of them is running already (Watch-Tests.ps1, or another run), since both would use the same test
    databases and reset them under each other.

.PARAMETER Since
    The commit to compare with: HEAD, what isn't committed yet, or main, everything this branch has changed.

.PARAMETER Class
    Only test classes whose name matches, as dotnet test's --filter-class: "*ProductsViewModel*". A project with none
    passes with "Zero tests ran". It goes by names, so it can miss the tests of something else that uses the class.

.PARAMETER List
    Say what would run and why, and run nothing.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/Test-Changed.ps1 -List
#>
[CmdletBinding()]
param(
    [string]$Since = 'HEAD',
    [string[]]$Class,
    [switch]$List
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# The repo's folder spelled as the caller's terminal spells it: the build records each project's folder with its drive
# letter's case, so paths in the other case would make this run recompile every project, and the next run from the
# terminal recompile them again.
$here = [Environment]::CurrentDirectory
$repoAsCalled = if ($here.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { $here.Substring(0, $repo.Length) } else { $repo }

function ConvertTo-CalledPath([string]$Path) {
    $repoAsCalled + $Path.Substring($repo.Length)
}

# Every project, with the projects it references and the files outside its folder it takes in (a linked appsettings.json).
$projects = @{}
foreach ($file in Get-ChildItem (Join-Path $repo 'src'), (Join-Path $repo 'tests') -Filter '*.csproj' -Recurse -Depth 1) {
    $xml = [xml](Get-Content -LiteralPath $file.FullName -Raw)
    $folder = $file.DirectoryName
    $linked = foreach ($item in $xml.SelectNodes('//*[@Include]')) {
        $include = $item.GetAttribute('Include')
        if ($item.LocalName -in 'ProjectReference', 'PackageReference', 'Using' -or $include -match '[*?$;]') {
            continue
        }
        $full = [IO.Path]::GetFullPath((Join-Path $folder $include))
        if (-not $full.StartsWith($folder + '\', [StringComparison]::OrdinalIgnoreCase)) {
            $full
        }
    }
    $projects[$file.BaseName] = [pscustomobject]@{
        Name       = $file.BaseName
        Short      = $file.BaseName -replace '^TinapayanRMS\.', ''
        Folder     = $folder
        Path       = $file.FullName
        References = @($xml.SelectNodes('//ProjectReference') | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.GetAttribute('Include')) })
        Linked     = @($linked)
        IsTest     = $file.BaseName.EndsWith('.Tests')
    }
}

$changed = @(& git -C $repo diff --name-only $Since --)
if ($LASTEXITCODE -ne 0) {
    throw "git couldn't compare with '$Since'."
}
$changed = @($changed + @(& git -C $repo ls-files --others --exclude-standard) | Where-Object { $_ } | Sort-Object -Unique)
if ($changed.Count -eq 0) {
    Write-Output "Nothing has changed since $Since, so there's nothing to test."
    return
}

$buildSettings = '^(Directory\.(Build|Packages)\.(props|targets)|tests/Directory\.Build\.(props|targets)|global\.json|\.editorconfig|TinapayanRMS\.slnx|NuGet\.config)$'
$readByNoTest = '^(docs/|scripts/|\.claude/|[^/]+\.md$|\.mcp\.json$|\.git(ignore|attributes)$|apm\.(yml|lock\.yaml)$|skills-lock\.json$)'

$touched = @{}      # project name -> the changed files in it
$everything = @()   # why every test project runs, when one has to
$ignored = 0
foreach ($path in $changed) {
    $full = [IO.Path]::GetFullPath((Join-Path $repo $path))
    $owners = @($projects.Values | Where-Object { $full.StartsWith($_.Folder + '\', [StringComparison]::OrdinalIgnoreCase) -or $_.Linked -contains $full })
    if ($owners.Count -gt 0) {
        foreach ($owner in $owners) {
            $touched[$owner.Name] = @($touched[$owner.Name]) + $path | Where-Object { $_ }
        }
    } elseif ($path -match $buildSettings) {
        $everything += "a build setting changed ($path)"
    } elseif ($path -match $readByNoTest) {
        $ignored++
    } else {
        $everything += "a file outside the projects changed ($path)"
    }
}

# What each test project is built from, itself included.
function Get-Closure([string]$Name, [hashtable]$Seen) {
    if ($Seen.ContainsKey($Name) -or -not $projects.ContainsKey($Name)) {
        return
    }
    $Seen[$Name] = $true
    foreach ($reference in $projects[$Name].References) {
        Get-Closure $reference $Seen
    }
}

$tests = @($projects.Values | Where-Object IsTest | Sort-Object Name)
$selected = @()
foreach ($test in $tests) {
    $closure = @{}
    Get-Closure $test.Name $closure
    $why = @($touched.Keys | Where-Object { $closure.ContainsKey($_) } | Sort-Object | ForEach-Object {
        $count = @($touched[$_]).Count
        '{0} ({1} file{2})' -f $projects[$_].Short, $count, $(if ($count -eq 1) { '' } else { 's' })
    })
    if ($everything.Count -gt 0) {
        $why = @($everything[0])
    }
    if ($why.Count -gt 0) {
        $selected += [pscustomobject]@{ Project = $test; Why = $why -join ', ' }
    }
}

Write-Output ("{0} changed file{1} since {2}{3}." -f $changed.Count, $(if ($changed.Count -eq 1) { '' } else { 's' }), $Since,
    $(if ($ignored -gt 0) { ", $ignored of them docs or tooling no test reads" } else { '' }))
if ($selected.Count -eq 0) {
    Write-Output 'No test project can see these changes, so there is nothing to run.'
    return
}
foreach ($pick in $selected) {
    Write-Output ('  {0,-22} {1}' -f $pick.Project.Short, $pick.Why)
}
foreach ($skip in $tests | Where-Object { $selected.Project -notcontains $_ }) {
    Write-Output ('  {0,-22} not run: nothing it is built from changed' -f $skip.Short)
}
if ($List) {
    return
}

foreach ($pick in $selected) {
    $running = Get-Process -Name $pick.Project.Name -ErrorAction SilentlyContinue
    if ($running) {
        Write-Output "$($pick.Project.Short) is running already (process $($running[0].Id), perhaps Watch-Tests.ps1). Stop it first: both runs would reset the same test databases."
        exit 1
    }
}

$arguments = @()
foreach ($pattern in $Class) {
    $arguments += '--filter-class', $pattern
}

if ($selected.Count -eq $tests.Count) {
    & dotnet test --solution (Join-Path $repoAsCalled 'TinapayanRMS.slnx') @arguments
} elseif ($selected.Count -eq 1) {
    & dotnet test --project (ConvertTo-CalledPath $selected[0].Project.Path) @arguments
} else {
    # A solution filter of just these, so they run side by side as the whole suite does. Outside the repo, where git
    # would count it as a change.
    $filter = Join-Path ([IO.Path]::GetTempPath()) 'TinapayanRMS.Test-Changed.slnf'
    $solution = [ordered]@{
        path     = Join-Path $repoAsCalled 'TinapayanRMS.slnx'
        projects = @($selected | ForEach-Object { $_.Project.Path.Substring($repo.Length + 1) })
    }
    ConvertTo-Json @{ solution = $solution } -Depth 3 | Set-Content -LiteralPath $filter -Encoding UTF8
    & dotnet test --solution $filter @arguments
}
exit $LASTEXITCODE
