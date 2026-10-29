<#
  Drives the running TinapayanRMS WPF app: launch it on the secondary monitor, capture it, click, type, pick a main tab.
  One command per run:

    driver.ps1 launch                 start the built exe (unless it's running), put it maximized on the secondary monitor, shot "launch"
    driver.ps1 shot <name>            capture the main window (PrintWindow) to <shots>\<name>.png
    driver.ps1 screen <name>          capture the whole monitor the window is on: popups, drop-downs and dialogs included
    driver.ps1 click <x> <y> [name]   real left click at window coordinates (pixels of a shot), then shot <name> if given
    driver.ps1 type <keys> [name]     SendKeys into whatever has focus ({ESC}, {TAB}, {ENTER}), then shot <name> if given
    driver.ps1 tab <title> [name]     click the main tab titled <title> (Raw Materials, Sub-Assemblies, Products, ...)
    driver.ps1 rect                   print the main window's screen rectangle
    driver.ps1 close                  ask the app to close (it may ask about unsaved changes: use screen to see)

  Shots go to $env:TEMP\tinapayanrms-shots. Coordinates are physical pixels: the script is per-monitor DPI aware.
#>
param([Parameter(Mandatory)][string]$Command, [string]$A, [string]$B, [string]$C)
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class W {
  [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
# Per-monitor v2 DPI awareness, before anything is placed, clicked or captured: the primary monitor is scaled, and a
# DPI-unaware script gets scaled rectangles and captures.
[void][W]::SetThreadDpiAwarenessContext([IntPtr](-4))

$Root = (Resolve-Path "$PSScriptRoot\..\..\..").Path
$Exe = Join-Path $Root 'src\TinapayanRMS.UI\bin\Debug\net11.0-windows10.0.19041.0\win-x64\TinapayanRMS.UI.exe'
$Shots = Join-Path $env:TEMP 'tinapayanrms-shots'
New-Item -ItemType Directory -Force $Shots | Out-Null

# MainWindow is a visible HwndWrapper[...] window titled "Tinapayan Festival Bakeshoppe - Recipe & Costing".
# Process.MainWindowHandle can be one of the app's hidden windows instead.
function Get-AppWindow {
  $proc = Get-Process TinapayanRMS.UI -ErrorAction SilentlyContinue | Select-Object -First 1
  if (-not $proc) { return [IntPtr]::Zero }
  $script:found = [IntPtr]::Zero
  $cb = [W+EnumProc]{ param($h, $l)
    $procId = 0; [void][W]::GetWindowThreadProcessId($h, [ref]$procId)
    if ($procId -ne $proc.Id -or -not [W]::IsWindowVisible($h)) { return $true }
    $c = New-Object Text.StringBuilder 256; [void][W]::GetClassName($h, $c, 256)
    $t = New-Object Text.StringBuilder 256; [void][W]::GetWindowText($h, $t, 256)
    if ($c.ToString() -like 'HwndWrapper*' -and $t.ToString() -like '*Recipe & Costing*') { $script:found = $h; return $false }
    return $true }
  [void][W]::EnumWindows($cb, [IntPtr]::Zero)
  return $script:found
}

function Require-Window {
  $h = Get-AppWindow
  if ($h -eq [IntPtr]::Zero) { throw "TinapayanRMS isn't running (driver.ps1 launch)" }
  return $h
}

# Bringing a window to the front takes AttachThreadInput around SetForegroundWindow; SetWindowPos alone lets clicks
# land on whatever was in front.
function Focus-Window($h) {
  $fg = [W]::GetForegroundWindow(); $fgPid = 0
  $fgThread = [W]::GetWindowThreadProcessId($fg, [ref]$fgPid); $me = [W]::GetCurrentThreadId()
  [void][W]::AttachThreadInput($me, $fgThread, $true)
  [void][W]::BringWindowToTop($h); [void][W]::SetForegroundWindow($h)
  [void][W]::AttachThreadInput($me, $fgThread, $false)
}

function Get-Rect($h) { $r = New-Object W+RECT; [void][W]::GetWindowRect($h, [ref]$r); return $r }

function Save-Shot($h, $name) {
  $r = Get-Rect $h
  $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
  $g = [Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
  [void][W]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc); $g.Dispose()
  $path = Join-Path $Shots "$name.png"; $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  "shot $path"
}

function Save-Screen($h, $name) {
  $r = Get-Rect $h
  $screen = [Windows.Forms.Screen]::FromRectangle((New-Object Drawing.Rectangle $r.L, $r.T, ($r.R - $r.L), ($r.B - $r.T)))
  $mon = $screen.Bounds
  $bmp = New-Object Drawing.Bitmap $mon.Width, $mon.Height
  $g = [Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($mon.X, $mon.Y, 0, 0, $bmp.Size); $g.Dispose()
  $path = Join-Path $Shots "$name.png"; $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  "screen $path (monitor at $($mon.X),$($mon.Y), window at $($r.L - $mon.X),$($r.T - $mon.Y) in it)"
}

function Click-At($h, [int]$x, [int]$y) {
  Focus-Window $h
  $r = Get-Rect $h
  [void][W]::SetCursorPos($r.L + $x, $r.T + $y); Start-Sleep -Milliseconds 100
  [W]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); [W]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
  "clicked $x,$y (screen $($r.L + $x),$($r.T + $y))"
}

switch ($Command) {
  'launch' {
    if (-not (Test-Path $Exe)) { throw "not built: $Exe (dotnet build src/TinapayanRMS.UI)" }
    if (-not (Get-Process TinapayanRMS.UI -ErrorAction SilentlyContinue)) { Start-Process $Exe -WorkingDirectory (Split-Path $Exe) }
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 90 -and $h -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 500; $h = Get-AppWindow }
    if ($h -eq [IntPtr]::Zero) { throw "no main window after 45 s" }
    $target = [Windows.Forms.Screen]::AllScreens | Where-Object { -not $_.Primary } | Select-Object -First 1
    if (-not $target) { $target = [Windows.Forms.Screen]::PrimaryScreen }
    $w = $target.WorkingArea
    # Restored, moved, and sized again once there (WPF rescales a window moved onto a monitor of another DPI), then maximized.
    [void][W]::ShowWindow($h, 9)
    [void][W]::SetWindowPos($h, [IntPtr]::Zero, $w.X, $w.Y, $w.Width, $w.Height, 0x0044)
    Start-Sleep -Milliseconds 400
    [void][W]::SetWindowPos($h, [IntPtr]::Zero, $w.X, $w.Y, $w.Width, $w.Height, 0x0044)
    [void][W]::ShowWindow($h, 3)
    Start-Sleep -Seconds 4
    Save-Shot $h 'launch'
  }
  'shot'   { Save-Shot (Require-Window) $A }
  'screen' { Save-Screen (Require-Window) $A }
  'rect'   { $r = Get-Rect (Require-Window); "window $($r.L),$($r.T) to $($r.R),$($r.B)" }
  'click'  {
    $h = Require-Window
    Click-At $h ([int]$A) ([int]$B)
    if ($C) { Start-Sleep -Seconds 2; Save-Shot $h $C }
  }
  'type'   {
    $h = Require-Window; Focus-Window $h
    [Windows.Forms.SendKeys]::SendWait($A)
    "typed $A"
    if ($B) { Start-Sleep -Seconds 2; Save-Shot $h $B }
  }
  'tab'    {
    # The main tabs are UI Automation TabItems named by their titles; a real click selects them reliably.
    $h = Require-Window
    $cond = New-Object Windows.Automation.AndCondition(
      (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty, $A)),
      (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::TabItem)))
    $el = [Windows.Automation.AutomationElement]::FromHandle($h).FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $el) { throw "no tab titled '$A'" }
    # Not $b: PowerShell names are case-insensitive, and $B is the shot's name.
    $bounds = $el.Current.BoundingRectangle; $r = Get-Rect $h
    Click-At $h ([int]($bounds.X + $bounds.Width / 2 - $r.L)) ([int]($bounds.Y + $bounds.Height / 2 - $r.T))
    if ($B) { Start-Sleep -Seconds 3; Save-Shot $h $B }
  }
  'close'  {
    $proc = Get-Process TinapayanRMS.UI -ErrorAction SilentlyContinue
    if (-not $proc) { "not running"; break }
    [void]$proc.CloseMainWindow()
    if ($proc.WaitForExit(10000)) { "closed" } else { "still open: it may be asking about unsaved changes (driver.ps1 screen <name>)" }
  }
  default  { throw "unknown command '$Command'" }
}
