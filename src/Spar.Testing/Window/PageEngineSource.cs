namespace Spar.Testing;

/// <summary>
/// The window-test engine's script (<c>PageEngine.js</c>), embedded in this assembly. It is host-neutral: the Windows
/// driver installs it in an isolated world through the DevTools Protocol, and the Linux driver will run the same text.
/// Apps never reference this assembly, so their executables carry none of it.
/// </summary>
internal static class PageEngineSource
{
    private static readonly Lazy<string> Source = new(Load);

    public static string Text => Source.Value;

    private static string Load()
    {
        using var stream = typeof(PageEngineSource).Assembly.GetManifestResourceStream("Spar.Testing.PageEngine.js")
            ?? throw new InvalidOperationException("Spar.Testing was built without its page engine.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
