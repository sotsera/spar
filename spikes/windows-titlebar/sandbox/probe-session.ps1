# Runs inside Windows Sandbox as the logged-in user: checks that the session has an input desktop, that real
# cursor input works and that the screen renders, by capturing the guest screen to a PNG.
param([string]$Out = 'C:\spar\out')
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class U {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
}
'@
$log = Join-Path $Out 'session.txt'
"user=$env:USERNAME session=$((Get-Process -Id $PID).SessionId)" | Out-File $log
$ok = [U]::SetCursorPos(200, 150); $p = New-Object U+POINT; [void][U]::GetCursorPos([ref]$p)
"setcursor=$ok pos=$($p.X),$($p.Y) fg=$([U]::GetForegroundWindow())" | Out-File $log -Append
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) { "screen $($s.Bounds) primary=$($s.Primary)" | Out-File $log -Append }
try {
  $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
  $bmp.Save((Join-Path $Out 'session.png')); "capture ok" | Out-File $log -Append
} catch { "capture failed: $_" | Out-File $log -Append }
