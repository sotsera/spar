using System.Globalization;
using Spar.Hosting;

namespace Spar.Windows;

/// <summary>
/// A Development-only variable that sends trusted input to the page through the in-process DevTools Protocol's
/// <c>Input</c> domain (decision "Window-test profiles, trusted input, the page's pickers and the Sandbox runner"), so
/// the browser defaults that need input can be checked without touching the desktop's real mouse and keyboard
/// (<c>docs/evidence/m1b-security-defaults.md</c>). It is read only when <see cref="SparDevelopment.IsSupported"/>,
/// which is off and trimmed in Production, so a shipped app contains neither the variable's name nor the method names.
/// <para>
/// <c>SPAR_DEV_INPUT</c> is a list of steps separated by <c>;</c>, run in order from the first <c>ui-ready</c>:
/// <c>wait:&lt;ms&gt;</c>; <c>click:x,y</c>, <c>right:x,y</c>, <c>back:x,y</c>, <c>forward:x,y</c> (a mouse button at
/// the page's CSS pixel); <c>key:&lt;keys&gt;</c> such as <c>F5</c>, <c>ctrl+r</c>, <c>alt+ArrowLeft</c>, <c>F12</c>, <c>Enter</c>.
/// </para>
/// </summary>
internal static class DevelopmentInput
{
    public const string Variable = "SPAR_DEV_INPUT";

    /// <summary>Arms the steps for the first <c>ui-ready</c> (UI thread).</summary>
    public static void Watch(WebViewBridge bridge, BrowserView view, HostLog log)
    {
        if (!SparDevelopment.IsSupported || Environment.GetEnvironmentVariable(Variable) is not { Length: > 0 } steps)
        {
            return;
        }

        var fired = false;
        bridge.DocumentReady += () =>
        {
            if (!fired)
            {
                fired = true;
                _ = RunAsync(view, steps, log);
            }
        };
    }

    private static async Task RunAsync(BrowserView view, string steps, HostLog log)
    {
        foreach (var step in steps.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                await RunStepAsync(view, step, log);
            }
            catch (Exception exception)
            {
                log.Error($"Development input: step '{step}' failed", exception);
            }
        }

        log.Info("Development input: all steps sent.");
    }

    private static async Task RunStepAsync(BrowserView view, string step, HostLog log)
    {
        var colon = step.IndexOf(':', StringComparison.Ordinal);
        var kind = colon < 0 ? step : step[..colon];
        var argument = colon < 0 ? "" : step[(colon + 1)..];
        switch (kind)
        {
            case "wait":
                await Task.Delay(int.Parse(argument, NumberStyles.None, CultureInfo.InvariantCulture));
                return;
            case "click" or "right" or "back" or "forward":
                await MouseAsync(view, kind == "click" ? "left" : kind, argument);
                break;
            case "key":
                await KeyAsync(view, argument);
                break;
            default:
                log.Warn($"Development input: unknown step '{step}'.");
                return;
        }

        log.Info($"Development input: sent {step}.");
    }

    private static async Task MouseAsync(BrowserView view, string button, string point)
    {
        var parts = point.Split(',');
        var x = int.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture);
        var y = int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture);
        await view.CallDevToolsAsync("Input.dispatchMouseEvent", $$"""{"type":"mouseMoved","x":{{x}},"y":{{y}}}""");
        await view.CallDevToolsAsync("Input.dispatchMouseEvent", $$"""{"type":"mousePressed","x":{{x}},"y":{{y}},"button":"{{button}}","clickCount":1}""");
        await view.CallDevToolsAsync("Input.dispatchMouseEvent", $$"""{"type":"mouseReleased","x":{{x}},"y":{{y}},"button":"{{button}}","clickCount":1}""");
    }

    private static async Task KeyAsync(BrowserView view, string keys)
    {
        var modifiers = 0;
        var parts = keys.Split('+');
        foreach (var modifier in parts[..^1])
        {
            modifiers |= modifier.ToLowerInvariant() switch
            {
                "alt" => 1,
                "ctrl" => 2,
                "shift" => 8,
                _ => throw new FormatException($"Unknown modifier '{modifier}'."),
            };
        }

        var (key, code, virtualKey) = parts[^1] switch
        {
            "F5" => ("F5", "F5", 116),
            "F12" => ("F12", "F12", 123),
            "ArrowLeft" => ("ArrowLeft", "ArrowLeft", 37),
            "ArrowRight" => ("ArrowRight", "ArrowRight", 39),
            "Enter" => ("Enter", "Enter", 13),
            var letter when letter.Length == 1 && char.IsAsciiLetter(letter[0]) =>
                (letter.ToLowerInvariant(), "Key" + letter.ToUpperInvariant(), (int)char.ToUpperInvariant(letter[0])),
            var other => throw new FormatException($"Unknown key '{other}'."),
        };

        var common = $"\"key\":\"{key}\",\"code\":\"{code}\",\"windowsVirtualKeyCode\":{virtualKey},\"modifiers\":{modifiers}";
        // Enter acts (a focused button's click) through its character, which only a keyDown with text carries.
        var down = key == "Enter" ? $$"""{"type":"keyDown","text":"\r",{{common}}}""" : $$"""{"type":"rawKeyDown",{{common}}}""";
        await view.CallDevToolsAsync("Input.dispatchKeyEvent", down);
        await view.CallDevToolsAsync("Input.dispatchKeyEvent", $$"""{"type":"keyUp",{{common}}}""");
    }
}
