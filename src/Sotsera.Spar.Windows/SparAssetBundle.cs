using System.IO.Compression;
using System.Text;

namespace Sotsera.Spar.Windows;

/// <summary>An immutable, bounded archive of application-owned production resources. No filesystem paths are served.</summary>
public sealed class SparAssetBundle
{
    private const int MaximumFiles = 256;
    private const int MaximumFileBytes = 16 * 1024 * 1024;
    private const int MaximumTotalBytes = 64 * 1024 * 1024;

    private readonly Dictionary<string, Asset> _assets;

    public string ContractFingerprint { get; }

    private SparAssetBundle(Dictionary<string, Asset> assets, string fingerprint)
    {
        _assets = assets;
        ContractFingerprint = fingerprint;
    }

    /// <summary>Reads a seekable ZIP containing index.html and spar-contract.txt. The caller retains the stream.</summary>
    public static SparAssetBundle Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek || stream.Length > MaximumTotalBytes)
        {
            throw new InvalidDataException("Use a readable, seekable asset archive of at most 64 MiB.");
        }

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count is < 2 or > MaximumFiles)
        {
            throw new InvalidDataException("A Spar asset bundle requires 2–256 files.");
        }

        var assets = new Dictionary<string, Asset>(StringComparer.Ordinal);
        var total = 0L;
        foreach (var entry in archive.Entries)
        {
            var path = entry.FullName;
            if (!IsAssetPath(path) || entry.Length > MaximumFileBytes || (total += entry.Length) > MaximumTotalBytes)
            {
                throw new InvalidDataException("Invalid asset path or bundle size limit exceeded.");
            }

            var contentType = ContentType(path);
            var bytes = new byte[(int)entry.Length];
            using var source = entry.Open();
            source.ReadExactly(bytes);
            if (source.ReadByte() != -1 || !assets.TryAdd(path, new Asset(bytes, contentType)))
            {
                throw new InvalidDataException("Duplicate asset or inconsistent archive length.");
            }
        }

        if (!assets.ContainsKey("index.html") || !assets.Remove("spar-contract.txt", out var contract))
        {
            throw new InvalidDataException("Missing index.html or spar-contract.txt.");
        }

        var fingerprint = Encoding.UTF8.GetString(contract.Bytes);
        if (fingerprint.Length != 64 || !fingerprint.All(char.IsAsciiHexDigit))
        {
            throw new InvalidDataException("The asset contract must be a SHA-256 fingerprint.");
        }

        return new SparAssetBundle(assets, fingerprint);
    }

    internal bool TryGet(string documentUri, string? requestUri, out byte[] bytes, out string contentType)
    {
        // Exact string prefix and canonical archive names reject encoded traversal, queries, foreign origins and aliases.
        var origin = documentUri[..(documentUri.LastIndexOf('/') + 1)];
        if (requestUri is not null && requestUri.StartsWith(origin, StringComparison.Ordinal)
            && _assets.TryGetValue(requestUri[origin.Length..], out var asset))
        {
            bytes = asset.Bytes;
            contentType = asset.ContentType;
            return true;
        }

        bytes = [];
        contentType = "text/plain; charset=utf-8";
        return false;
    }

    private static bool IsAssetPath(string path)
    {
        return path.Length is > 0 and <= 240 && path.Split('/').All(segment =>
            segment.Length > 0 && segment is not "." and not ".."
            && segment.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'));
    }

    private static string ContentType(string path)
    {
        return Path.GetExtension(path) switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json",
            ".txt" => "text/plain; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            _ => throw new InvalidDataException("Unsupported production asset extension: " + path)
        };
    }

    private sealed record Asset(byte[] Bytes, string ContentType);
}
