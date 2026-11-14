# Host side: places one Windows Sandbox client window (by handle) left of the whole virtual screen at the given
# size, without activating it (SetWindowPlacement with SW_SHOWNOACTIVATE), or minimizes it again with -Minimize.
# It touches only the named window, which belongs to the sandbox client this spike started.
param([Parameter(Mandatory)][long]$Hwnd, [int]$Width = 1920, [int]$Height = 1200, [switch]$Minimize, [switch]$Query)
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class P {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct WP { public int Length, Flags, ShowCmd; public POINT Min, Max; public RECT Normal; }
  [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr h, ref WP p);
  [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr h, ref WP p);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
'@
$h = [IntPtr]$Hwnd
$wpid = 0; [void][P]::GetWindowThreadProcessId($h, [ref]$wpid)
$proc = Get-Process -Id $wpid -ErrorAction Stop
if ($proc.ProcessName -ne 'WindowsSandboxRemoteSession') { throw "not a sandbox client window: $($proc.ProcessName)" }
$wp = New-Object P+WP; $wp.Length = [Runtime.InteropServices.Marshal]::SizeOf($wp)
[void][P]::GetWindowPlacement($h, [ref]$wp)
if (-not $Query) {
  if ($Minimize) { $wp.ShowCmd = 7 } # SW_SHOWMINNOACTIVE
  else {
    $left = [P]::GetSystemMetrics(76) # SM_XVIRTUALSCREEN
    $wp.Normal.L = $left - $Width - 200; $wp.Normal.T = 0; $wp.Normal.R = $wp.Normal.L + $Width; $wp.Normal.B = $Height
    $wp.ShowCmd = 4 # SW_SHOWNOACTIVATE
  }
  [void][P]::SetWindowPlacement($h, [ref]$wp)
}
$r = New-Object P+RECT; [void][P]::GetWindowRect($h, [ref]$r)
"virtual=$([P]::GetSystemMetrics(76)),$([P]::GetSystemMetrics(77)) $([P]::GetSystemMetrics(78))x$([P]::GetSystemMetrics(79)) maxTrack=$([P]::GetSystemMetrics(59))x$([P]::GetSystemMetrics(60)) window=[$($r.L),$($r.T),$($r.R),$($r.B)] showCmd=$($wp.ShowCmd) foregroundIsClient=$([P]::GetForegroundWindow() -eq $h)"
