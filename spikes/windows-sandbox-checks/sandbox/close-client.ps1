# Host side: posts WM_CLOSE to one Windows Sandbox client window (by handle) after checking that it belongs to a
# WindowsSandboxRemoteSession process. Used only for the clients this spike's `wsb connect` calls started.
param([Parameter(Mandatory)][long]$Hwnd)
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class C {
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
}
'@
$h = [IntPtr]$Hwnd
$wpid = 0; [void][C]::GetWindowThreadProcessId($h, [ref]$wpid)
$p = Get-Process -Id $wpid -ErrorAction Stop
if ($p.ProcessName -ne 'WindowsSandboxRemoteSession') { throw "not a sandbox client: $($p.ProcessName)" }
"close $Hwnd pid=$wpid posted=$([C]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero))"
