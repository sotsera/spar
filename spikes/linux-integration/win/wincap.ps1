# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File win/wincap.ps1 -Title <WSLg window title> [-Out file.png]
# Prints the Windows-side rectangle of the WSLg (RAIL) window whose title starts with -Title and, with -Out, saves an
# image of that window only, through PrintWindow. No input is sent and no window is activated.
param([Parameter(Mandatory)] [string] $Title, [string] $Out)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class SparWin {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  public delegate bool EnumProc(IntPtr hwnd, IntPtr data);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr data);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
  public static List<KeyValuePair<IntPtr, string>> Find(string prefix) {
    var found = new List<KeyValuePair<IntPtr, string>>();
    EnumWindows((h, d) => { var b = new StringBuilder(512); GetWindowTextW(h, b, 512); var t = b.ToString();
      if (t.StartsWith(prefix, StringComparison.Ordinal)) found.Add(new KeyValuePair<IntPtr, string>(h, t)); return true; }, IntPtr.Zero);
    return found;
  }
}
"@
$matches = [SparWin]::Find($Title)
if ($matches.Count -eq 0) { "WIN not-found '$Title'"; exit 1 }
foreach ($m in $matches) {
  $hwnd = $m.Key
  $r = New-Object SparWin+RECT
  [void][SparWin]::GetWindowRect($hwnd, [ref]$r)
  $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
  "WIN title='$($m.Value)' rect=$($r.Left),$($r.Top) size=${w}x$h visible=$([SparWin]::IsWindowVisible($hwnd)) iconic=$([SparWin]::IsIconic($hwnd)) zoomed=$([SparWin]::IsZoomed($hwnd))"
  if ($Out -and $w -gt 0 -and $h -gt 0 -and [SparWin]::IsWindowVisible($hwnd)) {
    $bitmap = New-Object System.Drawing.Bitmap $w, $h
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    $ok = [SparWin]::PrintWindow($hwnd, $hdc, 2)
    $graphics.ReleaseHdc($hdc); $graphics.Dispose()
    $bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose()
    "WIN saved=$Out printwindow=$ok"
    $Out = $null
  }
}
