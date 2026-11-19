using System.ComponentModel;
using System.Text;
using Spar.Hosting;

namespace Spar.Runtime;

/// <summary>
/// What the app declares in <c>spar.ContentSecurity(…)</c> and <c>spar.Permissions(…)</c>, as the build read it from
/// <c>Program.cs</c> (plan section 3b): the generated catalog carries one, and the hosts compose the policy they send
/// from it (<see cref="Hosting.PagePolicy"/>). Each list holds normalized sources, each once, in the order first
/// declared. The default is the strict policy with nothing added.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SparPageSecurity
{
    /// <summary>Nothing declared: the strict default.</summary>
    public static SparPageSecurity Default { get; } = new();

    public IReadOnlyList<string> FontSrc { get; init; } = [];

    public IReadOnlyList<string> ImgSrc { get; init; } = [];

    public IReadOnlyList<string> ConnectSrc { get; init; } = [];

    public IReadOnlyList<string> MediaSrc { get; init; } = [];

    public IReadOnlyList<string> ScriptSrc { get; init; } = [];

    public IReadOnlyList<string> StyleSrc { get; init; } = [];

    public IReadOnlyList<string> FrameSrc { get; init; } = [];

    public bool AllowInlineStyles { get; init; }

    public bool AllowEval { get; init; }

    public bool AllowWebAssembly { get; init; }

    public bool StyleNonce { get; init; }

    /// <summary><c>require-trusted-types-for 'script'</c>; false after <c>WithoutTrustedTypes()</c>.</summary>
    public bool TrustedTypes { get; init; } = true;

    /// <summary>Subresource Integrity on the Release bundle; false after <c>WithoutIntegrity()</c>.</summary>
    public bool Integrity { get; init; } = true;

    public CspReports Listener { get; init; } = CspReports.Always;

    public CspReports Endpoint { get; init; } = CspReports.Always;

    /// <summary>
    /// The declared features, each once, in the enum's order. An array, not a list interface: a collection of an enum
    /// behind an interface costs a NativeAOT image tens of kilobytes of generic code.
    /// </summary>
    public Permission[] Permissions { get; init; } = [];

    /// <summary>
    /// One line that names every value, in a fixed order: two declarations are the same exactly when their
    /// descriptions are. The Development check compares the run's declaration with the build's through it.
    /// </summary>
    public string Describe()
    {
        var text = new StringBuilder();
        Append(text, "font-src", FontSrc);
        Append(text, "img-src", ImgSrc);
        Append(text, "connect-src", ConnectSrc);
        Append(text, "media-src", MediaSrc);
        Append(text, "script-src", ScriptSrc);
        Append(text, "style-src", StyleSrc);
        Append(text, "frame-src", FrameSrc);
        text.Append("inline-styles=").Append(AllowInlineStyles ? "yes" : "no")
            .Append("; eval=").Append(AllowEval ? "yes" : "no")
            .Append("; webassembly=").Append(AllowWebAssembly ? "yes" : "no")
            .Append("; style-nonce=").Append(StyleNonce ? "yes" : "no")
            .Append("; trusted-types=").Append(TrustedTypes ? "yes" : "no")
            .Append("; integrity=").Append(Integrity ? "yes" : "no")
            .Append("; listener=").Append(PageSecurityNames.Of(Listener))
            .Append("; endpoint=").Append(PageSecurityNames.Of(Endpoint))
            .Append("; permissions=");
        for (var i = 0; i < Permissions.Length; i++)
        {
            text.Append(i == 0 ? "" : ",").Append(PageSecurityNames.Of(Permissions[i]));
        }

        return text.ToString();
    }

    private static void Append(StringBuilder text, string name, IReadOnlyList<string> sources)
    {
        if (sources.Count > 0)
        {
            text.Append(name).Append(' ').AppendJoin(' ', sources).Append("; ");
        }
    }
}
