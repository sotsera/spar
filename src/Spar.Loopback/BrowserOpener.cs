using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Spar.Hosting;

namespace Spar.Loopback;

/// <summary>
/// Opens the page in a browser at the first run of a dev session (<see cref="LoopbackSession"/>). The system's default
/// browser unless <see cref="Variable"/> says otherwise: <c>none</c> opens nothing, and any other value is a command, a
/// program and its arguments separated by spaces (double quotes keep spaces in one argument), with <c>{url}</c> replaced
/// by the page's address, or the address appended when it has no <c>{url}</c>. A developer names a browser or profile
/// with it, and Spar's tests a recorder, so no test opens the developer's own browser.
/// </summary>
internal static class BrowserOpener
{
    /// <summary>The variable that names the opener.</summary>
    public const string Variable = "SPAR_LOOPBACK_BROWSER";

    /// <summary>What opens <paramref name="url"/>: null for nothing, otherwise the process to start.</summary>
    public static ProcessStartInfo? For(Uri url, string? setting)
    {
        var address = url.AbsoluteUri;
        if (setting is not { Length: > 0 } || setting.Trim().Length == 0)
        {
            // The shell's own handler for http addresses: ShellExecuteEx on Windows, xdg-open on Linux.
            return new ProcessStartInfo(address) { UseShellExecute = true };
        }

        if (setting.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var words = Split(setting);
        var info = new ProcessStartInfo(words[0]) { UseShellExecute = false };
        var placed = false;
        foreach (var word in words.Skip(1))
        {
            placed |= word.Contains("{url}", StringComparison.Ordinal);
            info.ArgumentList.Add(word.Replace("{url}", address, StringComparison.Ordinal));
        }

        if (!placed)
        {
            info.ArgumentList.Add(address);
        }

        return info;
    }

    /// <summary>Opens <paramref name="url"/> off the caller's thread; a failure is a warning, never a stop.</summary>
    public static Task OpenAsync(Uri url, string? setting, HostLog log) => Task.Run(() =>
    {
        ProcessStartInfo? info;
        try
        {
            info = For(url, setting);
        }
        catch (ArgumentException exception)
        {
            log.Warn($"{Variable} is not a command Spar can run ({exception.Message}): open the page's address yourself.");
            return;
        }

        if (info is null)
        {
            log.Info($"The browser is not opened ({Variable}=none).");
            return;
        }

        try
        {
            using var process = Process.Start(info);
            log.Info(info.UseShellExecute ? "The default browser was asked to open the page." : $"{info.FileName} was asked to open the page.");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            log.Warn($"No browser could be opened ({exception.Message}): open the page's address yourself.");
        }
    });

    /// <summary>The words of a command: separated by spaces and tabs, with double quotes around a word that has them.</summary>
    internal static List<string> Split(string command)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var quoted = false;
        var started = false;
        foreach (var character in command)
        {
            if (character == '"')
            {
                quoted = !quoted;
                started = true;
                continue;
            }

            if (!quoted && character is ' ' or '\t')
            {
                if (started)
                {
                    words.Add(word.ToString());
                    word.Clear();
                    started = false;
                }

                continue;
            }

            word.Append(character);
            started = true;
        }

        if (quoted)
        {
            throw new ArgumentException("a double quote is not closed");
        }

        if (started)
        {
            words.Add(word.ToString());
        }

        return words.Count > 0 ? words : throw new ArgumentException("it names no program");
    }
}
