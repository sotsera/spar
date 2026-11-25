namespace Spar.Tool;

/// <summary>
/// The tool's exit codes (decision "The spar tool (implementation record; choices for review; 2026-11-22)"): the same
/// numbers for every command, so a script tells "something to fix" from "the command was wrong" from "the tool broke".
/// </summary>
internal static class ExitCodes
{
    /// <summary>doctor: nothing to fix (information only). init: written, or nothing to do.</summary>
    public const int Success = 0;

    /// <summary>doctor: warnings and no errors. init: nothing written (declined, or no terminal to ask and no --yes).</summary>
    public const int Warnings = 1;

    /// <summary>doctor: at least one error. init: a step failed (the scaffolder, npm, a file it could not read).</summary>
    public const int Errors = 2;

    /// <summary>init: written, except Vite's configuration, which was too unusual to edit; the snippet was printed.</summary>
    public const int Partial = 3;

    /// <summary>A wrong command line (sysexits' EX_USAGE).</summary>
    public const int Usage = 64;

    /// <summary>The tool itself failed (sysexits' EX_SOFTWARE): a bug to report.</summary>
    public const int Internal = 70;

    /// <summary>init's name for <see cref="Warnings"/>: nothing was written.</summary>
    public const int NotWritten = Warnings;
}
