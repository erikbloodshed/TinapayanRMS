<#
.SYNOPSIS
    Starts the tinapayanrms MCP server from a copy of its build. It's what .mcp.json has an AI assistant run.

.DESCRIPTION
    Builds TinapayanRMS.Mcp (Release), copies what the build put in its bin\Release\net11.0 to a folder of its own under
    bin\Release\runs, and runs that copy on this script's standard input and output, the protocol's channel.

    A running program holds its files. While the server ran from bin\Release\net11.0 itself, a build that had to replace
    them failed after 13 seconds of retries (MSB3026), so a second Claude Code session's start after a change to the
    server's code failed. A server run from a copy holds only the copy. Each copy is named for what's in it, so a start
    with nothing new runs the copy already there, and a copy no server runs from any more goes at the next start.

    One start at a time builds and copies (a mutex), so two sessions starting together don't build over each other.
    The server ends when this script does, however it's ended (a job object), as it did when dotnet run ran it.
    Nothing but the server writes to standard output: the build's output goes to standard error, which Claude Code keeps
    in its MCP log. A build that fails stops the start, rather than run an older copy that might price differently.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$projectFolder = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\TinapayanRMS.Mcp'
$project = Join-Path $projectFolder 'TinapayanRMS.Mcp.csproj'
$buildFolder = Join-Path $projectFolder 'bin\Release\net11.0'
$runsFolder = Join-Path $projectFolder 'bin\Release\runs'
$exeName = 'TinapayanRMS.Mcp.exe'

function Write-Note([string]$Text) {
    [Console]::Error.WriteLine("Start-McpServer: $Text")
}

# Builds the server, with its output on standard error, and returns the build's exit code.
function Invoke-Build {
    # Under 'Stop', a line the build wrote to standard error would end the script.
    $ErrorActionPreference = 'Continue'
    & dotnet build $project -c Release -nologo -v:q 2>&1 | ForEach-Object { [Console]::Error.WriteLine("$_") }
    $LASTEXITCODE
}

# A name for what the build folder holds: each file's path, size and time.
function Get-BuildKey {
    $lines = Get-ChildItem -LiteralPath $buildFolder -Recurse -File | Sort-Object FullName | ForEach-Object {
        '{0}|{1}|{2}' -f $_.FullName.Substring($buildFolder.Length), $_.Length, $_.LastWriteTimeUtc.Ticks
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))
    } finally {
        $sha.Dispose()
    }
    -join ($hash[0..5] | ForEach-Object { $_.ToString('x2') })
}

# Whether a server runs from this copy: Windows won't open a running program's file for writing.
function Test-Running([string]$Folder) {
    $exe = Join-Path $Folder $exeName
    if (-not (Test-Path -LiteralPath $exe)) {
        return $false
    }
    try {
        [IO.File]::Open($exe, 'Open', 'ReadWrite', 'None').Dispose()
        return $false
    } catch {
        return $true
    }
}

# Ending this script ends the server, however it's ended, as ending dotnet run did: the server goes in a job object
# whose one handle is this script's, and Windows ends what's in a job like that when the handle closes. Otherwise a
# server whose script was killed would run on until its input closed.
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class McpServerJob
{
    [StructLayout(LayoutKind.Sequential)]
    struct BasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    const int ExtendedLimitInformation = 9;
    const uint KillOnJobClose = 0x2000;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateJobObject(IntPtr attributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    // The job's handle stays open until this process ends: closing it is what ends the server.
    public static void EndWithThisProcess(IntPtr process)
    {
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        var limits = new ExtendedLimits();
        limits.Basic.LimitFlags = KillOnJobClose;
        if (job == IntPtr.Zero
            || !SetInformationJobObject(job, ExtendedLimitInformation, ref limits, Marshal.SizeOf(limits))
            || !AssignProcessToJobObject(job, process))
        {
            throw new Win32Exception();
        }
    }
}
'@

$mutex = New-Object System.Threading.Mutex($false, 'Local\TinapayanRMS.Mcp.Start')
$owned = $false
try {
    try {
        $owned = $mutex.WaitOne([TimeSpan]::FromMinutes(3))
    } catch [System.Threading.AbandonedMutexException] {
        # A start that was ended midway (Claude Code gave up on it) left it, and this one owns it now.
        $owned = $true
    }
    if (-not $owned) {
        Write-Note 'another start has held the build for 3 minutes, so this one gives up'
        exit 1
    }

    $exitCode = Invoke-Build
    if ($exitCode -ne 0) {
        Write-Note "the build failed (exit code $exitCode)"
        exit $exitCode
    }

    $key = Get-BuildKey
    $runFolder = Join-Path $runsFolder $key
    if (-not (Test-Path -LiteralPath (Join-Path $runFolder $exeName))) {
        # Copied under another name first, so a copy cut short is never taken for a whole one.
        $partial = "$runFolder.partial"
        foreach ($leftover in $partial, $runFolder) {
            if (Test-Path -LiteralPath $leftover) {
                Remove-Item -LiteralPath $leftover -Recurse -Force
            }
        }
        $null = New-Item -ItemType Directory -Path $runsFolder -Force
        Copy-Item -LiteralPath $buildFolder -Destination $partial -Recurse
        Rename-Item -LiteralPath $partial -NewName $key
    }

    foreach ($old in Get-ChildItem -LiteralPath $runsFolder -Directory) {
        if ($old.Name -ne $key -and -not (Test-Running $old.FullName)) {
            Remove-Item -LiteralPath $old.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    # Started before the mutex is let go, so no other start can take this copy for one no server runs from. It gets this
    # script's standard input, output and error, and its working folder.
    $start = New-Object System.Diagnostics.ProcessStartInfo (Join-Path $runFolder $exeName)
    $start.UseShellExecute = $false
    $server = [System.Diagnostics.Process]::Start($start)
    try {
        [McpServerJob]::EndWithThisProcess($server.Handle)
    } catch {
        Write-Note "the server won't end with this script: $($_.Exception.Message)"
    }
    Write-Note "running bin\Release\runs\$key (process $($server.Id))"
} finally {
    if ($owned) {
        $mutex.ReleaseMutex()
    }
    $mutex.Dispose()
}

$server.WaitForExit()
exit $server.ExitCode
