<#
.SYNOPSIS
    Runs only the test projects a change can reach, a quicker check while developing. Before a commit or merge, run them
    all: dotnet test --solution TinapayanRMS.slnx.

.DESCRIPTION
    Takes the files changed since -Since (HEAD by default: what isn't committed yet, new files included), and runs every
    test project that references, directly or through others, a project one of them is in. A change to the screens runs
    Components.Tests, PageScripts.Tests and Benchmarks.Tests; one to Domain or ApplicationCore runs them all. A file another project links in (the MAUI
    app's appsettings.json) counts for that project too.

    A build setting (Directory.Build.props, Directory.Packages.props, global.json, .editorconfig, the solution) runs them
    all, and so does any other file outside a project, to be safe. Docs, scripts\ and .claude\ run nothing: no test reads
    them.

    Several projects run side by side from one dotnet test, through a solution filter, as the whole suite does. It
    refuses while one of them is running already (Watch-Tests.ps1, or another run), since both would use the same test
    databases and reset them under each other.

    A project that passed on these same changes isn't run again: after a run, each project that passed is stamped with
    what it ran on (the changed files it can see, their contents, the commit compared with and the classes asked for),
    in %LocalAppData%\TinapayanRMS\Test-Changed. So after fixing one project's failure, only that one runs. Any edit a
    project can see, or a commit, runs it again. -Rerun runs them anyway.

    Components.Tests doesn't rebuild the app to read its IL when none of the app's own files changed since its last
    build (UseBuiltApp): the app's XAML compiler takes about 10 s on every build, changed or not.

.PARAMETER Since
    The commit to compare with: HEAD, what isn't committed yet, or main, everything this branch has changed.

.PARAMETER Class
    Only test classes whose name matches, with * for any characters: "*ProductsState*". Several are joined into one
    --treenode-filter. A project with none passes with "Zero tests ran". It goes by names, so it can miss the tests of
    something else that uses the class.

.PARAMETER Near
    Only the test classes named after the changed files: a change to ProductRules.cs runs ProductRulesTests and
    ProductRulesFoldTests (every class whose name is the file's, then anything, then Tests), and a changed test file runs
    its own classes. A changed file with no class named after it is listed, and nothing runs for it. It goes by names,
    so it misses more than a project's run does: quick while editing one thing, never the last check.

.PARAMETER Rerun
    Run the projects that passed on these same changes too.

.PARAMETER List
    Say what would run and why, and run nothing.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/Test-Changed.ps1 -List

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/Test-Changed.ps1 -Near
#>
[CmdletBinding()]
param(
    [string]$Since = 'HEAD',
    [string[]]$Class,
    [switch]$Near,
    [switch]$Rerun,
    [switch]$List
)

if ($Near -and $Class) {
    throw '-Near picks the classes itself: give it or -Class, not both.'
}

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

$touched = @{}          # project name -> the changed files in it
$everything = @()       # why every test project runs, when one has to
$everythingFiles = @()  # the files that make every test project run
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
        $everythingFiles += $path
    } elseif ($path -match $readByNoTest) {
        $ignored++
    } else {
        $everything += "a file outside the projects changed ($path)"
        $everythingFiles += $path
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
        # The changed files this project can see: those in what it's built from, and any that make every project run.
        $sees = @(@($touched.Keys | Where-Object { $closure.ContainsKey($_) } | ForEach-Object { $touched[$_] }) + $everythingFiles |
            Where-Object { $_ } | Sort-Object -Unique)
        $selected += [pscustomobject]@{ Project = $test; Why = $why -join ', '; Sees = $sees; Classes = @() }
    }
}

# -Near: the test classes named after the changed files, in the projects those files reach.
$unmatched = @()
if ($Near) {
    $classesIn = @{}
    foreach ($pick in $selected) {
        $classesIn[$pick.Project.Name] = @(Get-ChildItem -LiteralPath $pick.Project.Folder -Filter '*.cs' -Recurse |
            Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
            ForEach-Object { [regex]::Matches((Get-Content -LiteralPath $_.FullName -Raw), '\bclass\s+(\w+Tests)\b') } |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    }
    foreach ($path in $changed) {
        $full = [IO.Path]::GetFullPath((Join-Path $repo $path))
        $owner = $projects.Values | Where-Object { $full.StartsWith($_.Folder + '\', [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
        if (-not $owner -or $path -match $readByNoTest) {
            continue
        }
        $found = $false
        if ($owner.IsTest) {
            # A changed test file: its own classes.
            $pick = $selected | Where-Object { $_.Project.Name -eq $owner.Name }
            if ($pick -and $path -like '*.cs' -and (Test-Path -LiteralPath $full)) {
                $own = @([regex]::Matches((Get-Content -LiteralPath $full -Raw), '\bclass\s+(\w+Tests)\b') | ForEach-Object { $_.Groups[1].Value })
                $pick.Classes = @($pick.Classes + $own | Sort-Object -Unique)
                $found = $own.Count -gt 0
            }
        } else {
            # A changed source file: the classes named after it (ProductForm.razor.cs: ProductForm, then anything, then Tests).
            $name = [IO.Path]::GetFileName($path) -replace '\..*$', ''
            $pattern = '^' + [regex]::Escape($name) + '\w*Tests$'
            foreach ($pick in $selected) {
                $named = @($classesIn[$pick.Project.Name] | Where-Object { $_ -match $pattern })
                if ($named.Count -gt 0) {
                    $pick.Classes = @($pick.Classes + $named | Sort-Object -Unique)
                    $found = $true
                }
            }
        }
        if (-not $found) {
            $unmatched += $path
        }
    }
    $selected = @($selected | Where-Object { $_.Classes.Count -gt 0 })
}

# A project that passed on these same changes: skipped, unless -Rerun.
$stampFolder = Join-Path $env:LOCALAPPDATA 'TinapayanRMS\Test-Changed'
$stampFile = Join-Path $stampFolder 'passed.json'
$stamps = @{}
try {
    if (Test-Path -LiteralPath $stampFile) {
        (Get-Content -LiteralPath $stampFile -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $stamps[$_.Name] = $_.Value }
    }
} catch {
    $stamps = @{}
}
$base = (& git -C $repo rev-parse $Since).Trim()
$sha = [Security.Cryptography.SHA256]::Create()
function Get-Stamp($Pick) {
    $classes = if ($Near) { $Pick.Classes } else { @($Class) }
    $parts = @($base, ($classes -join '|')) + @($Pick.Sees | ForEach-Object {
        $file = Join-Path $repo $_
        $_ + '=' + $(if (Test-Path -LiteralPath $file -PathType Leaf) { (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash } else { 'gone' })
    })
    [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($parts -join "`n"))) -replace '-', ''
}
foreach ($pick in $selected) {
    $pick | Add-Member -NotePropertyName Stamp -NotePropertyValue (Get-Stamp $pick)
}
$passedBefore = @($selected | Where-Object { -not $Rerun -and $stamps[$_.Project.Name] -eq $_.Stamp })
$toRun = @($selected | Where-Object { $passedBefore -notcontains $_ })

Write-Output ("{0} changed file{1} since {2}{3}." -f $changed.Count, $(if ($changed.Count -eq 1) { '' } else { 's' }), $Since,
    $(if ($ignored -gt 0) { ", $ignored of them docs or tooling no test reads" } else { '' }))
foreach ($path in $unmatched) {
    Write-Output "  no test class is named after $path"
}
if ($selected.Count -eq 0) {
    if ($Near) {
        Write-Output 'No test class is named after these changes, so -Near has nothing to run: run without it.'
    } else {
        Write-Output 'No test project can see these changes, so there is nothing to run.'
    }
    return
}
foreach ($pick in $selected) {
    $what = if ($Near) { $pick.Classes -join ', ' } else { $pick.Why }
    $note = if ($passedBefore -contains $pick) { 'passed on these same changes, not run again (-Rerun runs it): ' } else { '' }
    Write-Output ('  {0,-22} {1}{2}' -f $pick.Project.Short, $note, $what)
}
foreach ($skip in $tests | Where-Object { $selected.Project -notcontains $_ }) {
    $why = if ($Near) { 'no class named after the changes' } else { 'nothing it is built from changed' }
    Write-Output ('  {0,-22} not run: {1}' -f $skip.Short, $why)
}
if ($List) {
    return
}
if ($toRun.Count -eq 0) {
    Write-Output 'Every project these changes reach passed on them already: -Rerun runs them again.'
    return
}

foreach ($pick in $toRun) {
    $running = Get-Process -Name $pick.Project.Name -ErrorAction SilentlyContinue
    if ($running) {
        Write-Output "$($pick.Project.Short) is running already (process $($running[0].Id), perhaps Watch-Tests.ps1). Stop it first: both runs would reset the same test databases."
        exit 1
    }
}

if ($Near) {
    $Class = @($toRun | ForEach-Object { $_.Classes } | Sort-Object -Unique)
}
$arguments = @()

# Components.Tests reads the app's IL, and building the app runs its WinUI XAML compiler every time, about 10 s with
# nothing changed. When none of the app's own files changed since its last build, it reads that build's assembly instead
# (UseBuiltApp, in Components.Tests' project file).
if ($toRun.Project.Name -contains 'TinapayanRMS.Components.Tests' -and -not $touched.ContainsKey('TinapayanRMS.Maui')) {
    $app = $projects['TinapayanRMS.Maui'].Folder
    $built = Get-ChildItem -Path (Join-Path $app 'bin\Debug\net*-windows*\win-x64\TinapayanRMS.Maui.dll') -ErrorAction SilentlyContinue |
        Select-Object -First 1
    $newest = Get-ChildItem -LiteralPath $app -File -Recurse | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($built -and $newest -and $built.LastWriteTimeUtc -gt $newest.LastWriteTimeUtc) {
        $arguments += '-p:UseBuiltApp=true'
        Write-Output "The app isn't rebuilt: Components.Tests reads its last build, which none of its files has changed since."
    }
}

if ($Class) {
    # TUnit's tree-node filter is /assembly/namespace/class/test, and (a)|(b) matches either. A project none of the
    # classes are in runs no test, which Microsoft.Testing.Platform ends with exit code 8: that's a pass here.
    $classes = if ($Class.Count -eq 1) { $Class[0] } else { ($Class | ForEach-Object { "($_)" }) -join '|' }
    $arguments += '--treenode-filter', "/*/*/$classes/*", '--ignore-exit-code', '8'
}

# The run's lines are shown as they come, and kept to see which projects passed.
if ($toRun.Count -eq $tests.Count) {
    & dotnet test --solution (Join-Path $repoAsCalled 'TinapayanRMS.slnx') @arguments 2>&1 | Tee-Object -Variable output
} elseif ($toRun.Count -eq 1) {
    & dotnet test --project (ConvertTo-CalledPath $toRun[0].Project.Path) @arguments 2>&1 | Tee-Object -Variable output
} else {
    # A solution filter of just these, so they run side by side as the whole suite does. Outside the repo, where git
    # would count it as a change.
    $filter = Join-Path ([IO.Path]::GetTempPath()) 'TinapayanRMS.Test-Changed.slnf'
    $solution = [ordered]@{
        path     = Join-Path $repoAsCalled 'TinapayanRMS.slnx'
        projects = @($toRun | ForEach-Object { $_.Project.Path.Substring($repo.Length + 1) })
    }
    ConvertTo-Json @{ solution = $solution } -Depth 3 | Set-Content -LiteralPath $filter -Encoding UTF8
    & dotnet test --solution $filter @arguments 2>&1 | Tee-Object -Variable output
}
$exitCode = $LASTEXITCODE

# Stamp each project that passed: every one when the run did, otherwise those its summary says passed.
$passed = if ($exitCode -eq 0) {
    $toRun
} else {
    $text = ($output | Out-String)
    @($toRun | Where-Object { $text -match ([regex]::Escape("$($_.Project.Name).dll") + '[^\r\n]*\bpassed\b') })
}
if ($passed.Count -gt 0) {
    foreach ($pick in $passed) {
        $stamps[$pick.Project.Name] = $pick.Stamp
    }
    try {
        New-Item -ItemType Directory -Force -Path $stampFolder | Out-Null
        ConvertTo-Json ([pscustomobject]$stamps) | Set-Content -LiteralPath $stampFile -Encoding UTF8
    } catch {
        Write-Output "The projects that passed couldn't be noted ($($_.Exception.Message)), so they'll run again next time."
    }
}
exit $exitCode
