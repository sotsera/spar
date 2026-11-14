# Copied from spikes/windows-sandbox-checks.
# Host side: lists visible top-level windows of the Windows Sandbox client processes (read-only), and optionally
# minimizes them, which hands activation back to the window below. Never touches other processes' windows.
param([switch]$Minimize)
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class W {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
'@
$fg = [W]::GetForegroundWindow()
$script:rows = [System.Collections.Generic.List[string]]::new()
[W]::EnumWindows({ param($h, $l)
  if ([W]::IsWindowVisible($h)) {
    $wpid = 0; [void][W]::GetWindowThreadProcessId($h, [ref]$wpid)
    $p = Get-Process -Id $wpid -ErrorAction SilentlyContinue
    if ($p -and $p.ProcessName -like '*Sandbox*') {
      $sb = New-Object System.Text.StringBuilder 256; [void][W]::GetWindowText($h, $sb, 256)
      $script:rows.Add("$h pid=$wpid $($p.ProcessName) '$sb' foreground=$($h -eq $fg)")
      if ($Minimize) { [void][W]::ShowWindowAsync($h, 6); $script:rows.Add("minimized $h") }
    }
  }
  $true }, [IntPtr]::Zero) | Out-Null
$script:rows
