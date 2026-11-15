// Must not compile: the Dismissed case of a file dialog is missing and there is no default arm (expected CS8509).
// Built only with -p:SparCheckMissingCase=true.
using Spar;

namespace SqlTool;

internal static class FileDialogMissingCase
{
    public static string Describe(Result<FileGrant, FileDialogError> picked) => picked switch
    {
        FileGrant file => file.Name,
        FileDialogError.AccessDenied e => e.Path,
        FileDialogError.Unavailable e => e.Reason,
        FileDialogError.Busy => "busy",
    };
}
