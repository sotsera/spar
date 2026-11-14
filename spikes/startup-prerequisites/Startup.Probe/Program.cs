using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Startup.Probe;

// Usage (Windows Sandbox only):
//   Startup.Probe run <out> <label> <actions> <exe> [args...]   actions: comma list of shot, dump, expand, link,
//                                                                dismiss:<button name>, wait:<ms>, or none
//   Startup.Probe theme dark|light
//   Startup.Probe shot <png>
internal static unsafe class Program
{
    private static uint s_pid;
    private static readonly List<(nint Hwnd, string Class, string Title)> s_windows = [];

    private static int Main(string[] args)
    {
        if (!Environment.UserName.Equals("WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("the probe shows and drives windows: Windows Sandbox only");
            return 2;
        }

        return args[0] switch
        {
            "run" => Run(args[1], args[2], args[3], args[4], args[5..]),
            "theme" => Theme(args[1] == "dark"),
            "shot" => ShotOnly(args[1]),
            _ => 2,
        };
    }

    private static int ShotOnly(string path)
    {
        Shot.Screen(Shot.FullScreen(), path);
        return 0;
    }

    // The guest's own colour mode (the guest is disposable), announced like the Settings app does.
    private static int Theme(bool dark)
    {
        const string Key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        Registry.SetValue(Key, "AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        Registry.SetValue(Key, "SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
        Native.SendMessageTimeout(0xFFFF, 0x001A, 0, "ImmersiveColorSet", 0x2, 5000, out _); // HWND_BROADCAST, WM_SETTINGCHANGE
        Console.WriteLine($"theme dark={dark}");
        return 0;
    }

    private static int Run(string outDir, string label, string actions, string exe, string[] appArgs)
    {
        Directory.CreateDirectory(outDir);
        var summary = new Dictionary<string, string>();
        var start = new ProcessStartInfo(exe) { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in appArgs)
        {
            start.ArgumentList.Add(a);
        }

        var clock = Stopwatch.StartNew();
        using var p = Process.Start(start)!;
        s_pid = (uint)p.Id;
        var stderr = p.StandardError.ReadToEndAsync();
        double? firstWindow = null, dialogAt = null;
        nint dialog = 0;
        while (clock.ElapsedMilliseconds < 90_000 && !p.HasExited)
        {
            var windows = Windows();
            if (firstWindow is null && windows.Count > 0)
            {
                firstWindow = clock.Elapsed.TotalMilliseconds;
                summary["firstWindowClass"] = windows[0].Class;
            }

            var d = windows.FirstOrDefault(w => w.Class == "#32770");
            if (d.Hwnd != 0)
            {
                dialogAt = clock.Elapsed.TotalMilliseconds;
                dialog = d.Hwnd;
                summary["dialogTitle"] = d.Title;
                break;
            }

            Thread.Sleep(2);
        }

        summary["firstWindowMs"] = Ms(firstWindow);
        summary["dialogMs"] = Ms(dialogAt);
        if (dialog != 0)
        {
            Thread.Sleep(700); // let it paint
            Native.Rect frame;
            Native.DwmGetWindowAttribute(dialog, 9, &frame, sizeof(Native.Rect)); // DWMWA_EXTENDED_FRAME_BOUNDS
            summary["dialogRect"] = frame.ToString();
            summary["dialogDpi"] = Native.GetDpiForWindow(dialog).ToString(CultureInfo.InvariantCulture);
            var step = 0;
            foreach (var action in actions.Split(','))
            {
                step++;
                var name = $"{label}-{step:00}-{action.Replace(':', '-')}";
                switch (action.Split(':')[0])
                {
                    case "shot":
                        Shot.Screen(Shot.FullScreen(), Path.Combine(outDir, name + "-screen.png"));
                        Shot.Screen(frame, Path.Combine(outDir, name + "-dialog.png"));
                        break;
                    case "dump":
                        File.WriteAllText(Path.Combine(outDir, name + "-uia.txt"), Uia.Dump(dialog));
                        break;
                    case "expand":
                        summary[name] = Uia.Invoke(dialog, 50000, "Show details");
                        Thread.Sleep(500);
                        Native.DwmGetWindowAttribute(dialog, 9, &frame, sizeof(Native.Rect));
                        break;
                    case "link":
                        summary[name] = Uia.Invoke(dialog, 50005, null);
                        break;
                    case "button":
                        summary[name] = Uia.Invoke(dialog, 50000, action[(action.IndexOf(':') + 1)..]);
                        break;
                    case "dismiss":
                        summary[name] = Uia.Invoke(dialog, 50000, action[(action.IndexOf(':') + 1)..]);
                        break;
                    case "wait":
                        Thread.Sleep(int.Parse(action.Split(':')[1], CultureInfo.InvariantCulture));
                        break;
                }
            }
        }

        var exited = p.WaitForExit(20_000);
        summary["exited"] = exited.ToString();
        if (!exited)
        {
            p.Kill(); // our own child, left open by a failed dismissal
        }

        summary["exitCode"] = exited ? p.ExitCode.ToString(CultureInfo.InvariantCulture) : "";
        summary["totalMs"] = Ms(clock.Elapsed.TotalMilliseconds);
        summary["stderr"] = stderr.Wait(5000) ? stderr.Result : "(not read)";
        summary["label"] = label;
        File.WriteAllText(Path.Combine(outDir, label + ".probe.json"), JsonSerializer.Serialize(summary, ProbeJson.Default.DictionaryStringString));
        Console.WriteLine($"{label} dialog={summary["dialogMs"]} exit={summary["exitCode"]}");
        return 0;
    }

    private static string Ms(double? ms) => ms is null ? "" : ms.Value.ToString("0.0", CultureInfo.InvariantCulture);

    private static List<(nint Hwnd, string Class, string Title)> Windows()
    {
        s_windows.Clear();
        Native.EnumWindows(&OnWindow, 0);
        return [.. s_windows];
    }

    [UnmanagedCallersOnly]
    private static int OnWindow(nint hwnd, nint data)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == s_pid && Native.IsWindowVisible(hwnd))
        {
            var buffer = stackalloc char[256];
            var n = Native.GetClassName(hwnd, buffer, 256);
            var cls = new string(buffer, 0, n);
            n = Native.GetWindowText(hwnd, buffer, 256);
            s_windows.Add((hwnd, cls, new string(buffer, 0, n)));
        }

        return 1;
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class ProbeJson : System.Text.Json.Serialization.JsonSerializerContext;
