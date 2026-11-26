using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Spar.Testing;

/// <summary>What a scene's capture holds: the PNG, the engine's tree as JSON, the calls as JSON, and the problems.</summary>
internal sealed record SceneCaptureResult(byte[] Png, string Tree, string Calls, IReadOnlyList<string> Problems);

/// <summary>
/// A scene's capture (decision "M2c's decide-first questions", items 5 and 6), once the scene method returned: the page
/// is made still (each animation finished, an endless one paused at its start, the caret transparent, through the
/// page's own Web Animations and CSSOM, which its Content Security Policy allows), then the capture waits until the
/// page has been idle for <see cref="Quiet"/> (no bridge call running or just ended, fonts loaded, no animation
/// running, no timeout of the page's due within the wait's limit), then takes the PNG of the viewport, the accessibility
/// tree as <c>spar tree</c> reads it, the bridge calls, and the problems: a page still busy at the wait's limit, Spar's
/// warnings and errors for the app, the page's errors and its console messages.
/// </summary>
internal static class SceneCapture
{
    /// <summary>How long the page must be idle before the capture.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(200);

    /// <summary>How long the capture waits for the page to be idle; past it, the capture is taken as the page is.</summary>
    private static readonly TimeSpan IdleLimit = TimeSpan.FromSeconds(10);

    private const int TreeLimit = 5000;

    /// <summary>UTF-8 without a byte order mark, as the tool and editors read the files.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The page's half of the idle wait, run at the start of every document in the page's own world, before the page's
    /// scripts: <c>setTimeout</c> and <c>clearTimeout</c> (and <c>clearInterval</c>, which clears a timeout too) keep
    /// the time each pending timeout is due, and <c>__sparSceneTimer()</c> answers how many milliseconds the soonest is
    /// away, or -1 with none. A page that changes itself from a timeout (a layer removed after 400 ms) would otherwise
    /// be captured before or after the change, depending on when the rest of the page went quiet. Intervals are endless
    /// and not counted. The timers are the system's; nothing is fast-forwarded.
    /// </summary>
    public const string TimerScript = """
        (() => {
          const realSet = setTimeout;
          const realClear = clearTimeout;
          const realClearInterval = clearInterval;
          const now = performance.now.bind(performance);
          const due = new Map();
          globalThis.setTimeout = function setTimeout(handler, delay, ...args) {
            if (typeof handler !== "function") {
              return realSet(handler, delay, ...args);
            }

            const id = realSet(function (...values) {
              due.delete(id);
              return handler.apply(this, values);
            }, delay, ...args);
            due.set(id, now() + Math.max(0, Number(delay) || 0));
            return id;
          };
          globalThis.clearTimeout = function clearTimeout(id) {
            due.delete(id);
            return realClear(id);
          };
          globalThis.clearInterval = function clearInterval(id) {
            due.delete(id);
            return realClearInterval(id);
          };
          Object.defineProperty(globalThis, "__sparSceneTimer", {
            value: () => {
              let soonest = -1;
              const at = now();
              for (const time of due.values()) {
                const left = Math.max(0, time - at);
                if (soonest < 0 || left < soonest) {
                  soonest = left;
                }
              }

              return soonest;
            },
          });
        })();
        """;

    /// <summary>
    /// Makes the page still and answers what still moves: finishes each finite animation, pauses each endless one at
    /// its start, makes the caret transparent, waits for fonts and two frames, then counts the running animations and
    /// reads how far away the soonest pending timeout is (-1 with none, or without <see cref="TimerScript"/>).
    /// </summary>
    private const string StillScript = """
        async () => {
          document.documentElement.style.setProperty("caret-color", "transparent", "important");
          for (const animation of document.getAnimations()) {
            try {
              const timing = animation.effect && animation.effect.getComputedTiming();
              if (timing && timing.iterations === Infinity) {
                animation.pause();
                animation.currentTime = 0;
              } else {
                animation.finish();
              }
            } catch {
              animation.cancel();
            }
          }

          await document.fonts.ready;
          await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
          const moving = document.getAnimations().filter(animation => animation.playState === "running").length;
          const timer = typeof globalThis.__sparSceneTimer === "function" ? globalThis.__sparSceneTimer() : -1;
          return [moving, timer];
        }
        """;

