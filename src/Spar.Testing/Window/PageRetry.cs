using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Spar.Hosting;

namespace Spar.Testing;

/// <summary>One try of a wait: done with a value, not yet (and why), or failed for good.</summary>
internal readonly record struct Attempt<T>(bool Done, T? Value, string? NotYet, string? Failure, bool Again = false)
{
    public static Attempt<T> Success(T value) => new(true, value, null, null);

    /// <summary>Not yet; <paramref name="again"/> tries once more at once (an element still moving).</summary>
    public static Attempt<T> Retry(string why, bool again = false) => new(false, default, why, null, again);

    public static Attempt<T> Fail(string why) => new(false, default, null, why);
}

/// <summary>
/// The waiting rules of window tests: each action and assertion is tried until it holds or its time is up, with short
/// pauses that grow (0, 20, 50, 100, 100, 250, 500 ms, then 500 ms), and its failure says what was searched for and
/// what was found at the last try. A time-out never replays anything: an action's input is sent only once its checks
/// passed.
/// </summary>
internal static class PageRetry
{
    private static readonly int[] Pauses = [0, 20, 50, 100, 100, 250, 500];

    public static async Task<T> RunAsync<T>(string what, TimeSpan timeout, Func<TimeSpan, Task<Attempt<T>>> attempt)
    {
        var started = Stopwatch.GetTimestamp();
        var tries = 0;
        string? last = null;
        while (true)
        {
            tries++;
            Attempt<T> result;
            try
            {
                result = await attempt(PageSession.Remaining(started, timeout));
            }
            catch (SparDevToolsException exception)
            {
                // A document or world gone between the request and its answer (a navigation, a re-render).
                result = Attempt<T>.Retry(exception.Error ?? exception.Message);
            }

            if (result.Done)
            {
                return result.Value!;
            }

            if (result.Failure is { } failure)
            {
                throw new SparPageException($"{what}: {failure}");
            }

            last = result.NotYet;
            var left = PageSession.Remaining(started, timeout);
            if (left <= TimeSpan.Zero)
            {
                var spent = Stopwatch.GetElapsedTime(started).TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture);
                throw new SparPageException(
                    $"{what}: timed out after {spent} ms ({tries} {(tries == 1 ? "try" : "tries")}). Last try: {last ?? "no answer from the page"}");
            }

            var pause = result.Again ? 0 : Pauses[Math.Min(tries - 1, Pauses.Length - 1)];
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(pause, left.TotalMilliseconds)));
        }
    }

    /// <summary>
    /// An engine answer as an attempt: <c>ok</c> is done, <c>retry</c> is not yet (with what is near when nothing
    /// matched), <c>strict</c> fails at once listing the matches, <c>error</c> fails at once.
    /// </summary>
    public static Attempt<JsonElement> Read(JsonElement? answer, SparLocator locator)
    {
        if (answer is not { } value || value.ValueKind != JsonValueKind.Object)
        {
            return Attempt<JsonElement>.Retry("the page is loading");
        }

        if (PageJson.Bool(value, "ok"))
        {
            return Attempt<JsonElement>.Success(value);
        }

        if (PageJson.Int(value, "strict") is var count and > 0)
        {
            return Attempt<JsonElement>.Fail(StrictViolation(locator, count, PageJson.Strings(value, "matches")));
        }

        if (PageJson.String(value, "error") is { } error)
        {
            return Attempt<JsonElement>.Fail(error);
        }

        return Attempt<JsonElement>.Retry(NotYet(value, locator), again: PageJson.Bool(value, "unstable"));
    }

    public static string StrictViolation(SparLocator locator, int count, IReadOnlyList<string> matches) =>
        $"strict mode: {locator} matches {count} elements, and an action needs one: {string.Join("; ", matches)}{(count > matches.Count ? "; …" : "")}. " +
        "Name it more closely, or pick one with First, Last or Nth.";

    /// <summary>Why not yet; for "no element matches", which step found nothing and what is near.</summary>
    public static string NotYet(JsonElement value, SparLocator locator)
    {
        var reason = PageJson.String(value, "retry") ?? "not yet";
        if (reason != "no element matches")
        {
            return reason;
        }

        return NothingMatches(locator, PageJson.Int(value, "step", -1), PageJson.Strings(value, "near"));
    }

    /// <summary>"No element matches", the step of the chain that found nothing, and up to five elements near it.</summary>
    public static string NothingMatches(SparLocator locator, int step, IReadOnlyList<string> near)
    {
        var text = new StringBuilder("no element matches ");
        text.Append(step >= 0 && step < locator.Chain.Count && locator.Chain.Count > 1
            ? $"{locator.Chain[step].Description} (step {step + 1} of {locator})"
            : locator.ToString());
        text.Append(near.Count == 0 ? "; nothing similar is on the page" : "; near: " + string.Join("; ", near));
        return text.ToString();
    }
}

/// <summary>A window test's action or assertion that failed: what it searched for, and what it found at the last try.</summary>
public sealed class SparPageException(string message) : Exception(message);
