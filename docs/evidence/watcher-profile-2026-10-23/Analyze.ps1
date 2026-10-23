param([string]$Name='traced-worker')
$ErrorActionPreference='Stop'
$directory=(Resolve-Path "artifacts/watcher-profile/$Name").Path
$receipt=Get-Content (Join-Path $directory 'receipt.json') -Raw|ConvertFrom-Json
$tool=(Resolve-Path 'artifacts/coreclr-startup/tools/.store/dotnet-trace/10.0.750501/dotnet-trace/10.0.750501/tools/net8.0/any').Path
foreach($dependency in @('Microsoft.Diagnostics.FastSerialization.dll','Microsoft.Diagnostics.Tracing.TraceEvent.dll')){Add-Type -Path (Join-Path $tool $dependency)}
$references=@((Get-ChildItem (Join-Path $PSHOME 'ref') -Filter '*.dll').FullName)+(Join-Path $tool 'Microsoft.Diagnostics.Tracing.TraceEvent.dll')+(Join-Path $tool 'Microsoft.Diagnostics.FastSerialization.dll')
Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;

public sealed class ProfileWindow
{
    public string Name { get; set; }
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public int Samples;
    public int ManagedSamples;
    public int WithoutStack;
    public int JittingMethods;
    public Dictionary<string, int> Leaves = new();
    public Dictionary<string, int> Inclusive = new();
    public Dictionary<string, int> Types = new();
    public Dictionary<string, int> Work = new();
    public Dictionary<string, int> GeneratorFrames = new();
}

public static class SdkProfileReader
{
    private static void Increment(Dictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out var count);
        counts[key] = count + 1;
    }

    public static void Read(string input, string windowsJson, string output)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var windows = JsonSerializer.Deserialize<ProfileWindow[]>(windowsJson, options);
        var etlx = TraceLog.CreateFromEventPipeDataFile(input, input + ".etlx", null);
        using var log = new TraceLog(etlx);
        var providers = new Dictionary<string, int>();
        var sampleProcesses = new HashSet<int>();
        var pendingLoads = new Dictionary<string, (DateTimeOffset Start, string Name)>();
        var loads = new List<object>();
        var firstSample = DateTimeOffset.MaxValue;
        var lastSample = DateTimeOffset.MinValue;
        foreach (var item in log.Events)
        {
            var utc = new DateTimeOffset(item.TimeStamp.ToUniversalTime());
            Increment(providers, item.ProviderName);
            if (item.EventName == "AssemblyLoader/Start" || item.EventName == "AssemblyLoader/Stop")
            {
                var assembly = item.PayloadByName("AssemblyName")?.ToString() ?? "unknown";
                var key = item.ThreadID + "|" + assembly;
                if (item.EventName == "AssemblyLoader/Start")
                {
                    pendingLoads[key] = (utc, assembly.Split(',')[0]);
                }
                else if (pendingLoads.Remove(key, out var begin))
                {
                    var elapsed = (utc - begin.Start).TotalMilliseconds;
                    if (elapsed >= 50)
                    {
                        loads.Add(new { name = begin.Name, start = begin.Start, end = utc, elapsedMs = elapsed });
                    }
                }
            }
            var relevant = windows.Where(window => utc >= window.Start && utc <= window.End).ToArray();
            if (item.EventName == "Method/JittingStarted")
            {
                foreach (var window in relevant)
                {
                    window.JittingMethods++;
                }
            }
            if (item.ProviderName != "Microsoft-DotNETCore-SampleProfiler")
            {
                continue;
            }
            sampleProcesses.Add(item.ProcessID);
            if (utc < firstSample)
            {
                firstSample = utc;
            }
            if (utc > lastSample)
            {
                lastSample = utc;
            }
            if (relevant.Length == 0)
            {
                continue;
            }
            var frames = new List<string>();
            var stack = item.CallStack();
            while (stack != null && frames.Count < 256)
            {
                var address = stack.CodeAddress;
                var method = address.FullMethodName;
                frames.Add(string.IsNullOrEmpty(method) ? address.ModuleName + "!<unresolved>" : method);
                stack = stack.Caller;
            }
            var type = item.PayloadNames.Contains("Type") ? item.PayloadByName("Type")?.ToString() ?? "unknown" : "unknown";
            foreach (var window in relevant)
            {
                window.Samples++;
                Increment(window.Types, type);
                if (type != "Managed")
                {
                    continue;
                }
                window.ManagedSamples++;
                if (frames.Any(frame => frame.Contains("Microsoft.CodeAnalysis.GeneratorDriver.RunGeneratorsCore")))
                {
                    Increment(window.Work, "generator-pipeline");
                }
                if (frames.Any(frame => frame.Contains("Microsoft.CodeAnalysis.EditAndContinue")))
                {
                    Increment(window.Work, "edit-and-continue");
                }
                foreach (var frame in frames.Distinct().Where(frame => frame.Contains("System.Text.Json.SourceGeneration") || frame.Contains("Microsoft.Interop.") || frame.Contains("System.Text.RegularExpressions.Generator")))
                {
                    Increment(window.GeneratorFrames, frame);
                }
                if (frames.Count == 0)
                {
                    window.WithoutStack++;
                }
                else
                {
                    Increment(window.Leaves, frames[0]);
                }
                foreach (var frame in frames.Distinct())
                {
                    Increment(window.Inclusive, frame);
                }
            }
        }
        var result = new
        {
            coverage = new { startUtc = log.SessionStartTime.ToUniversalTime(), endUtc = log.SessionEndTime.ToUniversalTime(), eventsLost = log.EventsLost, firstSample, lastSample, processIds = sampleProcesses.ToArray() },
            providers,
            windows = windows.Select(window => new
            {
                window.Name, window.Start, window.End, window.Samples, window.ManagedSamples, window.WithoutStack, window.JittingMethods,
                sampleTypes = window.Types,
                work = window.Work,
                namedGeneratorFrames = window.GeneratorFrames.OrderByDescending(pair => pair.Value).Take(15).Select(pair => new { method = pair.Key, samples = pair.Value }),
                inclusive = window.Inclusive.OrderByDescending(pair => pair.Value).Take(100).Select(pair => new { method = pair.Key, samples = pair.Value }),
                leaves = window.Leaves.OrderByDescending(pair => pair.Value).Take(40).Select(pair => new { method = pair.Key, samples = pair.Value })
            }),
            completedLoadsOver50Ms = loads,
            unfinishedLoads = pendingLoads.Values.Select(value => new { name = value.Name, start = value.Start })
        };
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
}
'@
foreach($generation in @(1,2)){
  $windows=[Collections.Generic.List[object]]::new()
  $launch=$receipt.events|Where-Object{$_.kind -eq 'process-started' -and $_.role -eq 'watcher' -and $_.generation -eq $generation}|Select-Object -First 1
  $ready=$receipt.events|Where-Object{$_.kind -eq 'application-ready' -and $_.generation -eq $generation}|Select-Object -First 1
  $windows.Add(@{name='startup';start=$launch.observedAt;end=$ready.observedAt})
  foreach($edit in @($receipt.edits|Where-Object generation -eq $generation)){
    $windows.Add(@{name="edit-$($edit.edit)";start=$edit.startedAtUtc;end=$edit.runtimeUpdateAt})
  }
  [SdkProfileReader]::Read((Join-Path $directory "watcher-$generation.nettrace"),($windows.ToArray()|ConvertTo-Json),(Join-Path $directory "profile-$generation.json"))
}
