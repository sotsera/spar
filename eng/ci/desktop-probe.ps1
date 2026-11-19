# Temporary CI probe (PR #2): where the hosted Windows runner's processes live, and whether a WebView2 app starts on the
# runner's desktop and on a desktop created in the runner's window station. Removed once the black-box placement on CI
# is settled.
param([Parameter(Mandatory)] [string] $Executable)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class DesktopProbe
{
    [DllImport("user32", SetLastError = true)] static extern IntPtr GetProcessWindowStation();
    [DllImport("user32", SetLastError = true)] static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("kernel32")] static extern uint GetCurrentThreadId();
    [DllImport("user32", SetLastError = true)] static extern bool GetUserObjectInformationW(IntPtr handle, int index, byte[] buffer, int length, out int needed);
    [DllImport("user32", SetLastError = true)] static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateDesktopW(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr attributes);
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool CreateProcessW(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation information);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo { public int cb; public string reserved; public string desktop; public string title; public int x, y, width, height, columns, rows, fill, flags; public short show, reserved2; public IntPtr reserved3, input, output, error; }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInformation { public IntPtr process, thread; public int processId, threadId; }

    static string Name(IntPtr handle)
    {
        var buffer = new byte[512];
        int needed;
        return GetUserObjectInformationW(handle, 2, buffer, buffer.Length, out needed)
            ? Encoding.Unicode.GetString(buffer, 0, Math.Max(0, needed - 2))
            : "(name failed " + Marshal.GetLastWin32Error() + ")";
    }

    static string Flags(IntPtr handle)
    {
        var buffer = new byte[12];
        int needed;
        return GetUserObjectInformationW(handle, 1, buffer, buffer.Length, out needed)
            ? "0x" + BitConverter.ToInt32(buffer, 8).ToString("X")
            : "(flags failed " + Marshal.GetLastWin32Error() + ")";
    }

    public static string Report()
    {
        var station = GetProcessWindowStation();
        var desktop = GetThreadDesktop(GetCurrentThreadId());
        var input = OpenInputDesktop(0, false, 0x0001);
        var inputText = input == IntPtr.Zero ? "(OpenInputDesktop failed " + Marshal.GetLastWin32Error() + ")" : Name(input);
        return "station " + Name(station) + " flags " + Flags(station) + "; thread desktop " + Name(desktop) + "; input desktop " + inputText;
    }

    public static string StationName() { return Name(GetProcessWindowStation()); }

    public static void CreateDesktop(string name)
    {
        if (CreateDesktopW(name, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero) == IntPtr.Zero)
        {
            throw new InvalidOperationException("CreateDesktop failed " + Marshal.GetLastWin32Error());
        }
    }

    public static int Start(string executable, string desktop)
    {
        var startup = new StartupInfo { cb = Marshal.SizeOf(typeof(StartupInfo)), desktop = desktop };
        ProcessInformation information;
        if (!CreateProcessW(executable, new StringBuilder("\"" + executable + "\""), IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, null, ref startup, out information))
        {
            throw new InvalidOperationException("CreateProcess failed " + Marshal.GetLastWin32Error());
        }

        return information.processId;
    }
}
'@

"session $([System.Diagnostics.Process]::GetCurrentProcess().SessionId), user $([Environment]::UserName), interactive $([Environment]::UserInteractive)"
[DesktopProbe]::Report()
$station = [DesktopProbe]::StationName()
[DesktopProbe]::CreateDesktop("spar-probe")

$cases = [ordered]@{
    "runner's desktop" = $null
    "new desktop, full name" = "$station\spar-probe"
    "new desktop, name only" = "spar-probe"
}

foreach ($case in $cases.Keys) {
    $root = Join-Path $Env:RUNNER_TEMP ("probe-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $Env:LOCALAPPDATA = $root
    $Env:APPDATA = $root
    $processId = [DesktopProbe]::Start($Executable, $cases[$case])
    try {
    $log = Get-ChildItem -Path $root -Recurse -Filter app.log -ErrorAction SilentlyContinue | Select-Object -First 1
    $text = ""
    for ($i = 0; $i -lt 300; $i++) {
        Start-Sleep -Milliseconds 100
        $log = Get-ChildItem -Path $root -Recurse -Filter app.log -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($log) {
            $stream = [IO.File]::Open($log.FullName, 'Open', 'Read', 'ReadWrite, Delete')
            try { $text = [IO.StreamReader]::new($stream).ReadToEnd() } finally { $stream.Dispose() }
            if ($text -match 'ui-ready|Start-up problem|could not start') { break }
        }
    }

    $verdict = if ($text -match 'ui-ready') { 'READY' } elseif ($text -match 'Start-up problem|could not start') { 'FAILED' } else { 'NO ANSWER' }
    "--- $case ($($cases[$case])): $verdict"
    ($text -split "`n" | Where-Object { $_ -match 'ui-ready|controller|could not|Start-up|environment at|window shown' }) -join "`n"
    }
    finally {
        taskkill /T /F /PID $processId 2>&1 | Out-Null
        Start-Sleep -Seconds 2
    }
}
