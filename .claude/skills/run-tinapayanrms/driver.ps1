<#
.SYNOPSIS
    Launches and drives the TinapayanRMS window (the .NET MAUI Blazor Hybrid app) for a check, without the mouse.

.DESCRIPTION
    The app is started with WebView2's remote debugging on (port 9222), and its page is driven through it by driver.js
    (playwright-core, installed on first use into %LOCALAPPDATA%\TinapayanRMS\dev\playwright): clicks, keys and
    screenshots go to the page, never to the mouse or the screen, so nothing the user has open is touched. The window
    itself (its frame, its title bar's caption) is captured with PrintWindow and asked with WM_NCHITTEST, which read this
    window only. Shots go to %TEMP%\tinapayanrms-shots\<name>.png.

    launch                  start the built app (unless it's running), wait for its first screen, shot "launch"
    shot <name>             the page
    window <name>           the whole window, frame included (PrintWindow)
    go <address> [name]     open an address: raw-materials, products, products/42, reports, settings/keyboard
    click <selector> [name] click what a Playwright selector finds: "text=Products", "button[aria-label='Save']"
    type <text> [name]      type into what has the cursor
    press <key> [name]      press a key: Enter, Escape, Control+k
    eval <javascript>       run it in the page and print what it returns
    hit <x> <y> [<x> <y>…]  what Windows' hit test says at points of the window (2 caption, 1 client, 12 top edge)
    smoke                   every screen and Settings page, then a record of each record screen: fails on the error
                            bar, a screen's error panel, a red toast or a page error (the check before an install)
    close                   the title bar's Close, which asks about unsaved changes; says so if the window stays

    TINAPAYANRMS_EXE, when set, is the copy to drive (a trial install, say) in place of the Debug build.
#>
param(
    [Parameter(Mandatory, Position = 0)][string]$Command,
    [Parameter(Position = 1, ValueFromRemainingArguments)][string[]]$Arguments
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
# TINAPAYANRMS_SATORI=1 drives the Satori GC build instead (dotnet build src/TinapayanRMS.Maui -p:UseSatoriGC=true).
$bin = if ($env:TINAPAYANRMS_SATORI) { 'bin\Satori\Debug' } else { 'bin\Debug' }
# TINAPAYANRMS_EXE drives another copy: one the install script put in a trial folder, say, as the shop will run it.
$exe = if ($env:TINAPAYANRMS_EXE) { (Resolve-Path -LiteralPath $env:TINAPAYANRMS_EXE).Path }
       else { Join-Path $repo "src\TinapayanRMS.Maui\$bin\net11.0-windows10.0.19041.0\win-x64\TinapayanRMS.Maui.exe" }
$port = 9222
$shots = Join-Path $env:TEMP 'tinapayanrms-shots'
$tools = Join-Path $env:LOCALAPPDATA 'TinapayanRMS\dev\playwright'
New-Item -ItemType Directory -Force $shots | Out-Null

Add-Type @"
using System; using System.Runtime.InteropServices;
public static class TinapayanWindow {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  public struct RECT { public int L, T, R, B; } public struct POINT { public int X, Y; }
}
"@
# Per-monitor aware, so a window's pixels are the screen's (the development PC is at 125%).
[TinapayanWindow]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

function Get-App {
    Get-Process TinapayanRMS.Maui -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Select-Object -First 1
}

# A TCP connect, not an HTTP request: Windows PowerShell's web requests look for a proxy first, which can take longer
# than the wait itself.
function Test-Debugging {
    $client = New-Object Net.Sockets.TcpClient
    try { $client.ConnectAsync('127.0.0.1', $port).Wait(1000) -and $client.Connected } catch { $false } finally { $client.Dispose() }
}

function Invoke-Page {
    if (-not (Test-Path (Join-Path $tools 'node_modules\playwright-core'))) {
        New-Item -ItemType Directory -Force $tools | Out-Null
        npm install --prefix $tools playwright-core@1.48.2 --no-audit --no-fund --silent | Out-Null
    }
    $env:NODE_PATH = Join-Path $tools 'node_modules'
    $env:SHOTS = $shots
    node (Join-Path $PSScriptRoot 'driver.js') $port @args
    if ($LASTEXITCODE -ne 0) { throw "driver.js $($args[0]) failed" }
}

function Get-WindowHandle {
    $app = Get-App
    if (-not $app -or $app.MainWindowHandle -eq 0) { throw 'The app is not running: launch it first.' }
    $app.MainWindowHandle
}

switch ($Command) {
    'launch' {
        if (Get-App) {
            if (-not (Test-Debugging)) { throw 'This build is running without its debugging port: close it, then launch again.' }
            Write-Output 'Already running.'
        }
        else {
            if (-not (Test-Path $exe)) { throw "Not built: dotnet build src/TinapayanRMS.Maui ($exe)" }
            if (Test-Debugging) { throw "Port $port is taken by another web view: close it first." }
            $env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$port"
            Start-Process -FilePath $exe | Out-Null
            $deadline = (Get-Date).AddSeconds(60)
            while (-not (Test-Debugging)) {
                if ((Get-Date) -gt $deadline) { throw 'The window did not open its debugging port within 60 seconds.' }
                Start-Sleep -Milliseconds 500
            }
        }
        Invoke-Page ready
        Invoke-Page shot launch
    }
    'window' {
        $h = Get-WindowHandle
        $r = New-Object TinapayanWindow+RECT
        [TinapayanWindow]::GetWindowRect($h, [ref]$r) | Out-Null
        Add-Type -AssemblyName System.Drawing
        $bitmap = New-Object System.Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $dc = $graphics.GetHdc()
        # 2: PW_RENDERFULLCONTENT, without which WebView2's content draws black.
        [TinapayanWindow]::PrintWindow($h, $dc, 2) | Out-Null
        $graphics.ReleaseHdc($dc); $graphics.Dispose()
        $file = Join-Path $shots "$(if ($Arguments) { $Arguments[0] } else { 'window' }).png"
        $bitmap.Save($file); $bitmap.Dispose()
        Write-Output $file
    }
    'hit' {
        $h = Get-WindowHandle
        for ($i = 0; $i + 1 -lt $Arguments.Count; $i += 2) {
            $point = New-Object TinapayanWindow+POINT
            $point.X = [int]$Arguments[$i]; $point.Y = [int]$Arguments[$i + 1]
            [TinapayanWindow]::ClientToScreen($h, [ref]$point) | Out-Null
            $lParam = [IntPtr](($point.Y -shl 16) -bor ($point.X -band 0xFFFF))
            Write-Output ("({0},{1}) -> {2}" -f $Arguments[$i], $Arguments[$i + 1], [TinapayanWindow]::SendMessage($h, 0x84, [IntPtr]::Zero, $lParam))
        }
    }
    'close' {
        if (-not (Get-App)) { Write-Output 'Not running.'; break }
        # The page can go as the click lands, which is the window closing, not a failure.
        try { Invoke-Page click "button[aria-label='Close']" } catch { }
        $deadline = (Get-Date).AddSeconds(15)
        while ((Get-App) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
        if (Get-App) { Write-Output 'Still open: it is asking about unsaved changes (shot to see it), or still closing.' } else { Write-Output 'Closed.' }
    }
    default { Invoke-Page $Command @Arguments }
}
