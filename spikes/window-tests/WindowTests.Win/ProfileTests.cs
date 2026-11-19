using System.Diagnostics;
using System.Text.Json;
using Spar.WindowTests.Interop;

namespace Spar.WindowTests;

/// <summary>
/// Question 1: an in-process loop of "tests", each needing a fresh profile, in one of four modes:
/// <c>inprivate</c> (shared environment, InPrivate controller per test), <c>profile-delete</c> and <c>profile-clear</c>
/// (shared environment, a new ProfileName per test, then Profile8.Delete or ClearBrowsingDataAll), and <c>udf</c>
/// (a new user-data folder and environment per test, deleted after BrowserProcessExited). Each test checks that it
/// starts empty, then leaves data behind in every store for the next test to find.
/// </summary>
internal static class ProfileTests
{
    private const int WindowManagement = 12;
    private const int PermissionAllow = 1;

    public static async Task<int> RunAsync(Options options)
    {
        var mode = options.Get("mode") ?? "inprivate";
        var count = options.Int("count", 25);
        var root = Paths.Udf(options.Get("root") ?? "profiles-" + mode);
        var runId = Guid.NewGuid().ToString("N")[..6];
        var hangAt = options.Int("hang-at", -1);
        var reputationOn = options.Has("reputation-on");
        using var cache = new CacheServer();
        Log.Write("START", $"mode={mode} count={count} root={root} pid={Environment.ProcessId} cache={cache.Origin}");

        Env? shared = null;
        if (mode != "udf")
        {
            var started = Stopwatch.GetTimestamp();
            shared = await Env.CreateAsync(Path.Combine(root, "shared"));
            Log.Write("SHARED-ENV", $"ms={Time.Ms(started):F1}");
        }

        var setups = new List<double>();
        var teardowns = new List<double>();
        var leaks = new List<string>();
        for (var i = 0; i < count; i++)
        {
            // ---- Setup ----------------------------------------------------------------------------------------
            var setupStart = Stopwatch.GetTimestamp();
            var env = shared ?? await Env.CreateAsync(Path.Combine(root, $"udf-{runId}-{i}"));
            var envMs = Time.Ms(setupStart);
            var profile = mode switch
            {
                "inprivate" => options.Get("private-name") is { } name ? "~private:" + name : "~private",
                "profile-delete" or "profile-clear" => $"t{runId}{i}",
                _ => "default",
            };
            var permissionEvents = new List<string>();
            var view = await View.CreateAsync(env, profile, beforeFirstUse: v =>
            {
                v.OnPermissionRequested(args =>
                {
                    var kind = args.GetPermissionKind();
                    permissionEvents.Add($"kind={kind} user={args.GetIsUserInitiated()} saves={args.GetSavesInProfile()}");
                    args.PutState(kind == WindowManagement ? PermissionAllow : 2);
                    args.PutSavesInProfile(1);
                });
                if (reputationOn)
                {
                    v.WebView.GetSettings().PutIsReputationCheckingRequired(1);
                }

                return Task.CompletedTask;
            });
            var controllerMs = Time.Ms(setupStart) - envMs;
            var pageStart = Stopwatch.GetTimestamp();
            await view.NavigateAsync("https://app.localhost/probe.html");
            var pageMs = Time.Ms(pageStart);
            var setupMs = Time.Ms(setupStart);

            // ---- Body: what did the previous test leave? ---------------------------------------------------------
            var before = (await view.EvaluateAsync("probe.read()")).GetRawText();
            var screens = await view.EvaluateTextAsync("probe.screens()", userGesture: true);
            var permissionsFired = permissionEvents.Count;
            var settings = new PermissionSettingsHandler();
            view.GetProfile().GetNonDefaultPermissionSettings(settings);
            var stored = string.Join(';', await settings.Task);
            var hitsBefore = cache.ScriptHits;
            await view.NavigateAsync(cache.Origin + "/cache.html");
            var firstLoadHits = cache.ScriptHits - hitsBefore;
            await view.NavigateAsync(cache.Origin + "/cache.html?again");
            await view.NavigateAsync(cache.Origin + "/cache.html");
            var secondLoadHits = cache.ScriptHits - hitsBefore - firstLoadHits;
            await view.NavigateAsync("https://app.localhost/probe.html");
            await view.EvaluateAsync($"probe.write('T{i}')");
            var after = (await view.EvaluateAsync("probe.read()")).GetRawText();
            var memory = ProcessTools.Memory(Paths.Marker(env.Path)).Sum(s => s.PrivateBytes) / 1048576.0;

            using (var document = JsonDocument.Parse(before))
            {
                var state = document.RootElement;
                var found = new List<string>();
                if (state.GetProperty("ls").ValueKind != JsonValueKind.Null) found.Add("localStorage");
                if (state.GetProperty("idb").ValueKind != JsonValueKind.Null) found.Add("indexedDB");
                if (state.GetProperty("cookie").ValueKind != JsonValueKind.Null) found.Add("cookie");
                if (state.GetProperty("sw").ToString() != "0") found.Add("serviceWorker");
                if (state.GetProperty("wm").ToString() == "granted" || (permissionsFired == 0 && screens.StartsWith("granted", StringComparison.Ordinal))) found.Add("permission");
                if (firstLoadHits == 0) found.Add("httpCache");
                if (i > 0 && found.Count > 0)
                {
                    leaks.Add($"{i}:{string.Join('+', found)}");
                }

                Log.Write("BODY", $"mode={mode} i={i} leftovers=[{string.Join(',', found)}] before={before} screens={screens} " +
                    $"permissionEvents=[{string.Join(';', permissionEvents)}] stored=[{stored}] cacheFirstLoad={firstLoadHits} cacheSecondLoad={secondLoadHits} " +
                    $"after={after} browserPrivateMB={memory:F1}");
            }

            if (i == hangAt)
            {
                // The crash scenario: the harness kills this process here, mid-test.
                Log.Write("MIDTEST", $"udf={env.Path}");
                await Task.Delay(Timeout.Infinite);
            }

            // ---- Teardown -------------------------------------------------------------------------------------
            var teardownStart = Stopwatch.GetTimestamp();
            var detail = "";
            switch (mode)
            {
                case "inprivate":
                    view.Close();
                    break;
                case "profile-delete":
                {
                    var deleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var handler = new ProfileDeletedHandler(() => deleted.TrySetResult());
                    var profileObject = view.GetProfile();
                    profileObject.AddDeleted(handler);
                    profileObject.Delete();
                    var ok = await Task.WhenAny(deleted.Task, Task.Delay(5000)) == deleted.Task;
                    view.Close();
                    GC.KeepAlive(handler);
                    detail = $"deletedEvent={ok}";
                    break;
                }

                case "profile-clear":
                {
                    var cleared = new HresultCompletedHandler();
                    view.GetProfile().ClearBrowsingDataAll(cleared);
                    detail = $"clearHr=0x{await cleared.Task:X8}";
                    view.Close();
                    break;
                }

                default:
                {
                    view.Close();
                    var exited = await Task.WhenAny(env.Exited.Task, Task.Delay(15000)) == env.Exited.Task;
                    var exitMs = Time.Ms(teardownStart);
                    var gone = await WaitForNoProcessesAsync(env.Path, 15000);
                    var deleted = Paths.TryDelete(env.Path, out var error);
                    detail = $"browserExited={exited} exitMs={exitMs:F1} gone={gone} deleted={deleted}{(deleted ? "" : " error=" + error)}";
                    break;
                }
            }

            var teardownMs = Time.Ms(teardownStart);
            setups.Add(setupMs);
            teardowns.Add(teardownMs);
            Log.Write("TEST", $"mode={mode} i={i} setupMs={setupMs:F1} envMs={envMs:F1} controllerMs={controllerMs:F1} pageMs={pageMs:F1} " +
                $"teardownMs={teardownMs:F1} totalMs={setupMs + teardownMs:F1} {detail}");
        }

        if (shared is not null && options.Has("memory"))
        {
            await MemoryAsync(shared, mode, runId);
        }

        Log.Write("SUMMARY", $"mode={mode} setup {Stats.Summary(setups)}");
        Log.Write("SUMMARY", $"mode={mode} setup-after-first {Stats.Summary(setups.Skip(1).ToList())}");
        Log.Write("SUMMARY", $"mode={mode} teardown {Stats.Summary(teardowns)}");
        Log.Write("SUMMARY", $"mode={mode} total {Stats.Summary(setups.Zip(teardowns, (s, t) => s + t).ToList())}");
        Log.Write("SUMMARY", $"mode={mode} leaks=[{string.Join(' ', leaks)}]");

        if (shared is not null)
        {
            var closeStart = Stopwatch.GetTimestamp();
            await Task.Delay(100);
            var exited = await Task.WhenAny(shared.Exited.Task, Task.Delay(15000)) == shared.Exited.Task;
            var gone = await WaitForNoProcessesAsync(shared.Path, 15000);
            var (files, bytes) = Paths.Size(shared.Path);
            var profiles = Directory.Exists(Path.Combine(shared.Path, "EBWebView"))
                ? Directory.GetDirectories(Path.Combine(shared.Path, "EBWebView"), "WV2Profile_*").Length
                : 0;
            var deleted = Paths.TryDelete(shared.Path, out var error);
            Log.Write("SHARED-END", $"browserExited={exited} gone={gone} ms={Time.Ms(closeStart):F1} files={files} kB={bytes / 1024} " +
                $"profileFolders={profiles} deleted={deleted} {error}");
        }

        return 0;
    }

