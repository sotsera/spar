using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Spar.Logging;

/// <summary>
/// The app's log file (plan M1b, build step 7): <c>app.log</c> in the logs folder, written by a task of its own from a
/// bounded queue, so no caller, the window's UI thread included, waits for the disk. Every line of the app goes there:
/// the host's from its first moment, then, through <see cref="SparFileLoggerProvider"/>, every logger of the app's
/// services. Several processes of the app may append to it at once (a relaunch writes there too); each batch is one
/// append. A full queue drops the newest entries and counts them, and the next batch says how many. Rotation comes in
/// M3: until then a file over <see cref="RolloverBytes"/> is moved aside to <c>app.1.log</c> when a run opens it.
/// </summary>
internal sealed partial class LogFile
{
    /// <summary>The file's name in the logs folder.</summary>
    public const string FileName = "app.log";

    /// <summary>How many entries may wait for the disk; more are dropped and counted.</summary>
    public const int QueueLength = 10_000;

    /// <summary>The size above which a run moves the file aside before appending.</summary>
    public const long RolloverBytes = 5 * 1024 * 1024;

    /// <summary>How long a close waits for the last lines to reach the disk.</summary>
    public const int FlushLimitMilliseconds = 2_000;

    /// <summary>How many entries one write takes at most.</summary>
    private const int EntriesPerBatch = 512;

    // FILE_APPEND_DATA | SYNCHRONIZE without FILE_WRITE_DATA, the three share modes, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL.
    private const uint AppendOnly = 0x0004 | 0x00100000;
    private const uint ShareAll = 0x1 | 0x2 | 0x4;
    private const uint OpenAlways = 4;
    private const uint AttributeNormal = 0x80;

    private readonly Channel<LogEntry> _entries;
    private readonly Task _writer;
    private long _dropped;
    private long _droppedWritten;

    private LogFile(string folder, int queueLength)
    {
        _entries = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(queueLength) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        Path = System.IO.Path.Combine(folder, FileName);
        _writer = Task.Run(() => WriteAllAsync(folder));
    }

    /// <summary>The file's full path.</summary>
    public string Path { get; }

    /// <summary>Entries dropped because the queue was full, since the file was opened.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Starts the writer; the folder is created by it, off the caller's thread.</summary>
    public static LogFile Open(string folder) => new(folder, QueueLength);

    /// <summary>The same with a queue of <paramref name="queueLength"/> entries, for the queue's own tests.</summary>
    internal static LogFile Open(string folder, int queueLength) => new(folder, queueLength);

    /// <summary>Queues one entry; never blocks and never throws. A full queue, or a closed file, drops it.</summary>
    public void Write(LogEntry entry)
    {
        if (!_entries.Writer.TryWrite(entry))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>Queues one entry logged now.</summary>
    public void Write(LogLevel level, string category, string message, Exception? exception = null) =>
        Write(new LogEntry(DateTime.UtcNow, level, category, message, exception));

    /// <summary>
    /// Ends the file: the entries queued so far are written, within <see cref="FlushLimitMilliseconds"/>. It waits on
    /// the calling thread: call it where nothing that draws waits (process exit, the shutdown watch's own thread).
    /// </summary>
    public void Close() => Close(TimeSpan.FromMilliseconds(FlushLimitMilliseconds));

    /// <summary>
    /// Ends the file, waiting at most <paramref name="limit"/> for the entries queued so far: at session end, where
    /// Windows may end the process as soon as <c>WM_ENDSESSION</c> returns, within what is left of its budget.
    /// </summary>
    public void Close(TimeSpan limit)
    {
        _entries.Writer.TryComplete();
        _ = _writer.Wait(limit > TimeSpan.Zero ? limit : TimeSpan.Zero);
    }

    /// <summary>Ends the file without blocking: completes once the queued entries are written, or at the limit.</summary>
    public async Task CloseAsync()
    {
        _entries.Writer.TryComplete();
        _ = await Task.WhenAny(_writer, Task.Delay(FlushLimitMilliseconds)).ConfigureAwait(false);
    }

    private async Task WriteAllAsync(string folder)
    {
        SafeFileHandle file;
        try
        {
            Directory.CreateDirectory(folder);
            MoveAsideWhenLarge();
            file = OpenForAppending(Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // No log file: standard error still gets the host's lines, and the app keeps running.
            Console.Error.WriteLine($"spar warn: the log file {Path} cannot be opened: {exception.Message}");
            await DrainAsync().ConfigureAwait(false);
            return;
        }

        using (file)
        {
            var batch = new StringBuilder();
            while (await _entries.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                batch.Clear();
                for (var count = 0; count < EntriesPerBatch && _entries.Reader.TryRead(out var entry); count++)
                {
                    LogLine.Append(batch, entry);
                }

                AppendDropped(batch);
                Append(file, batch);
            }

            // Entries dropped after the last batch, the closing ones included.
            batch.Clear();
            AppendDropped(batch);
            if (batch.Length > 0)
            {
                Append(file, batch);
            }
        }
    }

    /// <summary>One line saying how many entries were dropped since the last such line, when any were.</summary>
    private void AppendDropped(StringBuilder batch)
    {
        var dropped = Dropped;
        var lost = dropped - _droppedWritten;
        if (lost <= 0)
        {
            return;
        }

        _droppedWritten = dropped;
        var message = $"{lost} log entries were dropped: the queue for the disk was full.";
        LogLine.Append(batch, new LogEntry(DateTime.UtcNow, LogLevel.Warning, "Spar.Log", message, null));
    }

    /// <summary>One write per batch, which the append-only handle puts whole at the end, whoever else appends.</summary>
    private static void Append(SafeFileHandle file, StringBuilder batch)
    {
        var bytes = Encoding.UTF8.GetBytes(batch.ToString());
        try
        {
            var offset = OperatingSystem.IsWindows() ? 0 : RandomAccess.GetLength(file);
            RandomAccess.Write(file, bytes, offset);
        }
        catch (IOException)
        {
            // A full disk loses log lines, never the app.
        }
    }

    /// <summary>
    /// On Windows an append-only handle (<c>FILE_APPEND_DATA</c> without <c>FILE_WRITE_DATA</c>). A <c>FileStream</c>
    /// opened with <c>FileMode.Append</c> writes at the position it tracks itself, so two processes appending at once
    /// overwrite each other's lines (measured: 2,001 of 4,000 lines left); with this handle Windows ignores the offset
    /// and appends. Elsewhere each write goes at the file's length (the Linux host, M1c, checks two writers).
    /// </summary>
    private static SafeFileHandle OpenForAppending(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return File.OpenHandle(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        }

        var handle = CreateFile(path, AppendOnly, ShareAll, 0, OpenAlways, AttributeNormal, 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new IOException($"CreateFileW failed with error {error}.", Marshal.GetHRForLastWin32Error());
        }

        return handle;
    }

    private void MoveAsideWhenLarge()
    {
        var current = new FileInfo(Path);
        if (!current.Exists || current.Length < RolloverBytes)
        {
            return;
        }

        try
        {
            File.Move(Path, System.IO.Path.ChangeExtension(Path, ".1.log"), overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Another process of the app has it open without sharing deletion, or moved it first: append anyway.
        }
    }

    private async Task DrainAsync()
    {
        while (await _entries.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (_entries.Reader.TryRead(out _))
            {
            }
        }
    }

    [LibraryImport("kernel32", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);
}
