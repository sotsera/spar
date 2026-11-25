using System.Text;

namespace Spar.Runtime;

/// <summary>
/// The build's identity (decision "M1d's decide-first questions"): the variant, the git revision, whether the work tree
/// had uncommitted changes, and the contracts' fingerprint. The generator writes it into every app as one marked block
/// of printable ASCII, <c>[spar-build-identity/1 variant=Production revision=… dirty=false fingerprint=…]</c>, which the
/// host writes in its first log line and <c>spar doctor</c> reads from the file without running it. An empty value is
/// unknown: no revision outside a git work tree, no tree state when git could not tell. Readers ignore keys they do not
/// know, so a later version may add some. This file is linked into the spar tool, which reads the same format.
/// </summary>
internal sealed record BuildIdentity(string Variant, string? Revision, bool? Dirty, string Fingerprint)
{
    /// <summary>How every block starts: a reader looks for these bytes in the file.</summary>
    public const string Marker = "[spar-build-identity/1 ";

    /// <summary>The longest block a reader accepts, its marker and brackets included.</summary>
    public const int MaxLength = 512;

    /// <summary>Reads one block, which starts with <see cref="Marker"/> and ends with <c>]</c>; null when it is not one.</summary>
    public static BuildIdentity? Parse(ReadOnlySpan<byte> block)
    {
        var marker = Encoding.ASCII.GetBytes(Marker);
        if (block.Length > MaxLength || !block.StartsWith(marker) || block[^1] != (byte)']')
        {
            return null;
        }

        var body = block[marker.Length..^1];
        foreach (var value in body)
        {
            if (!IsAllowed(value) && value != (byte)' ' && value != (byte)'=')
            {
                return null;
            }
        }

        string? variant = null;
        string? revision = null;
        string? dirty = null;
        string? fingerprint = null;
        foreach (var field in Encoding.ASCII.GetString(body).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = field.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                return null;
            }

            var value = field[(separator + 1)..];
            switch (field[..separator])
            {
                case "variant":
                    variant = value;
                    break;
                case "revision":
                    revision = value;
                    break;
                case "dirty":
                    dirty = value;
                    break;
                case "fingerprint":
                    fingerprint = value;
                    break;
            }
        }

        if (variant is null || revision is null || dirty is null || fingerprint is null || dirty is not ("" or "true" or "false"))
        {
            return null;
        }

        return new BuildIdentity(variant, revision.Length == 0 ? null : revision, dirty.Length == 0 ? null : dirty == "true", fingerprint);
    }

    /// <summary>The identity as the host's first log line says it, or that the app carries none.</summary>
    public static string Describe(ReadOnlySpan<byte> block) =>
        Parse(block)?.Describe() ?? "no build identity (built without Spar's generator)";

    /// <summary>
    /// <c>build Production, revision 4f2c… (no uncommitted changes), contract fingerprint 59cb559befd9b76b</c>; the
    /// revision part says when there is none, and when the tree's state is unknown.
    /// </summary>
    public string Describe()
    {
        var variant = Variant.Length == 0 ? "of an unknown variant" : Variant;
        var revision = Revision is null ? "no revision (not built in a git work tree)" : $"revision {Revision} ({Tree()})";
        return $"build {variant}, {revision}, contract fingerprint {Fingerprint}";
    }

    /// <summary>The block's bytes for these values, as the generator writes it.</summary>
    public string ToBlock()
    {
        var dirty = Dirty switch
        {
            true => "true",
            false => "false",
            null => "",
        };
        return $"{Marker}variant={Variant} revision={Revision ?? ""} dirty={dirty} fingerprint={Fingerprint}]";
    }

    /// <summary>The characters a value may have: ASCII letters, digits, <c>.</c>, <c>_</c> and <c>-</c>.</summary>
    public static bool IsAllowed(byte value) =>
        value is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9')
            or (byte)'.' or (byte)'_' or (byte)'-';

    private string Tree() => Dirty switch
    {
        true => "uncommitted changes",
        false => "no uncommitted changes",
        null => "tree state unknown",
    };
}
