# Sends Tab twice to a WSLg window through Windows input (SendInput), which WSLg forwards over RDP to the Wayland
# compositor. Refuses unless the window is in the foreground, so no keystroke can land in another application.
#   sendkeys.ps1 -Title <substring> [-WaitMs 15000] [-Count 2]
param([Parameter(Mandatory)] [string] $Title, [int] $WaitMs = 15000, [int] $Count = 2)
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Keys
{
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public static IntPtr Find(string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => { var b = new StringBuilder(512); GetWindowText(h, b, 512); if (IsWindowVisible(h) && b.ToString().Contains(title)) { found = h; return false; } return true; }, IntPtr.Zero);
        return found;
    }
    public static void Tab() { keybd_event(0x09, 0x0f, 0, UIntPtr.Zero); keybd_event(0x09, 0x0f, 2, UIntPtr.Zero); }
}
"@
$clock = [Diagnostics.Stopwatch]::StartNew()
$hwnd = [IntPtr]::Zero
while ($clock.ElapsedMilliseconds -lt $WaitMs -and ($hwnd = [Keys]::Find($Title)) -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 20 }
if ($hwnd -eq [IntPtr]::Zero) { "SENDKEYS window not found"; exit 2 }
Start-Sleep -Milliseconds 1500
$wasForeground = [Keys]::GetForegroundWindow() -eq $hwnd
if (-not $wasForeground) { [Keys]::SetForegroundWindow($hwnd) | Out-Null; Start-Sleep -Milliseconds 200 }
if ([Keys]::GetForegroundWindow() -ne $hwnd) { "SENDKEYS refused: window not in the foreground (was foreground at start: $wasForeground)"; exit 3 }
for ($i = 0; $i -lt $Count; $i++) { [Keys]::Tab(); Start-Sleep -Milliseconds 200 }
"SENDKEYS sent $Count Tab (window was foreground at start: $wasForeground)"
