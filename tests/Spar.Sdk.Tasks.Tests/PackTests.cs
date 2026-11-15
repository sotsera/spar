using System.Text;
using Microsoft.Build.Framework;
using Spar.Hosting;
using Spar.Sdk.Tasks.Pack;

namespace Spar.Sdk.Tasks.Tests;

/// <summary>The packing step's format and asset checks, read back with the runtime's reader.</summary>
public sealed class PackTests : IDisposable
{
    private readonly DirectoryInfo _page = Directory.CreateTempSubdirectory("spar-pack-");

    public void Dispose() => _page.Delete(recursive: true);

    [Fact]
    public void Every_file_reads_back_byte_for_byte_with_its_type()
    {
        var files = new[]
        {
            File("index.html", Encoding.UTF8.GetBytes("<!doctype html><title>Notebook</title>" + new string(' ', 400))),
            File("assets/index-abc.js", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("export const page = 1;\n", 400)))),
            File("assets/font.woff2", Noise(20_000)),
        };

        var frontend = SparFrontend.Read(FrontendPack.Write(files));

        foreach (var file in files)
        {
            Assert.True(frontend.TryGet(file.Path, out var content, out _));
            Assert.Equal(System.IO.File.ReadAllBytes(file.Source), content.ToArray());
        }

        Assert.True(frontend.TryGet("assets/index-abc.js", out _, out var type));
        Assert.Equal("text/javascript; charset=utf-8", type);
        Assert.False(frontend.TryGet("assets/missing.js", out _, out _));
    }

    [Fact]
    public void Text_is_compressed_and_what_Brotli_cannot_shrink_by_5_percent_is_stored()
    {
        var files = new[]
        {
            File("assets/index-abc.js", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("export const page = 1;\n", 400)))),
            File("assets/font.woff2", Noise(20_000)),
        };

        var entries = SparFrontend.Read(FrontendPack.Write(files)).Entries.ToDictionary(e => e.Path);

        Assert.True(entries["assets/index-abc.js"].Compressed);
        Assert.True(entries["assets/index-abc.js"].StoredLength < entries["assets/index-abc.js"].OriginalLength / 10);
        Assert.False(entries["assets/font.woff2"].Compressed);
        Assert.Equal(20_000, entries["assets/font.woff2"].StoredLength);
    }

    [Fact]
    public void The_same_files_give_the_same_bytes_whatever_their_order()
    {
        var a = File("a.js", Encoding.UTF8.GetBytes("export const a = 1;"));
        var b = File("b.css", Encoding.UTF8.GetBytes("p { color: red; }"));

        Assert.Equal(FrontendPack.Write([a, b]), FrontendPack.Write([b, a]));
    }

    [Theory]
    [InlineData("assets/index.js.map", "a source map")]
    [InlineData(".git/config", "a hidden file or folder")]
    [InlineData(".env", "a hidden file or folder")]
    [InlineData("config/.npmrc", "a hidden file or folder")]
    [InlineData("certs/server.pem", "a key or secret file")]
    [InlineData("keys/id_ed25519.pub", "a key or secret file")]
    [InlineData("tools/setup.exe", "a file type the hosts do not serve")]
    public void Files_that_must_not_ship_are_refused(string path, string reason)
    {
        var problem = Assert.Single(FrontendPack.Check([File(path, [1, 2, 3])]));

        Assert.Equal(path, problem.Path);
        Assert.StartsWith(reason, problem.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_that_differ_only_by_case_are_refused()
    {
        var source = File("logo.svg", Encoding.UTF8.GetBytes("<svg/>"));

        var problem = Assert.Single(FrontendPack.Check([source, source with { Path = "Logo.svg" }]));

        Assert.Equal("logo.svg and Logo.svg", problem.Path);
    }

    [Fact]
    public void The_notices_compress_and_decode_whole()
    {
        var text = string.Concat(Enumerable.Repeat("MIT License\n\nPermission is hereby granted, free of charge…\n", 200));

        var compressed = FrontendPack.Compress(Encoding.UTF8.GetBytes(text));

        Assert.True(compressed.Length < text.Length / 10);
        using var decoded = new System.IO.Compression.BrotliStream(new MemoryStream(compressed), System.IO.Compression.CompressionMode.Decompress);
        Assert.Equal(text, new StreamReader(decoded, Encoding.UTF8).ReadToEnd());
    }

    private PageFile File(string path, byte[] content)
    {
        var full = Path.Combine(_page.FullName, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllBytes(full, content);
        return new PageFile(path, new AbsolutePath(full));
    }

    private static byte[] Noise(int length)
    {
        var bytes = new byte[length];
        new Random(7).NextBytes(bytes);
        return bytes;
    }
}
