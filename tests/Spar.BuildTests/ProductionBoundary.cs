using System.Text;

namespace Spar.BuildTests;

/// <summary>
/// What a Production executable must not carry (plan section 5): the test host, the loopback host and the test
/// libraries. Type names are UTF-8 in a native image's metadata; string literals are UTF-16. Each marker is unique to
/// Spar, since generic names give false positives (pitfalls: <c>LoopbackHost</c> is also a <c>System.Uri</c> flag).
/// </summary>
internal static class ProductionBoundary
{
    private static readonly string[] Types =
    [
        "SparTestSession", "SparTestApp", "ISparEntryPoint", "LoopbackHost", "LoopbackChannel", "LoopbackAssets",
        "Spar.Loopback", "Spar.Testing", "Microsoft.AspNetCore.Server.Kestrel",
    ];

    private static readonly string[] Literals = ["/_spar/channel", "/_spar/send", "Spar.TestHost.IsSupported"];

    /// <summary>
    /// What a Production page bundle must not carry: the fake backend of <c>@spar/app/testing</c> (its messages start
    /// with "fakeSpar") and the generated tables and error writers only that module uses.
    /// </summary>
    public static readonly string[] PageMarkers = ["fakeSpar", "__sparTesting", "createFakeSpar"];

    /// <summary>The page markers a folder's JavaScript files contain; empty for a clean Production bundle.</summary>
    public static IReadOnlyList<string> FindInPage(string directory, IEnumerable<string> markers)
    {
        var scripts = Directory.GetFiles(directory, "*.js", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();
        return markers.Where(m => scripts.Any(s => s.Contains(m, StringComparison.Ordinal))).ToList();
    }

    /// <summary>The markers <paramref name="image"/> contains; empty for a clean Production executable.</summary>
    public static IReadOnlyList<string> Find(byte[] image) =>
        Types.Where(t => Contains(image, t, Encoding.UTF8))
            .Concat(Literals.Where(l => Contains(image, l, Encoding.Unicode)))
            .ToList();

    public static bool Contains(byte[] image, string text, Encoding encoding) =>
        image.AsSpan().IndexOf(encoding.GetBytes(text)) >= 0;
}
