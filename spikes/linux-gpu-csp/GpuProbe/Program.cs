using System.Diagnostics;
using System.Text.Json;
using Spar.Spikes.GpuProbe;

// Usage: GpuProbe <mode> [options]; one JSON line on stdout.
//   none                                   process start only (baseline)
//   egl [--platform surfaceless|default|device] [--no-context]
//   devices                                EGL device enumeration only
//   node                                   /dev/dri/renderD* access and driver
//   combined                               node first; EGL (surfaceless, with context) only when a node is openable
//   gdk [--window] [--gsk-after-init cairo] [--pre-egl]
var mode = args.Length > 0 ? args[0] : "none";
var phase = new Phase();
var rssBefore = Rss();
var output = new MemoryStream();
using (var w = new Utf8JsonWriter(output))
{
    w.WriteStartObject();
    w.WriteString("mode", mode);
    w.WriteString("args", string.Join(' ', args));
    string verdict = "unknown";
    string reason = "";
    switch (mode)
    {
        case "none":
            break;
        case "egl":
            {
                var platform = Option(args, "--platform") ?? "surfaceless";
                var r = EglProbe.Run(platform, !args.Contains("--no-context"), phase);
                (verdict, reason) = Classify.FromEgl(r);
                WriteEgl(w, r);
                break;
            }
        case "devices":
            {
                var r = EglProbe.Devices(phase);
                WriteEgl(w, r);
                break;
            }
        case "node":
            {
                var nodes = RenderNodeProbe.Run(drmVersion: true);
                phase.Mark(null, "node");
                (verdict, reason) = Classify.FromNodes(nodes);
                WriteNodes(w, nodes);
                break;
            }
        case "combined":
            {
                var nodes = RenderNodeProbe.Run(drmVersion: true);
                phase.Mark(null, "node");
                WriteNodes(w, nodes);
                if (!nodes.Any(n => n.Openable))
                {
                    (verdict, reason) = ("software", "no openable render node; EGL skipped");
                    w.WriteBoolean("eglRan", false);
                }
                else
                {
                    var r = EglProbe.Run("surfaceless", context: true, phase);
                    (verdict, reason) = Classify.FromEgl(r);
                    w.WriteBoolean("eglRan", true);
                    WriteEgl(w, r);
                }
                break;
            }
        case "gdk":
            {
                if (args.Contains("--pre-egl"))
                {
                    var pre = EglProbe.Run("surfaceless", context: true, phase);
                    w.WriteString("preEglRenderer", pre.GlRenderer);
                    phase.Mark(null, "pre-egl");
                }
                var r = GdkProbe.Run(args.Contains("--window"), Option(args, "--gsk-after-init"), phase);
                (verdict, reason) = Classify.FromRenderer(r.GlRenderer, r.GlVendor, r.Error);
                w.WriteString("gtkVersion", r.GtkVersion);
                w.WriteBoolean("gtkInitialized", r.Initialized);
                w.WriteNumber("threadsBeforeInit", r.ThreadsBeforeInit);
                w.WriteNumber("threadsAfterInit", r.ThreadsAfterInit);
                if (r.UseEs is bool es)
                {
                    w.WriteBoolean("useEs", es);
                }
                w.WriteString("glVendor", r.GlVendor);
                w.WriteString("glRenderer", r.GlRenderer);
                w.WriteString("glVersion", r.GlVersion);
                w.WriteString("windowRenderer", r.WindowRenderer);
                w.WriteString("error", r.Error);
                WritePhases(w, "gdkPhases", r.Phases);
                break;
            }
        default:
            Console.Error.WriteLine("unknown mode " + mode);
            return 2;
    }
    var total = phase.Total;
    w.WriteString("verdict", verdict);
    w.WriteString("reason", reason);
    w.WriteNumber("detectMs", Math.Round(total, 3));
    w.WriteNumber("rssKbBefore", rssBefore);
    w.WriteNumber("rssKbAfter", Rss());
    w.WriteNumber("threadsAtEnd", GdkProbe.Threads());
    WritePhases(w, "phases", phase.All);
    w.WriteString("env", $"GSK_RENDERER={Environment.GetEnvironmentVariable("GSK_RENDERER")} LIBGL_ALWAYS_SOFTWARE={Environment.GetEnvironmentVariable("LIBGL_ALWAYS_SOFTWARE")} GALLIUM_DRIVER={Environment.GetEnvironmentVariable("GALLIUM_DRIVER")}");
    w.WriteEndObject();
}
Console.Out.WriteLine(System.Text.Encoding.UTF8.GetString(output.ToArray()));
return 0;

