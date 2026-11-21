using System.Text;
using Microsoft.Extensions.Logging;

namespace Spar.DevServer;

/// <summary>
/// The development server's output, read from its file as it grows (decision "M1d step 1's shape": Vite's output goes to
/// <c>app.log</c> under <c>Spar.DevServer</c>, and to the console when the app has one). Each line is logged once, as
/// Vite wrote it, and echoed to standard output; the last <see cref="Kept"/> lines stay for the error page. A run that
/// reuses a server starts at the file's end, so only what the server writes from then on is this run's. It reads off
/// every UI thread, polling the file a few times a second.
/// </summary>
internal sealed class DevServerOutput : IDisposable
{
    /// <summary>How many of the last lines the error page shows.</summary>
    public const int Kept = 60;

    /// <summary>The longest line kept or logged; the rest of a longer one is cut.</summary>
    private const int MaxLine = 2_000;

    /// <summary>How often the file is read again once its end is reached.</summary>
    private const int PollMilliseconds = 100;

    private readonly ILogger _logger;
    private readonly bool _console;
    private readonly Queue<string> _last = new();
    private readonly Lock _gate = new();
    private CancellationTokenSource? _reading;
    private Task _reader = Task.CompletedTask;

    /// <param name="console">Whether lines are echoed to standard output (not in a test process).</param>
    public DevServerOutput(ILogger logger, bool console)
    {
        _logger = logger;
        _console = console;
    }

    /// <summary>The last lines read, oldest first.</summary>
    public IReadOnlyList<string> Last
    {
        get
        {
            lock (_gate)
            {
                return [.. _last];
            }
        }
    }

    /// <summary>Reads <paramref name="path"/> from its start (a new server) or its end (a reused one), until the next follow or dispose.</summary>
    public void Follow(string path, bool fromEnd)
    {
        Stop();
        lock (_gate)
        {
            _last.Clear();
        }

        var reading = new CancellationTokenSource();
        _reading = reading;
        _reader = Task.Run(() => ReadAsync(path, fromEnd, reading.Token), CancellationToken.None);
    }

    /// <summary>Waits until everything written so far is read: a server that ended has written its last words.</summary>
    public async Task CatchUpAsync()
    {
        // Three polls: the reader reaches the file's end at least once after this call.
        await Task.Delay(PollMilliseconds * 3).ConfigureAwait(false);
    }

    public void Dispose() => Stop();

    private void Stop()
    {
        _reading?.Cancel();
        _reading?.Dispose();
        _reading = null;
    }

    private async Task ReadAsync(string path, bool fromEnd, CancellationToken token)
    {
        var pending = new StringBuilder();
        var decoder = Encoding.UTF8.GetDecoder();
        var bytes = new byte[16 * 1024];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        FileStream? file = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                file ??= Open(path, fromEnd);
                var read = file is null ? 0 : await file.ReadAsync(bytes, token).ConfigureAwait(false);
                if (read == 0)
                {
                    await Task.Delay(PollMilliseconds, token).ConfigureAwait(false);
                    continue;
                }

                var count = decoder.GetChars(bytes, 0, read, chars, 0);
                Lines(pending, chars.AsSpan(0, count));
            }
        }
        catch (OperationCanceledException)
        {
            // Followed another file, or the run ended.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Log(LogLevel.Warning, default, $"The development server's output ({path}) could not be read: {exception.Message}", null, static (text, _) => text);
        }
        finally
        {
            if (file is not null)
            {
                await file.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static FileStream? Open(string path, bool fromEnd)
    {
        try
        {
            var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (fromEnd)
            {
                file.Seek(0, SeekOrigin.End);
            }

            return file;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Splits what was read into lines (LF, with a CR before it dropped); a partial line waits for its end.</summary>
    private void Lines(StringBuilder pending, ReadOnlySpan<char> text)
    {
        while (text.Length > 0)
        {
            var end = text.IndexOf('\n');
            if (end < 0)
            {
                pending.Append(text);
                return;
            }

            pending.Append(text[..end]);
            text = text[(end + 1)..];
            var line = pending.ToString().TrimEnd('\r');
            pending.Clear();
            Line(line.Length > MaxLine ? string.Concat(line.AsSpan(0, MaxLine), "…") : line);
        }
    }

    private void Line(string line)
    {
        lock (_gate)
        {
            _last.Enqueue(line);
            if (_last.Count > Kept)
            {
                _last.Dequeue();
            }
        }

        if (line.Length == 0)
        {
            return;
        }

        _logger.Log(LogLevel.Information, default, line, null, static (text, _) => text);
        if (_console)
        {
            Console.Out.WriteLine(line);
        }
    }
}
