using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace WebView2Flags;

/// <summary>
/// The find API (<c>ICoreWebView2_28::get_Find</c>, <c>ICoreWebView2Environment15::CreateFindOptions</c>): match counts
/// for known text, next and previous, stop, and whether any window appears with the default find dialog suppressed and
/// not suppressed.
/// </summary>
internal static class FindScenario
{
    private static readonly Guid IidCoreWebView2_28 = new("62e50381-5bf5-51a8-aae0-f20a3a9c8a90");

    public static async Task RunAsync(
        CoreWebView2Environment env,
        CoreWebView2 view,
        nint hwnd,
        JsonObject result,
        string outDir,
        Func<string, string, Task<(double Ms, JsonObject Message, double BootMs)>> navigate)
    {
        // QueryInterface on the raw pointers first: this is the check the minimum Runtime needs.
        var raw = view.GetComICoreWebView2();
        var qi = new JsonObject { ["ICoreWebView2_28"] = Hex(HostRun.Probe(raw, IidCoreWebView2_28)) };
        Marshal.Release(raw);
        result["qi"] = qi;

        await navigate("/find.html", "ready");
        await Task.Delay(300);
        var baseline = WindowProbe.Snapshot();
        result["windowsBefore"] = baseline;
        var cases = new JsonArray();
        result["cases"] = cases;
        Directory.CreateDirectory(outDir);
        WindowProbe.Capture(hwnd, Path.Combine(outDir, "find-before.bmp"));

        (string Name, string Term, bool Case, bool Word, bool Suppress)[] runs =
        [
            ("insensitive", "needle", false, false, true),
            ("case-sensitive", "needle", true, false, true),
            ("whole-word", "needle", false, true, true),
            ("no-match", "zzqx", false, false, true),
            ("default-dialog", "needle", false, false, false),
            ("insensitive-again", "needle", false, false, true),
        ];

        CoreWebView2Find find;
        try
        {
            find = view.Find;
        }
        catch (Exception e)
        {
            result["findError"] = e.GetType().Name + ": " + e.Message;
            return;
        }

        foreach (var run in runs)
        {
            var events = new JsonArray();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            void OnCount(object? s, object a) => events.Add($"{clock.ElapsedMilliseconds} ms count={find.MatchCount}");
            void OnIndex(object? s, object a) => events.Add($"{clock.ElapsedMilliseconds} ms index={find.ActiveMatchIndex}");
            find.MatchCountChanged += OnCount;
            find.ActiveMatchIndexChanged += OnIndex;

            var options = env.CreateFindOptions();
            options.FindTerm = run.Term;
            options.IsCaseSensitive = run.Case;
            options.ShouldMatchWord = run.Word;
            options.SuppressDefaultFindDialog = run.Suppress;
            options.ShouldHighlightAllMatches = true;
            var item = new JsonObject { ["name"] = run.Name, ["term"] = run.Term, ["caseSensitive"] = run.Case, ["wholeWord"] = run.Word, ["suppressDefaultFindDialog"] = run.Suppress };
            try
            {
                await find.StartAsync(options);
                item["startCompletedMs"] = clock.ElapsedMilliseconds;
                item["countAtStart"] = find.MatchCount;
                await Task.Delay(600);
                item["count"] = find.MatchCount;
                item["index"] = find.ActiveMatchIndex;
                var shown = WindowProbe.Snapshot();
                item["newlyVisibleWindows"] = WindowProbe.NewlyVisible(baseline, shown);
                item["captured"] = WindowProbe.Capture(hwnd, Path.Combine(outDir, $"find-{run.Name}.bmp"));
                var popups = 0;
                foreach (var w in item["newlyVisibleWindows"]!.AsArray().Where(w => (int)w!["depth"]! == 0))
                {
                    var popup = nint.Parse(((string)w!["hwnd"]!)[2..], System.Globalization.NumberStyles.HexNumber);
                    WindowProbe.Capture(popup, Path.Combine(outDir, $"find-{run.Name}-popup-{popups++}.bmp"));
                }
                var steps = new JsonArray();
                foreach (var step in new[] { "next", "next", "previous", "previous", "previous" })
                {
                    if (step == "next")
                    {
                        find.FindNext();
                    }
                    else
                    {
                        find.FindPrevious();
                    }

                    await Task.Delay(250);
                    steps.Add($"{step} -> {find.ActiveMatchIndex}");
                }

                item["steps"] = steps;
                find.Stop();
                await Task.Delay(400);
                item["afterStop"] = $"count={find.MatchCount} index={find.ActiveMatchIndex}";
                item["newlyVisibleAfterStop"] = WindowProbe.NewlyVisible(baseline, WindowProbe.Snapshot());
            }
            catch (Exception e)
            {
                item["error"] = e.GetType().Name + ": " + e.Message;
            }

            find.MatchCountChanged -= OnCount;
            find.ActiveMatchIndexChanged -= OnIndex;
            item["events"] = events;
            cases.Add(item);
        }

        result["windowsAfter"] = WindowProbe.Snapshot();
    }

    private static string Hex(int hr) => $"0x{hr:X8}";
}
