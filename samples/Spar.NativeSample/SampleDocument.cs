namespace Spar.NativeSample;

internal static class SampleDocument
{
    public static string Create(bool probe, bool close)
    {
        var client = Read("Spar.Generated.Client.js");
        var application = Read(probe ? "Spar.Sample.Probe.js" : "Spar.Sample.App.js");
        return Read("Spar.Sample.App.html").Replace("/*__CLIENT__*/", client, StringComparison.Ordinal)
            .Replace("/*__APPLICATION__*/", "const closeProbe = " + (close ? "true;\n" : "false;\n") + application,
                StringComparison.Ordinal);
    }

    private static string Read(string name)
    {
        var assembly = name == "Spar.Generated.Client.js" ? typeof(SampleBackendBridge).Assembly : typeof(SampleDocument).Assembly;
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing sample resource: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
