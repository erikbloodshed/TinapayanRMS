# The window's starts for scripts/Compare-Benchmarks.ps1 -Window, which dot-sources this: each side's published app
# started on its prepared database, its window moved past the desktop's right edge as it appears (it opens centred on
# the mouse's monitor, WindowsWindow.OpenCentred), its page watched by window-start.js, then closed. The same helpers
# measure both sides: they come from the working tree whichever commits are compared.

if (-not ('BenchWindow' -as [type])) {
    Add-Type @"
using System; using System.Runtime.InteropServices;
public static class BenchWindow {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out RECT rect);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
}

# Per-monitor aware, so a window's place is in the screen's own pixels: an unaware process reads it scaled.
[void][BenchWindow]::SetProcessDpiAwarenessContext([IntPtr](-4))

# The password StartDatabase.PrepareAsync gives a prepared database (StartDatabase.Password).
$script:WindowPassword = 'bench-start'
$script:WindowPort = 9223

<#
    A helper thread of this process (a runspace, so it starts at once) that moves the window of whichever process id is
    put in its Shared.ProcessId past the virtual screen's right edge, as soon as it's shown and again whenever the app puts
    it back while it opens, and notes in Shared.ShownAt when it was first shown (Unix ms).
#>
function Start-WindowMover {
    $shared = [hashtable]::Synchronized(@{ ProcessId = 0; ShownAt = [long]0; Stop = $false })
    # SM_XVIRTUALSCREEN, SM_YVIRTUALSCREEN, SM_CXVIRTUALSCREEN: the whole desktop, every monitor.
    $x = [BenchWindow]::GetSystemMetrics(76) + [BenchWindow]::GetSystemMetrics(78) + 200
    $y = [BenchWindow]::GetSystemMetrics(77) + 40
    $shell = [powershell]::Create()
    [void]$shell.AddScript({
        param($shared, $x, $y)
        while (-not $shared.Stop) {
            $id = $shared.ProcessId
            if ($id -gt 0) {
                $handle = [IntPtr]::Zero
                try { $handle = [Diagnostics.Process]::GetProcessById($id).MainWindowHandle } catch { }
                if ($handle -ne [IntPtr]::Zero -and [BenchWindow]::IsWindowVisible($handle)) {
                    if ($shared.ShownAt -eq 0) { $shared.ShownAt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() }
                    $rect = New-Object BenchWindow+RECT
                    [void][BenchWindow]::GetWindowRect($handle, [ref]$rect)
                    # SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
                    if ($rect.Left -ne $x) { [void][BenchWindow]::SetWindowPos($handle, [IntPtr]::Zero, $x, $y, 0, 0, 0x15) }
                }
            }
            Start-Sleep -Milliseconds 5
        }
    }).AddArgument($shared).AddArgument($x).AddArgument($y)
    [pscustomobject]@{ Shell = $shell; Running = $shell.BeginInvoke(); Shared = $shared }
}

function Stop-WindowMover($mover) {
    $mover.Shared.Stop = $true
    [void]$mover.Shell.EndInvoke($mover.Running)
    $mover.Shell.Dispose()
}

<#
    One start of $Exe on $ConnectionString, measured from the process's start: when the window showed, when the page
    began, when the sign-in panel could be typed into, and how long Raw Materials' list took to show once the password
    was given; and the page's JS heap. The window is closed again, and waited for, before the next start can be.
#>
function Measure-WindowStart([string]$Exe, [string]$ConnectionString, $Mover, [string]$Watcher, [string]$Log) {
    $Mover.Shared.ShownAt = [long]0
    $env:ConnectionStrings__Default = $ConnectionString
    $process = Start-Process -FilePath $Exe -PassThru
    $Mover.Shared.ProcessId = $process.Id
    $startedAt = ([DateTimeOffset]$process.StartTime).ToUnixTimeMilliseconds()

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $output = & node $Watcher $script:WindowPort $script:WindowPassword 90000 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previous
    $output | Out-File -FilePath $Log -Append -Encoding utf8

    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(20000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        [void]$process.WaitForExit(10000)
    }
    $Mover.Shared.ProcessId = 0

    $json = @($output | Where-Object { "$_".StartsWith('{') }) | Select-Object -Last 1
    if ($exitCode -ne 0 -or -not $json) {
        throw "A start of $Exe wasn't seen to the end (window-start.js exited $exitCode): see $Log."
    }

    $marks = "$json" | ConvertFrom-Json
    $shownAt = $Mover.Shared.ShownAt
    [pscustomobject]@{
        windowMs          = if ($shownAt -gt 0) { [double]($shownAt - $startedAt) } else { [double]::NaN }
        pageMs            = [double]$marks.timeOrigin - $startedAt
        signInReadyMs     = [double]$marks.signInReady - $startedAt
        listAfterSignInMs = [double]$marks.listShown - [double]$marks.signingIn
        heapMB            = [math]::Round([double]$marks.heapBytes / 1MB, 2)
    }
}
