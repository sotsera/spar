using System.ComponentModel;

namespace Spar.Runtime;

/// <summary>
/// A desktop host (<c>Spar.Windows</c>, <c>Spar.Linux</c>): it builds the app, owns its window and returns the process
/// exit code. Spar's generated code hands the one for the current OS to <see cref="SparApp.Run(ISparHost)"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ISparHost
{
    int Run(SparApp app);
}
