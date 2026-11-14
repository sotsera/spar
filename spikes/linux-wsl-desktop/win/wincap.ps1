# Windows-side capture of a WSLg window (spike). WSLg shows each Linux toplevel as a Windows window (RDP RAIL), so
# what the screen shows is what a person sees, for Wayland and X11 clients alike.
#   wincap.ps1 -Title <substring> -Out <dir> [-WaitMs 15000] [-DurationMs 3000] [-Shots 1500,2500] [-Frames]
# Waits for a top-level window whose title contains -Title, then:
#   -Frames  captures the window's screen rectangle as fast as GDI allows for -DurationMs, classifies a central region
#            (dominant colour, white / light #f3f3f3 / dark #1f1f1f / black shares) per frame, writes frames.jsonl and a
#            PNG whenever the class changes;
#   -Shots   saves full-resolution PNGs of the window at the given offsets (ms after the window was found).
# Every record has the window rectangle, the foreground window's title, and ms since the window was found.
# Writes "<Out>/wincap.ready" once it is waiting, so the caller can start the app.
param(
    [Parameter(Mandatory)] [string] $Title,
    [Parameter(Mandatory)] [string] $Out,
    [int] $WaitMs = 15000,
    [int] $DurationMs = 3000,
    [string] $Shots = "",
    [switch] $Frames
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force $Out | Out-Null
Add-Type -ReferencedAssemblies System.Runtime, System.Threading.Thread, System.Collections, System.Diagnostics.Process, System.ComponentModel.Primitives, System.Runtime.InteropServices, System.Drawing.Common, System.Drawing.Primitives, System.Private.Windows.GdiPlus, System.Private.Windows.Core -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class WinCap
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    // The window's own pixels (PW_RENDERFULLCONTENT), never other windows behind its transparent shadow.
    static Bitmap Print(IntPtr hwnd)
    {
        RECT wr; GetWindowRect(hwnd, out wr);
        var bmp = new Bitmap(wr.Right - wr.Left, wr.Bottom - wr.Top, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) { var hdc = g.GetHdc(); PrintWindow(hwnd, hdc, 2); g.ReleaseHdc(hdc); }
        return bmp;
    }

    // The central region used for classification: never includes the window's edges or anything behind them.
    static Bitmap Center(Bitmap bmp)
    {
        var rect = new Rectangle(bmp.Width * 15 / 100, bmp.Height * 25 / 100, bmp.Width * 70 / 100, bmp.Height * 60 / 100);
        return bmp.Clone(rect, bmp.PixelFormat);
    }

    public static string Text(IntPtr hwnd) { var b = new StringBuilder(512); GetWindowText(hwnd, b, 512); return b.ToString(); }

    public static IntPtr Find(string title)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => { if (IsWindowVisible(h) && Text(h).Contains(title)) { found = h; return false; } return true; }, IntPtr.Zero);
        return found;
    }

    public static RECT Bounds(IntPtr hwnd) { RECT r; DwmGetWindowAttribute(hwnd, 9, out r, Marshal.SizeOf(typeof(RECT))); return r; }

    static string Esc(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\""); }

    static string Describe(IntPtr hwnd, RECT r, Stopwatch clock)
    {
        uint pid; GetWindowThreadProcessId(hwnd, out pid);
        string owner; try { owner = Process.GetProcessById((int)pid).ProcessName; } catch { owner = "?"; }
        var fg = GetForegroundWindow();
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "\"t\":{0:F1},\"rect\":[{1},{2},{3},{4}],\"title\":\"{5}\",\"owner\":\"{6}\",\"dpi\":{7},\"foreground\":{8},\"foregroundTitle\":\"{9}\"",
            clock.Elapsed.TotalMilliseconds, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Esc(Text(hwnd)), owner, GetDpiForWindow(hwnd),
            fg == hwnd ? "true" : "false", Esc(Text(fg)));
    }

    static string Classify(Bitmap bmp, out double white, out double light, out double dark, out double black)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int x0 = bmp.Width * 15 / 100, x1 = bmp.Width * 85 / 100, y0 = bmp.Height * 25 / 100, y1 = bmp.Height * 85 / 100;
        var bins = new Dictionary<int, int>();
        long w = 0, l = 0, d = 0, k = 0, n = 0;
        unsafe
        {
            for (int y = y0; y < y1; y += 3)
            {
                uint* row = (uint*)((byte*)data.Scan0 + y * data.Stride);
                for (int x = x0; x < x1; x += 3)
                {
                    uint p = row[x] & 0xffffff; int R = (int)(p >> 16), G = (int)((p >> 8) & 0xff), B = (int)(p & 0xff);
                    n++;
                    if (R >= 0xfa && G >= 0xfa && B >= 0xfa) w++;
                    else if (Math.Abs(R - 0xf3) <= 2 && Math.Abs(G - 0xf3) <= 2 && Math.Abs(B - 0xf3) <= 2) l++;
                    else if (Math.Abs(R - 0x1f) <= 2 && Math.Abs(G - 0x1f) <= 2 && Math.Abs(B - 0x1f) <= 2) d++;
                    else if (R <= 8 && G <= 8 && B <= 8) k++;
                    int key = (R >> 3 << 10) | (G >> 3 << 5) | (B >> 3);
                    int c; bins.TryGetValue(key, out c); bins[key] = c + 1;
                }
            }
        }
        bmp.UnlockBits(data);
        int top = 0, best = -1; foreach (var kv in bins) if (kv.Value > best) { best = kv.Value; top = kv.Key; }
        double t = Math.Max(1, n); white = w / t; light = l / t; dark = d / t; black = k / t;
        return string.Format("{0:x2}{1:x2}{2:x2}", (top >> 10) << 3, ((top >> 5) & 31) << 3, (top & 31) << 3);
    }

    public static int Run(string title, string outDir, int waitMs, int durationMs, int[] shots, bool frames)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4)); // per-monitor v2: physical pixels
        File.WriteAllText(Path.Combine(outDir, "wincap.ready"), "waiting");
        var wait = Stopwatch.StartNew();
        IntPtr hwnd = IntPtr.Zero;
        while (wait.ElapsedMilliseconds < waitMs && (hwnd = Find(title)) == IntPtr.Zero) System.Threading.Thread.Sleep(1);
        var log = new StreamWriter(Path.Combine(outDir, "wincap.jsonl"));
        if (hwnd == IntPtr.Zero) { log.WriteLine("{\"error\":\"window not found\"}"); log.Close(); return 2; }
        var clock = Stopwatch.StartNew();
        log.WriteLine("{\"found\":true,\"waitedMs\":" + wait.ElapsedMilliseconds + "," + Describe(hwnd, Bounds(hwnd), clock) + "}");
        int shot = 0, pngs = 0, count = 0; string last = "";
        var end = Math.Max(durationMs, shots.Length > 0 ? shots[shots.Length - 1] + 50 : 0);
        while (clock.ElapsedMilliseconds < end)
        {
            var r = Bounds(hwnd);
            int width = r.Right - r.Left, height = r.Bottom - r.Top;
            if (width <= 0 || height <= 0) { System.Threading.Thread.Sleep(1); continue; }
            bool wantShot = shot < shots.Length && clock.ElapsedMilliseconds >= shots[shot];
            if (!frames && !wantShot) { System.Threading.Thread.Sleep(1); continue; }
            using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                var at = clock.Elapsed.TotalMilliseconds;
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(width, height));
                var took = clock.Elapsed.TotalMilliseconds - at;
                if (wantShot)
                {
                    var name = "shot-" + shots[shot] + ".png";
                    using (var printed = Print(hwnd)) printed.Save(Path.Combine(outDir, name), ImageFormat.Png);
                    log.WriteLine("{\"shot\":\"" + name + "\"," + Describe(hwnd, r, clock) + "}");
                    shot++;
                }
                if (frames)
                {
                    double w, l, d, k; var dom = Classify(bmp, out w, out l, out d, out k);
                    var cls = w > 0.5 ? "white" : l > 0.5 ? "light" : d > 0.5 ? "dark" : k > 0.5 ? "black" : "other:" + dom;
                    string png = "";
                    if (cls != last && pngs < 60) { png = string.Format("frame-{0:D4}.png", count); using (var center = Center(bmp)) center.Save(Path.Combine(outDir, png), ImageFormat.Png); pngs++; }
                    last = cls;
                    log.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{{\"i\":{0},\"at\":{1:F1},\"took\":{2:F1},\"class\":\"{3}\",\"dom\":\"#{4}\",\"white\":{5:F3},\"light\":{6:F3},\"dark\":{7:F3},\"black\":{8:F3},\"rect\":[{9},{10},{11},{12}]{13}}}",
                        count, at, took, cls, dom, w, l, d, k, r.Left, r.Top, width, height, png == "" ? "" : ",\"png\":\"" + png + "\""));
                    count++;
                }
            }
            if (Find(title) == IntPtr.Zero) { log.WriteLine("{\"gone\":true,\"t\":" + clock.ElapsedMilliseconds + "}"); break; }
        }
        log.Close();
        return 0;
    }
}
"@ -CompilerOptions "/unsafe"

$shotList = @()
if ($Shots) { $shotList = [int[]]($Shots -split ",") }
exit [WinCap]::Run($Title, (Resolve-Path $Out).Path, $WaitMs, $DurationMs, $shotList, $Frames.IsPresent)