    /// <summary>Private bytes of the browser processes with 1 to 4 test windows open at once.</summary>
    private static async Task MemoryAsync(Env env, string mode, string runId)
    {
        var views = new List<View>();
        for (var k = 1; k <= 4; k++)
        {
            var profile = mode == "inprivate" ? $"~private:m{runId}{k}" : $"m{runId}{k}";
            var view = await View.CreateAsync(env, profile);
            await view.NavigateAsync("https://app.localhost/probe.html");
            views.Add(view);
            await Task.Delay(2000);
            var samples = ProcessTools.Memory(Paths.Marker(env.Path));
            Log.Write("MEM", $"mode={mode} windows={k} browserPrivateMB={samples.Sum(s => s.PrivateBytes) / 1048576.0:F1} processes={samples.Count} " +
                $"kinds=[{string.Join(',', samples.GroupBy(s => s.Kind).Select(g => $"{g.Key}x{g.Count()}"))}]");
        }

        foreach (var view in views)
        {
            view.Close();
        }
    }

    public static async Task<bool> WaitForNoProcessesAsync(string udfPath, int timeoutMilliseconds)
    {
        var started = Stopwatch.GetTimestamp();
        while (Time.Ms(started) < timeoutMilliseconds)
        {
            if (ProcessTools.Find(Paths.Marker(udfPath), keepHandles: false).Count == 0)
            {
                return true;
            }

            await Task.Delay(10);
        }

        return false;
    }
}