static string? Option(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static long Rss()
{
    foreach (var line in File.ReadLines("/proc/self/status"))
    {
        if (line.StartsWith("VmRSS:", StringComparison.Ordinal))
        {
            return long.Parse(line.AsSpan(6).Trim().TrimEnd("kB").Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
    return -1;
}

static void WriteEgl(Utf8JsonWriter w, EglResult r)
{
    w.WriteString("platform", r.Platform);
    w.WriteBoolean("libraryLoaded", r.LibraryLoaded);
    w.WriteBoolean("initialized", r.Initialized);
    w.WriteString("eglVersion", r.EglVersion);
    w.WriteString("eglVendor", r.EglVendor);
    w.WriteString("driverName", r.DriverName);
    w.WriteString("glVendor", r.GlVendor);
    w.WriteString("glRenderer", r.GlRenderer);
    w.WriteString("glVersion", r.GlVersion);
    w.WriteString("eglError", r.Error);
    w.WriteString("clientExtensions", r.ClientExtensions);
    if (r.Devices.Count > 0)
    {
        w.WriteStartArray("devices");
        foreach (var d in r.Devices)
        {
            w.WriteStringValue(d);
        }
        w.WriteEndArray();
    }
    WritePhases(w, "eglPhases", r.Phases);
}

static void WriteNodes(Utf8JsonWriter w, List<NodeInfo> nodes)
{
    w.WriteStartArray("nodes");
    foreach (var n in nodes)
    {
        w.WriteStartObject();
        w.WriteString("path", n.Path);
        w.WriteBoolean("openable", n.Openable);
        w.WriteString("openError", n.OpenError);
        w.WriteString("sysfsDriver", n.SysfsDriver);
        w.WriteString("pciVendor", n.PciVendor);
        w.WriteString("drmName", n.DrmName);
        w.WriteString("drmDesc", n.DrmDesc);
        w.WriteEndObject();
    }
    w.WriteEndArray();
}

static void WritePhases(Utf8JsonWriter w, string name, IEnumerable<(string Name, double Ms)> phases)
{
    w.WriteStartObject(name);
    foreach (var (n, ms) in phases)
    {
        w.WriteNumber(n, Math.Round(ms, 3));
    }
    w.WriteEndObject();
}

namespace Spar.Spikes.GpuProbe
{
    /// <summary>Elapsed time per step since the previous mark, and the total since construction.</summary>
    internal sealed class Phase
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        private long _last = Stopwatch.GetTimestamp();
        public readonly List<(string Name, double Ms)> All = [];

        public double Total => Stopwatch.GetElapsedTime(_start, _last).TotalMilliseconds;

        public void Mark(List<(string Name, double Ms)>? local, string name)
        {
            var now = Stopwatch.GetTimestamp();
            var ms = Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds;
            _last = now;
            local?.Add((name, ms));
            All.Add((name, ms));
        }
    }

    internal static class Classify
    {
        // Renderer strings of software rasterizers: Mesa (llvmpipe, softpipe, swrast), Vulkan (lavapipe), Google's
        // SwiftShader, and Microsoft's WARP behind D3D12 ("Microsoft Basic Render Driver").
        private static readonly string[] s_software =
            ["llvmpipe", "softpipe", "lavapipe", "swiftshader", "software rasterizer", "swrast", "basic render driver", "warp"];

        public static (string, string) FromRenderer(string? renderer, string? vendor, string? error)
        {
            if (renderer is null)
            {
                return ("software", "no GL renderer: " + (error ?? "?"));
            }
            foreach (var marker in s_software)
            {
                if (renderer.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return ("software", $"renderer '{renderer}' matches '{marker}'");
                }
            }
            return ("hardware", $"renderer '{renderer}' vendor '{vendor}'");
        }

        public static (string, string) FromEgl(EglResult r)
        {
            if (r.GlRenderer is not null || r.Error is not null)
            {
                return FromRenderer(r.GlRenderer, r.GlVendor, r.Error);
            }
            // No context was created: only the driver name is known.
            return r.DriverName is "swrast" or "kms_swrast"
                ? ("software", $"driver name '{r.DriverName}'")
                : ("hardware?", $"driver name '{r.DriverName}'");
        }

        public static (string, string) FromNodes(List<NodeInfo> nodes)
        {
            if (nodes.Count == 0)
            {
                return ("software", "no render node");
            }
            var hw = nodes.FirstOrDefault(RenderNodeProbe.IsHardwareCandidate);
            if (hw is not null)
            {
                return ("hardware", $"{hw.Path} openable, driver {hw.DrmName ?? hw.SysfsDriver}");
            }
            return nodes.Any(n => n.Openable)
                ? ("software", "only virtual-driver nodes are openable")
                : ("software", "no render node is openable");
        }
    }
}
