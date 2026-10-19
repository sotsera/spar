using System.Security.Cryptography;
using System.Text;

namespace Sotsera.Spar.Development;

// This is an explicit consumer source boundary, not an MSBuild project-graph evaluator.
internal sealed class BackendInputs : IDisposable
{
    private static readonly HashSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csproj", ".props", ".targets", ".json", ".config"
    };
    private static readonly string[] _ancestorFiles =
        ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.Config"];
    private static readonly HashSet<string> _excludedNames = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", "node_modules", ".git" };
    private readonly string[] _roots;
    private readonly string[] _excluded;
    private readonly HashSet<string> _explicitFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _restartFiles;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly string _contract;
    private readonly string? _configurationFile;
    private long _revision;
    private bool _overflow;

    internal BackendInputs(DevelopmentSettings settings)
    {
        _contract = settings.ContractFile!;
        _configurationFile = settings.ConfigurationFile;
        _restartFiles = new HashSet<string>(settings.RestartFiles, StringComparer.OrdinalIgnoreCase);
        _roots = new[] { Path.GetDirectoryName(settings.Project)!, Path.GetDirectoryName(_contract)! }
            .Concat(settings.BackendSourceDirectories).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _excluded = [settings.Frontend.WorkingDirectory, settings.SessionDirectory];
        _explicitFiles.UnionWith(_restartFiles);
        foreach (var root in _roots)
        {
            for (var parent = Directory.GetParent(root); parent is not null; parent = parent.Parent)
            {
                foreach (var name in _ancestorFiles)
                {
                    _explicitFiles.Add(Path.Combine(parent.FullName, name));
                }
            }
        }

        try
        {
            foreach (var root in _roots)
            {
                Watch(root, true);
            }
            foreach (var directory in _explicitFiles.Select(file => Path.GetDirectoryName(file)!)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Watch(directory, false);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal BackendSelection? Capture()
    {
        if (Volatile.Read(ref _overflow))
        {
            throw new IOException("Backend input notifications overflowed. Restart the session to reestablish source selection.");
        }
        var revision = Interlocked.Read(ref _revision);
        try
        {
            var files = new SortedSet<string>(_explicitFiles, StringComparer.OrdinalIgnoreCase);
            var entries = 0;
            foreach (var root in _roots)
            {
                Collect(root, files, ref entries, 0);
            }
            using var all = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var restart = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var contractHash = "missing";
            long total = 0;
            foreach (var file in files)
            {
                var hash = "missing";
                if (File.Exists(file))
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    total += stream.Length;
                    if (stream.Length > 4 * 1024 * 1024 || total > 32 * 1024 * 1024)
                    {
                        throw new InvalidDataException("Backend inputs exceed the 4 MiB/file or 32 MiB total limit.");
                    }
                    var bytes = new byte[checked((int)stream.Length)];
                    stream.ReadExactly(bytes);
                    if (stream.ReadByte() != -1)
                    {
                        return null;
                    }
                    hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                }
                var entry = Encoding.UTF8.GetBytes(file + "\0" + hash + "\n");
                all.AppendData(entry);
                if (IsRestartFile(file))
                {
                    restart.AppendData(entry);
                }
                if (string.Equals(file, _contract, StringComparison.OrdinalIgnoreCase))
                {
                    contractHash = hash;
                }
            }
            if (revision != Interlocked.Read(ref _revision))
            {
                return null;
            }
            return new BackendSelection(revision, Convert.ToHexStringLower(all.GetHashAndReset()),
                Convert.ToHexStringLower(restart.GetHashAndReset()), contractHash);
        }
        catch (IOException)
        {
            // Atomic saves and sharing violations are unsettled inputs, never a usable build selection.
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
    }

    private void Collect(string directory, SortedSet<string> files, ref int entries, int depth)
    {
        if (depth > 32)
        {
            throw new InvalidDataException("Backend input directories exceed 32 levels.");
        }
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if (++entries > 8192)
            {
                throw new InvalidDataException("Backend input directories exceed 8192 entries.");
            }
            if (IsExcluded(entry))
            {
                continue;
            }
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }
            if ((attributes & FileAttributes.Directory) != 0)
            {
                Collect(entry, files, ref entries, depth + 1);
            }
            else if (_extensions.Contains(Path.GetExtension(entry)))
            {
                files.Add(entry);
                if (files.Count > 2048)
                {
                    throw new InvalidDataException("Backend inputs exceed 2048 files.");
                }
            }
        }
    }

    private bool IsExcluded(string path)
    {
        if (string.Equals(path, _configurationFile, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (_excluded.Any(directory => IsWithin(path, directory)))
        {
            return true;
        }
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(_excludedNames.Contains);
    }

    private bool IsRestartFile(string path)
    {
        var contractDirectory = Path.GetDirectoryName(_contract)!;
        var separateContractDirectory = !string.Equals(contractDirectory, _roots[0], StringComparison.OrdinalIgnoreCase);
        var contractSource = string.Equals(path, _contract, StringComparison.OrdinalIgnoreCase)
            || separateContractDirectory && IsWithin(path, contractDirectory);
        return _explicitFiles.Contains(path) || contractSource
            || !string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWithin(string path, string directory)
    {
        return string.Equals(path, directory, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private void Watch(string directory, bool recursive)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = recursive,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _watchers.Add(watcher);
        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += (_, change) =>
        {
            RecordChange(change.OldFullPath);
            RecordChange(change.FullPath);
        };
        watcher.Error += (_, _) => Volatile.Write(ref _overflow, true);
        watcher.EnableRaisingEvents = true;
    }

    private void OnChanged(object sender, FileSystemEventArgs change)
    {
        if (change.ChangeType == WatcherChangeTypes.Changed && !_extensions.Contains(Path.GetExtension(change.FullPath))
            && !_explicitFiles.Contains(change.FullPath))
        {
            return;
        }
        RecordChange(change.FullPath);
    }

    private void RecordChange(string path)
    {
        if (_explicitFiles.Contains(path) || !IsExcluded(path) && _roots.Any(root => IsWithin(path, root))
            && (_extensions.Contains(Path.GetExtension(path)) || !Path.HasExtension(path)))
        {
            Interlocked.Increment(ref _revision);
        }
    }
}

internal sealed record BackendSelection(long Revision, string Hash, string RestartHash, string ContractHash);