    public static async Task<SceneCaptureResult> CaptureAsync(SparScene scene, CancellationToken cancellationToken)
    {
        var app = scene.App;
        var page = app.Page;
        await page.WaitForUiReadyAsync();

        // The pointer to the page's bottom-right corner, so the last click's element shows no hover style.
        await page.Mouse.MoveAsync(scene.Size.Width - 1, scene.Size.Height - 1);
        var notIdle = await WaitForIdleAsync(scene, page, cancellationToken);

        var png = await page.ScreenshotAsync();
        var tree = await TreeAsync(page);
        var calls = Calls(scene);
        var problems = new List<string>();
        if (notIdle is not null)
        {
            problems.Add(notIdle);
        }

        problems.AddRange(app.Problems.Select(problem => "app: " + problem));
        problems.AddRange(page.PageErrors.Select(error => "page error: " + error));
        problems.AddRange(page.ConsoleMessages.Select(message => $"console {message.Type}: {message.Text}"));
        return new SceneCaptureResult(png, tree, calls, problems);
    }

    /// <summary>Writes the capture's files into <paramref name="folder"/>, replacing an earlier capture's.</summary>
    public static async Task WriteAsync(SceneCaptureResult capture, string folder)
    {
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "page.png"), capture.Png);
        await File.WriteAllTextAsync(Path.Combine(folder, "tree.json"), capture.Tree, Utf8);
        await File.WriteAllTextAsync(Path.Combine(folder, "calls.json"), capture.Calls, Utf8);
        var problems = capture.Problems.Count == 0 ? "" : string.Join('\n', capture.Problems) + "\n";
        await File.WriteAllTextAsync(Path.Combine(folder, "problems.txt"), problems, Utf8);
    }

    /// <summary>
    /// Waits until the page has been idle for <see cref="Quiet"/>: no animation running, no bridge call running or
    /// ended within it, and no timeout due before <see cref="IdleLimit"/> runs out (one due later cannot fire before the
    /// capture). Answers null once idle, or, past the limit, what was still busy, which becomes one of the problems.
    /// </summary>
    private static async Task<string?> WaitForIdleAsync(SparScene scene, SparPage page, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var quietSince = 0L;
        var busyWith = "";
        while (Stopwatch.GetElapsedTime(started) < IdleLimit)
        {
            var still = await page.EvaluateAsync(StillScript);
            var moving = still[0].GetInt32();
            var timer = still[1].GetDouble();
            var calls = scene.Calls;
            var left = (IdleLimit - Stopwatch.GetElapsedTime(started)).TotalMilliseconds;
            busyWith =
                moving > 0 ? $"{moving} animation{(moving == 1 ? "" : "s")} running"
                : calls is not null && calls.Running(1) ? "a bridge call running"
                : timer >= 0 && timer <= left ? "a timeout pending"
                : calls is not null && Stopwatch.GetElapsedTime(calls.LastActivity) < Quiet ? "a bridge call just ended"
                : "";
            var now = Stopwatch.GetTimestamp();
            if (busyWith.Length > 0)
            {
                quietSince = 0;
            }
            else if (quietSince == 0)
            {
                quietSince = now;
            }
            else if (Stopwatch.GetElapsedTime(quietSince, now) >= Quiet)
            {
                return null;
            }

            await Task.Delay(50, cancellationToken);
        }

        return $"scene: the page was not idle after {IdleLimit.TotalSeconds:0} s ({(busyWith.Length > 0 ? busyWith : "quiet for less than " + Quiet.TotalMilliseconds + " ms")}); captured as it was";
    }

    private static async Task<string> TreeAsync(SparPage page)
    {
        var answer = await page.Session.EngineAsync("snapshot", writer => writer.WriteNumber("limit", TreeLimit), page.Timeout(null))
            ?? throw new SparPageException("The page engine did not answer for the tree.");
        return Indented(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("document", page.Session.Navigations);
            foreach (var property in answer.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteSorted(writer, property.Value);
            }

            writer.WriteEndObject();
        });
    }

    /// <summary>
    /// <paramref name="element"/> with every object's keys in order: WebKitGTK hands back the engine's objects with
    /// their keys in an order that changes between runs, and the same tree must be the same bytes.
    /// </summary>
    private static void WriteSorted(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteSorted(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteSorted(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string Calls(SparScene scene)
    {
        var records = scene.Calls?.Since(1) ?? [];
        return Indented(writer =>
        {
            writer.WriteStartArray();
            foreach (var record in records)
            {
                record.Write(writer);
            }

            writer.WriteEndArray();
        });
    }

    private static string Indented(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }
}
