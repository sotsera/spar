using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using Spar.Windows.Interop;

namespace Spar.Windows;

/// <summary>
/// The per-user log file (plan M1b, build step 7's first part): the host's lines in <c>host.log</c> in the app's logs
/// folder, written by a task of its own so no caller, the window's UI thread included, waits for the disk. Start-up
/// failures need it: an app started from an icon has no standard error, and an unregistered event-log source reads
/// poorly (plan section 3, "Missing prerequisites"). Several processes of the app may append to it at once (a relaunch
/// writes there too); each batch is one append. Rotation comes in M3: until then a file over
/// <see cref="RolloverBytes"/> is moved aside to <c>host.1.log</c> when a run opens it.
/// </summary>
internal sealed class LogFile
{
    /// <summary>The file's name in the logs folder.</summary>
    public const string FileName = Spar.Hosting.StartupReport.LogFileName;

    /// <summary>The size above which a run moves the file aside before appending.</summary>
    private const long RolloverBytes = 5 * 1024 * 1024;

    /// <summary>How long the process waits at exit for the last lines to reach the disk.</summary>
    private const int FlushLimitMilliseconds = 2_000;

    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _writer;

    private LogFile(string folder)
    {
        Path = System.IO.Path.Combine(folder, FileName);
        _writer = Task.Run(() => WriteAllAsync(folder));
    }

    /// <summary>The file's full path.</summary>
    public string Path { get; }

    /// <summary>Starts the writer; the folder is created by it, off the caller's thread.</summary>
    public static LogFile Open(string folder) => new(folder);

    /// <summary>Queues one line; never blocks and never throws.</summary>
    public void Write(string line) => _lines.Writer.TryWrite(line);

    /// <summary>Ends the file: the lines queued so far are written, within a short limit (process exit).</summary>
    public void Close()
    {
        _lines.Writer.TryComplete();

        // The process is ending, on its main thread with no window left, so this wait blocks nothing that draws.
        _ = _writer.Wait(FlushLimitMilliseconds);
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
            // No log file: standard error still gets every line, and the app keeps running.
            Console.Error.WriteLine($"spar warn: the log file {Path} cannot be opened: {exception.Message}");
            await DrainAsync();
            return;
        }

        using (file)
        {
            var batch = new StringBuilder();
            while (await _lines.Reader.WaitToReadAsync())
            {
                batch.Clear();
                while (_lines.Reader.TryRead(out var line))
                {
                    batch.Append(line).Append("\r\n");
                }

                // One write per batch, which the append-only handle puts whole at the end, whoever else appends.
                var bytes = Encoding.UTF8.GetBytes(batch.ToString());
                try
                {
                    RandomAccess.Write(file, bytes, fileOffset: 0);
                }
                catch (IOException)
                {
                    // A full disk loses log lines, never the app.
                }
            }
        }
    }

    /// <summary>
    /// An append-only handle (<c>FILE_APPEND_DATA</c> without <c>FILE_WRITE_DATA</c>). A <c>FileStream</c> opened with
    /// <c>FileMode.Append</c> writes at the position it tracks itself, so two processes appending at once overwrite each
    /// other's lines (measured: 2,001 of 4,000 lines left); with this handle Windows ignores the offset and appends.
    /// </summary>
    private static SafeFileHandle OpenForAppending(string path)
    {
        var handle = Native.CreateFile(path, Native.AppendOnly, Native.ShareAll, 0, Native.OpenAlways, Native.AttributeNormal, 0);
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
        catch (IOException)
        {
            // Another process of the app has it open without sharing deletion, or moved it first: append anyway.
        }
    }

    private async Task DrainAsync()
    {
        while (await _lines.Reader.WaitToReadAsync())
        {
            while (_lines.Reader.TryRead(out _))
            {
            }
        }
    }
}
